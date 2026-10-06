# v0.1 Mission Selection

GHMR does not claim that difficulty has one objective, universal ranking. The
v0.1 pool is a defensible **hardest/notorious challenge set**: three original
story missions per game that recur in difficulty discussions, test different
skills, and can potentially be launched and verified without redistributing
game content.

## Selection rules

A mission must satisfy all of these gates before Normal mode can ship:

1. It is an original single-player story mission in the supported edition.
2. It has recurring community or editorial evidence of difficulty, notoriety
   or unusually punishing mechanics.
3. Its inclusion adds a meaningful challenge type rather than duplicating all
   three missions from the same game.
4. The adapter can start it from a controlled setup, distinguish success from
   failure/cancel, and repeat the same mission after failure.
5. Completing it cannot make a later locked mission impossible.

The first two gates choose candidates. The last three can still replace a
candidate during adapter testing.

## Current pool and rationale

| Game | Mission | Why it is defensible |
|---|---|---|
| GTA III DE | Espresso-2-Go! | A timed, map-wide route with nine targets; repeatedly appears in hardest-mission lists. |
| GTA III DE | S.A.M. | Precision timing, a moving aircraft target and heavy police response; repeatedly ranked among GTA III's hardest. |
| GTA III DE | The Exchange | Long finale, restricted starting equipment and dense combat. |
| Vice City DE | Demolition Man | Timed RC-helicopter control challenge and one of the series' best-known difficulty spikes. |
| Vice City DE | The Driver | An underpowered-car race complicated by traffic and police interference. |
| Vice City DE | Death Row | Tight rescue timer, dense gunfight and escape while Lance's health drains. |
| San Andreas DE | Supply Lines... | Restricted RC-plane handling, aiming and fuel management; one of the strongest consensus choices. |
| San Andreas DE | End of the Line | Multi-stage finale testing combat, pursuit and endurance. DE checkpoints reduce retry punishment, but not its scale or breadth. |
| San Andreas DE | Wrong Side of the Tracks | Famous for AI-dependent shooting and positioning. Experienced players may find it easy, but its notoriety makes it a defensible challenge-pool choice. |
| GTA IV CE | Three Leaf Clover | Long bank escape with sustained combat, police pressure and limited safe recovery. |
| GTA IV CE | The Snow Storm | Dense hospital shootout followed by a difficult NOOSE escape; frequently raised in player difficulty discussions. |
| GTA IV CE | Out of Commission | Revenge-ending finale combining pursuit, combat, boat and helicopter sequences. |
| GTA V Enhanced | The Big Score | The Obvious approach is the more combat-heavy final heist route and is repeatedly included in difficulty lists. |
| GTA V Enhanced | Minor Turbulence | Demanding low-altitude flight, interception and aerial action. |
| GTA V Enhanced | Derailed | Precision dirt-bike jump followed by a multi-character firefight and escape. It has weaker consensus than the pool's most notorious picks and remains especially important to validate in playtesting. |

For implementation, GTA III uses source-checked mission indices `72`
(Espresso-2-Go!), `73` (S.A.M.) and `79` (The Exchange). Their scripted reward
jumps are used only as mission-specific evidence alongside a stable mission
end; a generic money increase is not accepted. The installed Definitive
Edition remains the final authority, so the bridge status stays provisional
until the two-game validation run passes.

## Evidence sample

These are examples, not a vote count or a claim that every source agrees:

- WatchMojo's series-wide list includes **Death Row**, **The Driver**,
  **Demolition Man**, **Three Leaf Clover**, **The Snow Storm**, **S.A.M.**,
  **Espresso-2-Go!**, **The Exchange** and **Wrong Side of the Tracks**:
  <https://www.watchmojo.com/articles/top-20-hardest-missions-in-gta>
- ScreenRant's Vice City list includes all three current VC selections:
  <https://screenrant.com/hardest-missions-grand-theft-auto-vice-city/>
- A San Andreas player discussion explicitly names **Wrong Side of the
  Tracks**, **End of the Line** and **Supply Lines...** together:
  <https://www.reddit.com/r/GTASA/comments/n9tp0z/what_are_the_hardest_missions_in_gta_san_andreas/>
- A separate San Andreas ranking places **Wrong Side of the Tracks** and
  **End of the Line** at numbers two and one while also including **Supply
  Lines...**:
  <https://gurugamer.com/pc-console/gta-san-andreas-mission-list-16716>
- FandomSpot's GTA IV ranking includes **The Snow Storm** and **Out of
  Commission**, while broader series lists repeatedly include **Three Leaf
  Clover**:
  <https://www.fandomspot.com/hardest-missions-gta4/>
- FandomSpot's GTA V list includes **Derailed**, **Minor Turbulence** and **The
  Big Score**:
  <https://www.fandomspot.com/hardest-missions-gta5/>
- A second GTA V ranking lists **The Big Score** first and also includes
  **Minor Turbulence** and **Derailed**:
  <https://gurugamer.com/pc-console/hardest-mission-in-gta-5-16466/>

## How the list can change

Before v0.1, a candidate can be replaced if controlled-launch testing exposes
broken prerequisites, unreliable completion evidence, unsafe save effects or
edition-specific changes that remove the intended challenge. Any replacement
must be documented here and in the exact mission-set self-test.
