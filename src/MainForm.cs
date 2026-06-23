using System;
using System.Drawing;
using System.Windows.Forms;

namespace YoloMouse
{
    public class MainForm : Form
    {
        private static readonly (string Name, int Vk)[] Keys =
        {
            ("Right Mouse", 0x02), ("Mouse 4 (XBUTTON1)", 0x05), ("Mouse 5 (XBUTTON2)", 0x06),
            ("Left Mouse", 0x01), ("Left Shift", 0xA0), ("Left Ctrl", 0xA2), ("Left Alt", 0xA4),
            ("Caps Lock", 0x14), ("Space", 0x20), ("F1", 0x70), ("F2", 0x71), ("F3", 0x72),
        };

        private readonly Shared _sh;
        private FlowLayoutPanel _root;
        private TextBox _modelPath;
        private ComboBox _ports;
        private Label _lblModel, _lblSerial, _lblStat, _lblMsg;
        private PictureBox _pic;
        private readonly Timer _timer;

        public MainForm(Shared sh)
        {
            _sh = sh;
            Text = "YoloMouse (C#) - YOLOv10 cursor mover";
            ClientSize = new Size(560, 940);

            _root = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = true
            };
            Controls.Add(_root);

            Settings s;
            lock (_sh.Gate) s = _sh.Settings.Clone();

            BuildModel(s);
            BuildBackend(s);
            BuildActivation(s);
            BuildDetection(s);
            BuildSmoothing(s);
            BuildStatus();

            _timer = new Timer { Interval = 40 };
            _timer.Tick += OnTick;
            _timer.Start();

            FormClosing += (a, b) => { _sh.Running = false; _timer.Stop(); };
        }

        // ---- section builders --------------------------------------------------
        private void BuildModel(Settings s)
        {
            AddHeader("Model");
            var row = Row();
            _modelPath = new TextBox { Width = 380 };
            var browse = new Button { Text = "Browse", AutoSize = true };
            browse.Click += (a, b) =>
            {
                using var d = new OpenFileDialog { Filter = "ONNX model (*.onnx)|*.onnx|All files|*.*" };
                if (d.ShowDialog() == DialogResult.OK) _modelPath.Text = d.FileName;
            };
            row.Controls.Add(_modelPath);
            row.Controls.Add(browse);

            var row2 = Row();
            var gpu = new CheckBox { Text = "Use GPU (DirectML/CUDA, else CPU)", Checked = s.UseGpu, AutoSize = true };
            gpu.CheckedChanged += (a, b) => SetS(x => x.UseGpu = gpu.Checked);
            var load = new Button { Text = "Load model", AutoSize = true };
            load.Click += (a, b) =>
            {
                lock (_sh.Gate) { _sh.LoadModelPath = _modelPath.Text; _sh.DoLoad = _modelPath.Text.Length > 0; }
            };
            _lblModel = new Label { Text = "not loaded", AutoSize = true, ForeColor = Color.Chocolate };
            row2.Controls.Add(load);
            row2.Controls.Add(gpu);
            row2.Controls.Add(_lblModel);
        }

        private void BuildBackend(Settings s)
        {
            AddHeader("Output backend");
            var row = Row();
            var rbWin = new RadioButton { Text = "Windows (SendInput)", Checked = s.Backend == Backend.Windows, AutoSize = true };
            var rbSer = new RadioButton { Text = "RP2040/RP2350 HID", Checked = s.Backend == Backend.Serial, AutoSize = true };
            rbWin.CheckedChanged += (a, b) => { if (rbWin.Checked) SetS(x => x.Backend = Backend.Windows); };
            rbSer.CheckedChanged += (a, b) => { if (rbSer.Checked) SetS(x => x.Backend = Backend.Serial); };
            row.Controls.Add(rbWin);
            row.Controls.Add(rbSer);

            var row2 = Row();
            _ports = new ComboBox { Width = 140, DropDownStyle = ComboBoxStyle.DropDownList };
            _ports.Items.AddRange(SerialMouse.ListPorts());
            if (_ports.Items.Count > 0) _ports.SelectedIndex = 0;
            var refresh = new Button { Text = "Refresh", AutoSize = true };
            refresh.Click += (a, b) => { _ports.Items.Clear(); _ports.Items.AddRange(SerialMouse.ListPorts()); if (_ports.Items.Count > 0) _ports.SelectedIndex = 0; };
            var connect = new Button { Text = "Connect", AutoSize = true };
            connect.Click += (a, b) => { if (_ports.SelectedItem != null) lock (_sh.Gate) { _sh.ConnectPort = _ports.SelectedItem.ToString(); _sh.DoConnect = true; } };
            var disconnect = new Button { Text = "Disconnect", AutoSize = true };
            disconnect.Click += (a, b) => { lock (_sh.Gate) _sh.DoDisconnect = true; };
            _lblSerial = new Label { Text = "disconnected", AutoSize = true, ForeColor = Color.Chocolate };
            row2.Controls.Add(_ports);
            row2.Controls.Add(refresh);
            row2.Controls.Add(connect);
            row2.Controls.Add(disconnect);
            row2.Controls.Add(_lblSerial);
        }

