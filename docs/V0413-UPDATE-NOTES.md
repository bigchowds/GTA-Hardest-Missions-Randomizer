# v0.4.13 GTA V pass-screen update

Derailed can show Mission Passed while its replay scripts remain active. The
bridge's existing completion signature waits for those scripts to stop, so
the controller can remain Running even after the visible pass.

## Changes

- While the accepted Derailed attempt is Running, the controller checks the
  foreground GTA V window every 600 milliseconds. It matches the English
  gold Mission Passed word silhouette, then uses built-in Windows text
  recognition to read the separate Derailed title.
- Two successive matching observations confirm completion through the same
  guarded event path as native bridge completion. GTA V need not return to
  Story Mode. A following game starts the normal close/launch handoff; GTA V
  last finishes the run and stays open.
- The run ID, selected mission, bridge session and failure count are checked
  again before acceptance. A stale observation, pending retry or duplicate
  native completion cannot advance another mission.
- Recognition inspects the top half of the foreground game's client area in
  memory. It does not save screenshots, upload images or simulate input.
  The controller log records first detection, confirmation and recognition
  errors. Native bridge detection remains available.
- The v0.4.12 overlay visibility and actual Completed counter changes remain
  included. Game bridges and saves are unchanged by this update.

## Install

1. Keep the previous extracted build as a fallback and close the controller.
2. Extract the Source Update ZIP and upload its contents into the repository
   root, preserving the folders and replacing matching files.
3. Extract the Build Workflow ZIP and upload its flat build.yml directly into
   .github/workflows last. Use the Actions run from this final commit.
4. Download GHMR-controller-v0.4.13-win-x64. Extract its inner
   GHMR-v0.4.13-gtav-pass-screen-win-x64.zip into a fresh folder.
5. Launch the controller and confirm v0.4.13. No bridge reinstall is needed
   if the previously working bridges are already installed.

## Focused test

In Options, select Fixed test order: GTA V, then GTA III. Start Run, complete
Derailed and keep GTA V foreground while Mission Passed and Derailed appear.
Allow about one to two seconds for the two observations. Do not manually
return to Story Mode or close the game. The completed counter should become
1 / 2 and GHMR should start the GTA III handoff. This short route is sufficient
to check this fault; Stop Run after GTA III becomes playable.

For a final-mission check, use only GTA V. Derailed's pass screen should produce
Finished and 1 / 1 completed with frozen timers; GTA V remains open.

The screenshot template is for the English result screen. English Windows
text recognition must be installed. Recognition cannot work with a black or
occluded capture. If exclusive fullscreen or HDR causes this, test borderless
windowed mode. An unavailable recogniser or capture exception is logged; no
completion is guessed from exiting the game.

If it does not advance, keep GTA V open and use Options -> Open Logs. Supply
the updated GHMR-controller.log and gtav_enhanced-bridge.log from the ipc
folder beside it, plus a screenshot of the result screen.

## Verification status

The controller compiles with warnings treated as errors. Screenshot-derived
gold-mask fixtures match at the original and half resolutions. Negative
silhouette, missing-banner and wrong-title cases reject. Coordinator tests
cover GTA V to GTA III, GTA V last, stale run/session/index/attempt, retry
state, duplicate screen confirmation and a late native completion.

These checks do not run Windows screen capture or the Windows OCR engine.
Live recognition and game closing require the focused test above.
Before public beta release, complete a Random order five-game run on the
exact packaged build and confirm Finished with 5 / 5 completed.
