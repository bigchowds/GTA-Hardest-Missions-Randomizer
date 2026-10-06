# San Andreas Definitive Edition: Mission-State Probe

San Andreas DE on Rockstar Games Launcher build `1.0.112.6680` has passed clean launch and CLEO Redux `1.5.0` x64 initialization. This read-only test checks whether GHMR can observe the start and end of an ordinary story mission. It does not change mission state, progression, player data or save files.

## Before launching

1. Exit San Andreas completely.
2. Open the San Andreas installation folder and browse to `Gameface\Binaries\Win64\CLEO`.
3. Remove any earlier GHMR test `.js` file from that folder, if one is present.
4. Copy `mods\cleo-probe\ghmr_sa_mission_state_probe.js` into the `CLEO` folder.
5. Leave `tsconfig.json`, `.config`, `CLEO_PLUGINS` and all generated files alone.

Only one GHMR probe should be present during this test. Do not copy files from the Vice City installation.

## Run the test

1. Launch San Andreas by the same method that made CLEO Redux `1.5.0` initialize successfully.
2. Load or start a game while CJ is not inside a mission.
3. Start an ordinary story mission. The opening missions such as **Big Smoke** or **Sweet & Kendl** are suitable.
4. Play until the mission succeeds or fails and control returns to free roam.
5. Exit the game normally.
6. Open `Gameface\Binaries\Win64\cleo_redux.log`.

Expected GHMR lines include:

```text
[GHMR] SA mission-state probe started
[GHMR] Host: sa_unreal
[GHMR] Initial ONMISSION=false
[GHMR] Mission transition 1: ONMISSION=false -> true
[GHMR] Mission transition 2: ONMISSION=true -> false
```

The transition numbers or initial value may differ if the script begins while a mission is already active. The important result is whether the value changes at the real mission boundaries.

## What this proves

A successful result proves the San Andreas bridge can observe a basic mission lifecycle using the same event shape as the Vice City bridge. The `ONMISSION` flag alone cannot distinguish success from failure or uniquely identify a mission, so those require later probes before the randomizer can control a Normal-mode run.

Send back the updated `cleo_redux.log`. GHMR log lines contain only the game host, public software versions and mission-flag changes; they do not contain a Windows username or save contents.
