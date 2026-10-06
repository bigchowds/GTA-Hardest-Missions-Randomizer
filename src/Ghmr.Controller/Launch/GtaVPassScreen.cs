using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace Ghmr.Controller.Launch;

internal enum PassScreenResult { NoMatch, DerailedPassed, OcrUnavailable }

internal sealed class GtaVPassScreen
{
    private OcrEngine? _engine;

    public async Task<PassScreenResult> ReadAsync(nint window, Func<bool> stillCurrent)
    {
        if (GetForegroundWindow() != window || !stillCurrent()) return PassScreenResult.NoMatch;
        if (!GetClientRect(window, out NativeRect rect) || rect.Right < 640 || rect.Bottom < 240)
            return PassScreenResult.NoMatch;
        NativePoint origin = new();
        if (!ClientToScreen(window, ref origin)) return PassScreenResult.NoMatch;
        // Inspect only the result-banner half of this game's client area. No
        // desktop-wide capture, image file, input simulation, or upload occurs.
        using Bitmap capture = new(rect.Right, rect.Bottom / 2, PixelFormat.Format32bppArgb);
        using (Graphics graphics = Graphics.FromImage(capture))
            graphics.CopyFromScreen(origin.X, origin.Y, 0, 0, capture.Size);
        byte[] mask = GoldMask(capture);
        if (!GtaVPassScreenRecognition.TryFindBanner(capture.Width, capture.Height, mask, out PassBannerBounds banner))
            return PassScreenResult.NoMatch;

        if (_engine is null)
        {
            Windows.Globalization.Language? language = OcrEngine.AvailableRecognizerLanguages
                .FirstOrDefault(value => value.LanguageTag.StartsWith("en", StringComparison.OrdinalIgnoreCase));
            if (language is not null) _engine = OcrEngine.TryCreateFromLanguage(language);
            if (_engine is null) return PassScreenResult.OcrUnavailable;
        }
        Rectangle titleBounds = Rectangle.Intersect(new Rectangle(
            banner.X + banner.Width / 4, banner.Y + banner.Height,
            banner.Width / 2, Math.Max(1, banner.Height)), new Rectangle(Point.Empty, capture.Size));
        if (titleBounds.Width < 20 || titleBounds.Height < 10) return PassScreenResult.NoMatch;
        using Bitmap title = capture.Clone(titleBounds, PixelFormat.Format32bppArgb);
        double scale = Math.Min(2D, (OcrEngine.MaxImageDimension - 1D) / Math.Max(title.Width, title.Height));
        using Bitmap image = new(title, new Size(
            Math.Max(1, (int)(title.Width * scale)), Math.Max(1, (int)(title.Height * scale))));
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
        using SoftwareBitmap bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore);
        OcrResult result = await _engine.RecognizeAsync(bitmap);
        if (!stillCurrent() || GetForegroundWindow() != window) return PassScreenResult.NoMatch;
        return GtaVPassScreenRecognition.IsDerailedTitle(result.Lines.Select(line => line.Text))
            ? PassScreenResult.DerailedPassed : PassScreenResult.NoMatch;
    }

    private static byte[] GoldMask(Bitmap capture)
    {
        BitmapData data = capture.LockBits(new Rectangle(Point.Empty, capture.Size),
            ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            byte[] pixels = new byte[data.Stride * capture.Height];
            Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
            byte[] mask = new byte[capture.Width * capture.Height];
            for (int y = (int)(capture.Height * .20); y < capture.Height * .78; y++)
            for (int x = (int)(capture.Width * .15); x < capture.Width * .92; x++)
            {
                int offset = y * data.Stride + x * 4;
                int b = pixels[offset], g = pixels[offset + 1], r = pixels[offset + 2];
                if (r >= 110 && g >= 85 && b <= 115 && r - b >= 60 && g - b >= 40 &&
                    g >= .68 * r && g <= 1.02 * r) mask[y * capture.Width + x] = 1;
            }
            return mask;
        }
        finally { capture.UnlockBits(data); }
    }

    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(nint window, out NativeRect rect);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ClientToScreen(nint window, ref NativePoint point);
}
