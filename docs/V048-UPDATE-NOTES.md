# v0.4.8 bridge-version guard

## Confirmed cause of the stalled San Andreas completion

The v0.4.7 controller log identified the installed San Andreas bridge as
v0.1.1. It accepted `bridgeReady`, started Wrong Side of the Tracks and later
received `playerControlLost`, but it never received `missionCompleted` or
`missionFailed`. The player then stopped the run. The current v0.1.4 bridge is
the adapter that supports completion detection on the designated 100% save.

The repository screenshot explained how the mismatch was created: the v0.4.7
commit updated `src` and `tests`, while `mods` and `.github/workflows` still
showed older commits. GitHub Actions therefore compiled the new controller and
copied the old adapter still present at
`mods/cleo-bridge/ghmr_sa_bridge.js` into the download. The v0.1.4 file supplied
in the source ZIP was correct; it was not committed by the folder upload.

## Guard behavior

Before starting a Beta or diagnostic run, v0.4.8 reads the installed bridge for
every required game and compares its embedded version with the controller's
required version. A mismatch blocks launch, records the evidence in
`GHMR-controller.log`, and opens Setup Game Bridges on the first affected game.
After repair, setup reads the installed copy back and displays the verified
version.

A second check validates the `bridgeVersion` in the live `bridgeReady` event.
If a game loaded an older script despite the file preflight, the controller
aborts before issuing `startMission` and tells the player which version to
repair.

The build now runs `tools/Test-BridgeVersionCompatibility.mjs`. It verifies
that all five source adapters advertise the versions required by the
controller and that the workflow stages those exact source paths.

## Transient INI access

The supplied log also contained one short sharing violation while San Andreas
rewrote its INI frame. The next poll succeeded 87 milliseconds later and the
mission ran, so this was not the completion failure. v0.4.8 keeps the first
brief collision in the diagnostic log and only shows a player-facing bridge
access problem after three consecutive failed polls.

## Validation boundary

The JavaScript source checks, including the new bridge-version check, run in
the source workspace. GitHub Actions performs the .NET 8 Windows compilation
and compiled self-tests because the source workspace has no .NET SDK. A live
San Andreas-only completion followed by the fixed five-game route remains the
final integration validation.
