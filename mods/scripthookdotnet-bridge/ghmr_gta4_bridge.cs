// GHMR GTA IV: Complete Edition bridge v0.1.17 retained event delivery
//
// This source file is compiled by GTA IV ScriptHookDotNet at game startup.
// It deliberately uses only the public GTA IV native API and local files under
// %LOCALAPPDATA%\GHMR. It does not use networking, inject another module or
// require CLEO Redux plugins.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using GTA;
using GTA.Native;

public sealed class GhmrGta4BridgeV017 : Script
{
    private const string GameId = "gta4";
    private const string MissionId = "gta4.three_leaf_clover";
    private const string ScriptName = "Packie3";
    private const int ExpectedReward = 250000;
    private const int StableTicksRequired = 5;
    private const int NativeStartTimeoutTicks = 900;
    private const int LaunchObservationTicks = 100;
    private const int EndedConfirmationTicks = 20;
    private const int NativeMissionStartHour = 12;

    private readonly string ipcDirectory;
    private readonly string controllerPath;
    private readonly string gamePath;
    private readonly string logPath;
    private readonly Queue<string> outgoing = new Queue<string>();

    private string session = "";
    private string instanceId = "";
    private int sentSequence;
    private int receivedSequence;
    private int eventId;
    private string publishedMessage = "";
    private long lastHealthyPulse;
    private bool connected;
    private bool readySent;

    private string phase = "idle";
    private string runId = "";
    private string activeMissionId = "";
    private int stableTicks;
    private int launchTicksRemaining;
    private int nativeStartTicksRemaining;
    private int endedTicks;
    private int previousMoney;
    private bool launchIssued;
    private bool missionObservedActive;
    private bool controlWasBlockedAfterLaunch;
    private bool rewardObserved;
    private bool completionCueObserved;
    private bool completionCueWasPlaying;
    private bool controlReported;
    private bool releaseFadeRequested;
    private bool releaseFadeStarted;
    private bool packieBlipWaitLogged;
    private bool nativeTimePrepared;
    private bool compatibilityFallbackErrorLogged;
    private int blipScanAttempts;

    public GhmrGta4BridgeV017() : this(null) { }

    internal GhmrGta4BridgeV017(string testDirectory)
    {
        Interval = 100;
        ipcDirectory = testDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GHMR", "controller", "ipc");
        controllerPath = Path.Combine(ipcDirectory, GameId + "-controller.ini");
        gamePath = Path.Combine(ipcDirectory, GameId + "-game.ini");
        logPath = Path.Combine(ipcDirectory, "gta4-bridge.log");
        Directory.CreateDirectory(ipcDirectory);
        Log("GTA IV ScriptHookDotNet bridge v0.1.17 retained event delivery loading");
        Tick += OnTick;
    }

    private void OnTick(object sender, EventArgs args)
    {
        try
        {
            TickBridge();
        }
        catch (Exception exception)
        {
            Log("Unhandled bridge error: " + exception);
            if (connected && phase != "released")
            {
                SendEnvelope("bridgeError", "mission-launch-failed");
                phase = "released";
            }
        }
    }