        private void BuildActivation(Settings s)
        {
            AddHeader("Activation");
            var en = new CheckBox { Text = "MOVER ENABLED (master switch)", AutoSize = true, ForeColor = Color.SeaGreen };
            en.CheckedChanged += (a, b) => _sh.MoverEnabled = en.Checked;
            _root.Controls.Add(en);

            var row = Row();
            var mode = new ComboBox { Width = 120, DropDownStyle = ComboBoxStyle.DropDownList };
            mode.Items.AddRange(new object[] { "Hold key", "Toggle key", "Always on" });
            mode.SelectedIndex = (int)s.Activation;
            mode.SelectedIndexChanged += (a, b) => SetS(x => x.Activation = (Activation)mode.SelectedIndex);
            var key = new ComboBox { Width = 170, DropDownStyle = ComboBoxStyle.DropDownList };
            foreach (var k in Keys) key.Items.Add(k.Name);
            key.SelectedIndex = Array.FindIndex(Keys, k => k.Vk == s.ActivationVk);
            if (key.SelectedIndex < 0) key.SelectedIndex = 0;
            key.SelectedIndexChanged += (a, b) => SetS(x => x.ActivationVk = Keys[key.SelectedIndex].Vk);
            row.Controls.Add(new Label { Text = "Mode", AutoSize = true, Padding = new Padding(0, 6, 0, 0) });
            row.Controls.Add(mode);
            row.Controls.Add(new Label { Text = "Key", AutoSize = true, Padding = new Padding(8, 6, 0, 0) });
            row.Controls.Add(key);

            var click = new CheckBox { Text = "Click left button when on target", Checked = s.ClickOnTarget, AutoSize = true };
            click.CheckedChanged += (a, b) => SetS(x => x.ClickOnTarget = click.Checked);
            _root.Controls.Add(click);
        }

        private void BuildDetection(Settings s)
        {
            AddHeader("Detection & capture");
            AddSliderF("Confidence", 0.05f, 0.95f, s.Conf, v => SetS(x => x.Conf = v));
            var full = new CheckBox { Text = "Capture full screen", Checked = s.FullScreen, AutoSize = true };
            full.CheckedChanged += (a, b) => SetS(x => x.FullScreen = full.Checked);
            _root.Controls.Add(full);
            AddSliderI("FOV box size (px)", 128, 1080, s.FovSize, v => SetS(x => x.FovSize = v));

            var row = Row();
            var tm = new ComboBox { Width = 220, DropDownStyle = ComboBoxStyle.DropDownList };
            tm.Items.AddRange(new object[] { "Nearest to cursor", "Nearest to screen center", "Highest score" });
            tm.SelectedIndex = (int)s.TargetMode;
            tm.SelectedIndexChanged += (a, b) => SetS(x => x.TargetMode = (TargetMode)tm.SelectedIndex);
            row.Controls.Add(new Label { Text = "Target", AutoSize = true, Padding = new Padding(0, 6, 0, 0) });
            row.Controls.Add(tm);
        }

        private void BuildSmoothing(Settings s)
        {
            AddHeader("Smoothing & movement");
            AddSliderF("Smoothing (0 snap .. high smooth)", 0f, 0.99f, s.Smoothing, v => SetS(x => x.Smoothing = v));
            AddSliderI("Max speed (px/tick)", 1, 300, (int)s.MaxSpeed, v => SetS(x => x.MaxSpeed = v));
            AddSliderF("Gain", 0.1f, 3.0f, s.Gain, v => SetS(x => x.Gain = v));
            AddSliderI("Deadzone (px)", 0, 30, (int)s.Deadzone, v => SetS(x => x.Deadzone = v));
            AddSliderF("Target jitter filter", 0f, 0.95f, s.TargetEma, v => SetS(x => x.TargetEma = v));
            AddSliderI("Tick rate (Hz)", 30, 500, s.TickHz, v => SetS(x => x.TickHz = v));
        }

