// GHMR GTA V Enhanced bridge v0.1.13 (foreground restore confirmation)
//
// ScriptHookVDotNet compiles this readable source at game startup.  This
// bridge deliberately lets GTA V's own replay controller start and retry
// Derailed. It observes public game/native state and exchanges bounded local
// INI messages with GHMR. For the initial replay only, it validates GTA V's
// live mission catalog and submits the same guarded mission-repeat request that
// the built-in Replay Mission menu submits. GTA V pauses normal
// ScriptHookVDotNet ticks while its restore-point Alert is open, so a
// short-lived CLR timer sends one physical Enter key down/up pair through
// Windows after the validated request. The key is emitted only when this GTA
// process owns the foreground window. It never starts exile3 directly,
// teleports the player, changes a save, or uses networking.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using GTA;
using GTA.Native;
using GTA.UI;

public sealed class GhmrGtavEnhancedBridgeV0113 : Script
{
    private const string GameId = "gtav_enhanced";
    private const string MissionId = "gtav.derailed";
    private const string MissionScript = "exile3";
    private const string ReplayControllerScript = "replay_controller";
    private const string MissionRepeatControllerScript = "mission_repeat_controller";
    private const string MissionWatcherScript = "mission_stat_watcher";
    private const int MissionCatalogBase = 93274;
    // GTA script globals encode an array's element count at the named base.
    // Global_93274[0] therefore begins at global 93275, not global 93274.
    private const int MissionCatalogFirstElementOffset = 1;
    private const int MissionCatalogEntryCount = 94;
    private const int MissionCatalogEntrySize = 34;
    private const int MissionCatalogScriptHashOffset = 6;
    private const int MissionRepeatSelectionIndex = 80609;
    private const int MissionRepeatSelectionType = 80610;
    private const int MissionRepeatFlags = 80611;
    private const int MainMissionReplayType = 1;
    private const uint WindowsInputKeyboard = 1;
    private const ushort MainEnterScanCode = 0x1C;
    private const uint KeyEventScanCode = 0x0008;
    private const uint KeyEventKeyUp = 0x0002;
    private const int RestoreConfirmInitialDelayMilliseconds = 1200;
    private const int RestoreConfirmFocusRetryMilliseconds = 500;
    private const int RestoreConfirmMaxFocusAttempts = 20;
    private const int StableTicksRequired = 8;
    private const int EndedConfirmationTicks = 40;
    private const int CompletionConfirmationTicks = 2;
    private const int InstructionRepeatTicks = 120;
    private const int NativeReplayLaunchTimeoutTicks = 480;
    private const int BridgeTickInterval = 250;

    private readonly string _ipcDirectory;
    private readonly string _controllerPath;
    private readonly string _gamePath;
    private readonly string _logPath;
    private readonly Queue<string> _outgoing = new Queue<string>();
    private readonly object _logSync = new object();
    private readonly object _restoreConfirmSync = new object();

    private string _session = "";
    private string _instanceId = "";
    private string _publishedMessage = "";
    private int _sentSequence;
    private int _receivedSequence;
    private int _eventId;
    private bool _connected;
    private bool _readySent;

    private string _phase = "idle";
    private string _runId = "";
    private string _activeMissionId = "";
    private int _stableTicks;
    private int _endedTicks;
    private int _completionTicks;
    private int _instructionTicks;
    private bool _nativeReplayRequested;
    private bool _missionRepeatControllerObserved;
    private int _nativeReplayWaitTicks;
    private System.Threading.Timer _restoreConfirmTimer;
    private int _restoreConfirmGeneration;
    private int _restoreConfirmFocusAttempts;
    private int _requestedReplayCatalogIndex = -1;
    private int _previousReplaySelectionIndex = -1;
    private int _previousReplaySelectionType;
    private bool _missionObserved;
    private bool _watcherObserved;
    private bool _gameplayObserved;
    private bool _controlReported;
    private bool _failureReported;
    private bool _previousCutscene;
    private bool _sawCutsceneAfterGameplay;
    private DateTime _lastPostGameplayCutsceneEndedUtc = DateTime.MinValue;
    public GhmrGtavEnhancedBridgeV0113()
    {
        Interval = BridgeTickInterval;
        _ipcDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GHMR", "controller", "ipc");
        _controllerPath = Path.Combine(
            _ipcDirectory, GameId + "-controller.ini");
        _gamePath = Path.Combine(_ipcDirectory, GameId + "-game.ini");
        _logPath = Path.Combine(_ipcDirectory, GameId + "-bridge.log");
        Directory.CreateDirectory(_ipcDirectory);
        Log("GTA V Enhanced bridge v0.1.13 foreground restore confirmation loading");
        Tick += OnTick;
        Aborted += OnAborted;
    }

