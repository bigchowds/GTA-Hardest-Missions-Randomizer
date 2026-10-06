# San Andreas to GTA III Handoff Test

This is the first real cross-game validation route. It is intentionally fixed
and is not a competitive randomizer run:

1. **Wrong Side of the Tracks** in San Andreas: Definitive Edition;
2. **Espresso-2-Go!** in GTA III: Definitive Edition.

The fixed order makes a failure reproducible. It proves mission launch,
completion evidence, fade, safe process exit, next-game launch and the second
bridge handshake before the fifteen-mission shuffle is used in game.

## Safety rules

- Back up each game's save folder before the first run. Use a disposable copy
  of a clean story-complete free-roam baseline, not the only copy of a personal
  save. San Andreas bridge v0.1.4 supports a literal 100.00% baseline by
  observing `Missions Passed` when completion percentage is capped; see
  `SAVE-BASELINE.md`.
- GHMR requests the game's ordinary window-close action. It has no force-kill
  code. If the game does not close within 20 seconds, the controller stops the
  automatic handoff and asks the player to close and launch manually.
- The Rockstar Games Launcher may remain open between games.
- Administrator access is needed only if Windows protects a game folder during
  bridge installation. The controller should be reopened normally afterward.
- Selected executable paths remain in
  `%LOCALAPPDATA%\GHMR\controller\game-launch-profiles.json`. They are not put
  in run audit logs or release archives.

## Prepare a CI release build

Push the source to GitHub and let the included Windows workflow produce
`GHMR-controller-win-x64.zip`. That package contains the controller, the
readable CLEO file transport and both readable adapter scripts. It does not
include GHMR's formerly blocked native `.cleo` plugin.

Do not attempt the game test from a source-only archive without building the
controller on Windows first.

## Configure both games

1. Exit both games completely.
2. Start `GHMR.Controller.exe` from the CI release package.
3. Select **Setup Games & Bridges**.
4. Select **GTA San Andreas: Definitive Edition**, browse to
   `Gameface\Binaries\Win64\SanAndreas.exe`, then select
   **Install/Repair Bridge**.
5. Select **GTA III: Definitive Edition**, browse to
   `Gameface\Binaries\Win64\LibertyCity.exe`, then select
   **Install/Repair Bridge**.
6. Close the setup window.

Repairing the GTA III bridge also removes GHMR's obsolete
`ghmr_gta3_outcome_probe.js`; the controller bridge now owns that telemetry.

## Isolate GTA III during development

Select **Test GTA III Only** to validate the repaired INI transport and
Espresso-2-Go! without replaying the already-proven San Andreas mission. This
button is a development aid and is not part of the competitive run UI.

Bridge v0.1.4 deliberately does not create a vehicle, weapon, marker or other
gameplay object. Normal mode preserves Espresso-2-Go!'s original mission setup;
use or steal a vehicle exactly as in ordinary play. A clean, story-complete
free-roam baseline keeps every island available without changing the mission.
After Claude is controllable in stable free roam, the bridge waits three
seconds before launching the mission. A load, menu or active mission resets
that stability window.

The installer verifies CLEO Redux x64 and its IniFiles64 extension, combines the
readable INI transport with the game script, and installs the resulting
`ghmr_*_bridge[fs].js`. It replaces GHMR's old command definitions and removes
only GHMR's previous blocked plugin and script. It does not change unrelated
CLEO extensions. Files used for controller communication stay in the current
user's `%LOCALAPPDATA%\GHMR\controller\ipc` directory.

## Run the test

1. Select **Start SA → GTA III Test**. Do not launch either game first.
2. The controller launches San Andreas and starts **Wrong Side of the Tracks**.
3. Complete the mission normally. Skipping its cutscene is allowed.
4. Expected development handoff: San Andreas fades, closes normally, and GTA
   III launches. The release UI will cover this process with a full-screen GHMR
   transition curtain; the current raw process switch is not the final visual.
5. GTA III starts **Espresso-2-Go!**. Nearby stalls appear as they are
   discovered; the game does not reveal all nine after the first stall.
6. For the isolated v0.1.4 verification, fail once and choose the normal
   checkpoint Retry. GTA III may briefly return Claude to free roam while CLEO
   reloads. The recovery and stability checks overlap; about three seconds after
   Claude becomes controllable, confirm that the bridge launches Espresso-2-Go!
   from the beginning again, without a permanent black screen or reroll.
7. Complete the retried mission normally.
8. Expected finish: GTA III fades and closes normally; the controller displays
   `All 2 missions complete.`

For this first cross-game run, completing both missions once is enough. Failure
and Retry remain part of the adapter tests, but there is no value in repeatedly
retesting them before the handoff itself is proven.

## What counts as a pass

- neither mission needs to be selected from an in-game marker;
- no future mission is shown before the preceding mission completes;
- San Andreas is no longer running when GTA III launches;
- GTA III receives the same controller run and starts only Espresso-2-Go!;
- the final counter is `2 / 2` with no bridge error or manual reroll;
- no save corruption or unexpected game process termination occurs.

Keep the controller's local audit file and both `cleo_redux.log` files after the
test. They are diagnostic evidence and should be reviewed for personal paths or
account information before being shared publicly.

## Current verification boundary

The controller handoff, fixed plan and both adapters pass automated simulation.
Wrong Side of the Tracks completed, the controller accepted its evidence,
faded the game and advanced to GTA III. The revised shared-write transport then
started Espresso-2-Go! on the installed game. That test exposed two v0.1.1
issues now covered by regression tests: an `ONMISSION` clear was mistaken for
failure, and any bridge error faded the still-running game permanently. A later
story-complete-baseline test reached all nine targets, then showed that Retry
returned the internally launched mission to free roam without restarting it.
v0.1.4 checks frame length/checksum before acknowledging an in-place INI
command, does not add gameplay objects, shortens the initial stable-free-roam
window to three seconds, and explicitly relaunches the same locked index after
failure. That relaunch and final completion still require in-game confirmation.

The GTA III adapter uses mission indices `72`, `73` and `79`. `ONMISSION` is
not accepted as success or failure: completion requires the selected mission's
full scripted reward jump, while death/arrest is explicit failure evidence.
The mission list and reward behavior were checked against
the public converted mission sources in
[GTA-III-SCM-Converted](https://github.com/Lighnat0r-pers/GTA-III-SCM-Converted),
while the installed Definitive Edition remains the final authority.
