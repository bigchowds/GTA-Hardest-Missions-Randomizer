# GHMR v0.1 Design Specification

Status: controller-to-San-Andreas bridge source milestone, 23 September 2026

## Supported platform

- Windows 10 or Windows 11
- Legitimate PC installations; no ROMs or emulators
- GTA III, Vice City and San Andreas: Definitive Edition
- GTA IV: Complete Edition
- GTA V Enhanced, Story Mode only

Classic editions may be considered later, but are not v0.1 targets.

## Normal mode rules

1. All 15 approved missions are placed in one global shuffle at the start of a
   run. Mission one is randomly selected from GTA III DE, Vice City DE or San
   Andreas DE; after that opening constraint, the order is not balanced or
   forced to alternate games, so missions from the same game may appear
   consecutively.
2. The complete order is generated and locked before mission one begins.
3. The player cannot see or select the internal seed.
4. A mission failure restarts that same mission with no reroll.
5. The next mission is revealed only after completing the current mission.
6. Original mission weapons, vehicles, enemies, objectives, physics and damage remain unchanged.
7. Gameplay time runs only while the player has control during an active mission.
8. Cross-game launch time, loading screens and automatic setup are excluded from gameplay time.
9. Quitting abandons the run. A new run receives a new hidden order.
10. The final result records gameplay time, real elapsed time, deaths, mode, game versions, mod version and an internal run identifier.

## Chaos mode direction

Chaos is deliberately deferred until repeated Normal-mode runs are reliable. It will use the same mission shuffle and failure rules, then apply mission-specific modifier pools.

- Weapon and vehicle replacements are whitelist-based.
- Every mission's exact modifier bundle is rolled once and locked across every retry; dying can never reroll an easier bundle.
- Probabilities are explicit configuration values so they can be tuned after playtesting rather than hidden in game-bridge code.
- Rare powerful rolls are permitted only when they do not trivialise the competitive category.
- No generated combination may be impossible.
- Unsupported combinations are excluded rather than secretly altering vanilla vehicle performance or weapon damage.
- Ideas such as weapon probabilities or additional Vagos in **Wrong Side of the Tracks** remain candidates, not v0.1 Normal-mode behaviour.
- Chaos receives a separate result category.

## v0.1 scope

The first playable version targets fifteen reliable missions: three from each game.

Candidate baseline:

| Game | Mission A | Mission B | Mission C |
|---|---|---|---|
| GTA III DE | Espresso-2-Go! | S.A.M. | The Exchange |
| Vice City DE | Demolition Man | The Driver | Death Row |
| San Andreas DE | Supply Lines... | End of the Line | Wrong Side of the Tracks |
| GTA IV | Out of Commission | Three Leaf Clover | The Snow Storm |
| GTA V Enhanced | The Big Score (Obvious) | Minor Turbulence | Derailed |

The mission list remains provisional until each mission's clean start, success, failure and restart states have been tested on the supported builds.
The pool is described publicly as missions repeatedly cited among the games' hardest or most notorious, not as an objective universal ranking. See `MISSION-SELECTION.md` for the selection method and evidence.

San Andreas launch index `29` has passed direct launch, objective failure,
Retry and manual completion observation. Indices `73` and `110` are encoded in
the catalog but remain provisional pending the same in-game test for **Supply
Lines...** and the complete three-part **End of the Line** sequence.

GTA III launch indices `72`, `73` and `79` and their scripted rewards were
checked against public converted mission sources. The adapter requires the
reward jump as well as mission end, so ordinary cash pickups or a cancelled
mission cannot advance the run. These rules remain provisional until the fixed
San Andreas to GTA III in-game handoff test passes.

## Architecture direction

- A persistent external Windows controller owns the run order, state, timer and transitions.
- A small bridge inside each game reports readiness, player control, mission start, mission failure and mission success.
- Game bridges never decide the next mission.
- The controller writes an append-only run log so a crash cannot silently turn into a valid record.
- Game adapters are isolated so an update to one title does not require redesigning the other four.
- Only one game process is kept alive. Preloading or suspending several games
  retains multiple Unreal engines, launcher sessions, RAM and GPU allocations
  and is not a supported transition strategy. A three-second post-close cleanup
  period is excluded from gameplay time, and the next title is launched through
  its owning platform rather than by bypassing Steam/Rockstar ownership.
- Personal saves are never disposable run state. A verified per-game baseline
  is staged as a dedicated copy when world-state prerequisites require one; see
  `SAVE-BASELINE.md`.
- The controller UI supports keyboard plus XInput navigation, including controllers translated to XInput by Steam Input.
- Gamepad navigation is disabled while the controller window lacks focus, so gameplay inputs cannot trigger run-management actions.

## Release integrity

- Source code will be available for inspection.
- Release files will be versioned and checksummed.
- Normal and Chaos modes refuse unsupported executable versions or an incomplete five-game setup.
- Development probes must not write save files or persist progression; tests
  that run original mission logic require a disposable or backed-up save and
  an exit without saving.
- Public files must not contain personal information, machine-specific paths, runtime logs or private diagnostic output.
- Release builds and archives must pass `docs/RELEASE-CHECKLIST.md`.
- Public Git history must use the developer's chosen public identity and private commit address.
