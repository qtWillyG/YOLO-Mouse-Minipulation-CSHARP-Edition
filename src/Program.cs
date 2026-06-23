using System.Threading;
using System.Windows.Forms;

namespace YoloMouse
{
    internal static class Program
    {
        [System.STAThread]
        static void Main()
        {
            // Per-monitor-v2 so capture pixels line up with screen pixels.
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            var shared = new Shared();
            var worker = new Thread(() => Worker.Run(shared)) { IsBackground = true, Name = "yolomouse-worker" };
            worker.Start();

            Application.Run(new MainForm(shared));

            shared.Running = false;
            worker.Join(1000);
        }
    }
}
