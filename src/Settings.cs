using System.Drawing;

namespace YoloMouse
{
    public enum Backend { Windows, Serial }
    public enum Activation { Hold, Toggle, Always }
    public enum TargetMode { Cursor, Center, Score }

    /// All tunable runtime parameters. Edited by the UI thread, snapshotted by
    /// the worker thread once per tick (guarded by Shared.Gate).
    public class Settings
    {
        public Backend Backend = Backend.Windows;

        // detection
        public float Conf = 0.35f;
        public bool UseGpu = true;

        // capture
        public bool FullScreen = true;
        public int FovSize = 640;

        // targeting
        public TargetMode TargetMode = TargetMode.Cursor;
        public float TargetEma = 0.40f;

        // smoothing / movement
        public float Smoothing = 0.70f;
        public float MaxSpeed = 60f;
        public float Deadzone = 3f;
        public float Gain = 1f;

        // activation
        public Activation Activation = Activation.Hold;
        public int ActivationVk = 0x02; // VK_RBUTTON
        public bool ClickOnTarget = false;

        // loop
        public int TickHz = 144;

        public Settings Clone() => (Settings)MemberwiseClone();
    }

    /// Everything the UI thread and worker thread exchange.
    public class Shared
    {
        public readonly object Gate = new object();
        public Settings Settings = new Settings();

        public volatile bool Running = true;
        public volatile bool MoverEnabled = false;
        public volatile bool PreviewEnabled = true;

        // commands: UI -> worker (guarded by Gate)
        public string LoadModelPath;
        public bool DoLoad;
        public string ConnectPort;
        public bool DoConnect;
        public bool DoDisconnect;

        // status: worker -> UI (guarded by Gate)
        public bool ModelLoaded;
        public string Provider = "-";
        public bool SerialConnected;
        public bool SerialVerified;
        public bool Active;
        public float Fps;
        public int DetCount;
        public string Message = "Idle. Load a model to begin.";

        // preview handoff: worker creates a Bitmap, UI consumes & displays it
        public Bitmap PendingPreview;
    }
}