    private void TickBridge()
    {
        if (!RefreshTransport())
        {
            if (connected)
                Log("Controller pulse temporarily unavailable; preserving active mission state");
            connected = false;
            return;
        }

        if (!connected)
        {
            connected = true;
            Log("Controller transport connected");
        }

        if (!readySent)
        {
            readySent = SendEnvelope("bridgeReady", null);
        }

        string commandJson;
        while ((commandJson = PollCommand()).Length > 0)
        {
            ProcessCommand(commandJson);
        }

        if (phase == "preparing")
        {
            if (PlayerCanBeControlled() && !TargetScriptActive())
            {
                stableTicks++;
                if (stableTicks >= StableTicksRequired &&
                    SendEnvelope("missionPrepared", null))
                {
                    phase = "prepared";
                    Log("Stable free roam confirmed; mission may launch");
                }
            }
            else
            {
                stableTicks = 0;
            }
        }

        if (phase == "launchPending")
        {
            if (TargetScriptActive())
            {
                missionObservedActive = true;
                launchIssued = true;
                phase = "starting";
                Log("Attached to Three Leaf Clover started by GTA IV's mission manager");
            }
            else if (PlayerCanBeControlled())
            {
                stableTicks++;
                if (stableTicks >= StableTicksRequired && MoveToNativeMissionStart())
                {
                    phase = "waitingForNativeStart";
                }
                else if (stableTicks >= StableTicksRequired)
                {
                    nativeStartTicksRemaining--;
                    if (nativeStartTicksRemaining <= 0)
                    {
                        SendEnvelope("bridgeError", "native-Packie-blip-not-available");
                        phase = "released";
                        Log("GTA IV did not expose its Person_Packie blip; verify SGTA406 and suitable clothing");
                    }
                }
            }
            else
            {
                stableTicks = 0;
            }
        }

        // GTA IV owns its death/failure screen and phone checkpoint retry.
        // Never move the player or start Packie3 while that native flow is
        // active. Doing so is what made the older direct-launch bridge freeze
        // on the death frame. Simply reattach when GTA IV starts Packie3.
        if (phase == "failureReported" || phase == "awaitingNativeRetry")
        {
            if (TargetScriptActive())
                AttachToNativeRetry();
        }

        if (phase == "waitingForNativeStart")
        {
            if (TargetScriptActive())
            {
                missionObservedActive = true;
                launchIssued = true;
                phase = "starting";
                Log("GTA IV mission manager started " + MissionId);
            }
            else
            {
                nativeStartTicksRemaining--;
                if (nativeStartTicksRemaining <= 0)
                {
                    SendEnvelope("bridgeError", "native-mission-marker-not-available");
                    phase = "released";
                    Log("Native mission marker did not start Three Leaf Clover; verify SGTA406 and suitable clothing");
                }
            }
        }

        if (phase == "starting")
        {
            if (TargetScriptActive())
            {
                missionObservedActive = true;
                launchTicksRemaining = LaunchObservationTicks;
                bool controlNow = PlayerCanBeControlled();
                if (!controlNow)
                    controlWasBlockedAfterLaunch = true;

                // Packie3 performs its suit/smart-shoes gate before the
                // opening cutscene. Merely seeing controllable free roam here
                // is not mission gameplay. Wait until the cutscene has taken
                // control at least once and then returned it to Niko.
                if (controlWasBlockedAfterLaunch && controlNow &&
                    SendEnvelope("playerControlGained", null))
                {
                    phase = "active";
                    controlReported = true;
                    Log("Player control confirmed in " + MissionId);
                }
            }
            else if (missionObservedActive)
            {
                endedTicks++;
                if (endedTicks >= EndedConfirmationTicks)
                {
                    SendEnvelope("bridgeError", "mission-prerequisite-rejected-suit-required");
                    phase = "released";
                    Log("Mission exited before its opening cutscene; equip a suit, tie and smart shoes");
                }
            }
            else if (launchIssued)
            {
                launchTicksRemaining--;
                if (launchTicksRemaining <= 0)
                {
                    SendEnvelope("bridgeError", "mission-script-did-not-start");
                    phase = "released";
                }
            }
        }

        if (phase == "active")
        {
            bool active = TargetScriptActive();
            bool controlNow = active && PlayerCanBeControlled();

            if (controlReported && !controlNow)
            {
                if (SendEnvelope("playerControlLost", null))
                    controlReported = false;
            }
            else if (!controlReported && controlNow)
            {
                if (SendEnvelope("playerControlGained", null))
                    controlReported = true;
            }

            ObserveCompletionEvidence();
            endedTicks = missionObservedActive && !active ? endedTicks + 1 : 0;
            // A clean 100% save can already be at the money ceiling. The fresh
            // native success cue plus a confirmed end of Packie3 is independent
            // positive completion evidence; script exit alone remains failure.
            if ((rewardObserved && (completionCueObserved || !active)) ||
                (completionCueObserved && endedTicks >= EndedConfirmationTicks))
            {
                if (SendEnvelope("missionCompleted", null))
                {
                    phase = "completedAwaitCommand";
                    Log("Completion confirmed for " + MissionId);
                }
            }
            else if (missionObservedActive && !active)
            {
                if (endedTicks >= EndedConfirmationTicks &&
                    SendEnvelope("missionFailed", "mission-ended-without-completion"))
                {
                    phase = "failureReported";
                    Game.DisplayText(
                        "Mission failed - use GTA IV's phone Retry.",
                        8000);
                    Log("Target script ended without completion evidence; waiting for GTA IV native phone Retry");
                }
            }
            else
            {
                endedTicks = 0;
            }
        }

        if (phase == "released" && releaseFadeRequested && !releaseFadeStarted)
        {
            releaseFadeStarted = true;
            Function.Call("DO_SCREEN_FADE_OUT", 500);
        }
    }

