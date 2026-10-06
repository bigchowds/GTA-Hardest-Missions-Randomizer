# Install the five-mission beta

This guide applies to controller **v0.4.14**, packaged as **v0.4.14-beta.1**. The packaging revision changes documentation and release contents; the tested mission and handoff logic is preserved.

## 1. Prepare your games and saves

You need Windows x64 and all five supported editions. Windows 10 version 2004 or newer is the controller's target; the English text-recognition features also need the corresponding Windows OCR support. See [tested compatibility](TESTED-COMPATIBILITY.md) for the known game/runtime combinations.

Back up your personal saves before testing. GHMR does **not** automatically copy, isolate, restore or install save files. Keep untouched baseline backups and restore your working copies before each fresh attempt. Check what Resume will load; an autosave from an earlier test can take you to the wrong state. Handle cloud-save synchronization separately.

| Game | Baseline |
|---|---|
| GTA III DE | Clean story-complete/100% save loading neutral free roam, with all Espresso targets accessible. Avoid a beginning-game autosave or active Give Me Liberty sequence. |
| Vice City DE | Clean story-complete/100% save loading neutral free roam. |
| San Andreas DE | Clean story-complete/100% save loading neutral free roam. |
| GTA IV Complete Edition | Clean save **before Three Leaf Clover**, with the green Packie marker available. The developer's tested slot was `SGTA406`, titled **Waste Not Want Knots**. Niko must wear a suit, tie and smart shoes. A slot filename alone does not make another save compatible. |
| GTA V Enhanced | Clean Story Mode save with Derailed available for replay. |

For counted attempts, these saves must never have been edited or cheated on. No saves are bundled. [Baseline policy](SAVE-BASELINE.md).

## 2. Install the external scripting runtimes

Use the official upstream sources linked in [TESTED-COMPATIBILITY.md](TESTED-COMPATIBILITY.md). Keep an already working runtime intact rather than mixing DLLs from different releases.

### Definitive Edition Trilogy

- GTA III DE and San Andreas DE: tested with **CLEO Redux 1.5.0 x64**.
- Vice City DE: tested with **CLEO Redux 1.4.2 x64**. This matters: 1.5.0 previously failed to initialize on the tested VC build.
- Use the x64 runtime and an appropriate ASI loader. The expected runtime file is `cleo_redux64.asi` beside the real game executable.
- Include the **IniFiles** extension. GHMR expects `CLEO/CLEO_PLUGINS/IniFiles64.cleo`.
- Launch each game once so CLEO creates its configuration, then close it. GHMR expects `CLEO/.config/unknown_x64.json` for this tested installation layout.

Select the real executable in `Gameface/Binaries/Win64`, not a store launcher. GHMR installs a combined `[fs].js` bridge and patches the local CLEO definitions with the bundled INI commands. It does not install CLEO Redux itself. A redirected CLEO directory needs to be resolved before GHMR's installer can use this expected layout.

### GTA IV Complete Edition

Install a compatible GTA IV ScriptHookDotNet runtime. The tested runtime reports **1.7.1.9** on game build **1.2.0.59**. GHMR's installer checks for `ScriptHookDotNet.asi` and `ScriptHook.dll` beside `GTAIV.exe`; install the complete matching runtime and its upstream prerequisites, not just those two files.

Select the inner `GTAIV/GTAIV.exe` in GHMR. CLEO Redux is not the runtime used by the production GTA IV bridge.

### GTA V Enhanced

Install a Script Hook V build compatible with your Enhanced executable, a compatible ASI loader, and **ScriptHookVDotNet Enhanced**. The development baseline used Enhanced **1.1.0.6**.

Keep `ScriptHookVDotNet.asi`, `ScriptHookVDotNet2.dll`, `ScriptHookVDotNet3.dll` and `ScriptHookVDotNet.ini` from one matching release beside `GTA5_Enhanced.exe`. Also keep the compatible `ScriptHookV.dll`. Follow the upstream runtime requirements for .NET Framework and Visual C++ dependencies. The self-contained GHMR controller runtime does not replace these game-side requirements.

