using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Threading;

namespace YoloMouse
{
    /// Worker thread: capture -> detect -> pick target -> smooth -> move.
    public static class Worker
    {
        public static void Run(Shared sh)
        {
            using var cap = new ScreenCapture();
            using var det = new Detector();
            var winMouse = new WindowsMouse();
            var serMouse = new SerialMouse();

            (float x, float y)? ema = null;
            double accX = 0, accY = 0;
            bool toggle = false, prevKey = false, inDz = false;
            int frames = 0;
            var fpsClock = Stopwatch.StartNew();
            var tick = new Stopwatch();

            while (sh.Running)
            {
                tick.Restart();

                // snapshot settings + drain commands
                Settings s;
                string loadPath = null, connectPort = null;
                bool doDisconnect = false;
                lock (sh.Gate)
                {
                    s = sh.Settings.Clone();
                    if (sh.DoLoad) { loadPath = sh.LoadModelPath; sh.DoLoad = false; }
                    if (sh.DoConnect) { connectPort = sh.ConnectPort; sh.DoConnect = false; }
                    if (sh.DoDisconnect) { doDisconnect = true; sh.DoDisconnect = false; }
                }

                if (loadPath != null)
                {
                    SetMessage(sh, "Loading model...");
                    string msg = det.Load(loadPath, s.UseGpu, out bool ok);
                    lock (sh.Gate) { sh.ModelLoaded = ok; sh.Provider = det.Provider; sh.Message = msg; }
                }
                if (connectPort != null)
                {
                    bool opened = serMouse.Connect(connectPort);
                    lock (sh.Gate)
                    {
                        sh.SerialConnected = opened;
                        sh.SerialVerified = serMouse.Verified;
                        sh.Message = opened && serMouse.Verified ? $"Connected + verified on {connectPort}"
                                   : opened ? $"Opened {connectPort} (no firmware reply - check sketch)"
                                   : $"Failed to open {connectPort}";
                    }
                }
                if (doDisconnect)
                {
                    serMouse.Disconnect();
                    lock (sh.Gate) { sh.SerialConnected = false; sh.SerialVerified = false; sh.Message = "Serial disconnected"; }
                }

                IMouseBackend mouse = s.Backend == Backend.Serial ? serMouse : (IMouseBackend)winMouse;

                // capture region
                var (scrW, scrH) = Native.ScreenSize();
                int rx, ry, rw, rh;
                if (s.FullScreen) { rx = 0; ry = 0; rw = scrW; rh = scrH; }
                else
                {
                    rw = rh = Math.Clamp(s.FovSize, 64, Math.Min(scrW, scrH));
                    rx = scrW / 2 - rw / 2;
                    ry = scrH / 2 - rh / 2;
                }

                Bitmap frame = null;
                try { frame = cap.Grab(rx, ry, rw, rh); } catch { }

                // detect
                List<Detection> dets = (frame != null && det.Loaded) ? det.Infer(frame, s.Conf) : new List<Detection>();
                lock (sh.Gate) sh.DetCount = dets.Count;

                // choose target (screen coords)
                (float x, float y)? target = null;
                if (dets.Count > 0)
                {
                    var (cx, cy) = Native.Cursor();
                    float refX, refY;
                    if (s.TargetMode == TargetMode.Center) { refX = scrW / 2f; refY = scrH / 2f; }
                    else { refX = cx; refY = cy; }

                    double best = double.PositiveInfinity;
                    foreach (var d in dets)
                    {
                        float ox = rx + d.Cx, oy = ry + d.Cy;
                        double metric = s.TargetMode == TargetMode.Score
                            ? -d.Score
                            : (ox - refX) * (ox - refX) + (oy - refY) * (oy - refY);
                        if (metric < best) { best = metric; target = (ox, oy); }
                    }
                }

                // EMA jitter filter
                if (target.HasValue)
                {
                    if (!ema.HasValue) ema = target;
                    else
                    {
                        float a = Math.Clamp(s.TargetEma, 0f, 0.95f);
                        ema = (a * ema.Value.x + (1 - a) * target.Value.x,
                               a * ema.Value.y + (1 - a) * target.Value.y);
                    }
                }
                else ema = null;

                // activation
                bool kd = Native.KeyDown(s.ActivationVk);
                bool act;
                switch (s.Activation)
                {
                    case Activation.Always: act = true; break;
                    case Activation.Toggle:
                        if (kd && !prevKey) toggle = !toggle;
                        act = toggle; break;
                    default: act = kd; break;
                }
                prevKey = kd;
                act = act && sh.MoverEnabled && ema.HasValue && mouse.Ok;
                lock (sh.Gate) sh.Active = act;

                // smoothing / movement
                if (act)
                {
                    var (cx, cy) = Native.Cursor();
                    double dx = ema.Value.x - cx;
                    double dy = ema.Value.y - cy;
                    double dist = Math.Sqrt(dx * dx + dy * dy);
                    if (dist <= s.Deadzone)
                    {
                        if (s.ClickOnTarget && !inDz) mouse.Click(MouseButton.Left);
                        inDz = true; accX = accY = 0;
                    }
                    else
                    {
                        inDz = false;
                        double step = (1.0 - Math.Clamp(s.Smoothing, 0f, 0.99f)) * s.Gain;
                        double mvx = dx * step, mvy = dy * step;
                        double mlen = Math.Sqrt(mvx * mvx + mvy * mvy);
                        if (mlen > s.MaxSpeed) { double k = s.MaxSpeed / mlen; mvx *= k; mvy *= k; }
                        mvx += accX; mvy += accY;
                        int imx = (int)mvx, imy = (int)mvy; // truncate toward zero
                        accX = mvx - imx; accY = mvy - imy;
                        if (imx != 0 || imy != 0) mouse.MoveRelative(imx, imy);
                    }
                }
                else { accX = accY = 0; inDz = false; }

                // preview
                if (sh.PreviewEnabled && frame != null)
                {
                    var bmp = MakePreview(frame, dets);
                    Bitmap stale;
                    lock (sh.Gate) { stale = sh.PendingPreview; sh.PendingPreview = bmp; }
                    stale?.Dispose(); // superseded before the UI displayed it
                }

                // fps + pacing
                if (++frames >= 15)
                {
                    float fps = (float)(frames / fpsClock.Elapsed.TotalSeconds);
                    lock (sh.Gate) sh.Fps = fps;
                    frames = 0; fpsClock.Restart();
                }
                int hz = Math.Clamp(s.TickHz, 30, 1000);
                int budgetMs = Math.Max(0, 1000 / hz);
                int spent = (int)tick.ElapsedMilliseconds;
                if (spent < budgetMs) Thread.Sleep(budgetMs - spent);
            }

            serMouse.Disconnect();
        }

        private static void SetMessage(Shared sh, string m)
        {
            lock (sh.Gate) sh.Message = m;
        }

        private static Bitmap MakePreview(Bitmap frame, List<Detection> dets)
        {
            var vis = new Bitmap(frame); // independent copy the UI will own
            using (var g = Graphics.FromImage(vis))
            using (var pen = new Pen(Color.Lime, 2))
            {
                foreach (var d in dets)
                    g.DrawRectangle(pen, d.X, d.Y, Math.Max(1, d.W), Math.Max(1, d.H));
            }
            const int maxW = 480;
            if (vis.Width > maxW)
            {
                int nh = Math.Max(1, vis.Height * maxW / vis.Width);
                var small = new Bitmap(maxW, nh);
                using (var g = Graphics.FromImage(small)) g.DrawImage(vis, 0, 0, maxW, nh);
                vis.Dispose();
                return small;
            }
            return vis;
        }
    }
}
