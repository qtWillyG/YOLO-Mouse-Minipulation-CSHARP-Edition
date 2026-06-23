# YoloMouse (C#)

.NET / WinForms port of the tool. It captures the screen, detects an object
with a **YOLOv10 `.onnx`** model, and moves the mouse onto it with adjustable
**smoothing**. Same two output backends:

1. **Windows mouse (SendInput)** — pure software, nothing to plug in.
2. **RP2040 / RP2350 USB-HID** — the included Arduino firmware turns the board
   into a real USB mouse; the PC sends it move/click packets over USB-serial.

GUI is **Windows Forms**; inference is **Microsoft.ML.OnnxRuntime**.

> The firmware and 7-byte serial protocol are identical to the C++ / Python /
> Rust versions — a board flashed for any of them works here.

---

## 1. Layout

```
YoloMouseCs/
├─ YoloMouse.csproj
├─ firmware/mouse_hid.ino   <- flash to the RP2040 / RP2350
└─ src/
   ├─ Program.cs            <- entry (sets DPI mode, starts worker + form)
   ├─ Settings.cs           <- settings + shared state
   ├─ Detector.cs           <- YOLOv10 ONNX (OnnxRuntime + System.Drawing)
   ├─ ScreenCapture.cs      <- CopyFromScreen grabber
   ├─ MouseBackends.cs      <- SendInput + serial + Win32 interop
   ├─ Worker.cs             <- capture->detect->target->smooth->move loop
   └─ MainForm.cs           <- WinForms GUI (built in code)
```

---

## 2. Prerequisites

- **Windows 10/11 (x64)**
- **.NET SDK 8 or newer** (https://dotnet.microsoft.com/download). This repo
  targets `net10.0-windows` (matches a .NET 10 SDK). To use .NET 8 instead,
  change `<TargetFramework>` in `YoloMouse.csproj` to `net8.0-windows` and
  install the **.NET 8 Desktop Runtime**.

NuGet pulls ONNX Runtime automatically; no manual native install.

---

## 3. Build & run

```powershell
cd YoloMouseCs
dotnet run -c Release
```

(or `dotnet build -c Release` then run `bin\Release\net10.0-windows\YoloMouse.exe`)

### GPU (optional)
For DirectML (any AMD/NVIDIA/Intel GPU on Windows), swap the ONNX Runtime
package in `YoloMouse.csproj`:

```xml
<!-- replace Microsoft.ML.OnnxRuntime with: -->
<PackageReference Include="Microsoft.ML.OnnxRuntime.DirectML" Version="1.20.1" />
```

The code already tries the DirectML/CUDA execution provider and falls back to
CPU if it isn't present.

---

## 4. Get a YOLOv10 `.onnx` model

```bash
pip install ultralytics
yolo export model=yolov10n.pt format=onnx opset=13   # or your own best.pt
```

Expects YOLOv10's end-to-end output `[1, N, 6]` (`x1,y1,x2,y2,score,class`) at
**640×640** (the default export). For another `imgsz`, change `InputSize` in
`src/Detector.cs`.

---

## 5. Flash the firmware (only for the RP2040 / RP2350 backend)

1. Install the **Arduino IDE**.
2. *Preferences → Additional Boards Manager URLs*:
   `https://github.com/earlephilhower/arduino-pico/releases/download/global/package_rp2040_index.json`
   then *Boards Manager* → install **"Raspberry Pi Pico/RP2040/RP2350"**.
3. *Manage Libraries* → install **Adafruit TinyUSB Library**.
4. Pick your board (*Raspberry Pi Pico* or *Pico 2* for RP2350).
5. **Important:** *Tools → USB Stack → "Adafruit TinyUSB"**.
6. Open `firmware/mouse_hid.ino`, hold BOOTSEL while first plugging in, Upload.

---

## 6. Use it

1. **Model:** *Browse* to your `.onnx`, *Load model*.
2. **Output backend:** *Windows* (ready), or *RP2040/RP2350 HID* → pick the COM
   port → *Connect* ("verified" = firmware answered the ping).
3. **Activation:** tick **MOVER ENABLED**, choose *Hold key* (default Right
   Mouse), *Toggle key*, or *Always on*.
4. **Smoothing & movement:** tune to taste (below).
5. Aim at a screen with the target (e.g. a white dot on black). The preview
   shows green detection boxes; the cursor eases onto the chosen target.

### Smoothing controls
| Control | Effect |
|---|---|
| **Smoothing** | 0 = snap instantly; higher = slower, smoother glide |
| **Max speed (px/tick)** | hard cap on cursor movement per tick |
| **Gain** | overall strength multiplier |
| **Deadzone (px)** | stop (and optionally click) once this close |
| **Target jitter filter** | smooths the *target point* to kill detection jitter |
| **Tick rate (Hz)** | how often the loop runs |

---

## 7. Notes

- **Nothing moves:** confirm *MOVER ENABLED* is on, you're holding the trigger
  key, and the preview shows a detection (lower *Confidence* if not).
- Multi-monitor capture uses the **primary** monitor.
- **`.onyx` vs `.onnx`:** the format is `.onnx`; rename if needed.
