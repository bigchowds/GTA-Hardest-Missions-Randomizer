# v0.4.14 GTA V handoff close

The v0.4.13 result-screen check advanced Derailed, but the subsequent normal
window-close request opened GTA V's interactive quit confirmation. After its
20-second close timeout, the old handoff returned without launching the next
game. A later Task Manager close did not resume it.

## Changes

- After an accepted GTA V completion advances to another game, GHMR bypasses
  the normal window-close request. It verifies the GTA5_Enhanced process name
  and full executable path against the configured gameplay executable, pins
  that process handle, and ends only that process. Steam, Rockstar Launcher
  and descendant processes are not termination targets.
- This discards the completed replay's unsaved state. The accepted pass has
  already been saved in GHMR's run state before closure is requested. GHMR
  does not modify or delete save files.
- GHMR waits for the verified game process to exit before its platform-settle
  delay and selected next-game launch. An ambiguous or unverifiable process
  is not terminated. Other games retain their normal close behaviour.
- If completed GTA V cannot be closed automatically, the controller waits for
  manual process exit and then resumes the selected next-game launch. It does
  not attempt to close a newly opened GTA V process during recovery.
- Stop Run, a new run, and controller shutdown cancel pending handoffs. Closing
  an unfinished active mission still aborts the run without advancing. GTA V
  last still produces Finished and remains open.
- Existing v0.4.12 overlay/counter and v0.4.13 pass-screen changes are included.
  No replacement game bridge is part of this update.

## Install and focused test

1. Close the controller and preserve the previous extracted build.
2. Extract the Source Update ZIP. Upload its contents to the repository root,
   preserving folders and replacing matching files.
3. Extract the Build Workflow ZIP. Upload build.yml directly into
   .github/workflows last. Use the Actions run created by that final commit.
4. Download GHMR-controller-v0.4.14-win-x64 and extract its inner
   GHMR-v0.4.14-gtav-handoff-close-win-x64.zip into a fresh folder.
5. Start the controller and confirm v0.4.14. Previously working installed
   bridges remain compatible; bridge setup is not required for this update.
6. In Options, choose Fixed test order: GTA V, then GTA III. Complete Derailed
   and keep GTA V foreground at Mission Passed. Leave the result screen alone.
   Expected: Completed 1 / 2, GTA V exits without its quit confirmation, then
   GTA III launches after platform settling.

If automatic closure is blocked, the status should explicitly say that GHMR
is waiting for GTA V to close. Close only GTA V in Task Manager; the selected
GTA III launch should resume automatically. Stop Run while waiting should
cancel this recovery.

## Verification status

The core and controller compile with warnings treated as errors. Tests cover
the dedicated GTA V close route, normal final closure, initial launch, missing
next-game setup, unsuccessful close, manual-exit continuation, identity refusal
for launchers/another installation, and cancellation. Coordinator tests cover
manual closure after a pass without aborting or counting twice, exactly one
next launch, and Stop Run while awaiting manual exit.

These tests use simulated process operations and exits. Actual Windows process
termination, launcher recovery, screen capture and text recognition still need
the focused live test above. Before public beta, complete a Random order
five-game run on the exact release package and confirm Finished with 5 / 5.