    private void OnTick(object sender, EventArgs eventArgs)
    {
        try
        {
            TickBridge();
        }
        catch (Exception exception)
        {
            Log("Unhandled bridge error: " + exception);
            if (_connected && _phase != "released")
            {
                SendEnvelope("bridgeError", "gtav-bridge-fault");
                _phase = "released";
            }
        }
    }

    private void TickBridge()
    {
        if (!RefreshTransport())
        {
            if (_connected)
            {
                Log("Controller pulse temporarily unavailable; preserving mission state");
            }
            _connected = false;
            return;
        }

        if (!_connected)
        {
            _connected = true;
            Log("Controller transport connected");
        }

        if (!_readySent)
        {
            _readySent = SendEnvelope("bridgeReady", null);
        }

        string commandJson;
        while ((commandJson = PollCommand()).Length > 0)
        {
            ProcessCommand(commandJson);
        }

        if (_phase == "preparing")
        {
            if (StableFreeRoam())
            {
                _stableTicks++;
                if (_stableTicks >= StableTicksRequired &&
                    SendEnvelope("missionPrepared", null))
                {
                    _phase = "prepared";
                    Log("Stable Story Mode free roam confirmed; Derailed may be replayed");
                }
            }
            else
            {
                _stableTicks = 0;
            }
        }

        if (_phase == "awaitingReplay")
        {
            if (TargetMissionActive())
            {
                AttachToMission("Detected Derailed started by GTA V's replay controller");
            }
            else if (_nativeReplayRequested)
            {
                _nativeReplayWaitTicks++;
                if (!_missionRepeatControllerObserved &&
                    ScriptCount(MissionRepeatControllerScript) > 0)
                {
                    _missionRepeatControllerObserved = true;
                    CancelRestorePointConfirmation();
                    Log("Rockstar mission_repeat_controller accepted the Derailed request");
                }

                _instructionTicks++;
                if (_instructionTicks == 1 ||
                    _instructionTicks >= InstructionRepeatTicks)
                {
                    _instructionTicks = 1;
                    Screen.ShowSubtitle(
                        "GHMR asked GTA V to prepare Derailed",
                        8000);
                }

                if (_nativeReplayWaitTicks >= NativeReplayLaunchTimeoutTicks)
                {
                    string reason = _missionRepeatControllerObserved
                        ? "gtav-native-replay-mission-timeout"
                        : "gtav-native-replay-controller-timeout";
                    CancelUnclaimedNativeReplayRequest();
                    ResetNativeReplayRequest();
                    SendEnvelope("bridgeError", reason);
                    _phase = "released";
                    Log("Native Derailed replay timed out: " + reason);
                }
            }
        }

        // GTA V owns its death/failure screens and native Retry flow.  The
        // controller acknowledges the failure, but this bridge never starts or
        // cleans exile3 while GTA V may still be unwinding the failed attempt.
        if (_phase == "awaitingNativeRetry")
        {
            if (TargetMissionActive() && !PlayerDead() &&
                (Game.IsCutsceneActive || PlayerCanPlay()))
            {
                AttachToMission("Attached safely to Derailed restarted by GTA V");
            }
        }

        if (_phase == "active")
        {
            ObserveActiveMission();
        }
    }