        private void BuildStatus()
        {
            AddHeader("Status");
            _lblStat = new Label { AutoSize = true, Text = "FPS: 0   Detections: 0   Mover active: no" };
            _lblMsg = new Label { AutoSize = true, MaximumSize = new Size(520, 0), Text = "" };
            _root.Controls.Add(_lblStat);
            _root.Controls.Add(_lblMsg);
            var prev = new CheckBox { Text = "Show preview", Checked = _sh.PreviewEnabled, AutoSize = true };
            prev.CheckedChanged += (a, b) => _sh.PreviewEnabled = prev.Checked;
            _root.Controls.Add(prev);
            _pic = new PictureBox { SizeMode = PictureBoxSizeMode.AutoSize, BorderStyle = BorderStyle.FixedSingle };
            _root.Controls.Add(_pic);
        }

        // ---- periodic status + preview refresh ---------------------------------
        private void OnTick(object sender, EventArgs e)
        {
            bool modelLoaded, serConn, serVer, active;
            string provider, msg;
            float fps; int det;
            lock (_sh.Gate)
            {
                modelLoaded = _sh.ModelLoaded; provider = _sh.Provider;
                serConn = _sh.SerialConnected; serVer = _sh.SerialVerified;
                active = _sh.Active; fps = _sh.Fps; det = _sh.DetCount; msg = _sh.Message;
            }

            _lblModel.Text = modelLoaded ? $"loaded ({provider})" : "not loaded";
            _lblModel.ForeColor = modelLoaded ? Color.SeaGreen : Color.Chocolate;
            if (serVer) { _lblSerial.Text = "verified"; _lblSerial.ForeColor = Color.SeaGreen; }
            else if (serConn) { _lblSerial.Text = "open (unverified)"; _lblSerial.ForeColor = Color.Goldenrod; }
            else { _lblSerial.Text = "disconnected"; _lblSerial.ForeColor = Color.Chocolate; }
            _lblStat.Text = $"FPS: {fps:0}   Detections: {det}   Mover active: {(active ? "YES" : "no")}";
            _lblMsg.Text = msg;

            if (_sh.PreviewEnabled)
            {
                Bitmap bmp;
                lock (_sh.Gate) { bmp = _sh.PendingPreview; _sh.PendingPreview = null; }
                if (bmp != null)
                {
                    var old = _pic.Image;
                    _pic.Image = bmp;
                    old?.Dispose();
                }
            }
        }

        // ---- tiny widget helpers ----------------------------------------------
        private void AddHeader(string t) =>
            _root.Controls.Add(new Label { Text = t, Font = new Font(Font, FontStyle.Bold), AutoSize = true, Margin = new Padding(3, 10, 3, 2) });

        private FlowLayoutPanel Row()
        {
            var r = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, WrapContents = false, AutoSize = true, Margin = new Padding(3) };
            _root.Controls.Add(r);
            return r;
        }

        private void AddSliderF(string label, float min, float max, float val, Action<float> onChange)
        {
            var panel = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Margin = new Padding(3) };
            var lbl = new Label { AutoSize = true };
            var tb = new TrackBar { Minimum = (int)Math.Round(min * 100), Maximum = (int)Math.Round(max * 100), TickStyle = TickStyle.None, Width = 480 };
            tb.Value = Math.Clamp((int)Math.Round(val * 100), tb.Minimum, tb.Maximum);
            void Upd() => lbl.Text = $"{label}: {tb.Value / 100f:0.00}";
            Upd();
            tb.Scroll += (a, b) => { Upd(); onChange(tb.Value / 100f); };
            panel.Controls.Add(lbl);
            panel.Controls.Add(tb);
            _root.Controls.Add(panel);
        }

        private void AddSliderI(string label, int min, int max, int val, Action<int> onChange)
        {
            var panel = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Margin = new Padding(3) };
            var lbl = new Label { AutoSize = true };
            var tb = new TrackBar { Minimum = min, Maximum = max, TickStyle = TickStyle.None, Width = 480 };
            tb.Value = Math.Clamp(val, min, max);
            void Upd() => lbl.Text = $"{label}: {tb.Value}";
            Upd();
            tb.Scroll += (a, b) => { Upd(); onChange(tb.Value); };
            panel.Controls.Add(lbl);
            panel.Controls.Add(tb);
            _root.Controls.Add(panel);
        }

        private void SetS(Action<Settings> apply)
        {
            lock (_sh.Gate) apply(_sh.Settings);
        }
    }
}
