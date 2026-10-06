# GTA V Enhanced: Compatibility Probe v2

This test verifies that ScriptHookVDotNet Enhanced can load a raw v3 C# bridge on the installed GTA V Enhanced build. It observes only the native mission flag and pause state.

The probe does not start, finish, fail or modify missions, saves, entities or memory. Its diagnostic log contains timestamps and game/runtime state only.

Version 2 appends a new instance marker instead of clearing the diagnostic log. This preserves state records if GTA V or ScriptHookVDotNet reloads the managed script domain while entering or leaving a replay mission.

## Existing installation

The test machine already runs a GTA V Enhanced mod menu, so its Script Hook V and ASI-loading foundation is known to work. Do not replace the working ASI loader or `ScriptHookV.dll` during this test.

ScriptHookVDotNet Enhanced `1.1.0.6` requires:

- C++ Script Hook V;
- .NET Framework 4.8 or newer;
- Microsoft Visual C++ Redistributable for Visual Studio 2019 x64.

Do not install or repair Windows dependencies pre-emptively. Check them only if ScriptHookVDotNet Enhanced fails to load.

## Install ScriptHookVDotNet Enhanced

1. Exit GTA V completely.
2. Back up the GTA V Enhanced root folder or at least any existing files with `ScriptHookVDotNet` in their names.
3. Open Steam, right-click **Grand Theft Auto V Enhanced**, choose **Manage**, then **Browse local files**.
4. Confirm that this folder contains `GTA5_Enhanced.exe` and the already-working `ScriptHookV.dll`.
5. Open `ScriptHookVDotNetEnhanced-v1.1.0.6.zip`.
6. Copy these four matching files into the folder containing `GTA5_Enhanced.exe`:

```text
ScriptHookVDotNet.asi
ScriptHookVDotNet2.dll
ScriptHookVDotNet3.dll
ScriptHookVDotNet.ini
```

Do not copy the `Docs` or SDK folders, `.pdb` files, or another ASI loader. Do not mix files from different ScriptHookVDotNet Enhanced versions.

7. Create a folder named `scripts` beside `GTA5_Enhanced.exe` if it does not already exist.
8. Copy `ghmr_gtav_probe.3.cs` into that `scripts` folder. Keep the `.3.cs` ending exactly as supplied.

Expected layout:

```text
GTA5_Enhanced.exe
ScriptHookV.dll
ScriptHookVDotNet.asi
ScriptHookVDotNet2.dll
ScriptHookVDotNet3.dll
ScriptHookVDotNet.ini
scripts\
  ghmr_gtav_probe.3.cs
```

## Run the controlled test

1. Delete an existing `%LOCALAPPDATA%\GHMR\ghmr_gtav_probe.log` once before launching, so the returned file contains only this controlled run.
2. Launch GTA V Enhanced using the same Story Mode method that already loads the mod menu. Keep BattlEye disabled as required for single-player ASI mods.
3. Enter Story Mode and wait in ordinary free roam for at least ten seconds.
4. Confirm that **GHMR GTA V probe active** appears briefly as a subtitle.
5. From the pause menu, start any short **Replay Mission**.
6. Once gameplay begins, remain in the mission for at least ten seconds. Do not use the mod menu during this test.
7. Deliberately fail once and select **Retry**.
8. After the retry begins, either complete the mission or exit back to free roam through the normal mission-failed options.
9. Wait in free roam for at least ten seconds, then exit the game normally.

Send back both files:

```text
<GTA V Enhanced folder>\ScriptHookVDotNet.log
%LOCALAPPDATA%\GHMR\ghmr_gtav_probe.log
```

The GHMR log may contain several `new probe instance` sections if the managed script domain reloads. Across those sections it should show free roam as `missionActive=False`, the replay as `missionActive=True`, and free roam after completion or exit as `missionActive=False`.

## Observed result

Verified on 22 September 2026 with ScriptHookVDotNet Enhanced `1.1.0.6` (API assembly `3.9.0.0`) and the installed GTA V Enhanced build reported by the API as game version `1012`:

- the raw `.3.cs` probe loaded successfully;
- ordinary free roam reported `missionActive=False`;
- starting the replay changed the flag from `False` to `True`;
- the flag stayed `True` through a deliberate failure and Retry, so a retry does not look like a new mission roll;
- leaving the replay changed the flag from `True` to `False`;
- a later managed-script stop and restart was retained correctly by probe v2.

This validates `Game.IsMissionActive` as the GTA V adapter's baseline lifecycle signal. It does not, on its own, distinguish successful completion from manually quitting a replay, so Normal-mode progression must require a separate completion signal before accepting the final `True -> False` transition.

The follow-up is the read-only Derailed lifecycle probe documented in
`GTAV-DERAILED-LIFECYCLE-TEST.md`. It observes the internal `exile3` mission
and GTA V's own Retry/result controller scripts without using build-specific
memory offsets or changing game state.

## Safe rollback

If Story Mode crashes or hangs before loading, remove only these items and launch again:

```text
ScriptHookVDotNet.asi
ScriptHookVDotNet2.dll
ScriptHookVDotNet3.dll
ScriptHookVDotNet.ini
scripts\ghmr_gtav_probe.3.cs
```

Leave the previously working `ScriptHookV.dll`, ASI loader and mod menu untouched. Preserve any generated `ScriptHookVDotNet.log` for diagnosis.
