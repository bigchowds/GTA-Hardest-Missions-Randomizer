# GTA V Enhanced: Derailed Lifecycle Test

This is the first isolated GTA V mission test. It records GTA V's own
mission, Retry and results-screen scripts while **Derailed** is replayed.

The probe is deliberately read-only. It does not launch or terminate a game
script, teleport a character, change time or weather, alter a save, or write
game memory. The goal is to identify a stable completion signal before the
controller is allowed to advance to another game.

## What is being observed

GTA V identifies Derailed as story mission ID `16` and runs its gameplay in
the `exile3` script. Probe v3 records changes to:

- GTA V's native mission, pause and cutscene flags;
- the player's dead/alive state;
- `exile3`;
- GTA V's own mission-repeat, failure, flow and stat/result scripts.

This should show the difference between a native Retry and a real
Mission Passed result without using build-specific memory addresses.

## Install the probe

1. Close GTA V completely.
2. In Steam, right-click **Grand Theft Auto V Enhanced**, choose
   **Manage**, then **Browse local files**.
3. Open the existing `scripts` folder beside `GTA5_Enhanced.exe`.
4. Remove the old GHMR file `ghmr_gtav_probe.3.cs` if it is still present.
   Do not remove ScriptHookVDotNet, Script Hook V, the ASI loader, or an
   unrelated trainer/mod.
5. Copy `ghmr_gtav_derailed_probe.3.cs` into `scripts`.
6. Delete this old diagnostic log once, if present:

```text
%LOCALAPPDATA%\GHMR\ghmr_gtav_derailed_probe.log
```

Expected relevant layout:

```text
GTA5_Enhanced.exe
ScriptHookV.dll
ScriptHookVDotNet.asi
ScriptHookVDotNet2.dll
ScriptHookVDotNet3.dll
ScriptHookVDotNet.ini
scripts\
  ghmr_gtav_derailed_probe.3.cs
```

## Run one controlled test

1. Launch GTA V Enhanced exactly as usual for modded Story Mode, with
   BattlEye disabled as required for single-player ASI mods.
2. Load the 100% Story Mode save and wait in ordinary free roam.
3. Confirm that **GHMR Derailed probe v3 active** appears briefly.
4. Open **Pause > Game > Replay Mission > Derailed** and start it normally.
5. Once gameplay begins, deliberately fail once by normal gameplay or gunfire.
6. Select GTA V's own **Retry** option and confirm that Derailed restarts.
7. Complete Derailed normally. Do not use a trainer's mission-complete command
   during this diagnostic run.
8. When the Mission Passed/results screen appears, leave it visible for at
   least ten seconds before continuing.
9. After returning to free roam, wait another ten seconds, then exit normally.

Send back both files:

```text
%LOCALAPPDATA%\GHMR\ghmr_gtav_derailed_probe.log
<GTA V Enhanced folder>\ScriptHookVDotNet.log
```

## What counts as success

The test passes when the log shows:

1. `exile3` starting;
2. the failed attempt entering GTA V's native Retry flow;
3. `exile3` starting again without a new random mission;
4. a distinct results/completion sequence after the successful attempt;
5. a clean return to free roam.

Once that sequence is known, it can become the GTA V controller bridge's
accepted completion evidence. Until then, a simple `missionActive=True ->
False` transition is not trusted because manually quitting a replay can
produce the same transition.

