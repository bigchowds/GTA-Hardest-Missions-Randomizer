# v0.4.6 visible handoff and San Andreas start update

This update addresses three live-test problems together.

## Visible randomized mission

The controller and transition overlay now display the selected game and
mission immediately. The order is still randomized, so concealing it added
confusion without protecting a fixed route. Most importantly, the controller's
real handoff status is no longer replaced by a generic hidden-message line.

## Resilient normal close

GHMR previously sent one normal window-close request and waited. It now repeats
that same safe request every two seconds during the existing 20-second timeout.
Mouse movement and focus changes do not control this retry. GHMR still never
force-kills, suspends or trims a game process. If a title still refuses to
close, the now-visible status tells the player exactly what to close and launch
manually; the randomized mission remains locked rather than rerolling.

## Faster movement-proof San Andreas start

San Andreas bridge v0.1.4 starts after three seconds of stable free roam when
the native mission-ready flag is available. SA can keep that flag false while
CJ moves, so five stable seconds now acts as a bounded fallback. Continuous
movement can no longer delay Wrong Side of the Tracks indefinitely, while the
proven five-second protection against duplicate CJ and the black-and-white
startup state remains intact.

## Install and test

1. Upload this update into the existing repository and replace matching files.
2. Wait for **Build and self-test** to turn green.
3. Download `GHMR-controller-win-x64` and extract
   `GHMR-v0.4.6-visible-handoff-sa-start-win-x64.zip` to a fresh folder.
4. Close every GTA game.
5. Run v0.4.6 and use **Setup Game Bridges** once to install/repair the updated
   San Andreas bridge.
6. Test San Andreas -> GTA III. Keep CJ moving after the save appears: Wrong
   Side of the Tracks should still start within about five seconds. After
   completion, SA should receive repeated normal close requests and GTA III
   should launch.
