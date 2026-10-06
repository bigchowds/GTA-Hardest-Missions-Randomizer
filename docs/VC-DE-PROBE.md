# Vice City Definitive Edition: Phase 0 Probe

Status: **passed** on the Rockstar Games Launcher executable `1.0.112.6680` with CLEO Redux `1.4.2` x64.

This test answers one question: **can the current Vice City DE installation load a minimal CLEO Redux JavaScript file?** It does not alter missions, progression or saves.

## 1. Find the real game executable

In Steam, open Vice City DE's Properties, choose **Installed Files**, then **Browse**.

The executable required by CLEO Redux is normally:

`Gameface\Binaries\Win64\ViceCity.exe`

Do not select only the top-level trilogy folder.

## 2. Record its version

Right-click `ViceCity.exe`, choose **Properties → Details**, and note **File version**.

Alternatively, right-click `tools\Get-GHMRCompatibility.ps1`, choose **Run with PowerShell**, and send back the Vice City section of the JSON report created on the Desktop. Review the path first if you do not want to share your Windows username.

If Windows blocks the script, open PowerShell in the extracted project directory and run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Get-GHMRCompatibility.ps1
```

## 3. Install the tested CLEO Redux build

For the tested Rockstar Games Launcher build, use CLEO Redux `1.4.2` x64 and point its installer at the folder containing `ViceCity.exe`. Allow it to install the 64-bit ASI loader.

CLEO Redux `1.5.0` x64 was also tried on the same installation. Its ASI loaded far enough to create a three-line log header, but it did not identify the host, populate `.config`, or run JavaScript. Keep `1.4.2` pinned for this executable until that regression is understood. Other game executables must be tested separately rather than assumed to have the same result.

After installation, the Win64 folder should normally contain:

- `cleo_redux64.asi`
- `version.dll`
- a `CLEO` directory (created during installation or first launch)

If CLEO cannot write there, it can use `%APPDATA%\CLEO Redux` for its folder and log instead.

## 4. Add the harmless probe

Copy:

`mods\cleo-probe\ghmr_vc_probe.js`

into the VC DE `CLEO` directory, then launch Story Mode.

Expected result:

- `cleo_redux.log` contains lines beginning with `[GHMR]`.
- The in-game message may not be visible on every build. The log is the authoritative result.

## 5. If nothing happens

Check in this order:

1. The install location is the exact `Gameface\Binaries\Win64` directory.
2. You installed the **64-bit** build and ASI loader.
3. `version.dll` and `cleo_redux64.asi` are beside `ViceCity.exe`.
4. Look for `cleo_redux.log` both beside the executable and in `%APPDATA%\CLEO Redux`.
5. If no log exists, the ASI loader did not load; temporarily rename the loader only according to CLEO Redux/Ultimate ASI Loader guidance.
6. If the log contains only the CLEO header and never identifies `vc_unreal`, confirm that CLEO Redux `1.4.2` x64—not `1.5.0`—is installed for the tested executable.
7. If the log stops at `Checking for updates...`, set `CheckUpdates=0` in `cleo.ini` and retry.
8. Verify the executable through its launcher if the base game itself behaves abnormally.

Do not add any other mods during this test. A clean one-variable test tells us whether the bridge technology works.

## What to send back

- Vice City executable file version
- Whether `cleo_redux.log` exists
- The last 20 lines of the log if the message did not appear
- Whether the older failed mod was made for classic Vice City or Definitive Edition, if known

After this probe succeeds, remove `ghmr_vc_probe.js` before installing the mission-state probe. The generated `tsconfig.json` and files in `CLEO\.config` are normal CLEO development files and can remain in place.
