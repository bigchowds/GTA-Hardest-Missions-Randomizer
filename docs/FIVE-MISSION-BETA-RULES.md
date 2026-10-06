# Five-mission beta run rules

The beta is community testing. It does not provide a cheat-proof leaderboard or automatic detection of every external modification.

## Before a counted attempt

1. Prepare pristine, never-cheated saves matching [SAVE-BASELINE.md](SAVE-BASELINE.md).
2. Use clean story-complete/100% baselines where supported. **GTA IV needs the documented pre–Three Leaf Clover baseline**, with Packie's marker and suitable clothing, rather than an arbitrary 100% save.
3. Restore untouched baseline copies before each fresh attempt. Do not reuse changed autosaves from a previous run. GHMR does not restore saves automatically.
4. Install and verify all five bridges and external runtimes.
5. Use **Start Run in Random order**. Fixed-order/subset diagnostics are QA routes, not a normal five-mission beta attempt.

## Allowed

- GHMR and its documented bridge dependencies.
- Ordinary Steam Input/controller remapping and accessibility settings that do not automate gameplay or change game speed.
- OBS, capture cards and passive recording/streaming overlays.
- LiveSplit as an optional passive timer/manual split display. No automatic integration is bundled.
- Resolution/window/display changes that do not alter gameplay logic or timing.
- Native mission Retry after failure. GHMR keeps the same selected mission; no rerolls.

## Not allowed in a counted attempt

- Trainers or mod menus, even briefly.
- Teleporting, invincibility, vehicle/weapon spawning, money/stat changes, wanted changes or mission-completion shortcuts.
- Save editors, cheated saves or impossible inventory/stat/world state.
- Macros, turbo, automated gameplay inputs, game-speed changes or external suspension of game logic.
- Mods that alter missions, traffic, pedestrians, police, vehicles, weapons, damage, physics or checkpoints.
- Skipping, replacing or rerolling the selected mission.

GHMR's own disclosed mission preparation, native replay request, menu handling and handoff actions are part of the challenge setup. They are separate from player-used cheats.

## Timing and completion

- **Gameplay Time** counts bridge-confirmed playable mission periods. Loading, game handoffs and supported cutscene/non-control periods are excluded.
- **Real Elapsed** measures the whole attempt for transparency.
- A failed mission adds a failure and keeps the same mission; its earlier gameplay remains in the attempt's time.
- Success requires **Finished and 5/5 confirmed completions**. Reaching the final mission is not completion.
- The final game remains open after Finished.

Trainer-assisted debugging is welcome as **QA/unranked**, but must not be submitted as a counted attempt. Keep the controller log/audit files and ideally record the attempt if reporting a problem.