    private void ProcessCommand(string json)
    {
        int protocol = GetJsonInt(json, "protocol");
        string type = GetJsonString(json, "type");
        string game = GetJsonString(json, "game");
        string commandInstance = GetJsonString(json, "bridgeSessionId");
        string commandRun = GetJsonString(json, "runId");
        string commandMission = GetJsonString(json, "missionId");

        if (protocol != 1 || game != GameId || commandInstance != instanceId ||
            type.Length == 0)
        {
            SendEnvelope("bridgeError", "invalid-controller-command");
            phase = "released";
            return;
        }

        if (type == "prepareMission")
        {
            if (commandMission != MissionId)
            {
                SendEnvelope("bridgeError", "unknown-mission");
                phase = "released";
                return;
            }
            runId = commandRun;
            activeMissionId = commandMission;
            phase = "preparing";
            ResetAttemptEvidence();
        }
        else if (type == "startMission" || type == "restartMission")
        {
            if (commandRun != runId || commandMission != activeMissionId)
            {
                SendEnvelope("bridgeError", "start-context-mismatch");
                phase = "released";
                return;
            }
            ResetAttemptEvidence();
            if (type == "restartMission")
            {
                phase = "awaitingNativeRetry";
                Game.DisplayText(
                    "Use GTA IV's phone Retry to continue.",
                    8000);
                Log("Controller acknowledged failure; waiting for GTA IV native phone Retry");
            }
            else
            {
                phase = "launchPending";
                Log("Native mission-marker launch requested");
            }
        }
        else if (type == "releaseBridge")
        {
            releaseFadeRequested = GetJsonString(json, "reason") != "done";
            phase = "released";
        }
        else if (type == "abortRun")
        {
            releaseFadeRequested = false;
            phase = "released";
        }
        else if (type == "ping")
        {
            SendEnvelope("heartbeat", null);
        }
        else
        {
            SendEnvelope("bridgeError", "unsupported-command");
            phase = "released";
        }
    }

    private bool MoveToNativeMissionStart()
    {
        if (Player.Character == null || !Player.Character.Exists())
            return false;

        // Three Leaf Clover's native marker is available from 06:00 to 19:00.
        // Set a safe midday value only once. Older builds repeated this every
        // 100 ms while waiting for the marker, which visibly fought GTA IV's
        // lighting/weather updates.
        if (!nativeTimePrepared)
        {
            Function.Call("SET_TIME_OF_DAY", NativeMissionStartHour, 0);
            nativeTimePrepared = true;
            Log("Set mission-compatible time to 12:00 once");
        }

        Vector3 start;
        if (!TryFindPackieBlip(out start))
        {
            if (!packieBlipWaitLogged)
            {
                packieBlipWaitLogged = true;
                Log("Waiting for GTA IV's live Person_Packie blip");
            }
            return false;
        }

        Game.FadeScreenOut(500, true);
        Player.Character.Position = start;
        World.LoadEnvironmentNow(start);
        Game.FadeScreenIn(500);

        launchIssued = true;
        launchTicksRemaining = LaunchObservationTicks;
        nativeStartTicksRemaining = NativeStartTimeoutTicks;
        stableTicks = 0;
        Log("Moved Niko to live Packie blip at " +
            FormatVector(start) + "; waiting for Packie3");
        return true;
    }