    private bool TryRequestNativeMissionReplay(out string failureReason)
    {
        failureReason = "";
        if (!StableFreeRoam())
        {
            failureReason = "gtav-not-ready-for-native-replay";
            return false;
        }

        int missionHash = Game.GenerateHash(MissionScript);
        int catalogIndex = -1;
        int matches = 0;
        for (int index = 0; index < MissionCatalogEntryCount; index++)
        {
            int hashIndex = MissionCatalogBase +
                MissionCatalogFirstElementOffset +
                (index * MissionCatalogEntrySize) +
                MissionCatalogScriptHashOffset;
            if (GlobalVariable.Get(hashIndex).Read<int>() == missionHash)
            {
                catalogIndex = index;
                matches++;
            }
        }

        // The live catalog is a build sentinel as well as a lookup. Never
        // touch replay state when the expected Enhanced layout is absent or
        // ambiguous.
        if (matches != 1)
        {
            failureReason = "gtav-derailed-catalog-not-found";
            Log(
                "Refused native replay request: exile3 catalog matches=" +
                matches);
            return false;
        }

        GlobalVariable repeatFlags = GlobalVariable.Get(MissionRepeatFlags);
        if (repeatFlags.IsBitSet(0) || repeatFlags.IsBitSet(2) ||
            ScriptCount(MissionRepeatControllerScript) > 0)
        {
            failureReason = "gtav-mission-repeat-busy";
            return false;
        }

        GlobalVariable selectionIndex =
            GlobalVariable.Get(MissionRepeatSelectionIndex);
        GlobalVariable selectionType =
            GlobalVariable.Get(MissionRepeatSelectionType);
        int previousIndex = selectionIndex.Read<int>();
        int previousType = selectionType.Read<int>();
        if (previousIndex < -1 || previousIndex >= MissionCatalogEntryCount ||
            (previousType != 0 && previousType != 1 && previousType != 7))
        {
            failureReason = "gtav-native-replay-layout-mismatch";
            Log(
                "Refused native replay request: implausible selection state " +
                previousIndex + "/" + previousType);
            return false;
        }

        try
        {
            selectionIndex.Write<int>(catalogIndex);
            selectionType.Write<int>(MainMissionReplayType);
            if (selectionIndex.Read<int>() != catalogIndex ||
                selectionType.Read<int>() != MainMissionReplayType)
            {
                selectionIndex.Write<int>(previousIndex);
                selectionType.Write<int>(previousType);
                failureReason = "gtav-native-replay-selection-rejected";
                return false;
            }

            repeatFlags.SetBit(0);
            if (!repeatFlags.IsBitSet(0))
            {
                selectionIndex.Write<int>(previousIndex);
                selectionType.Write<int>(previousType);
                failureReason = "gtav-native-replay-request-rejected";
                return false;
            }
        }
        catch (Exception exception)
        {
            try
            {
                if (repeatFlags.IsBitSet(0))
                {
                    repeatFlags.ClearBit(0);
                }
                selectionIndex.Write<int>(previousIndex);
                selectionType.Write<int>(previousType);
            }
            catch
            {
            }
            failureReason = "gtav-native-replay-write-failed";
            Log("Native replay request write failed: " + exception.Message);
            return false;
        }

        _previousReplaySelectionIndex = previousIndex;
        _previousReplaySelectionType = previousType;
        _requestedReplayCatalogIndex = catalogIndex;
        _nativeReplayRequested = true;
        _missionRepeatControllerObserved = false;
        _nativeReplayWaitTicks = 0;
        _instructionTicks = 0;
        ScheduleRestorePointConfirmation();
        Screen.ShowSubtitle(
            "GHMR requested Derailed through GTA V Mission Replay",
            8000);
        Log(
            "Requested Rockstar mission replay for exile3; catalog index=" +
            catalogIndex);
        return true;
    }

    private void CancelUnclaimedNativeReplayRequest()
    {
        if (!_nativeReplayRequested || _missionRepeatControllerObserved ||
            ScriptCount(MissionRepeatControllerScript) > 0)
        {
            return;
        }

        try
        {
            GlobalVariable repeatFlags = GlobalVariable.Get(MissionRepeatFlags);
            GlobalVariable selectionIndex =
                GlobalVariable.Get(MissionRepeatSelectionIndex);
            GlobalVariable selectionType =
                GlobalVariable.Get(MissionRepeatSelectionType);
            if (repeatFlags.IsBitSet(0) && !repeatFlags.IsBitSet(2) &&
                selectionIndex.Read<int>() == _requestedReplayCatalogIndex &&
                selectionType.Read<int>() == MainMissionReplayType)
            {
                repeatFlags.ClearBit(0);
                selectionIndex.Write<int>(_previousReplaySelectionIndex);
                selectionType.Write<int>(_previousReplaySelectionType);
                Log("Cancelled an unclaimed native Derailed replay request");
            }
        }
        catch (Exception exception)
        {
            Log("Could not cancel native replay request safely: " + exception.Message);
        }
    }