Use only a single-player modding configuration and the upstream launch requirements, including GTA V's single-player anti-cheat configuration. GHMR does not change anti-cheat settings. Do not copy the optional Native Trainer from a Script Hook V archive into a counted-run setup.

## 3. Extract and configure GHMR

1. Download `GHMR-v0.4.14-beta.1-win-x64.zip` from this repository's Releases. The automatic Source code downloads are not the Windows app.
2. Extract **everything** into a writable folder, such as a dedicated folder under Documents. Keep `adapters`, `config`, `media` and `docs` beside `GHMR.Controller.exe`.
3. Launch `GHMR.Controller.exe` as your ordinary Windows user. The controller includes .NET; a separate .NET 8 installation is unnecessary for the released app. If Windows warns or blocks it, read the [unsigned-app notice](TRUST-AND-RELEASES.md#unsigned-windows-app): SmartScreen, Smart App Control and Defender Antivirus are different protections.
4. Select **Setup Game Bridges**.
5. Choose a game, browse to the real executable listed below, and choose **Install / Repair Bridge**. Repeat for all five games while they are closed.
6. Read the success message. If setup reports a missing runtime or configuration file, fix that prerequisite before retrying. Do not overwrite files in unrelated game folders.

| Game | Select | Installed bridge |
|---|---|---|
| GTA III DE | `LibertyCity.exe` | `CLEO/ghmr_gta3_bridge[fs].js` |
| Vice City DE | `ViceCity.exe` | `CLEO/ghmr_vc_bridge[fs].js` |
| San Andreas DE | `SanAndreas.exe` | `CLEO/ghmr_sa_bridge[fs].js` |
| GTA IV CE | `GTAIV.exe` | `scripts/ghmr_gta4_bridge.cs` |
| GTA V Enhanced | `GTA5_Enhanced.exe` | `scripts/ghmr_gtav_bridge.3.cs` |

The setup remembers your paths locally. Steam-owned installations use their store launch route; a configured executable still identifies the gameplay installation. For controller mapping, see [CONTROLLER-SUPPORT.md](CONTROLLER-SUPPORT.md).

## 4. Start a run

1. Load-test each baseline once and close the games before the attempt. Confirm that the games and runtimes work independently.
2. In **Options**, use **Random order — all five** for a normal beta attempt. Fixed order/subsets are for testing.
3. Choose **Start Run**. The first game is randomly selected from the Trilogy, and the remaining four are shuffled.
4. Select the compatible save or Resume if necessary. Select Story Mode in GTA V. Vice City's experimental Auto Resume may handle its recognized English menu, but manual Resume remains the fallback.
5. Play the selected mission. Use the game's native Retry flow after failure; the selected mission stays locked.
6. After a confirmed pass, GHMR records it and advances to the next game. Keep GTA V foreground at the English Derailed Mission Passed result so its early result recognition can operate.
7. **Finished / 5/5** is success. The final game stays open; close it normally when ready.

For handoffs after an accepted GTA V completion, GHMR directly ends the verified `GTA5_Enhanced` gameplay process to avoid its quit confirmation. The replay's unsaved state is discarded. Other games use normal closure. GHMR does not close Steam or Rockstar Launcher. [Trust details](TRUST-AND-RELEASES.md).

## Problems or uninstalling

Use **Options > Open Logs** and [TROUBLESHOOTING.md](TROUBLESHOOTING.md). To remove GHMR, close the controller and games, remove only the GHMR bridge files listed above, then delete the extracted GHMR folder. Do not delete the entire CLEO/scripts directory or another mod's files. Local GHMR settings/logs remain under `%LOCALAPPDATA%/GHMR` unless you remove them yourself.
