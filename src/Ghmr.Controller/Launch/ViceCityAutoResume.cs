using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;
using Ghmr.Controller.Diagnostics;

namespace Ghmr.Controller.Launch;

internal static class ViceCityAutoResume
{
    public static async Task<bool> TrySelectAsync(
        nint window, Func<bool> stillWaiting, ControllerDiagnosticLog? log)
    {
        if (GetForegroundWindow() != window || !stillWaiting()) return false;
        OcrEngine? engine = OcrEngine.TryCreateFromUserProfileLanguages();
        if (engine is null) return false;
        if (!GetClientRect(window, out NativeRect rect) || rect.Right < 320 || rect.Bottom < 240)
            return false;
        NativePoint origin = new();
        if (!ClientToScreen(window, ref origin)) return false;
        using Bitmap capture = new(rect.Right, rect.Bottom, PixelFormat.Format32bppArgb);
        using (Graphics graphics = Graphics.FromImage(capture))
            graphics.CopyFromScreen(origin.X, origin.Y, 0, 0, capture.Size);
        double scale = Math.Min(1D, (OcrEngine.MaxImageDimension - 1D) /
            Math.Max(capture.Width, capture.Height));
        using Bitmap image = new(capture, new Size(
            Math.Max(1, (int)(capture.Width * scale)), Math.Max(1, (int)(capture.Height * scale))));
        using MemoryStream png = new();
        image.Save(png, ImageFormat.Png);
        using InMemoryRandomAccessStream stream = new();
        using (DataWriter writer = new(stream.GetOutputStreamAt(0)))
        {
            writer.WriteBytes(png.ToArray());
            await writer.StoreAsync();
        }
        stream.Seek(0);
        BitmapDecoder decoder = await BitmapDecoder.CreateAsync(stream);
        using SoftwareBitmap bitmap = await decoder.GetSoftwareBitmapAsync(
            BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore);
        OcrResult result = await engine.RecognizeAsync(bitmap);
        MenuTextRegion[] lines = result.Lines.Where(line => line.Words.Count > 0).Select(line =>
        {
            double left = line.Words.Min(word => word.BoundingRect.X);
            double top = line.Words.Min(word => word.BoundingRect.Y);
            double right = line.Words.Max(word => word.BoundingRect.Right);
            double bottom = line.Words.Max(word => word.BoundingRect.Bottom);
            return new MenuTextRegion(line.Text, left, top, right - left, bottom - top);
        }).ToArray();
        MenuTextRegion? resume = ResumeMenuRecognition.FindResume(lines);
        if (resume is null || !stillWaiting() || GetForegroundWindow() != window)
            return false;
        int x = (int)((resume.X + resume.Width / 2D) / scale);
        int y = (int)((resume.Y + resume.Height / 2D) / scale);
        if (x < 0 || y < 0 || x >= rect.Right || y >= rect.Bottom) return false;
        nint point = (nint)((y << 16) | (x & 0xffff));
        // Unreal's menu may read the real cursor position when it handles a
        // client mouse message. Place it on the label while VC has focus.
        if (!SetCursorPos(origin.X + x, origin.Y + y) ||
            !stillWaiting() || GetForegroundWindow() != window) return false;
        // Send only client mouse messages to the recognised Resume label.
        // No global Enter spam, save modification, or clicks in other apps.
        bool posted = PostMessageW(window, 0x0200, 0, point) &&
            PostMessageW(window, 0x0201, 1, point);
        PostMessageW(window, 0x0202, 0, point);
        log?.Info("Startup", $"Vice City landing Resume recognised; targeted click sent={posted}");
        return posted;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X, Y; }
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(nint window, out NativeRect rect);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ClientToScreen(nint window, ref NativePoint point);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll", ExactSpelling = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessageW(nint window, uint message, nint wParam, nint lParam);
}