    private void ResetNativeReplayRequest()
    {
        CancelRestorePointConfirmation();
        _nativeReplayRequested = false;
        _missionRepeatControllerObserved = false;
        _nativeReplayWaitTicks = 0;
        _requestedReplayCatalogIndex = -1;
        _previousReplaySelectionIndex = -1;
        _previousReplaySelectionType = 0;
    }

    private void ScheduleRestorePointConfirmation()
    {
        int generation;
        lock (_restoreConfirmSync)
        {
            if (_restoreConfirmTimer != null)
            {
                _restoreConfirmTimer.Dispose();
                _restoreConfirmTimer = null;
            }
            _restoreConfirmGeneration++;
            generation = _restoreConfirmGeneration;
            _restoreConfirmFocusAttempts = 0;
            _restoreConfirmTimer = new System.Threading.Timer(
                RestorePointConfirmationTimer,
                generation,
                RestoreConfirmInitialDelayMilliseconds,
                RestoreConfirmFocusRetryMilliseconds);
        }
        Log(
            "Scheduled one foreground Windows Enter for the Derailed " +
            "restore-point confirmation");
    }

    private void RestorePointConfirmationTimer(object state)
    {
        int generation = (int)state;
        string resultLog = null;

        lock (_restoreConfirmSync)
        {
            if (_restoreConfirmTimer == null ||
                generation != _restoreConfirmGeneration)
            {
                return;
            }

            _restoreConfirmFocusAttempts++;
            IntPtr foregroundWindow = GetForegroundWindow();
            uint foregroundProcessId;
            bool gtaOwnsForeground = foregroundWindow != IntPtr.Zero &&
                GetWindowThreadProcessId(
                    foregroundWindow,
                    out foregroundProcessId) != 0 &&
                foregroundProcessId == GetCurrentProcessId();

            if (!gtaOwnsForeground)
            {
                if (_restoreConfirmFocusAttempts >=
                    RestoreConfirmMaxFocusAttempts)
                {
                    _restoreConfirmTimer.Dispose();
                    _restoreConfirmTimer = null;
                    resultLog =
                        "Did not send restore confirmation because GTA V " +
                        "did not own the foreground window";
                }
            }
            else
            {
                WindowsInput[] inputs = BuildMainEnterInput();
                uint inserted = SendInput(
                    (uint)inputs.Length,
                    inputs,
                    Marshal.SizeOf(typeof(WindowsInput)));
                int error = inserted == (uint)inputs.Length
                    ? 0
                    : Marshal.GetLastWin32Error();

                if (inserted == (uint)inputs.Length ||
                    _restoreConfirmFocusAttempts >=
                        RestoreConfirmMaxFocusAttempts)
                {
                    _restoreConfirmTimer.Dispose();
                    _restoreConfirmTimer = null;
                }

                if (inserted == (uint)inputs.Length)
                {
                    resultLog =
                        "Sent foreground Windows Enter for the Derailed " +
                        "restore-point confirmation; events=" + inserted +
                        ", focusAttempt=" + _restoreConfirmFocusAttempts;
                }
                else if (_restoreConfirmTimer == null)
                {
                    resultLog =
                        "Windows Enter confirmation failed; events=" +
                        inserted + ", error=" + error;
                }
            }
        }

        if (resultLog != null)
        {
            Log(resultLog);
        }
    }

    private void CancelRestorePointConfirmation()
    {
        lock (_restoreConfirmSync)
        {
            _restoreConfirmGeneration++;
            if (_restoreConfirmTimer != null)
            {
                _restoreConfirmTimer.Dispose();
                _restoreConfirmTimer = null;
            }
            _restoreConfirmFocusAttempts = 0;
        }
    }

