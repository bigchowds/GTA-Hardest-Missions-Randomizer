# San Andreas DE: Three-Mission Launch Probe

The first controlled launch of **Wrong Side of the Tracks** passed. This
follow-up probe exposes all three selected San Andreas missions while keeping
the original game responsible for mission logic, checkpoints and Retry:

| Key | Mission | Internal entry |
|---|---|---:|
| F6 | Wrong Side of the Tracks | 29 |
| F7 | Supply Lines... | 73 |
| F8 | End of the Line | 110 |

The original script table implements **End of the Line** as three linked
mission scripts, indices `110`, `111` and `112`. GHMR requests only part 1
(`110`); the game must perform the two later transitions itself.

## Save protection

These are real original missions and San Andreas DE can autosave their results.
Before every test, close the game and copy the complete profile-ID folder from:

`%USERPROFILE%\Documents\Rockstar Games\GTA San Andreas Definitive Edition\Profiles`

Keep the copy outside that directory. Rockstar Launcher cloud synchronization
is not a backup because it can synchronize the test autosave.

## Install

1. Exit San Andreas completely.
2. In `Gameface\Binaries\Win64\CLEO`, remove the earlier
   `ghmr_sa_wsott_launch_probe.js` and other GHMR probe scripts.
3. Copy
   `mods\cleo-launch-probe\ghmr_sa_three_mission_launch_probe.js` into `CLEO`.
4. Leave `.config`, `CLEO_PLUGINS`, `tsconfig.json` and generated files alone.

Only one mission should be tested per full game launch. Restart the game and
restore the backed-up save before testing another mission.

## Next test: Supply Lines...

1. Launch the game, load the disposable early-game save and reach free roam.
2. Press **F7 once**.
3. Confirm that the RC plane, Zero, targets, fuel display and objectives all
   appear correctly.
4. Deliberately fail once, select **Retry**, then complete the retried mission.
5. Wait at least five seconds after the mission-passed sequence.
6. Exit and send the `[GHMR]` lines from `cleo_redux.log`, plus any visual or
   gameplay problem observed.

The probe waits two seconds before treating an already-active mission as a
Retry recovery. This avoids confusing San Andreas' short `ONMISSION=true`
startup state with a real mission. After a Retry reload, it records a fresh
progress baseline so a later completion can still produce `OUTCOME PASS`.

## Sources for the provisional entries

- Sanny Builder's documented mission table identifies **Supply Lines...** as
  `ZERO2`, mission index `73`:
  <https://gtaforums.com/topic/982449-solved-add-functional-taxi-police-cars-etc-without-replacing-existing-vehicles/>
- The same original table identifies **End of the Line** parts 1–3 as indices
  `110`–`112`:
  <https://www.speedrun.com/gtasa/forums/26xh3>

The two new entries remain provisional until they pass on the supported
Definitive Edition build.
