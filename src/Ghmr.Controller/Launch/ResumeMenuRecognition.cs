namespace Ghmr.Controller.Launch;

public sealed record MenuTextRegion(string Text, double X, double Y, double Width, double Height);

public static class ResumeMenuRecognition
{
    public static MenuTextRegion? FindResume(IReadOnlyList<MenuTextRegion> lines)
    {
        static string Normalise(string text) =>
            string.Concat(text.Where(char.IsLetter)).ToLowerInvariant();
        // The English landing page contains both choices. A pause menu, a
        // loading screen, or New Game without an existing save is not a match.
        if (!lines.Any(line => Normalise(line.Text) == "newgame")) return null;
        return lines.FirstOrDefault(line =>
            Normalise(line.Text) is "resume" or "resumegame");
    }
}