    private bool TryFindPackieBlip(out Vector3 position)
    {
        position = new Vector3();

        // ScriptHookDotNet exposes GTA IV's live radar contacts. Searching by
        // icon makes this independent of guessed map coordinates and save-slot
        // variations. The screenshot-confirmed green P uses Person_Packie.
        for (int rawType = 1; rawType <= 8; rawType++)
        {
            Blip[] blips = Blip.GetAllBlipsOfType((BlipType)rawType);
            for (int index = 0; index < blips.Length; index++)
            {
                Blip blip = blips[index];
                if (blip != null && blip.Icon == BlipIcon.Person_Packie)
                {
                    position = blip.Position;
                    return true;
                }
            }
        }

        // ScriptHookDotNet v1.7.1.9 has a known compatibility bug in some
        // builds: GetAllBlipsOfType compares the requested numeric value with
        // the blip ICON rather than its TYPE. The v0.1.15 diagnostics proved
        // that behaviour on this installation (request 6 returned icon 6 and
        // request 7 returned icon 7). Querying 42, Person_Packie's icon ID,
        // therefore finds the game's live green P marker on affected builds.
        // Fixed ScriptHookDotNet builds simply return no match or reject the
        // out-of-range type, so keep the normal search above and guard this
        // compatibility path.
        try
        {
            Blip[] compatibilityBlips = Blip.GetAllBlipsOfType(
                (BlipType)(int)BlipIcon.Person_Packie);
            for (int index = 0; index < compatibilityBlips.Length; index++)
            {
                Blip blip = compatibilityBlips[index];
                if (blip != null && blip.Icon == BlipIcon.Person_Packie)
                {
                    position = blip.Position;
                    Log("Found Person_Packie through ScriptHookDotNet icon/type compatibility fallback");
                    return true;
                }
            }
        }
        catch (Exception exception)
        {
            if (!compatibilityFallbackErrorLogged)
            {
                compatibilityFallbackErrorLogged = true;
                Log("Person_Packie compatibility fallback unavailable: " +
                    exception.GetType().Name);
            }
        }

        blipScanAttempts++;
        if (blipScanAttempts == 1 ||
            blipScanAttempts == 50 ||
            blipScanAttempts == 200)
        {
            LogVisibleBlips(blipScanAttempts);
        }

        return false;
    }

    private void LogVisibleBlips(int attempt)
    {
        int total = 0;
        Log("Visible-blip diagnostic scan " + attempt + " begins");
        for (int rawType = 1; rawType <= 8; rawType++)
        {
            Blip[] blips;
            try
            {
                blips = Blip.GetAllBlipsOfType((BlipType)rawType);
            }
            catch (Exception exception)
            {
                Log("Blip type " + rawType + " scan failed: " +
                    exception.GetType().Name);
                continue;
            }

            for (int index = 0; index < blips.Length && total < 128; index++)
            {
                try
                {
                    Blip blip = blips[index];
                    if (blip == null) continue;
                    total++;
                    Log("Blip type=" + rawType +
                        " icon=" + ((int)blip.Icon).ToString(
                            CultureInfo.InvariantCulture) +
                        " position=" + FormatVector(blip.Position));
                }
                catch (Exception exception)
                {
                    Log("Blip type " + rawType + " entry failed: " +
                        exception.GetType().Name);
                }
            }
        }
        Log("Visible-blip diagnostic scan " + attempt +
            " ended; count=" + total);
    }

    private void AttachToNativeRetry()
    {
        missionObservedActive = true;
        launchIssued = true;
        endedTicks = 0;
        phase = "active";

        bool controlNow = PlayerCanBeControlled();
        if (controlNow && SendEnvelope("playerControlGained", null))
            controlReported = true;

        Log("Attached safely to Three Leaf Clover restarted by GTA IV");
    }

    private static string FormatVector(Vector3 value)
    {
        return "(" +
            value.X.ToString("0.000", CultureInfo.InvariantCulture) + ", " +
            value.Y.ToString("0.000", CultureInfo.InvariantCulture) + ", " +
            value.Z.ToString("0.000", CultureInfo.InvariantCulture) + ")";
    }

    private bool TargetScriptActive()
    {
        return Function.Call<int>(
            "GET_NUMBER_OF_INSTANCES_OF_STREAMED_SCRIPT", ScriptName) > 0;
    }

    private bool PlayerCanBeControlled()
    {
        return Function.Call<bool>("IS_PLAYER_PLAYING", 0) &&
            Function.Call<bool>("CAN_PLAYER_START_MISSION", 0) &&
            Function.Call<bool>("IS_PLAYER_CONTROL_ON", 0) &&
            !Function.Call<bool>("IS_PAUSE_MENU_ACTIVE");
    }

    private void ObserveCompletionEvidence()
    {
        int currentMoney = ReadPlayerMoney();
        int increase = currentMoney - previousMoney;
        if (increase >= ExpectedReward)
        {
            rewardObserved = true;
            Log("Completion reward observed; increase=" + increase);
        }
        previousMoney = currentMoney;
        bool cuePlaying = Function.Call<bool>("IS_MISSION_COMPLETE_PLAYING");
        if (cuePlaying && !completionCueWasPlaying && !completionCueObserved)
        {
            completionCueObserved = true;
            Log("Fresh native mission-complete audio cue observed");
        }
        completionCueWasPlaying = cuePlaying;
    }

