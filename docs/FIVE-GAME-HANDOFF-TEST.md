# Five-game handoff test

## Purpose

This is the first deterministic end-to-end integration route. It proves that
one controller run can accept completion, close the completed game normally,
launch the next game and keep the locked route intact across all five titles.
It remains a repeatable diagnostic route rather than the randomized public
five-mission run.

| Step | Game | Locked mission |
|---:|---|---|
| 1 | GTA San Andreas: Definitive Edition | Wrong Side of the Tracks |
| 2 | GTA III: Definitive Edition | Espresso-2-Go! |
| 3 | GTA Vice City: Definitive Edition | Demolition Man |
| 4 | GTA IV: Complete Edition | Three Leaf Clover |
| 5 | GTA V Enhanced | Derailed |

GTA V remains last in this fixed integration route. Bridge v0.1.13 selects
Derailed through Rockstar's native mission-repeat controller and confirms the
restore-point Alert with one foreground-process-guarded Windows Enter pulse;
no pause-menu navigation or manual confirmation is required.

## Before starting

1. Use the v0.4.8 bridge-guard artifact in a fresh
   extracted folder.
2. Keep the controller open for the complete route.
3. Close all five games before starting.
4. Confirm each game's story-complete test save is still installed.
5. Confirm **Setup Game Bridges** lists all five executable paths. Anyone
   upgrading from v0.3.8 or earlier must repair the three Trilogy bridges.
6. Follow `FIVE-MISSION-BETA-RULES.md`; trainer-assisted attempts are QA only.

## Run it

1. Select **Diagnostics → Test all five (fixed order)**.
2. GHMR launches a Steam-owned Trilogy game through its Steam App ID. A
   Rockstar-owned copy receives Rockstar's required commerce-provider argument.
   The old **Connecting to Social Club** dialog should therefore not appear;
   GHMR's exact-dialog OK click remains armed only as a fallback. If a Trilogy
   title stops at its own landing or save-selection screen, select the
   designated clean save or **Resume** once. This happens before gameplay
   timing. Do not hold or repeatedly press Enter.
3. San Andreas launches. Load the designated story-complete save and let GHMR
   start Wrong Side of the Tracks. Complete it normally.
4. GHMR fades/releases San Andreas, requests a normal close, allows a short
   three-second cleanup window and then launches GTA III through Steam. Load
   its designated save and complete Espresso-2-Go!. The controller should be
   minimized on a one-monitor PC or visible on a different monitor, and GTA III
   must not arrive with its pause menu open.
5. Repeat the same flow for Vice City and complete Demolition Man.
6. GTA IV launches next. Load its designated save. GHMR sets mission-compatible
   time once, moves Niko to Packie's live marker and lets GTA IV start Three
   Leaf Clover natively. Complete it normally.
7. GTA V Enhanced launches last. Load Story Mode and wait in free roam. GHMR
   automatically requests Derailed, confirms Rockstar's restore-point Alert
   and waits for the replay controller to start the mission; complete it
   normally.
8. The controller should show **Finished**, Progress **5 / 5**, and leave GTA V
   open.

On a normal mission failure, use the game's native Retry when offered. GHMR
must keep the same current mission and increment Failures; it must never skip
to the next title because of a death, arrest or mission quit.

## Expected handoff boundary

This checkpoint deliberately uses sequential switching: the completed game
closes and a three-second cleanup window finishes before the next platform
launch request. Steam-owned Trilogy titles use their Steam App IDs; Rockstar-
owned Trilogy titles use Rockstar's commerce-provider argument. The GTA-styled
transition overlay shows the newly selected game and mission together with the
close, launch and bridge stages over rotating creator thumbnails. Transition
music stops at stable destination preparation or its 60-second safety limit and
can be muted from Options. The complete thumbnail is displayed above a separate
compact status strip. The overlay relinquishes top-most status before the
destination needs focus, but remains open until the destination reports playable mission control;
an early process-detected or `bridgeReady` signal does not dismiss it. No game
is force-killed, and no attempt is made to merge, preload or suspend unrelated
game executables in RAM.

The controller's game-process discovery runs independently from its serialized
bridge-event dispatcher. This is important for launcher-owned games: GTA III
may already be in free roam and have sent `bridgeReady` while Rockstar/Steam is
still settling process discovery. That handshake is accepted immediately; it
does not wait behind the launch timeout. Every destination also receives a new
INI transport session immediately before launch, so an old acknowledged frame
cannot suppress its new `bridgeReady` event.

Each adapter starts its local event counter at `b1`. Controller v0.3.7 scopes
duplicate detection to the game and bridge instance, so GTA III's `b1` is not
discarded merely because San Andreas used the same local identifier earlier in
the run. An actual retransmission from the same bridge instance remains safely
deduplicated.

The footer reports a transport connection only for the current game. The
authoritative **Bridge** row changes from **Waiting** to **Verified session
active** after the bridge handshake is accepted.

San Andreas bridge v0.1.4 detects a successful replay on a 100% save through
the engine's `Missions Passed` counter. It does not depend solely on completion
percentage, which is capped at 100. A normal failed attempt advances neither
signal and therefore remains locked to Wrong Side of the Tracks.

If a game does not close within the safety timeout, GHMR stops automatic
switching and tells the player to close it normally. The run is not silently
rerolled.

## Proven before this route

- San Andreas: Wrong Side of the Tracks launch, objective failure, Retry and
  completion on the designated 100% baseline.
- GTA III: Espresso-2-Go! direct launch, full nine-target world state, exact
  $40,000 completion reward and clean controller release on bridge v0.1.5.
- Vice City: Demolition Man launch, Retry and completion.
- GTA IV: Three Leaf Clover native marker launch, death, phone Retry and exact
  completion reward.
- GTA V Enhanced: Derailed automatic replay selection, automatic restore-point
  confirmation, native Retry reattachment and completion signature.
