namespace Ghmr.Controller.Launch;

public readonly record struct PassBannerBounds(int X, int Y, int Width, int Height);

public static class GtaVPassScreenRecognition
{
    // Gold-letter silhouette from the English GTA V Mission Passed banner.
    // The font is too stylised for reliable ordinary text recognition. Only
    // this fixed banner is matched here; its separate mission title must also
    // be recognised as Derailed before a completion can be accepted.
    private static readonly byte[] Banner = Convert.FromBase64String(
        "AAAAAAAAAAAAAAAAAAAA8AAAAAAAAAAAAAAAAAAAAPAAAAAAAAAAAAAAAAAAAADwAAAAAAAAAAAAAAAAAAAA8AAAAAAAAAAAAAAAAAAAAPD/neePP8+ff+A///zzzx///7/333/vv//gv//++++///+/999/77//4L///vvvv///v/fff++//+C///7777///z/333/vv//gv//++++//+898N574L334L333nvvvffvvffAA++99+A98B544L337733zz/vvffgP//++ee/9++9999/77334L///vvvv/fvvffff++99+C///7777/3773333/vvffgv//++++/9++9559/77334J///PPvv/fvvQceeO+99+CB98AD74H377333nvvvffggffee++99++9999/77/34IH//vvvv//vvffff++/9+CB//7777//773333/vv/fggf/++++//++9999/77/34IH//vvvv//vveePP8+f9+AB//zxxx//");
    private const int TemplateWidth = 128;
    private const int TemplateHeight = 24;

    public static bool IsDerailedTitle(IEnumerable<string> lines) => lines.Any(line =>
        string.Concat(line.Where(char.IsLetter)).Equals("Derailed", StringComparison.OrdinalIgnoreCase));

    // Pixels represent the gold mask of the top half of GTA V's client area.
    // Normalisation handles resolution and display scaling, while retaining
    // the word silhouette so a failure banner or menu cannot count as a pass.
    public static bool TryFindBanner(int width, int height, ReadOnlySpan<byte> pixels,
        out PassBannerBounds bounds)
    {
        bounds = default;
        if (width < 320 || height < 120 || pixels.Length != width * height) return false;
        int leftLimit = (int)(width * .15), rightLimit = (int)(width * .92);
        int topLimit = (int)(height * .20), bottomLimit = (int)(height * .78);
        int[] rows = new int[bottomLimit - topLimit];
        int peak = 0;
        for (int y = topLimit; y < bottomLimit; y++)
        {
            int count = 0;
            for (int x = leftLimit; x < rightLimit; x++) if (pixels[y * width + x] != 0) count++;
            rows[y - topLimit] = count;
            peak = Math.Max(peak, count);
        }
        if (peak < width * .20) return false;
        int start = -1, top = 0, bottom = 0;
        for (int index = 0; index <= rows.Length; index++)
        {
            if (index < rows.Length && rows[index] >= peak * .35)
            {
                if (start < 0) start = index;
            }
            else if (start >= 0)
            {
                if (index - start > bottom - top) { top = start + topLimit; bottom = index + topLimit; }
                start = -1;
            }
        }
        int bandHeight = bottom - top;
        if (bandHeight < height * .04 || bandHeight > height * .30) return false;
        int left = rightLimit, right = leftLimit;
        for (int x = leftLimit; x < rightLimit; x++)
        {
            int count = 0;
            for (int y = top; y < bottom; y++) if (pixels[y * width + x] != 0) count++;
            if (count >= bandHeight * .20) { left = Math.Min(left, x); right = Math.Max(right, x + 1); }
        }
        if (right - left < width * .35) return false;
        int fullTop = bottom, fullBottom = top;
        for (int y = Math.Max(topLimit, top - bandHeight / 2);
            y < Math.Min(bottomLimit, bottom + bandHeight / 4); y++)
        {
            int count = 0;
            for (int x = left; x < right; x++) if (pixels[y * width + x] != 0) count++;
            if (count >= (right - left) * .025)
            { fullTop = Math.Min(fullTop, y); fullBottom = Math.Max(fullBottom, y + 1); }
        }
        if (fullBottom <= fullTop) return false;
        int observedInk = 0, templateInk = 0, intersection = 0;
        for (int gy = 0; gy < TemplateHeight; gy++)
        for (int gx = 0; gx < TemplateWidth; gx++)
        {
            int x0 = left + (right - left) * gx / TemplateWidth;
            int x1 = Math.Max(x0 + 1, left + (right - left) * (gx + 1) / TemplateWidth);
            int y0 = fullTop + (fullBottom - fullTop) * gy / TemplateHeight;
            int y1 = Math.Max(y0 + 1, fullTop + (fullBottom - fullTop) * (gy + 1) / TemplateHeight);
            int ink = 0;
            for (int y = y0; y < y1; y++) for (int x = x0; x < x1; x++)
                if (pixels[y * width + x] != 0) ink++;
            int bit = gy * TemplateWidth + gx;
            bool observed = ink * 2 >= (x1 - x0) * (y1 - y0);
            bool expected = (Banner[bit / 8] & (1 << (bit % 8))) != 0;
            if (observed) observedInk++;
            if (expected) templateInk++;
            if (observed && expected) intersection++;
        }
        if (2D * intersection / Math.Max(1, observedInk + templateInk) < .90) return false;
        bounds = new(left, fullTop, right - left, fullBottom - fullTop);
        return true;
    }
}