    private static WindowsInput[] BuildMainEnterInput()
    {
        WindowsInput[] inputs = new WindowsInput[2];
        inputs[0].Type = WindowsInputKeyboard;
        inputs[0].Data.Keyboard.ScanCode = MainEnterScanCode;
        inputs[0].Data.Keyboard.Flags = KeyEventScanCode;
        inputs[1].Type = WindowsInputKeyboard;
        inputs[1].Data.Keyboard.ScanCode = MainEnterScanCode;
        inputs[1].Data.Keyboard.Flags = KeyEventScanCode | KeyEventKeyUp;
        return inputs;
    }

#pragma warning disable CS0649
    [StructLayout(LayoutKind.Sequential)]
    private struct WindowsInput
    {
        public uint Type;
        public WindowsInputUnion Data;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct WindowsInputUnion
    {
        [FieldOffset(0)]
        public WindowsMouseInput Mouse;

        [FieldOffset(0)]
        public WindowsKeyboardInput Keyboard;

        [FieldOffset(0)]
        public WindowsHardwareInput Hardware;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowsMouseInput
    {
        public int X;
        public int Y;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowsKeyboardInput
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowsHardwareInput
    {
        public uint Message;
        public ushort ParameterLow;
        public ushort ParameterHigh;
    }
#pragma warning restore CS0649

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(
        uint numberOfInputs,
        [In] WindowsInput[] inputs,
        int sizeOfInput);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(
        IntPtr window,
        out uint processId);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentProcessId();

    private void ObserveActiveMission()
    {
        bool targetActive = TargetMissionActive();
        bool cutscene = Game.IsCutsceneActive;
        bool playerDead = PlayerDead();
        bool controlNow = targetActive && PlayerCanPlay();

        if (_controlReported && !controlNow)
        {
            if (SendEnvelope("playerControlLost", null))
            {
                _controlReported = false;
            }
        }
        else if (!_controlReported && controlNow)
        {
            if (SendEnvelope("playerControlGained", null))
            {
                _controlReported = true;
                _gameplayObserved = true;
                Log("Player control confirmed in " + MissionId);
            }
        }

        if (_gameplayObserved && cutscene && !_previousCutscene)
        {
            _sawCutsceneAfterGameplay = true;
            Log("Post-gameplay cutscene observed");
        }
        if (_sawCutsceneAfterGameplay && !cutscene && _previousCutscene)
        {
            _lastPostGameplayCutsceneEndedUtc = DateTime.UtcNow;
            Log("Post-gameplay cutscene ended");
        }
        _previousCutscene = cutscene;

        if (ScriptCount(MissionWatcherScript) > 0)
        {
            _watcherObserved = true;
        }

        if (playerDead && _gameplayObserved && !_failureReported)
        {
            ReportFailure("player-died-native-retry");
            return;
        }

        if (CompletionSignaturePresent(targetActive, playerDead))
        {
            _completionTicks++;
            if (_completionTicks >= CompletionConfirmationTicks &&
                SendEnvelope("missionCompleted", null))
            {
                _phase = "completedAwaitCommand";
                Log("Derailed completion confirmed from final cutscene and native cleanup");
            }
            return;
        }
        _completionTicks = 0;

        if (_missionObserved && !targetActive)
        {
            _endedTicks++;
            if (_endedTicks >= EndedConfirmationTicks && !_failureReported)
            {
                ReportFailure("mission-ended-without-completion");
            }
        }
        else
        {
            _endedTicks = 0;
        }
    }

    private bool CompletionSignaturePresent(bool targetActive, bool playerDead)
    {
        if (targetActive || playerDead || Game.IsMissionActive ||
            ScriptCount(ReplayControllerScript) > 0 ||
            ScriptCount(MissionRepeatControllerScript) > 0 ||
            ScriptCount(MissionWatcherScript) > 0 ||
            !_missionObserved || !_watcherObserved ||
            !_gameplayObserved || !_sawCutsceneAfterGameplay ||
            _lastPostGameplayCutsceneEndedUtc == DateTime.MinValue)
        {
            return false;
        }

        TimeSpan sinceCutscene =
            DateTime.UtcNow - _lastPostGameplayCutsceneEndedUtc;
        return sinceCutscene >= TimeSpan.Zero &&
            sinceCutscene <= TimeSpan.FromSeconds(60);
    }

    private void ReportFailure(string reason)
    {
        if (SendEnvelope("missionFailed", reason))
        {
            _failureReported = true;
            _phase = "failureReported";
            Screen.ShowSubtitle(
                "Mission failed - use GTA V's native Retry option",
                8000);
            Log("Failure reported; waiting for GTA V native Retry: " + reason);
        }
    }

    private void AttachToMission(string logMessage)
    {
        ResetNativeReplayRequest();
        ResetAttemptEvidence();
        _missionObserved = true;
        _watcherObserved = ScriptCount(MissionWatcherScript) > 0;
        _previousCutscene = Game.IsCutsceneActive;
        _phase = "active";
        Log(logMessage);

        if (PlayerCanPlay() && SendEnvelope("playerControlGained", null))
        {
            _controlReported = true;
            _gameplayObserved = true;
            Log("Player control confirmed in " + MissionId);
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

        if (protocol != 1 || game != GameId ||
            commandInstance != _instanceId || type.Length == 0)
        {
            CancelUnclaimedNativeReplayRequest();
            ResetNativeReplayRequest();
            SendEnvelope("bridgeError", "invalid-controller-command");
            _phase = "released";
            return;
        }

        if (type == "prepareMission")
        {
            if (commandMission != MissionId)
            {
                SendEnvelope("bridgeError", "unknown-mission");
                _phase = "released";
                return;
            }
            _runId = commandRun;
            _activeMissionId = commandMission;
            CancelUnclaimedNativeReplayRequest();
            ResetNativeReplayRequest();
            ResetAttemptEvidence();
            _stableTicks = 0;
            _phase = "preparing";
        }
        else if (type == "startMission" || type == "restartMission")
        {
            if (commandRun != _runId || commandMission != _activeMissionId)
            {
                CancelUnclaimedNativeReplayRequest();
                ResetNativeReplayRequest();
                SendEnvelope("bridgeError", "start-context-mismatch");
                _phase = "released";
                return;
            }

            if (type == "restartMission")
            {
                ResetNativeReplayRequest();
                ResetAttemptEvidence();
                _phase = "awaitingNativeRetry";
                Screen.ShowSubtitle(
                    "Use GTA V's native Retry option to continue Derailed",
                    8000);
                Log("Controller acknowledged failure; waiting for native Retry");
            }
            else
            {
                ResetAttemptEvidence();
                ResetNativeReplayRequest();
                string failureReason;
                if (TryRequestNativeMissionReplay(out failureReason))
                {
                    _phase = "awaitingReplay";
                }
                else
                {
                    SendEnvelope("bridgeError", failureReason);
                    _phase = "released";
                    Log("Native Derailed replay request refused: " + failureReason);
                }
            }
        }
        else if (type == "releaseBridge")
        {
            CancelUnclaimedNativeReplayRequest();
            ResetNativeReplayRequest();
            _phase = "released";
            Log(GetJsonString(json, "reason") == "done"
                ? "Run finished; leaving GTA V open"
                : "Mission accepted; bridge released for game handoff");
        }
        else if (type == "abortRun")
        {
            CancelUnclaimedNativeReplayRequest();
            ResetNativeReplayRequest();
            _phase = "released";
            Log("Run aborted; bridge released without altering GTA V");
        }
        else if (type == "ping")
        {
            SendEnvelope("heartbeat", null);
        }
        else
        {
            CancelUnclaimedNativeReplayRequest();
            ResetNativeReplayRequest();
            SendEnvelope("bridgeError", "unsupported-command");
            _phase = "released";
        }
    }

    private bool StableFreeRoam()
    {
        return !Game.IsMissionActive &&
            !Game.IsPaused &&
            !Game.IsCutsceneActive &&
            !Function.Call<bool>(Hash.NETWORK_IS_GAME_IN_PROGRESS) &&
            ScriptCount(MissionScript) == 0 &&
            ScriptCount(ReplayControllerScript) == 0 &&
            ScriptCount(MissionRepeatControllerScript) == 0 &&
            PlayerExists() &&
            !PlayerDead();
    }

    private bool TargetMissionActive()
    {
        return Game.IsMissionActive && ScriptCount(MissionScript) > 0;
    }

    private bool PlayerCanPlay()
    {
        return PlayerExists() &&
            !PlayerDead() &&
            !Game.IsPaused &&
            !Game.IsCutsceneActive &&
            Function.Call<bool>(Hash.IS_PLAYER_CONTROL_ON, 0);
    }

    private static bool PlayerExists()
    {
        try
        {
            Ped character = Game.Player.Character;
            return character != null && character.Exists();
        }
        catch
        {
            return false;
        }
    }

    private static bool PlayerDead()
    {
        try
        {
            Ped character = Game.Player.Character;
            return character != null && character.Exists() && character.IsDead;
        }
        catch
        {
            return false;
        }
    }

    private static int ScriptCount(string scriptName)
    {
        return Function.Call<int>(
            Hash.GET_NUMBER_OF_THREADS_RUNNING_THE_SCRIPT_WITH_THIS_HASH,
            unchecked((uint)Game.GenerateHash(scriptName)));
    }

    private void ResetAttemptEvidence()
    {
        _endedTicks = 0;
        _completionTicks = 0;
        _instructionTicks = 0;
        _missionObserved = false;
        _watcherObserved = false;
        _gameplayObserved = false;
        _controlReported = false;
        _failureReported = false;
        _previousCutscene = false;
        _sawCutsceneAfterGameplay = false;
        _lastPostGameplayCutsceneEndedUtc = DateTime.MinValue;
    }

    private void ResetMissionState()
    {
        CancelUnclaimedNativeReplayRequest();
        ResetNativeReplayRequest();
        _phase = "idle";
        _runId = "";
        _activeMissionId = "";
        _stableTicks = 0;
        ResetAttemptEvidence();
    }

    private bool RefreshTransport()
    {
        Dictionary<string, string> controller = ReadIni(_controllerPath);
        string nextSession = Get(controller, "session");
        long pulse;
        if (nextSession.Length == 0 ||
            !long.TryParse(Get(controller, "pulse"), out pulse) ||
            Math.Abs(NowMilliseconds() - pulse) > 5000)
        {
            return false;
        }

        if (nextSession != _session)
        {
            _session = nextSession;
            _instanceId = Guid.NewGuid().ToString("N");
            _sentSequence = 0;
            _receivedSequence = 0;
            _eventId = 0;
            _outgoing.Clear();
            _publishedMessage = "";
            WriteGameFile();
            _readySent = false;
            ResetMissionState();
        }

        int acknowledged;
        if (_outgoing.Count > 0 &&
            int.TryParse(Get(controller, "ack"), out acknowledged) &&
            acknowledged == _sentSequence)
        {
            string message = _outgoing.Dequeue();
            _sentSequence++;
            _publishedMessage = message;
            WriteGameFile(message);
        }
        return true;
    }

    private string PollCommand()
    {
        Dictionary<string, string> controller = ReadIni(_controllerPath);
        int sequence;
        int length;
        if (!int.TryParse(Get(controller, "sequence"), out sequence) ||
            sequence <= _receivedSequence ||
            !int.TryParse(Get(controller, "length"), out length))
        {
            return "";
        }

        string message = Get(controller, "part0") +
            Get(controller, "part1") + Get(controller, "part2");
        if (length < 2 || length > 254 || message.Length != length ||
            Get(controller, "checksum") != Checksum(message) ||
            !message.EndsWith("}", StringComparison.Ordinal))
        {
            return "";
        }

        _receivedSequence = sequence;
        WriteGameFile();
        return message;
    }

    private bool SendEnvelope(string type, string reason)
    {
        if (!_connected || _outgoing.Count >= 32)
        {
            return false;
        }

        _eventId++;
        StringBuilder json = new StringBuilder();
        json.Append("{\"protocol\":1,\"type\":\"").Append(Escape(type));
        json.Append("\",\"messageId\":\"b").Append(_eventId);
        json.Append("\",\"game\":\"").Append(GameId);
        json.Append("\",\"bridgeSessionId\":\"").Append(_instanceId).Append("\"");
        if (_runId.Length > 0)
        {
            json.Append(",\"runId\":\"").Append(Escape(_runId)).Append("\"");
        }
        if (_activeMissionId.Length > 0)
        {
            json.Append(",\"missionId\":\"").Append(Escape(_activeMissionId)).Append("\"");
        }
        if (type == "bridgeReady")
        {
            json.Append(",\"bridgeVersion\":\"0.1.13-foreground-restore-confirm\"");
            json.Append(",\"executableVersion\":\"")
                .Append(Escape(Game.Version.ToString())).Append("\"");
        }
        if (reason != null)
        {
            json.Append(",\"reason\":\"").Append(Escape(reason)).Append("\"");
        }
        json.Append("}");

        string encoded = json.ToString();
        if (Encoding.UTF8.GetByteCount(encoded) > 254)
        {
            Log("Refused oversized event " + type);
            return false;
        }
        _outgoing.Enqueue(encoded);
        return true;
    }

    private void WriteGameFile()
    {
        WriteGameFile(_publishedMessage);
    }

    private void WriteGameFile(string message)
    {
        string[] parts = Split(message);
        StringBuilder content = new StringBuilder();
        content.Append("[GHMR]\r\n");
        content.Append("session=").Append(_session).Append("\r\n");
        content.Append("sequence=").Append(_sentSequence).Append("\r\n");
        content.Append("ack=").Append(_receivedSequence).Append("\r\n");
        content.Append("eventId=").Append(_eventId).Append("\r\n");
        content.Append("instance=").Append(_instanceId).Append("\r\n");
        content.Append("length=").Append(message.Length).Append("\r\n");
        content.Append("checksum=").Append(GameChecksum(_sentSequence, message)).Append("\r\n");
        content.Append("part0=").Append(parts[0]).Append("\r\n");
        content.Append("part1=").Append(parts[1]).Append("\r\n");
        content.Append("part2=").Append(parts[2]).Append("\r\n");
        byte[] bytes = new UTF8Encoding(false).GetBytes(content.ToString());
        using (FileStream stream = new FileStream(
            _gamePath, FileMode.Create, FileAccess.Write,
            FileShare.ReadWrite | FileShare.Delete))
        {
            stream.Write(bytes, 0, bytes.Length);
        }
    }

    private static Dictionary<string, string> ReadIni(string path)
    {
        Dictionary<string, string> result =
            new Dictionary<string, string>(StringComparer.Ordinal);
        if (!File.Exists(path))
        {
            return result;
        }

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
                    {
                        result[line.Substring(0, equal)] =
                            line.Substring(equal + 1).Trim();
                    }
                }
            }
        }
        catch (IOException)
        {
        }
        return result;
    }

