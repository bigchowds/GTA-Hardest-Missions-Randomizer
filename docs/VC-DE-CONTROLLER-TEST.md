# Vice City Demolition Man Controller Test

This is one isolated development test, not a fifteen-mission randomizer run.
It validates the first real Vice City adapter without replaying San Andreas or
GTA III.

## What this build does

- launches the original **Demolition Man** mission script at Vice City mission
  index `19`;
- changes no vehicles, weapons, actors, timers or objectives;
- confirms success only after the mission's original `$1,000` reward appears;
- keeps the same locked mission after failure;
- supports either a native checkpoint Retry or a return to free roam without
  launching two mission scripts;
- leaves Vice City open after the final isolated-test completion; a fade and
  normal close are used only when another game actually follows.

The index and reward were checked against the
[public decompilation of Vice City's original `main.scm`](https://gist.github.com/wodim/735b59d7b35f28bc2f4b).
The installed Definitive Edition remains the final authority, which is why this
live test is required.

## Before the test

1. Use a disposable copy of a clean story-complete Vice City save that loads
   into ordinary free roam. Do not start from an active mission or cutscene.
2. Confirm the save's cash is below the game's maximum so the `$1,000` reward
   can be observed.
3. Install/repair the Vice City bridge from the v0.1.7 controller as described
   in `UPDATE-GITHUB-SOURCE.md`.
4. Leave CLEO Redux `1.4.2` x64 installed for the already-tested
   `ViceCity.exe` version `1.0.112.6680`.

## Run exactly this test

1. With Vice City closed, open `GHMR.Controller.exe`.
2. Select **Test Vice City Only**.
3. If Vice City stops at its own menu, load the clean story-complete save once.
4. After Tommy reaches stable free roam, allow roughly three seconds for the
   development safety check. **Demolition Man** should begin automatically.
5. First confirm the helicopter, four bombs, construction site, workers and
   vanilla timer all appear normally.
6. Deliberately fail once by destroying the RC helicopter or letting the timer
   expire. Choose the game's normal **Retry** option if it appears.
7. Confirm the same Demolition Man mission resumes or relaunches. It must not
   remain in free roam, launch another mission or create duplicate actors.
8. Complete the mission normally.

Expected result: the controller reaches `1 / 1`, reports completion, fades the
game and requests a normal close. A failed attempt increases **Failures** by
one and never changes the mission.

If anything differs, stop after that single run and keep both:

- `Gameface\Binaries\Win64\cleo_redux.log`;
- `%LOCALAPPDATA%\GHMR\controller\audit\` and the latest run log.

Do not repeatedly retry a broken state. The exact first failure is more useful
than several overlapping attempts.
