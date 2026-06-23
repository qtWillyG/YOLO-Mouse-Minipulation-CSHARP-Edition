using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Linq;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace YoloMouse
{
    public struct Detection
    {
        public float X, Y, W, H, Score;
        public int Class;
        public float Cx => X + W * 0.5f;
        public float Cy => Y + H * 0.5f;
    }

    /// YOLOv10 ONNX inference. YOLOv10 is NMS-free end-to-end: output is
    /// [1, N, 6] = (x1, y1, x2, y2, score, classId) in input-pixel space.
    public class Detector : IDisposable
    {
        private InferenceSession _session;
        private string _inputName;
        private const int InputSize = 640; // YOLOv10 default imgsz

        public bool Loaded => _session != null;
        public string Provider { get; private set; } = "none";

        public string Load(string path, bool useGpu, out bool ok)
        {
            ok = false;
            try
            {
                var opts = new SessionOptions
                {
                    GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL
                };
                Provider = "CPU";
                if (useGpu)
                {
                    // Either may throw if the provider isn't in the installed package.
                    try { opts.AppendExecutionProvider_DML(0); Provider = "DirectML"; }
                    catch
                    {
                        try { opts.AppendExecutionProvider_CUDA(0); Provider = "CUDA"; }
                        catch { Provider = "CPU"; }
                    }
                }

                _session?.Dispose();
                _session = new InferenceSession(path, opts);
                _inputName = _session.InputMetadata.Keys.First();
                ok = true;
                return $"Model loaded ({Provider}, input {InputSize}x{InputSize})";
            }
            catch (Exception e)
            {
                _session = null;
                return "Model load FAILED: " + e.Message;
            }
        }

        public List<Detection> Infer(Bitmap frame, float conf)
        {
            var result = new List<Detection>();
            if (_session == null || frame == null) return result;

            int w0 = frame.Width, h0 = frame.Height;
            float scale = Math.Min((float)InputSize / w0, (float)InputSize / h0);
            int nw = (int)Math.Round(w0 * scale);
            int nh = (int)Math.Round(h0 * scale);
            int padx = (InputSize - nw) / 2;
            int pady = (InputSize - nh) / 2;

            // letterbox into a 640x640 gray canvas
            var input = new DenseTensor<float>(new[] { 1, 3, InputSize, InputSize });
            using (var canvas = new Bitmap(InputSize, InputSize, PixelFormat.Format24bppRgb))
            {
                using (var g = Graphics.FromImage(canvas))
                {
                    g.Clear(Color.FromArgb(114, 114, 114));
                    g.InterpolationMode = InterpolationMode.Bilinear;
                    g.PixelOffsetMode = PixelOffsetMode.Half;
                    g.DrawImage(frame, new Rectangle(padx, pady, nw, nh));
                }

                var data = canvas.LockBits(new Rectangle(0, 0, InputSize, InputSize),
                                           ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
                int area = InputSize * InputSize;
                var span = input.Buffer.Span;
                unsafe
                {
                    byte* baseptr = (byte*)data.Scan0;
                    int stride = data.Stride;
                    for (int y = 0; y < InputSize; y++)
                    {
                        byte* row = baseptr + y * stride;
                        int rowOff = y * InputSize;
                        for (int x = 0; x < InputSize; x++)
                        {
                            // 24bpp RGB is stored as B,G,R
                            byte b = row[x * 3];
                            byte gr = row[x * 3 + 1];
                            byte r = row[x * 3 + 2];
                            span[rowOff + x] = r / 255f;             // R plane
                            span[area + rowOff + x] = gr / 255f;     // G plane
                            span[2 * area + rowOff + x] = b / 255f;  // B plane
                        }
                    }
                }
                canvas.UnlockBits(data);
            }

            var inputs = new List<NamedOnnxValue> { NamedOnnxValue.CreateFromTensor(_inputName, input) };
            using var outputs = _session.Run(inputs);
            var tensor = outputs.First().AsTensor<float>();
            var dims = tensor.Dimensions; // ReadOnlySpan<int>
            var dense = tensor as DenseTensor<float>;
            if (dense == null) return result;
            var flat = dense.Buffer.Span;
            if (flat.IsEmpty) return result;

            void Push(float x1, float y1, float x2, float y2, float score, float cls)
            {
                if (score < conf) return;
                result.Add(new Detection
                {
                    X = (x1 - padx) / scale,
                    Y = (y1 - pady) / scale,
                    W = (x2 - x1) / scale,
                    H = (y2 - y1) / scale,
                    Score = score,
                    Class = (int)Math.Round(cls)
                });
            }

            if (dims.Length == 3 && dims[2] == 6)        // [1, N, 6]
            {
                int n = dims[1];
                for (int i = 0; i < n; i++)
                {
                    int o = i * 6;
                    Push(flat[o], flat[o + 1], flat[o + 2], flat[o + 3], flat[o + 4], flat[o + 5]);
                }
            }
            else if (dims.Length == 3 && dims[1] == 6)   // [1, 6, N] transposed
            {
                int n = dims[2];
                for (int i = 0; i < n; i++)
                    Push(flat[i], flat[n + i], flat[2 * n + i], flat[3 * n + i], flat[4 * n + i], flat[5 * n + i]);
            }
            else if (dims.Length == 2 && dims[1] == 6)   // [N, 6]
            {
                int n = dims[0];
                for (int i = 0; i < n; i++)
                {
                    int o = i * 6;
                    Push(flat[o], flat[o + 1], flat[o + 2], flat[o + 3], flat[o + 4], flat[o + 5]);
                }
            }

            return result;
        }

        public void Dispose() => _session?.Dispose();
    }
}
