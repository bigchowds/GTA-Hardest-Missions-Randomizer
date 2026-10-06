// GHMR read-only Derailed lifecycle probe v3.1 for GTA V Enhanced.
//
// This probe observes GTA V's mission flag and selected Rockstar script
// threads. It does not start, stop, fail, complete or modify a mission; it
// does not teleport the player, alter a save, or write game memory.

using System;
using System.Collections.Generic;
using System.IO;
using GTA;
using GTA.Native;
using GTA.UI;

public sealed class GhmrGtavDerailedProbeV3 : Script
{
    private const int HeartbeatIntervalMilliseconds = 5000;

    // GTA V's internal story-mission script for Derailed is "exile3".
    // The remaining scripts coordinate the game's own replay, failure and
    // result screens. Watching all of them lets the test distinguish native
    // Retry from the genuine completion path without version-specific memory
    // offsets.
    private static readonly string[] ObservedScripts =
    {
        "exile3",
        "mission_repeat_controller",
        "replay_controller",
        "mission_stat_watcher",
        "mission_stat_alerter",
        "pausemenu_sp_repeat",
        "flow_controller"
    };

    private readonly string _logPath;
    private readonly Dictionary<string, int> _previousScriptCounts =
        new Dictionary<string, int>(StringComparer.Ordinal);
    private bool _loggingAvailable;
    private bool _initialized;
    private bool _faulted;
    private bool _previousMissionActive;
    private bool _previousPaused;
    private bool _previousCutsceneActive;
    private bool _previousPlayerDead;
    private bool _readyMessageShown;
    private DateTime _nextHeartbeatUtc;

    public GhmrGtavDerailedProbeV3()
    {
        Interval = 250;

        string logDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GHMR");
        _logPath = Path.Combine(logDirectory, "ghmr_gtav_derailed_probe.log");

        try
        {
            Directory.CreateDirectory(logDirectory);
            File.AppendAllText(_logPath, string.Empty);
            _loggingAvailable = true;
        }
        catch
        {
            _loggingAvailable = false;
        }

        WriteLine("[GHMR] ----- new Derailed probe instance -----");
        WriteLine("[GHMR] GTA V Enhanced Derailed lifecycle probe v3.1 started");
        WriteLine("[GHMR] SHVDN API assembly: " + typeof(Script).Assembly.GetName().Version);
        WriteLine("[GHMR] Game version: " + Game.Version);
        WriteLine("[GHMR] Read-only mode; replay Derailed from GTA V's pause menu");

        Screen.ShowSubtitle("GHMR Derailed probe v3.1 loaded", 5000);

        Tick += OnTick;
        Aborted += OnAborted;
    }

