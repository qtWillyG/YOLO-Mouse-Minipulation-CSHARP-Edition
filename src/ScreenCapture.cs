using System;
using System.Drawing;

namespace YoloMouse
{
    /// GDI screen-region grabber. Reuses one Bitmap between frames.
    public class ScreenCapture : IDisposable
    {
        private Bitmap _bmp;
        private Graphics _g;
        private int _w, _h;

        /// Grab (x,y,w,h) in virtual-screen pixels. The returned Bitmap is
        /// owned by this capture and reused on the next call - do not dispose it,
        /// and copy it if you need to keep it past the next Grab.
        public Bitmap Grab(int x, int y, int w, int h)
        {
            if (w <= 0 || h <= 0) return null;
            if (_bmp == null || _w != w || _h != h)
            {
                _g?.Dispose();
                _bmp?.Dispose();
                _bmp = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                _g = Graphics.FromImage(_bmp);
                _w = w; _h = h;
            }
            _g.CopyFromScreen(x, y, 0, 0, new Size(w, h), CopyPixelOperation.SourceCopy);
            return _bmp;
        }

        public void Dispose()
        {
            _g?.Dispose();
            _bmp?.Dispose();
            _g = null; _bmp = null;
        }
    }
}