    private static string Get(
        Dictionary<string, string> values,
        string key)
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

    private static string GameChecksum(int sequence, string value)
    {
        return Checksum(
            sequence.ToString(CultureInfo.InvariantCulture) + ":" + value);
    }

    private static string GetJsonString(string json, string key)
    {
        string marker = "\"" + key + "\":\"";
        int start = json.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0)
        {
            return "";
        }
        start += marker.Length;
        int end = json.IndexOf('"', start);
        return end < 0 ? "" : json.Substring(start, end - start);
    }

    private static int GetJsonInt(string json, string key)
    {
        string marker = "\"" + key + "\":";
        int start = json.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0)
        {
            return 0;
        }
        start += marker.Length;
        int end = start;
        while (end < json.Length && char.IsDigit(json[end]))
        {
            end++;
        }
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

    private void OnAborted(object sender, EventArgs eventArgs)
    {
        CancelRestorePointConfirmation();
        Log("GTA V Enhanced bridge stopped");
    }

    private void Log(string message)
    {
        try
        {
            lock (_logSync)
            {
                File.AppendAllText(
                    _logPath,
                    DateTime.Now.ToString(
                        "yyyy-MM-dd HH:mm:ss",
                        CultureInfo.InvariantCulture) +
                    " [GHMR] " + message + Environment.NewLine,
                    new UTF8Encoding(false));
            }
        }
        catch
        {
        }
    }
}