    private void OnTick(object sender, EventArgs eventArgs)
    {
        if (_faulted)
        {
            return;
        }

        try
        {
            bool missionActive = Game.IsMissionActive;
            bool paused = Game.IsPaused;
            bool cutsceneActive = Game.IsCutsceneActive;
            bool playerDead = IsPlayerDead();
            Dictionary<string, int> counts = ReadScriptCounts();

            if (!_initialized)
            {
                _previousMissionActive = missionActive;
                _previousPaused = paused;
                _previousCutsceneActive = cutsceneActive;
                _previousPlayerDead = playerDead;
                CopyCounts(counts);
                _nextHeartbeatUtc = DateTime.UtcNow.AddMilliseconds(
                    HeartbeatIntervalMilliseconds);
                _initialized = true;

                WriteLine("[GHMR] Initial state: " + FormatState(
                    missionActive,
                    paused,
                    cutsceneActive,
                    playerDead,
                    counts));
                return;
            }

            // The constructor may run behind GTA V's loading screen. Show the
            // useful confirmation once the player is actually in free roam.
            if (!_readyMessageShown &&
                !missionActive &&
                !paused &&
                !cutsceneActive)
            {
                _readyMessageShown = true;
                WriteLine("[GHMR] Free roam ready; replay Derailed now");
                Screen.ShowSubtitle(
                    "GHMR ready - Pause > Game > Replay Mission > Derailed",
                    7000);
            }

            if (missionActive != _previousMissionActive)
            {
                WriteLine(
                    "[GHMR] MISSION FLAG: " + _previousMissionActive +
                    " -> " + missionActive);
                _previousMissionActive = missionActive;
            }

            if (paused != _previousPaused)
            {
                WriteLine(
                    "[GHMR] PAUSE: " + _previousPaused + " -> " + paused);
                _previousPaused = paused;
            }

            if (cutsceneActive != _previousCutsceneActive)
            {
                WriteLine(
                    "[GHMR] CUTSCENE: " + _previousCutsceneActive +
                    " -> " + cutsceneActive);
                _previousCutsceneActive = cutsceneActive;
            }

            if (playerDead != _previousPlayerDead)
            {
                WriteLine(
                    "[GHMR] PLAYER DEAD: " + _previousPlayerDead +
                    " -> " + playerDead);
                _previousPlayerDead = playerDead;
            }

            foreach (string scriptName in ObservedScripts)
            {
                int previous = _previousScriptCounts[scriptName];
                int current = counts[scriptName];
                if (previous == current)
                {
                    continue;
                }

                WriteLine(
                    "[GHMR] SCRIPT " + scriptName + ": " +
                    previous + " -> " + current);
                _previousScriptCounts[scriptName] = current;

                if (string.Equals(
                    scriptName,
                    "exile3",
                    StringComparison.Ordinal) &&
                    previous == 0 &&
                    current > 0)
                {
                    Screen.ShowSubtitle("GHMR detected Derailed", 3000);
                }
            }

            if (DateTime.UtcNow >= _nextHeartbeatUtc)
            {
                WriteLine("[GHMR] HEARTBEAT: " + FormatState(
                    missionActive,
                    paused,
                    cutsceneActive,
                    playerDead,
                    counts));
                _nextHeartbeatUtc = DateTime.UtcNow.AddMilliseconds(
                    HeartbeatIntervalMilliseconds);
            }
        }
        catch (Exception exception)
        {
            _faulted = true;
            WriteLine(
                "[GHMR] ERROR: probe stopped after " +
                exception.GetType().FullName + ": " + exception.Message);
            Screen.ShowSubtitle("GHMR probe error - keep the log", 5000);
        }
    }

    private static Dictionary<string, int> ReadScriptCounts()
    {
        Dictionary<string, int> result =
            new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (string scriptName in ObservedScripts)
        {
            result[scriptName] = Function.Call<int>(
                Hash.GET_NUMBER_OF_THREADS_RUNNING_THE_SCRIPT_WITH_THIS_HASH,
                unchecked((uint)Game.GenerateHash(scriptName)));
        }

        return result;
    }

    private static bool IsPlayerDead()
    {
        try
        {
            Ped character = Game.Player.Character;
            return character != null && character.Exists() && character.IsDead;
        }
        catch
        {
            // GTA can tick scripts briefly before the Story Mode player exists.
            return false;
        }
    }

    private void CopyCounts(Dictionary<string, int> counts)
    {
        foreach (string scriptName in ObservedScripts)
        {
            _previousScriptCounts[scriptName] = counts[scriptName];
        }
    }

    private static string FormatState(
        bool missionActive,
        bool paused,
        bool cutsceneActive,
        bool playerDead,
        Dictionary<string, int> counts)
    {
        return
            "missionActive=" + missionActive +
            " paused=" + paused +
            " cutscene=" + cutsceneActive +
            " dead=" + playerDead +
            " scripts={" + FormatCounts(counts) + "}";
    }

    private static string FormatCounts(
        Dictionary<string, int> counts)
    {
        string[] values = new string[ObservedScripts.Length];
        for (int index = 0; index < ObservedScripts.Length; index++)
        {
            string scriptName = ObservedScripts[index];
            values[index] = scriptName + "=" + counts[scriptName];
        }

        return string.Join(",", values);
    }

    private void OnAborted(object sender, EventArgs eventArgs)
    {
        WriteLine("[GHMR] GTA V Enhanced Derailed lifecycle probe stopped");
    }

    private void WriteLine(string message)
    {
        if (!_loggingAvailable)
        {
            return;
        }

        try
        {
            File.AppendAllText(
                _logPath,
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") +
                " " + message + Environment.NewLine);
        }
        catch
        {
            _loggingAvailable = false;
        }
    }
}
