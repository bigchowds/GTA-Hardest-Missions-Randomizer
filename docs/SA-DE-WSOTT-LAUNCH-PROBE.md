# San Andreas DE: Wrong Side of the Tracks Launch Probe

This is GHMR's first controlled-launch test. It uses the game's supported
`LOAD_AND_LAUNCH_MISSION_INTERNAL` command to request original mission index
`29`, **Wrong Side of the Tracks**. The probe contains no Rockstar mission
script or game asset.

This is not yet the full randomizer. It answers one narrow question: can the
supported San Andreas DE build start this original mission directly, then
retain normal failure/retry and completion behaviour?

## Important save precaution

Use a disposable save and back up the full profile first. San Andreas DE can
autosave after a mission, so merely avoiding a manual save is not enough.

1. Close the game.
2. Paste this into File Explorer's address bar:
   `%USERPROFILE%\Documents\Rockstar Games\GTA San Andreas Definitive Edition\Profiles`
3. Copy the profile-ID folder inside it to a separate backup folder, such as
   one on the Desktop.
4. Keep that backup until the test is finished and the original save has been
   restored or confirmed intact. Rockstar Launcher cloud-save sync can also
   copy the test autosave, so do not treat the cloud copy as the backup.

Completing the mission can change story-progress variables in the currently
loaded game. **Do not make a manual save during or after this test.** Exit to
Windows when the test is done and restore/reload the backed-up state.

The script never writes a save by itself. The warning exists because it starts
a real original mission, whose own success logic changes the live game state.

## Install the probe

1. Exit San Andreas completely.
2. Open `Gameface\Binaries\Win64\CLEO` in the San Andreas DE installation.
3. Remove earlier GHMR probe `.js` files from that folder.
4. Copy `mods\cleo-launch-probe\ghmr_sa_wsott_launch_probe.js` into `CLEO`.
5. Leave `.config`, `CLEO_PLUGINS`, `tsconfig.json` and generated files alone.

This probe needs no network access and does not use the CLEO filesystem
permission. Only the normal CLEO Redux installation is required.

## Run the test

1. Launch San Andreas using the same method that loaded CLEO Redux `1.5.0`.
2. Load the disposable save and reach ordinary free roam with CJ controllable.
3. Press **F6 once**. The probe refuses to launch while another mission is
   active or while the player cannot start a mission.
4. Confirm that **Wrong Side of the Tracks** starts. If it starts correctly,
   deliberately fail once and choose the game's **Retry** option.
5. Complete the retried mission.
6. Wait in free roam for at least five seconds.
7. Exit the game **without saving**.
8. Open `Gameface\Binaries\Win64\cleo_redux.log` and send back the lines that
   begin with `[GHMR]`. Also describe anything visually wrong with the mission
   start, actors, bike, train, objectives, retry or ending.

Expected launch evidence:

```text
[GHMR] SA Wrong Side of the Tracks launch probe started
[GHMR] LAUNCH REQUEST: missionIndex=29 baselineProgress=...
[GHMR] LAUNCH COMMAND ACCEPTED
```

A successful completion should eventually add:

```text
[GHMR] OUTCOME PASS: progress advanced by ...
```

If F6 is rejected, the log explains the guard that blocked it. If the game
closes or the mission setup is incomplete, send the full `cleo_redux.log`; do
not repeatedly retry the launch in the same session.

## Evidence behind the command

- CLEO Redux documents that supported games expose their native commands to
  JavaScript, including calls through `native(...)`:
  <https://re.cleo.li/docs/en/api.html>
- The current Sanny Builder command definitions for `sa_unreal` define opcode
  `0417`, `LOAD_AND_LAUNCH_MISSION_INTERNAL`, as loading a mission from the
  list in the `main.scm` header:
  <https://github.com/sannybuilder/library/blob/master/sa_unreal/sa_unreal.json>
- A documented inspection of the original mission table identifies **Wrong
  Side of the Tracks** as mission `29` (`SMOKE3`):
  <https://sannybuilder.com/forums/viewtopic.php?id=285>

Mission index `29` was treated as provisional until this exact Definitive
Edition build passed the launch test recorded below.

## Test result — 23 September 2026

The controlled launch passed on San Andreas DE `1.0.112.6680` with CLEO Redux
`1.5.0` x64:

- F6 requested mission index `29`, and CLEO accepted the launch command.
- `ONMISSION` changed from false to true and **Wrong Side of the Tracks** began
  with the expected actors, bike, train and objectives.
- Letting the targets get too far away produced the normal failure and Retry
  prompt.
- Choosing Retry restarted the CLEO JavaScript runtime, reloaded the probe and
  resumed the same mission without visual or gameplay problems.
- The retried mission completed normally and displayed its expected ending.

The original probe's in-memory `launchedByProbe` value was cleared by the CLEO
runtime restart, so it could not label the later completion as `OUTCOME PASS`.
That is a probe-state limitation, not a mission failure. The follow-up
three-mission probe observes an already-active mission after a runtime reload
and takes a fresh progress baseline.
