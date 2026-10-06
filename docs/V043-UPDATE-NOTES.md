# v0.4.3 game-focus handoff update

This controller-only update fixes the destination game opening behind GHMR or
arriving at its pause menu after a cross-game transition.

## Behaviour

- One monitor: GHMR minimizes itself while gameplay is active.
- Two or more monitors: GHMR moves to a monitor that does not contain the game.
- The transition overlay is ownerless and cannot reactivate the controller when
  it closes.
- GHMR focuses the destination game's real window when it appears and repeats
  the focus handoff after the bridge confirms player control.
- The non-activating transition can remain visible above a focused game without
  taking keyboard or controller input from it.
- The controller restores itself if a launch fails or when the run ends.

No bridge script changed in v0.4.3. Do not run **Setup Game Bridges** again if
all five v0.4.2 bridges already report installed.

## Build and test

1. Upload the update ZIP's `.github`, `src`, `tools`, `docs` and `README.md`
   into the repository root, replacing matching files.
2. Commit the upload and wait for **Build and self-test** to turn green.
3. Download `GHMR-controller-win-x64` from that run.
4. Extract `GHMR-v0.4.3-game-focus-handoff-win-x64.zip` to a fresh folder.
5. Close every GTA game and run the new controller.
6. Select **Diagnostics -> Test all five (fixed order)**.
7. Complete Wrong Side of the Tracks and watch the San Andreas -> GTA III
   handoff. GTA III should receive focus and Espresso-2-Go! must begin without
   the controller covering it or the pause menu already open.

If only one monitor is connected, the controller remaining minimized during
gameplay is intentional. Its taskbar button is still available when the player
deliberately wants to view or stop the run.
