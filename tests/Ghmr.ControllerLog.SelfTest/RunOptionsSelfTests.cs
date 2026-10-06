using Ghmr.Controller;
using Ghmr.Controller.Launch;

internal static class RunOptionsSelfTests
{
    public static void Run(string directory)
    {
        ControllerPreferencesStore preferences = new(directory);
        Require(!preferences.Current.FixedTestOrderEnabled, "Existing users must default to random mode.");
        string[] route = ["gta4.three_leaf_clover", "gtav.derailed"];
        preferences.Save(new ControllerPreferences { FixedTestOrderEnabled = true, FixedMissionOrder = route });
        ControllerPreferencesStore loaded = new(directory);
        Require(loaded.Current.FixedTestOrderEnabled && loaded.Current.FixedMissionOrder.SequenceEqual(route),
            "Selected mission subset and order must survive a controller restart.");
        foreach (string[] invalid in new[] { Array.Empty<string>(), new[] { "unknown" }, new[] { route[0], route[0] } })
        {
            bool rejected = false;
            try { BetaRunChoices.ValidateFixedOrder(invalid); }
            catch (ArgumentException) { rejected = true; }
            Require(rejected, "Empty, duplicate and unknown test missions must be rejected.");
        }
        MenuTextRegion resume = new("RESUME", 10, 10, 100, 20);
        Require(ResumeMenuRecognition.FindResume([resume, new("New Game", 10, 30, 100, 20)]) == resume,
            "The English landing menu must identify Resume.");
        Require(ResumeMenuRecognition.FindResume([resume, new("Restart Mission", 10, 30, 100, 20)]) is null,
            "A gameplay pause menu must never match the startup recogniser.");
        Require(ResumeMenuRecognition.FindResume([new("New Game", 10, 30, 100, 20)]) is null,
            "A landing page without an existing save must never click New Game.");
    }
    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
}
