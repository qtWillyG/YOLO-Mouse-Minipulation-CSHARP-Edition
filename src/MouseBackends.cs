using System;
using System.IO.Ports;
using System.Runtime.InteropServices;

namespace YoloMouse
{
    public enum MouseButton { Left = 0, Right = 1, Middle = 2 }

    public interface IMouseBackend
    {
        void MoveRelative(int dx, int dy);
        void Click(MouseButton b);
        bool Ok { get; }
    }

    // ---- Win32 interop ------------------------------------------------------
    public static class Native
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int X; public int Y; }

        [StructLayout(LayoutKind.Sequential)]
        public struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct HARDWAREINPUT
        {
            public uint uMsg;
            public ushort wParamL;
            public ushort wParamH;
        }

        [StructLayout(LayoutKind.Explicit)]
        public struct InputUnion
        {
            [FieldOffset(0)] public MOUSEINPUT mi;
            [FieldOffset(0)] public KEYBDINPUT ki;
            [FieldOffset(0)] public HARDWAREINPUT hi;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct INPUT { public int type; public InputUnion U; }

        public const int INPUT_MOUSE = 0;
        public const uint MOUSEEVENTF_MOVE = 0x0001;
        public const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        public const uint MOUSEEVENTF_LEFTUP = 0x0004;
        public const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
        public const uint MOUSEEVENTF_RIGHTUP = 0x0010;
        public const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
        public const uint MOUSEEVENTF_MIDDLEUP = 0x0040;

        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [DllImport("user32.dll")]
        public static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        public static extern short GetAsyncKeyState(int vKey);

        [DllImport("user32.dll")]
        public static extern int GetSystemMetrics(int nIndex);

        public const int SM_CXSCREEN = 0;
        public const int SM_CYSCREEN = 1;

        public static (int x, int y) Cursor()
        {
            GetCursorPos(out var p);
            return (p.X, p.Y);
        }

        public static bool KeyDown(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

        public static (int w, int h) ScreenSize() =>
            (GetSystemMetrics(SM_CXSCREEN), GetSystemMetrics(SM_CYSCREEN));

        public static void SendMouse(uint flags, int dx, int dy)
        {
            var inp = new INPUT
            {
                type = INPUT_MOUSE,
                U = new InputUnion
                {
                    mi = new MOUSEINPUT { dx = dx, dy = dy, mouseData = 0, dwFlags = flags, time = 0, dwExtraInfo = IntPtr.Zero }
                }
            };
            SendInput(1, new[] { inp }, Marshal.SizeOf<INPUT>());
        }
    }

    // ---- Windows SendInput backend -----------------------------------------
    public class WindowsMouse : IMouseBackend
    {
        public bool Ok => true;

        public void MoveRelative(int dx, int dy)
        {
            if (dx != 0 || dy != 0) Native.SendMouse(Native.MOUSEEVENTF_MOVE, dx, dy);
        }

        public void Click(MouseButton b)
        {
            uint down, up;
            switch (b)
            {
                case MouseButton.Right: down = Native.MOUSEEVENTF_RIGHTDOWN; up = Native.MOUSEEVENTF_RIGHTUP; break;
                case MouseButton.Middle: down = Native.MOUSEEVENTF_MIDDLEDOWN; up = Native.MOUSEEVENTF_MIDDLEUP; break;
                default: down = Native.MOUSEEVENTF_LEFTDOWN; up = Native.MOUSEEVENTF_LEFTUP; break;
            }
            Native.SendMouse(down, 0, 0);
            Native.SendMouse(up, 0, 0);
        }
    }

    // ---- RP2040 / RP2350 serial backend ------------------------------------
    public class SerialMouse : IMouseBackend, IDisposable
    {
        private SerialPort _port;
        public bool Verified { get; private set; }
        public bool Ok => _port != null && _port.IsOpen;

        public static string[] ListPorts() => SerialPort.GetPortNames();

        private static byte[] Packet(byte cmd, byte d0, byte d1, byte d2, byte d3)
        {
            byte chk = (byte)(cmd ^ d0 ^ d1 ^ d2 ^ d3);
            return new byte[] { 0xAA, cmd, d0, d1, d2, d3, chk };
        }

        public bool Connect(string name)
        {
            Verified = false;
            try
            {
                _port = new SerialPort(name, 115200) { ReadTimeout = 80, WriteTimeout = 200 };
                _port.Open();
                var ping = Packet((byte)'P', 0, 0, 0, 0);
                _port.Write(ping, 0, ping.Length);
                try { if (_port.ReadByte() == 'K') Verified = true; } catch { }
                return true;
            }
            catch
            {
                _port = null;
                return false;
            }
        }

        public void Disconnect()
        {
            try { _port?.Close(); } catch { }
            _port = null;
            Verified = false;
        }

        private void Write(byte[] pkt)
        {
            try { _port?.Write(pkt, 0, pkt.Length); } catch { }
        }

        public void MoveRelative(int dx, int dy)
        {
            if (dx == 0 && dy == 0) return;
            short x = (short)Math.Clamp(dx, short.MinValue, short.MaxValue);
            short y = (short)Math.Clamp(dy, short.MinValue, short.MaxValue);
            ushort ux = (ushort)x, uy = (ushort)y;
            Write(Packet((byte)'M', (byte)(ux & 0xFF), (byte)(ux >> 8), (byte)(uy & 0xFF), (byte)(uy >> 8)));
        }

        public void Click(MouseButton b) => Write(Packet((byte)'C', (byte)b, 0, 0, 0));

        public void Dispose() => Disconnect();
    }
}
