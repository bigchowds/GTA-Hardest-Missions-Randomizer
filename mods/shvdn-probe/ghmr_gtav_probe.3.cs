// GHMR read-only compatibility probe v2 for GTA V Enhanced.
// It observes the native mission and pause flags and writes a local diagnostic log.
// It does not start, finish, fail or modify missions, saves, entities or game memory.

using System;
using System.IO;
using GTA;
using GTA.UI;

public sealed class GhmrGtavProbe : Script
{
    private const int HeartbeatIntervalMilliseconds = 5000;

    private readonly string _logPath;
    private bool _loggingAvailable;
    private bool _initialized;
    private bool _faulted;
    private bool _previousMissionActive;
    private bool _previousPaused;
    private DateTime _nextHeartbeatUtc;

    public GhmrGtavProbe()
    {
        Interval = 250;

        string logDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GHMR"
        );
        _logPath = Path.Combine(logDirectory, "ghmr_gtav_probe.log");

        try
        {
            Directory.CreateDirectory(logDirectory);
            // Append so GTA/SHVDN script-domain reloads cannot erase the
            // mission states recorded by the previous probe instance.
            File.AppendAllText(_logPath, string.Empty);
            _loggingAvailable = true;
        }
        catch
        {
            _loggingAvailable = false;
        }

        WriteLine("[GHMR] ----- new probe instance -----");
        WriteLine("[GHMR] GTA V Enhanced compatibility probe v2 started");
        WriteLine("[GHMR] SHVDN API assembly: " + typeof(Script).Assembly.GetName().Version);
        WriteLine("[GHMR] Game version: " + Game.Version);
        WriteLine("[GHMR] Ready; enter free roam, then start a replay mission");

        Screen.ShowSubtitle("GHMR GTA V probe active", 5000);

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

            if (!_initialized)
            {
                _previousMissionActive = missionActive;
                _previousPaused = paused;
                _nextHeartbeatUtc = DateTime.UtcNow.AddMilliseconds(
                    HeartbeatIntervalMilliseconds
                );
                _initialized = true;

                WriteLine(
                    "[GHMR] Initial state: missionActive=" + missionActive +
                    " paused=" + paused
                );
                return;
            }

            if (missionActive != _previousMissionActive)
            {
                WriteLine(
                    "[GHMR] MISSION STATE: " + _previousMissionActive +
                    " -> " + missionActive
                );
                _previousMissionActive = missionActive;
            }

            if (paused != _previousPaused)
            {
                WriteLine(
                    "[GHMR] PAUSE STATE: " + _previousPaused +
                    " -> " + paused
                );
                _previousPaused = paused;
            }

            if (DateTime.UtcNow >= _nextHeartbeatUtc)
            {
                WriteLine(
                    "[GHMR] HEARTBEAT: missionActive=" + missionActive +
                    " paused=" + paused
                );
                _nextHeartbeatUtc = DateTime.UtcNow.AddMilliseconds(
                    HeartbeatIntervalMilliseconds
                );
            }
        }
        catch (Exception exception)
        {
            _faulted = true;
            WriteLine(
                "[GHMR] ERROR: probe stopped after " +
                exception.GetType().FullName + ": " + exception.Message
            );
        }
    }

    private void OnAborted(object sender, EventArgs eventArgs)
    {
        WriteLine("[GHMR] GTA V Enhanced compatibility probe stopped");
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
                DateTime.Now.ToString("HH:mm:ss") + " " + message + Environment.NewLine
            );
        }
        catch
        {
            _loggingAvailable = false;
        }
    }
}
