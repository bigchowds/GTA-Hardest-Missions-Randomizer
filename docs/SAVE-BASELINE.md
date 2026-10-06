# Clean save baselines

The beta needs a compatible baseline for each game; one generic save rule cannot cover all five launch paths. Back up personal saves and keep untouched copies of the baselines. GHMR does **not** automatically stage, copy, restore or isolate saves in v0.4.14.

## Game-specific requirements

| Game | Baseline requirement |
|---|---|
| GTA III DE | Clean story-complete/100% free roam with all Espresso-2-Go! targets/world areas available. No active Give Me Liberty intro or retry. |
| Vice City DE | Clean story-complete/100% free roam. |
| San Andreas DE | Clean story-complete/100% free roam. Bridge 0.1.4 can confirm a replay at 100% using the Missions Passed counter. |
| GTA IV CE | Clean **pre–Three Leaf Clover** save with Packie's live mission marker available, and Niko wearing a suit, tie and smart shoes. Tested save title: **Waste Not Want Knots**, slot `SGTA406`. The current bridge uses the native marker and does not recreate a finished-story mission marker. |
| GTA V Enhanced | Clean Story Mode save with Derailed available for replay. |

The clean 100% preference applies where that state supports the launch route. **GTA IV is the documented exception:** an arbitrary 100% save is not the tested baseline for its marker-based launch. Save slot names alone do not guarantee compatibility.

## Every fresh attempt

1. Restore working copies from your untouched baseline backups while the games are closed.
2. Confirm Resume loads the intended baseline, rather than a recent autosave.
3. Load into neutral free roam: no mission, retry, intro cutscene, death/arrest cleanup or scripted event underway.
4. Handle cloud synchronization separately so it does not replace the local baseline with an unintended save.
5. Keep personal progression separate from challenge attempts. Native missions can change game state and produce rewards/autosaves; don't reuse that changed state as a fresh baseline.

## Counted attempts

Baselines must have been completed normally and never altered by a cheat code, trainer, mod menu or save editor. During the run, do not use gameplay-changing tools. See [FIVE-MISSION-BETA-RULES.md](FIVE-MISSION-BETA-RULES.md).

Trainer-assisted QA is allowed for reproducing a problem, but is unranked. GHMR does not claim that it can automatically identify every edited save or external tool.

## Save distribution

No saves are bundled in this beta. Do not redistribute another person's save just because it is publicly downloadable. Any future bundled baseline needs owner permission, known provenance, a privacy review, exact game/build verification and a checksum. Automatic staging/restoration is a future feature, not current behavior.
