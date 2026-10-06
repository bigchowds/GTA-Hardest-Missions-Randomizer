# GTA IV Complete Edition: Compatibility Probe v1

This first GTA IV test verifies that CLEO Redux loads as the `gta_iv` host and that a JavaScript bridge can observe the read-only `ONMISSION` flag.

The supported test target is GTA IV Complete Edition `1.2.0.59`. GTA IV is a 32-bit game, so its files are `cleo_redux.asi` and `dinput8.dll`, not the Trilogy's `cleo_redux64.asi` and `version.dll`.

The probe does not start, complete, fail, restart or modify missions or saves.

## Before installation

1. Keep `IV.Randomizer` outside the game directory for this test.
2. Launch unmodified GTA IV once, reach Story Mode, then exit normally.
3. In Steam, open **Library**, right-click **Grand Theft Auto IV: The Complete Edition**, choose **Manage**, then **Browse local files**.
4. Open the inner `GTAIV` folder containing `GTAIV.exe`.

If `dinput8.dll` is already beside `GTAIV.exe`, do not delete or replace it. It is normally an existing ASI loader. Keep a backup copy before changing the folder.

## Install CLEO Redux

1. Run the CLEO Redux `1.5.0` installer.
2. Select the folder containing `GTAIV.exe` as the destination.
3. Leave **CLEO Redux**, **API files**, **IniFiles**, **MemoryOperations**, **Input** and **Events** selected.
4. If the installer offers **Ultimate ASI Loader**, clear that checkbox because this test installation already has `dinput8.dll`.
5. Finish the installation.

Expected files beside `GTAIV.exe` include:

```text
CLEO\
cleo_redux.asi
cleo_redux.log    (created after the game runs)
dinput8.dll       (the existing ASI loader)
GTAIV.exe
```

Do not copy `cleo_redux64.asi`, `version.dll`, Trilogy scripts or `IV.Rainbomizer.asi` into this folder.

## Install and run the probe

1. Copy `ghmr_gta4_probe.js` into the new `GTAIV\CLEO` folder.
2. Launch GTA IV normally through Steam. Administrator mode should not be required for a Steam library folder.
3. Load Story Mode and remain in free roam for at least ten seconds.
4. Start any available mission and play until normal gameplay begins.
5. Remain in the active mission for at least ten seconds, then exit the game normally.
6. Send back the `cleo_redux.log` beside `GTAIV.exe`. If it is not there, check `%APPDATA%\CLEO Redux`.

The useful lines will resemble:

```text
[GHMR] GTA IV compatibility probe v1 started
[GHMR] Host: gta_iv
[GHMR] Host version: 1.2.0.59
[GHMR] Initial state: ONMISSION=false
[GHMR] MISSION STATE: false -> true
```

This first run does not require deliberately failing or completing the mission. Outcome and checkpoint behaviour are tested only after basic bridge loading succeeds.

## Observed result

The test installation successfully loaded CLEO Redux `1.5.0` x86, API definitions `0.108`, the `gta_iv` host and this JavaScript probe on GTA IV Complete Edition `1.2.0.59`. It coexisted with the installation's existing ASI loader and mod menu.

The player tested the story mission **Blow Your Cover**, died and retried several times, then completed it and observed Playboy X being added to the in-game contact list. The generic `ONMISSION` value remained `true` throughout the entire session, including after confirmed completion. It is therefore not a valid lifecycle or completion signal for the GTA IV adapter.

A mod-menu teleport was used during this compatibility test. That does not invalidate the bridge-loading result, but this run is not evidence for Normal-mode mission behaviour or timing.

Production rules:

- never advance GTA IV from the generic `ONMISSION` value;
- keep the same locked mission through failure and checkpoint retry;
- require mission-specific success evidence for Three Leaf Clover, The Snow Storm and Out of Commission;
- do not rely on the optional Events plugin hooks that reported unsupported-address warnings in this build.

The read-only probe is now superseded by the isolated v0.1.8 controller adapter.
Follow `GTA4-CONTROLLER-TEST.md` for the next live test; **Setup Games &
Bridges** removes the old probe when it installs the controller bridge.
