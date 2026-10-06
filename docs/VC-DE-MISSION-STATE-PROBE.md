# Vice City Definitive Edition: Mission-State Probe

This is the second read-only Vice City test. It checks whether GHMR can observe the start and end of a normal story mission. It does not change mission state, progression, player data or save files.

## Before launching

1. Confirm the basic probe in `docs/VC-DE-PROBE.md` already passed.
2. Exit the game completely.
3. In `Gameface\Binaries\Win64\CLEO`, remove `ghmr_vc_probe.js` if it is still present.
4. Copy `mods\cleo-probe\ghmr_vc_mission_state_probe.js` into that `CLEO` folder.
5. Leave the generated `tsconfig.json` and everything inside `CLEO\.config` alone.

Only one GHMR probe should be present during this test.

## Run the test

1. Launch Vice City by the same method that made CLEO Redux `1.4.2` initialize successfully.
2. Load or start a game while not inside a mission.
3. Begin **The Party**, or another ordinary story mission if it is already completed.
4. Play until the mission succeeds or fails and control returns to free roam.
5. Exit the game normally.
6. Open `Gameface\Binaries\Win64\cleo_redux.log`.

Expected GHMR lines include:

```text
[GHMR] VC mission-state probe started
[GHMR] Host: vc_unreal
[GHMR] Initial ONMISSION=false
[GHMR] Mission transition 1: ONMISSION=false -> true
[GHMR] Mission transition 2: ONMISSION=true -> false
```

The transition numbers or initial value may differ if the script begins while a mission is already active. The important result is whether the value changes at the real mission boundaries.

## What this proves

A successful result proves the game bridge can observe a basic mission lifecycle. The `ONMISSION` flag alone cannot tell success from failure or uniquely identify a mission, so those require later probes before the randomizer can control a Normal-mode run.

Send back every line beginning with `[GHMR]`. Those lines contain only the game host, public software versions and mission-flag changes; they do not contain a Windows username or save contents.
