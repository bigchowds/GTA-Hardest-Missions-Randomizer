using System;
using System.IO;
using System.Reflection;
using GTA.Native;

internal static class Program
{
    private static void Main()
    {
        string directory = Path.Combine(Path.GetTempPath(), "ghmr-gta4-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            TestRetainedDelivery(Path.Combine(directory, "delivery"));
            TestCompletion(Path.Combine(directory, "cue"), true, true);
            TestCompletion(Path.Combine(directory, "failure"), false, false);
            TestCompletion(Path.Combine(directory, "stale-cue"), true, false, true);
            Console.WriteLine("GTA IV bridge behavioural tests passed: retained events, write retry, native success cue and failure separation.");
        }
        finally { Function.Handler = null; Directory.Delete(directory, true); }
    }

    private static void TestRetainedDelivery(string directory)
    {
        GhmrGta4BridgeV017 bridge = new GhmrGta4BridgeV017(directory);
        string controller = Path.Combine(directory, "gta4-controller.ini");
        string game = Path.Combine(directory, "gta4-game.ini");
        WriteController(controller, 0);
        Invoke(bridge, "TickBridge"); // queue Ready
        Invoke(bridge, "TickBridge"); // publish Ready
        string ready = File.ReadAllText(game);
        Require(ready.Contains("bridgeReady") && ready.Contains("sequence=1"), "Ready must be published.");
        Invoke(bridge, "SendEnvelope", "playerControlLost", null);
        Invoke(bridge, "WriteGameFile"); // simulate a command acknowledgement
        Require(File.ReadAllText(game).Contains("bridgeReady"), "Queued event and command ack must preserve unacknowledged Ready.");
        WriteController(controller, 1);
        Invoke(bridge, "TickBridge");
        Require(File.ReadAllText(game).Contains("playerControlLost"), "Acknowledging Ready must deliver the next event.");
        Invoke(bridge, "SendEnvelope", "missionCompleted", null);
        Invoke(bridge, "SendEnvelope", "playerControlGained", null);
        Require(File.ReadAllText(game).Contains("playerControlLost"), "Enqueuing completion must preserve the previous frame.");
        WriteController(controller, 2);
        using (FileStream locked = new FileStream(game, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            bool failed = false;
            try { Invoke(bridge, "TickBridge"); }
            catch (IOException) { failed = true; }
            Require(failed, "The publication retry test requires a real denied write.");
        }
        Invoke(bridge, "TickBridge");
        Require(File.ReadAllText(game).Contains("missionCompleted") &&
            File.ReadAllText(game).Contains("sequence=3"), "Failed writes must retain completion and its next sequence.");
        Invoke(bridge, "TickBridge");
        Require(File.ReadAllText(game).Contains("missionCompleted"), "Completion must remain until acknowledged.");
        WriteController(controller, 3);
        Invoke(bridge, "TickBridge");
        Require(File.ReadAllText(game).Contains("playerControlGained") &&
            File.ReadAllText(game).Contains("sequence=4"), "The subsequent event must follow completion exactly once.");
    }

    private static void TestCompletion(string directory, bool successCue, bool expectedCompletion, bool cueAlreadyPlaying = false)
    {
        GhmrGta4BridgeV017 bridge = new GhmrGta4BridgeV017(directory);
        string controller = Path.Combine(directory, "gta4-controller.ini");
        string game = Path.Combine(directory, "gta4-game.ini");
        WriteController(controller, 0);
        Invoke(bridge, "TickBridge");
        Invoke(bridge, "TickBridge");
        WriteController(controller, 1);
        Set(bridge, "phase", "active");
        Set(bridge, "missionObservedActive", true);
        Set(bridge, "completionCueWasPlaying", cueAlreadyPlaying);
        Set(bridge, "runId", "attempt");
        Set(bridge, "activeMissionId", "gta4.three_leaf_clover");
        Function.Handler = (name, args) =>
        {
            if (name == "IS_MISSION_COMPLETE_PLAYING") return successCue;
            if (name == "GET_NUMBER_OF_INSTANCES_OF_STREAMED_SCRIPT") return 0;
            return null;
        };
        for (int tick = 0; tick < 21; tick++) Invoke(bridge, "TickBridge");
        string text = File.ReadAllText(game);
        Require(text.Contains(expectedCompletion ? "missionCompleted" : "missionFailed"),
            "Script ending must require positive success evidence; a fresh cue must work without a money increase.");
        Require(!text.Contains(expectedCompletion ? "missionFailed" : "missionCompleted"), "Failure and completion must remain distinct.");
        Function.Handler = null;
    }

    private static void WriteController(string path, int ack)
    {
        long pulse = (long)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalMilliseconds;
        File.WriteAllText(path, "[GHMR]\nsession=test\npulse=" + pulse + "\nack=" + ack + "\nsequence=0\n");
    }

    private static object Invoke(object target, string method, params object[] arguments)
    {
        MethodInfo selected = null;
        foreach (MethodInfo candidate in target.GetType().GetMethods(BindingFlags.Instance | BindingFlags.NonPublic))
            if (candidate.Name == method && candidate.GetParameters().Length == arguments.Length) selected = candidate;
        try { return selected.Invoke(target, arguments); }
        catch (TargetInvocationException exception) { throw exception.InnerException; }
    }
    private static void Set(object target, string field, object value) =>
        target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
