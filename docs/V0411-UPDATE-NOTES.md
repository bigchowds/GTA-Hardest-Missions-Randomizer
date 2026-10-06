# v0.4.11 handoff update

## Changes

- GTA IV bridge 0.1.17 retains each published event until the controller acknowledges it. Enqueuing another event or acknowledging a command no longer blanks the pending frame. Failed writes keep the queue head and sequence for retry.
- A fresh GTA IV mission-complete audio cue followed by a confirmed end of Packie3 can prove success when a full save's money limit prevents the $250,000 balance increase. A script ending without positive success evidence still counts as failure.
- Transition screen and music end when the next gameplay window is detected. They can end during boot, before its menu; GHMR does not inspect GTA IV's sign-in or menu audio. Late launcher progress cannot reopen the dismissed media. The controller remains away from the gameplay monitor.
- Options offers Random order (default) and Fixed test order. Tick missions to include, select a row and use UP/DOWN to choose the route. Start Run uses the chosen mode. Random mode includes all five missions and chooses a trilogy mission randomly for the first position.
- Start Run's subtitle shows its current order mode. The diagnostic all-five route remains fixed.
- Experimental VC Auto Resume uses Windows' local text recognition to find both English Resume and New Game labels, and sends one targeted click to Resume. It only runs while that game owns foreground focus and the current run has no accepted bridge. If recognition is unavailable, the menu is not English, fullscreen capture is blank, or the game ignores the click, select Resume manually. The option can be disabled. No screenshots are saved or uploaded.
- Normal game exit still stops the active run.

## Update and test

1. Stop the current run and close GTA IV normally. Keep the controller and GTA IV logs from the failed run.
2. Extract the Source Update ZIP. Upload its contents into the repository root, merging the existing paths. Ensure the `mods` and `tests` files are included.
3. Extract the Workflow ZIP and upload `build.yml` directly into `.github/workflows`, as the last commit. It runs the new bridge behaviour tests and publishes the v0.4.11 controller.
4. Extract the new controller artifact into a new folder. Open it, then use Setup Game Bridges to install/repair GTA IV. The installed bridge must report 0.1.17-retained-event-delivery; the version guard rejects the old one.
5. In Options select Fixed test order. Include only GTA IV and GTA V, in that order, then Save and Start Run. Complete Three Leaf Clover and confirm one switch to Derailed.
6. For transition/Resume validation, choose Vice City followed by GTA IV. Confirm media ends at the destination window and that VC's startup Resume activates once. Report if it still needs manual selection.
7. Switch Options back to Random order before the full five-game release test. Trainer-assisted runs are quick diagnostics; release validation uses the documented clean setup.

## Validation limits

The controller has been compiled with the C# compiler against Windows reference libraries. Core, controller/transport and stubbed GTA IV behavioural tests run locally, including delayed acknowledgements, denied writes, native success cues, fixed-route persistence, menu recognition and late transition progress. GitHub Actions remains the full Windows publish check. Live game timing, capture and VC input handling still need player testing.

The update's readable project files were checked for unwanted tool references. Existing remote repository history is not modified by this ZIP.
