# GTA V Enhanced controller test

## Test result

The isolated controller test has passed. The bridge detected a death, GHMR
changed to **Restarting** and incremented Failures, GTA V's native **Retry**
resumed the same Derailed replay, and genuine mission completion finished the
one-mission run. A transient empty INI frame observed during an in-place write
was harmless; the controller now retries that frame silently.

Automatic launch has also passed its live test. Bridge v0.1.13 found Derailed
at catalog index `16`, sent one foreground-guarded Windows Enter key pair for
the restore-point Alert, Rockstar's `mission_repeat_controller` accepted the
request, `exile3` started and GHMR received player control. An earlier
intentional death followed by native Retry safely returned the controller to
**Running**.

## What is proven

The read-only Derailed probe captured a complete native replay lifecycle on GTA
V Enhanced: `exile3` and the replay/stat watchers started, the opening cutscene
returned control, the mission survived a pause-menu Retry, the ending cutscene
ran, and GTA V then cleared the mission flag, `exile3`, `replay_controller` and
finally `mission_stat_watcher` in that order.

`ghmr_gtav_bridge.3.cs` turns that evidence into the first controller bridge.
It uses only ScriptHookVDotNet's public API and local files under
`%LOCALAPPDATA%\GHMR\controller\ipc`. It does not directly request/start
`exile3`, force script cleanup, teleport the player, alter time/weather, or
write save statistics.

## Automatic launch boundary

After Story Mode reaches stable free roam, the bridge validates GTA V
Enhanced's live mission catalog, selects Derailed and asks Rockstar's native
mission-repeat controller to launch it. It requires exactly one `exile3`
catalog match and plausible idle replay state before writing anything. It does
not directly start Rockstar's mission script or bypass replay setup.

GTA V displays its native restore-point notice during this handoff. Because the
Alert pauses normal ScriptHookVDotNet ticks, bridge v0.1.13 schedules one CLR
timer callback before requesting the replay. After 1.2 seconds it uses Win32
`SendInput` to emit main-keyboard Enter down/up only if the foreground window
belongs to the current GTA process. It stops after the first complete pair and
never disables the restore point, which remains part of GTA V's safe replay and
world-restoration flow.

## Install and test

1. Build/download the newest `GHMR-controller-win-x64` Actions artifact and
   extract it to a new folder.
2. Start `GHMR.Controller.exe` normally.
3. Open **Setup Games & Bridges** and select **GTA V Enhanced**.
4. Browse to `GTA5_Enhanced.exe`.
5. Select **Install/Repair Bridge**. This copies
   `scripts\ghmr_gtav_bridge.3.cs` and removes only GHMR's superseded GTA V
   probe scripts.
6. Close GTA V if it is already running, then select **Test GTA V Only**.
7. Load the same story-complete save used for the successful lifecycle probe.
8. Wait in free roam. GHMR should automatically request Derailed, dismiss the
   restore-point Alert and attach when Rockstar's native replay controller
   starts `exile3`. Do not press Enter during this check.
9. Play normally. For the failure check, fail once and choose GTA V's native
   **Retry**. GHMR records the failure but never forces cleanup during the
   death/failure frame.
10. Complete the mission. The controller should finish only after the final
    cutscene and native replay/stat cleanup. GTA V remains open after this
    isolated test.

The bridge log is:

`%LOCALAPPDATA%\GHMR\controller\ipc\gtav_enhanced-bridge.log`

## Expected controller states

| Moment | Expected state |
|---|---|
| Free roam and bridge connected | Preparing |
| Restore-point Alert | Preparing; dismissed automatically |
| Native replay request accepted | Preparing; GTA V instruction shown |
| Opening cutscene | Preparing |
| Player receives control | Running |
| Death/failure reported | Restarting; use native Retry |
| Native retry gives control | Running, same locked mission |
| Final cleanup signature | Finished |

Completion requires all of the following: Derailed was observed, gameplay was
observed, a later cutscene occurred and ended, the mission flag and `exile3`
cleared, `replay_controller` cleared, and `mission_stat_watcher` cleared within
the validated final-cutscene window. A simple quit/failure therefore cannot be
accepted as completion.
