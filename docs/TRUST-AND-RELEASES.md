# What GHMR does and how to verify a release

GHMR is a local Windows controller plus readable game-side bridge scripts. Its own controller runs as the current Windows user, stores settings/run state under `%LOCALAPPDATA%/GHMR/controller`, and coordinates one running GTA game at a time.

## Disclosed behavior in controller v0.4.14

- Starts configured games through the supported Steam/Rockstar launch route and communicates with local bridge files.
- Records confirmed mission events, gameplay/real timing, failures and local diagnostics.
- Installs GHMR scripts and required INI definitions into the selected game installation. It can replace/remove superseded GHMR files; it does not install third-party runtimes or delete unrelated mods.
- Plays the packaged local music and reads transition images. Transition media stops when the destination game window appears, with additional cleanup before mission launch and on controller exit.
- Moves/minimizes its UI as appropriate and tries to activate the destination game window. This beta can still need manual menu/foreground input.
- Narrowly confirms the known English Trilogy Social Club startup dialog; other errors are not generically dismissed.
- Optionally recognizes Vice City's English Resume menu and attempts a targeted Resume action.
- Captures the **foreground GTA V game window** locally for English Derailed Mission Passed/title recognition. It does not upload those captures or keep screenshots as diagnostic files.
- After an accepted GTA V completion with another game next, validates the configured `GTA5_Enhanced` executable/process identity and **directly ends that gameplay process**, waits for exit, then launches the next game. This discards the replay's unsaved state and bypasses its quit confirmation. Other games use ordinary close requests. The final game remains open after Finished. Steam and Rockstar Launcher are not termination targets.
- The GTA V bridge sends one guarded Enter pair for its restore-point Alert. Game-side scripting runtimes perform mission preparation/native replay operations; those runtimes have their own behavior and requirements.

GHMR has no telemetry, account login, auto-downloader or microphone recorder. It does not upload run data. YouTube/GitHub links open in your browser only when selected. Third-party launchers/runtimes may have separate network activity. Saves are not automatically staged or restored.

## Release verification

Use this repository's **Releases**, not a random mirror or a source-code ZIP presented as the Windows app.

Each beta package should include:

- `GHMR.Controller.exe`, config, all five readable adapters and transition media.
- `START-HERE.txt`, README, installation/compatibility/troubleshooting docs and the MIT licence.
- `release.json` identifying the package/controller versions, source commit and build run.
- `SHA256SUMS.txt` for packaged files, plus a separate ZIP `.sha256` alongside the release asset.

Build provenance attestations are generated for eligible public push builds. The workflow records the source commit; the release tag should point to that same commit. An attestation is build provenance, **not an Authenticode signature or proof that the app is safe**. No executable packer or obfuscator is used.

To compare the downloaded ZIP hash in PowerShell:

```powershell
Get-FileHash .\GHMR-v0.4.14-beta.1-win-x64.zip -Algorithm SHA256
```

Compare the returned hash with the release's `.sha256` file. Checksums detect a mismatch; downloading both from a compromised source is not independent verification. Source and CI output are available for inspection.

## Unsigned Windows app

The first beta is **unsigned**, so it can display an unknown publisher. Identify the exact Windows message before deciding what to do:

| Windows feature / message | Meaning and next step |
|---|---|
| **Microsoft Defender SmartScreen** warns about an unrecognized app | This can be a reputation warning for a new app; it is not proof that the app is safe or malicious. Verify the official release and checksum. Use only an available one-time prompt option if you have reviewed and trust the file; managed devices may not offer one. |
| **Windows 11 Smart App Control** blocks an app | It can block an unsigned app when it cannot establish trust. Microsoft currently provides **no per-app allow option**. The unsigned beta may be unavailable on that configuration; contact the maintainer. Antivirus exclusions and administrator mode do not supply a trusted signature. |
| **Microsoft Defender Antivirus** detects or quarantines a threat | Open **Windows Security > Protection history** and note the exact threat name and affected file. Report it with the GHMR version and SHA-256. Do not assume that a named detection is merely an unsigned-app warning or a false positive. |

Do not disable antivirus/security protections or add broad exclusions to run GHMR. A checksum, ZIP wrapper, source publication or GitHub release does not bypass Windows protection or establish that a file is safe. The maintainer should review a named detection and submit the exact artifact to the reporting vendor before distributing that detected build. Signing/reputation can be improved later, but neither replaces review or transparent builds.

Microsoft references: [Smart App Control FAQ](https://support.microsoft.com/en-us/windows/security/threat-malware-protection/smart-app-control-frequently-asked-questions), [Protection history](https://support.microsoft.com/en-us/windows/security/windows-security/protection-history-in-the-windows-security-app), [SmartScreen file reputation](https://learn.microsoft.com/en-us/deployedge/microsoft-edge-security-smartscreen).

Review logs before sharing: exceptions and third-party game/runtime logs can contain paths or account details. The public package contains no personal saves, private configuration, diagnostic logs, secrets or debug databases.