    private void ResetAttemptEvidence()
    {
        stableTicks = 0;
        launchTicksRemaining = LaunchObservationTicks;
        nativeStartTicksRemaining = NativeStartTimeoutTicks;
        endedTicks = 0;
        previousMoney = ReadPlayerMoney();
        launchIssued = false;
        missionObservedActive = false;
        controlWasBlockedAfterLaunch = false;
        rewardObserved = false;
        completionCueObserved = false;
        completionCueWasPlaying = Function.Call<bool>("IS_MISSION_COMPLETE_PLAYING");
        controlReported = false;
        releaseFadeRequested = false;
        releaseFadeStarted = false;
        packieBlipWaitLogged = false;
        nativeTimePrepared = false;
        compatibilityFallbackErrorLogged = false;
        blipScanAttempts = 0;
    }

    private void ResetMissionState()
    {
        phase = "idle";
        runId = "";
        activeMissionId = "";
        ResetAttemptEvidence();
    }

    private int ReadPlayerMoney()
    {
        try
        {
            return Player.Money;
        }
        catch
        {
            // ScriptHookDotNet can construct this script before a player exists
            // at the title screen. The first live tick will establish the real
            // baseline before completion evidence is evaluated.
            return 0;
        }
    }

    private bool RefreshTransport()
    {
        Dictionary<string, string> controller = ReadIni(controllerPath);
        string nextSession = Get(controller, "session");
        long pulse;
        if (nextSession.Length == 0 ||
            !long.TryParse(Get(controller, "pulse"), out pulse) ||
            Math.Abs(NowMilliseconds() - pulse) > 5000)
            return session.Length > 0 && NowMilliseconds() - lastHealthyPulse <= 2000;

        lastHealthyPulse = NowMilliseconds();

        if (nextSession != session)
        {
            session = nextSession;
            instanceId = Guid.NewGuid().ToString("N");
            sentSequence = 0;
            receivedSequence = 0;
            eventId = 0;
            publishedMessage = "";
            outgoing.Clear();
            WriteGameFile();
            readySent = false;
            ResetMissionState();
        }

        int acknowledged;
        if (outgoing.Count > 0 &&
            int.TryParse(Get(controller, "ack"), out acknowledged) &&
            acknowledged == sentSequence)
        {
            // Keep both the queue head and the previous committed frame until
            // the replacement file has actually been written. A transient
            // sharing violation must not drop an event or skip its sequence.
            string message = outgoing.Peek();
            WriteGameFile(message, sentSequence + 1);
            sentSequence++;
            publishedMessage = message;
            outgoing.Dequeue();
            Log("Published event sequence=" + sentSequence +
                " type=" + GetJsonString(message, "type"));
        }
        return true;
    }

    private string PollCommand()
    {
        Dictionary<string, string> controller = ReadIni(controllerPath);
        int sequence;
        int length;
        if (!int.TryParse(Get(controller, "sequence"), out sequence) ||
            sequence <= receivedSequence ||
            !int.TryParse(Get(controller, "length"), out length))
            return "";

        string message = Get(controller, "part0") +
            Get(controller, "part1") + Get(controller, "part2");
        if (length < 2 || length > 254 || message.Length != length ||
            Get(controller, "checksum") != Checksum(message) ||
            !message.EndsWith("}", StringComparison.Ordinal))
            return "";

        int previousReceivedSequence = receivedSequence;
        receivedSequence = sequence;
        try { WriteGameFile(); }
        catch
        {
            // Retry the command if its acknowledgement could not be written.
            receivedSequence = previousReceivedSequence;
            throw;
        }
        return message;
    }

