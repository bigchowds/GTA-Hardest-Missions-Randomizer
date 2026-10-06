# v0.4.12 transition visibility update

This controller update follows the successful v0.4.11 GTA IV-to-GTA V handoff.
It does not modify game bridges, mission detection, random order, or saves.

## Changes

- The ownerless handoff screen no longer drops top-most status when the next
  game starts launching. Its visibility refresh runs every half-second with
  non-activating window placement, keeping the controller on its own screen.
- Foreground standard Windows dialogs and modal windows with disabled owners
  stay above the overlay when they overlap its screen. GHMR does not click or
  dismiss them. Secure desktop prompts remain controlled by Windows.
  Restoring the controller on that screen also brings its controls above the
  overlay, so single-screen users can still stop a run or open Options.
- The overlay and music still end when the next game's visible window is
  detected, before its menu or save loads. They cannot be revived by late
  handoff progress. The existing safety limits remain in place.
- The controller now labels the counter **Completed** and counts accepted
  mission completions. Loading mission 2 of 2 shows 1 / 2 until it passes.
  A successful terminal run displays **Finished**.
- Controller protocol checks cover GTA V before another game and GTA V last,
  including actual selected launch target, completion counts, and frozen
  timers. These checks cannot prove native in-game completion detection.

## Update

1. Keep the v0.4.11 folder as a fallback and close the running controller.
2. Extract the Source Update ZIP. Upload its contents into the repository
   root, retaining their folders and replacing the corresponding files.
3. Extract the Build Workflow ZIP. Upload its flat `build.yml` directly into
   `.github/workflows` last. Use the Actions run created by this final commit.
4. Download `GHMR-controller-v0.4.12-win-x64`, then extract its inner
   `GHMR-v0.4.12-transition-visibility-win-x64.zip` into a fresh folder.
5. Start the new controller. Installed v0.4.11-compatible bridges stay valid;
   this update contains no replacement game bridge.

## Focused verification

Use Options -> Fixed test order to choose GTA V, then GTA III. Start Run.
After Derailed passes, leave GTA V open through its replay/restore cleanup
until GHMR confirms the pass and starts the next game. The completed count
must change to 1 / 2 and GTA III must launch. Watch the game monitor during
the handoff without clicking the controller. Stop Run after GTA III starts
if only checking this handoff.

For a final-mission check, select only GTA V, start the run, pass Derailed,
and leave it open until GHMR shows Finished with 1 / 1 completed and frozen
timers. Closing GTA V after Finished must preserve that result.

If GTA V returns to its pre-replay screen without completion being accepted,
keep the current log and provide the game's bridge log for diagnosis. Do not
mark completion automatically just because the player closes the game.

Before a public five-mission beta, switch Options back to Random order and
complete one full five-game run on the exact packaged build. Confirm Finished
and 5 / 5 completed. A single-screen check and retry check are also needed.
The two-screen window-placement change requires live Windows verification;
compilation and protocol tests cannot reproduce a real launcher/focus stack.
