using System.IO.Compression;
using Ghmr.Controller.Launch;

internal static class PassScreenSelfTests
{
    public static void Run()
    {
        foreach (string file in new[] { "DerailPassReference.bin.gz", "DerailPassSmall.bin.gz" })
        {
            using FileStream input = File.OpenRead(Path.Combine(AppContext.BaseDirectory, file));
            using GZipStream gzip = new(input, CompressionMode.Decompress);
            using BinaryReader reader = new(gzip);
            int width = reader.ReadInt32(), height = reader.ReadInt32();
            byte[] mask = reader.ReadBytes(width * height);
            Require(GtaVPassScreenRecognition.TryFindBanner(width, height, mask, out PassBannerBounds bounds),
                $"The supplied Derailed screenshot must match at {width} pixels.");
            Require(bounds.Width > width * .4 && bounds.Height > 20,
                "Recognition must find the banner rather than a small background detail.");
            byte[] reversed = new byte[mask.Length];
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++) reversed[y * width + x] = mask[y * width + width - 1 - x];
            Require(!GtaVPassScreenRecognition.TryFindBanner(width, height, reversed, out _),
                "Gold colour without the correct word silhouette must not match.");
            byte[] block = new byte[mask.Length];
            for (int y = bounds.Y; y < bounds.Y + bounds.Height; y++)
                Array.Fill(block, (byte)1, y * width + bounds.X, bounds.Width);
            Require(!GtaVPassScreenRecognition.TryFindBanner(width, height, block, out _),
                "A similarly sized gold background must not count as Mission Passed.");
            Require(!GtaVPassScreenRecognition.TryFindBanner(width, height, new byte[mask.Length], out _),
                "Free roam with no banner must not complete a mission.");
        }
        Require(GtaVPassScreenRecognition.IsDerailedTitle(["Derailed"]),
            "The plain mission title must be recognised.");
        Require(GtaVPassScreenRecognition.IsDerailedTitle(["DERAILED."]),
            "Case and incidental punctuation must not prevent title recognition.");
        foreach (string title in new[] { "Mission Failed", "Wrong Side of the Tracks", "Derailed failed", "" })
            Require(!GtaVPassScreenRecognition.IsDerailedTitle([title]),
                "A different or incomplete result title must not match.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
