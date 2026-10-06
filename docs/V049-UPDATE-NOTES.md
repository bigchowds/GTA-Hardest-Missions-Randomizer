# v0.4.9: verified event delivery

This is a targeted reliability update, not a public Beta release.

## Evidence and scope

The latest GTA III logs show Espresso launching while GHMR remains Preparing.
The captured game INI has eventId=5 but still publishes missionPrepared at
sequence=2, already acknowledged by the controller. This matches a reproduced
transport defect: unchecked native writes can discard an event and advance the
sender's in-memory sequence even though the event never reached the file.
The exact native write failure trigger in that live run is not established.

## Changes

- Keep the outgoing queue head until invalidation, payload and commit read-back
  are verified. Retry at the same sequence on subsequent polls.
- Accept a controller acknowledgement of a pending commit when only its
  read-back failed. Preserve queue order through sustained failures.
- Log one write-failure notice and one recovery notice in the CLEO log.
- Read INI files with FileShare.ReadWrite and FileShare.Delete so a controller
  read does not deny simultaneous game-side writes.
- Reject Trilogy installations missing the updated shared transport marker,
  even if their mission-bridge versions match.

No mission adapter logic, process handoff delay, input handling, transition UI
or audio behaviour was changed. The separate GTA III startup crash is not
claimed fixed by this patch.

## Install and test

For the source-update zip, upload src, tests, tools, docs and README.md to the
existing repository root. Upload ghmr_ini_transport.js directly inside
mods/cleo-bridge so a skipped folder cannot leave the old transport behind.
Upload build.yml directly inside .github/workflows last, then use the Actions
run for that final commit. Do not put either loose file in the repository root.
The update archive contains these files at their correct repository paths.

1. Close GHMR and all GTA games. Keep the previous controller folder available.
2. Build the updated source through GitHub Actions. The artifact is named
   GHMR-controller-v0.4.9-win-x64 and contains
   GHMR-v0.4.9-event-delivery-fix-win-x64.zip. Extract the inner zip into a fresh
   folder; do not run the controller from inside either zip.
3. Confirm Options shows GHMR v0.4.9 event delivery fix.
4. Use Install/Repair Bridges for San Andreas DE, GTA III DE and Vice City DE.
   The shared script is embedded in each installed bridge, so replacing only
   the controller executable is insufficient. IV/V adapters are unchanged.
5. Test GTA III alone first. Once Espresso becomes playable, GHMR should leave
   Preparing and its gameplay timer should advance when player control is
   available. Verify completion advances the run state as well.
6. If that passes, run Test All 5 Games to exercise SA-to-III handoff.

If it stalls, stop the run and retain GHMR-controller.log, the GTA III
cleo_redux.log and both gta3de INI files before starting another test.

## Automated verification

Node tests cover dropped writes at invalidation, each payload chunk and commit;
sustained write failure; queue order; recovery logging; and successful commits
with failed read-back, with and without an acknowledgement already received.
Windows C# self-tests cover stale shared-transport rejection and delivery while
a game-side writer holds the INI open, alongside the existing blocked-ack test.
The Windows C# tests must pass in Actions; a local Node pass does not establish
C# compilation or an in-game pass.

Local result: all 14 JavaScript simulations/source checks passed. The Windows
C# build and its new native file-sharing regression remain to be run in Actions.