    private bool SendEnvelope(string type, string reason)
    {
        if (!connected || outgoing.Count >= 32)
            return false;

        eventId++;
        StringBuilder json = new StringBuilder();
        json.Append("{\"protocol\":1,\"type\":\"").Append(Escape(type));
        json.Append("\",\"messageId\":\"b").Append(eventId);
        json.Append("\",\"game\":\"").Append(GameId);
        json.Append("\",\"bridgeSessionId\":\"").Append(instanceId).Append("\"");
        if (runId.Length > 0)
            json.Append(",\"runId\":\"").Append(Escape(runId)).Append("\"");
        if (activeMissionId.Length > 0)
            json.Append(",\"missionId\":\"").Append(Escape(activeMissionId)).Append("\"");
        if (type == "bridgeReady")
        {
            json.Append(",\"bridgeVersion\":\"0.1.17-retained-event-delivery\"");
            json.Append(",\"executableVersion\":\"1.2.0.59\"");
        }
        if (reason != null)
            json.Append(",\"reason\":\"").Append(Escape(reason)).Append("\"");
        json.Append("}");

        string encoded = json.ToString();
        if (Encoding.UTF8.GetByteCount(encoded) > 254)
        {
            Log("Refused oversized event " + type);
            return false;
        }
        outgoing.Enqueue(encoded);
        Log("Queued event type=" + type + "; waitingSequence=" + sentSequence);
        return true;
    }

    private void WriteGameFile()
    {
        // Command acknowledgements must preserve the unacknowledged event.
        WriteGameFile(publishedMessage, sentSequence);
    }

    private void WriteGameFile(string message, int sequence)
    {
        string[] parts = Split(message);
        StringBuilder content = new StringBuilder();
        content.Append("[GHMR]\r\n");
        content.Append("session=").Append(session).Append("\r\n");
        content.Append("sequence=").Append(sequence).Append("\r\n");
        content.Append("ack=").Append(receivedSequence).Append("\r\n");
        content.Append("eventId=").Append(eventId).Append("\r\n");
        content.Append("instance=").Append(instanceId).Append("\r\n");
        content.Append("part0=").Append(parts[0]).Append("\r\n");
        content.Append("part1=").Append(parts[1]).Append("\r\n");
        content.Append("part2=").Append(parts[2]).Append("\r\n");
        byte[] bytes = new UTF8Encoding(false).GetBytes(content.ToString());
        using (FileStream stream = new FileStream(
            gamePath, FileMode.Create, FileAccess.Write,
            FileShare.ReadWrite | FileShare.Delete))
        {
            stream.Write(bytes, 0, bytes.Length);
        }
    }

    private static Dictionary<string, string> ReadIni(string path)
    {
        Dictionary<string, string> result =
            new Dictionary<string, string>(StringComparer.Ordinal);
        if (!File.Exists(path)) return result;
        try
        {
            using (FileStream stream = new FileStream(
                path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete))
            using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    int equal = line.IndexOf('=');
                    if (equal > 0)
                        result[line.Substring(0, equal)] =
                            line.Substring(equal + 1).Trim();
                }
            }
        }
        catch (IOException) { }
        return result;
    }

    private static string Get(Dictionary<string, string> values, string key)
    {
        string value;
        return values.TryGetValue(key, out value) ? value : "";
    }

    private static string[] Split(string value)
    {
        string[] parts = new string[] { "", "", "" };
        for (int offset = 0; offset < value.Length; offset += 100)
        {
            int index = offset / 100;
            parts[index] = value.Substring(
                offset, Math.Min(100, value.Length - offset));
        }
        return parts;
    }

    private static string Checksum(string value)
    {
        unchecked
        {
            uint hash = 2166136261u;
            for (int index = 0; index < value.Length; index++)
            {
                hash ^= value[index];
                hash *= 16777619u;
            }
            return hash.ToString("x8", CultureInfo.InvariantCulture);
        }
    }

    private static string GetJsonString(string json, string key)
    {
        string marker = "\"" + key + "\":\"";
        int start = json.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0) return "";
        start += marker.Length;
        int end = json.IndexOf('"', start);
        return end < 0 ? "" : json.Substring(start, end - start);
    }

    private static int GetJsonInt(string json, string key)
    {
        string marker = "\"" + key + "\":";
        int start = json.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0) return 0;
        start += marker.Length;
        int end = start;
        while (end < json.Length && char.IsDigit(json[end])) end++;
        int value;
        return int.TryParse(json.Substring(start, end - start), out value)
            ? value : 0;
    }

    private static string Escape(string value)
    {
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    private static long NowMilliseconds()
    {
        return (long)(DateTime.UtcNow -
            new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;
    }

    private void Log(string message)
    {
        try
        {
            File.AppendAllText(
                logPath,
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) +
                " [GHMR] " + message + Environment.NewLine,
                new UTF8Encoding(false));
        }
        catch { }
    }
}
