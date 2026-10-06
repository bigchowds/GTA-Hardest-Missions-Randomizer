# Beta troubleshooting

Start with **Options > Open Logs**. Preserve the log from the failed attempt before repeatedly restarting. Use **Stop Run** to cancel a stuck attempt; manually closing an unfinished active game ends the run.

| Symptom | Check / action |
|---|---|
| Setup cannot find the CLEO runtime or definitions | Select the real Trilogy EXE in `Gameface/Binaries/Win64`. Install the correct x64 CLEO version and IniFiles extension, launch once, then close the game before repairing. |
| Bridge version is missing or old | Close the game and controller, extract the full beta ZIP, reopen GHMR and Install / Repair Bridge for that game. Replacing only the EXE does not update packaged adapters. |
| Vice City CLEO fails to initialize | The recorded VC build used CLEO Redux **1.4.2 x64**, not 1.5.0. Keep matching runtime files together. |
| Trilogy loads the intro or wrong save | Check the actual save selected by Resume. Use the clean baseline, not an autosave from an earlier test. Do not spam Enter through an unknown menu/dialog. |
| GTA III enters beginning-game free roam / missing Espresso targets | Use the verified story-complete world state; an early-game save or Give Me Liberty autosave is unsuitable. |
| GTA IV says Packie's marker is unavailable | Load a clean save from before Three Leaf Clover where its native marker exists. The developer used `SGTA406` / **Waste Not Want Knots**. Wear a suit, tie and smart shoes. A 100% finished-story save can be unsuitable. |
| GTA V waits at its Online / Story landing page | Select **Story Mode**. Automatic Story-tab selection is not included in this beta. |
| GTA V does not advance at Derailed's results | Keep the English Mission Passed/Derailed result visible and GTA V foreground. Check Windows English OCR support and window capture. Record the log before manually dismissing results. |
| Controller says GTA V is waiting to close after a confirmed pass | The automatic close could not verify/close the configured process. Close the completed GTA V process normally or through Task Manager; GHMR should resume the pending handoff. Stop Run cancels that pending launch. |
| Transition disappears before the main menu | Expected in this build: thumbnails/music end when the next game window appears. Startup logos or a short black screen can follow. |
| Game opens behind another window or pauses | Let its startup finish, then activate the game once. Preserve the focus warnings in the log. Check Steam/Rockstar dialogs and that the controller is using the intended monitor. Report if manual activation is consistently needed. |
| Music carries on into gameplay | Mute transition music in Options and report the exact handoff/time with the log and a short clip. Controller shutdown should stop its local music. |
| Final game stays open at Finished / 5/5 | Expected. Close it normally when ready. Completed 4/5 while the final mission is running is also expected. |
| Controller isn't detected | GHMR reads XInput while its window has focus. Try a suitable Steam Input mapping, or use the keyboard. See [controller support](CONTROLLER-SUPPORT.md). |
| Windows warns or blocks the EXE | This beta is unsigned. SmartScreen reputation warnings, Smart App Control blocks and Defender Antivirus threat detections require different handling. Follow the [Windows security notice](TRUST-AND-RELEASES.md#unsigned-windows-app); report the exact message/version/hash. Do not disable protection or add broad exclusions. |

## What to include in a report

- GHMR version shown in Options, game edition/executable build, store and runtime version.
- The current mission and the previous game in the run.
- Whether it was Random order, a Fixed test subset, or a single-game diagnostic.
- What the controller said, what the game showed, and whether manual input/closure was needed.
- `GHMR-controller.log` from Options > Open Logs. If relevant, the affected game's `cleo_redux.log` or `ScriptHookVDotNet.log`.
- A screenshot or clip covering the failed boundary, with its timestamp if using a full recording.

Review files before posting: logs from the controller, games or other runtimes can contain installation paths or account details. Remove anything private. Do not upload your personal saves, game binaries or complete game folder.

The controller's readable log is normally under `%LOCALAPPDATA%/GHMR/controller/logs/GHMR-controller.log`. Rotated log files may also be present. Run state and audit files remain under `%LOCALAPPDATA%/GHMR/controller`.

For bugs use this repository's **Issues** page. See [CONTRIBUTING.md](../CONTRIBUTING.md).
