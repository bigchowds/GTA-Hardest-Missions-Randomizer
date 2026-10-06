# Controller App: First Implementation Milestone

The external controller is the only component allowed to own the hidden run order, authoritative timer, failure count and cross-game progression. Game bridges report evidence; they never choose a mission or directly change the timer.

This milestone contains:

- readable controller diagnostics under `logs/GHMR-controller.log`, available
  through **Options -> Open Logs** during a run and after stopping it;
- completion-event delivery before INI acknowledgement writes, so a failed
  acknowledgement cannot silently discard an accepted incoming frame;
- a deterministic, locked fifteen-mission shuffle using a cryptographically generated internal seed;
- no reroll after `missionFailed`;
- separate gameplay and real-elapsed timers;
- atomic `active-run.json` state replacement after accepted state changes;
- append-only JSONL audit records written through to disk;
- duplicate-message, stale-run and stale-bridge-session rejection;
- automatic invalidation if the controller is restarted during a run;
- redaction of the internal seed and every unrevealed mission from the on-disk active state;
- a local-current-user INI file transport implementing protocol v1 through
  CLEO Redux's existing IniFiles64 extension;
- a San Andreas bridge that accepts only the three catalogued mission ids and
  preserves the same process session across CLEO's Retry-time script reload,
  with progress or `Missions Passed` completion evidence that works at the
  100% progress ceiling;
- a GTA III bridge with source-checked mission indices and mission-specific
  reward evidence rather than a generic `ONMISSION` completion rule;
- a Vice City bridge that launches Demolition Man by source-checked mission
  index and accepts only its exact scripted reward as completion evidence;
- a GTA IV ScriptHookDotNet bridge that finds Packie's live native marker,
  lets GTA IV's own mission manager launch Three Leaf Clover, preserves native
  phone Retry and requires its exact reward plus completion cue;
- a GTA V ScriptHookDotNet bridge that validates the live mission catalog,
  requests Derailed through Rockstar's native mission-repeat controller,
  confirms the restore-point Alert with one GTA-foreground-only Windows Enter
  key pair from a short-lived background timer,
  preserves GTA V's Retry flow and accepts only the verified result-cleanup
  signature as completion;
- an idempotent CLEO definition patcher and **Setup Games & Bridges** screen;
- a fixed five-game integration route plus isolated San Andreas, GTA III,
  Vice City, GTA IV and GTA V development-test buttons;
- guarded fade, normal process close and next-game launch, with no force-kill
  fallback;
- platform-correct Trilogy startup: Steam App IDs for Steam installs and
  `-scCommerceProvider=4` for Rockstar installs;
- a three-second post-close cleanup period before every cross-game launch;
- non-blocking cross-game process discovery, so a destination game's
  `bridgeReady` event cannot wait behind a launcher timeout;
- a two-minute, trilogy-only startup watcher that recognises Rockstar's exact
  **Connecting to Social Club** continue/cancel dialog and clicks its native OK
  button without global keyboard input;
- a fresh per-game INI transport generation at each handoff and current-game
  connection labels rather than stale generic connection status;
- a Windows Forms status UI with keyboard and XInput navigation;
- a simulated bridge and dependency-free self-test executable.

The San Andreas controller path has passed its first compiled in-game launch,
completion and handoff signal. A later run on a true 100% save exposed that
completion percentage alone cannot rise at the cap; bridge v0.1.4 therefore
also observes the game's `Missions Passed` counter. Both successful completion
and failure-then-Retry at 100% are regression-tested. GTA III's first handoff exposed a Windows file-
sharing race in the INI controller writer; the revised writer updates its small
frame in place with read/write sharing rather than renaming a file while CLEO
has it open. A second installed test proved that transport and directly started
Espresso-2-Go!, then exposed that GTA III can clear `ONMISSION` while the
internally launched mission remains playable. Later testing with a
story-complete baseline reached all nine targets and showed that Definitive
Edition's Retry returns an internally launched mission to free roam instead of
restarting it. Bridge v0.1.4 retains the locked context across the transient
mission-state change, reports explicit death/arrest or Retry-time runtime reload
as failure, waits for three seconds of stable free roam, and explicitly launches
the same mission index again. It limits fade-out to a successful release and
preserves the vanilla mission setup without adding a vehicle. The simulator and
adapter tests prove the protocol sequence, including completion, Retry-time
runtime reload recovery, same-index relaunch and the no-reroll invariant. A
guarded development probe separately
passed direct launch, objective failure, Retry and completion of the original
**Wrong Side of the Tracks** mission. These two proven halves must now be joined
in game before the adapter is marked verified.

The readable INI transport is reused by SA DE, III DE and Vice City DE. GTA IV
and GTA V use readable ScriptHookDotNet `.cs` adapters that write the same
bounded controller protocol to the user-local IPC directory. All five game
paths have passed isolated mission testing. The controller silently retries a
ScriptHook INI frame seen during its in-place rewrite instead of replacing
useful status text with a harmless incomplete-frame warning.

Rockstar Games Launcher can intermittently leave San Andreas DE, GTA III DE or
Vice City DE behind a standard Windows message box saying it is connecting to
Social Club and offering **OK** to continue or **Cancel** to quit. Until that
box closes, CLEO cannot connect to GHMR. Controller v0.3.4 arms its watcher
before starting a Definitive Edition process, requires both exact English
message lines and both buttons, and posts `BM_CLICK` only to that dialog's OK
control. It never synthesises Enter, so the acknowledgement cannot accidentally
select an intro-screen or new-game option. Other Social Club errors are left
visible for the player. The delayed no-bridge warning also names this dialog as
the first manual check instead of immediately misdiagnosing it as a damaged
bridge installation.

A live San-Andreas-to-GTA-III run then reached GTA III's splash artwork but
never loaded CLEO; Task Manager was required to close it. GTA III remained much
more reliable when started independently. Controller v0.3.5 added a ten-second
post-close delay as a conservative diagnostic workaround.

The affected install is Steam-owned, and its GTA III splash displayed the Steam
overlay. Controller v0.3.6 fixes the ownership handoff instead: GTA III DE,
Vice City DE and San Andreas DE launch through Steam App IDs `1546970`,
`1546990` and `1547000` when their configured executable is inside a Steam
library. Rockstar-owned copies launch their gameplay executable with the
metadata-defined `-scCommerceProvider=4` argument. The exact dialog confirmer
remains only as a fallback, and the post-close delay is reduced to three
seconds. This delay is outside gameplay timing. GHMR still does not preload,
suspend or keep other games resident; those approaches retain competing Unreal
Engine, Social Club, RAM and GPU state and are unsuitable for public hardware.

The next live attempt reached stable GTA III free roam and loaded bridge
v0.1.4. Its `bridgeReady` frame was acknowledged by the controller's INI
transport, but no `prepareMission` command was returned. Both San Andreas and
GTA III had used the locally generated message ID `b1`; the run engine was
deduplicating that bare ID across every game. Controller v0.3.7 uses the game,
bridge-instance session and local message ID together as the deduplication key.
This preserves retransmission protection without suppressing the destination
game's first handshake.

## Game bridge installation

The CI release package includes the readable transport and San Andreas, GTA
III, Vice City and GTA IV scripts. Choose **Setup Games & Bridges**, select a
game, browse to `SanAndreas.exe`, `LibertyCity.exe`, `ViceCity.exe` or the inner
`GTAIV\GTAIV.exe`, then choose
**Install/Repair Bridge**. The installer:

1. verifies the matching bridge runtime exists: CLEO Redux x64 plus IniFiles
   for the Definitive Edition trilogy, or ScriptHookDotNet for GTA IV;
2. installs the trilogy's `ghmr_*_bridge[fs].js` with its user-local IPC path,
   or GTA IV's `scripts\ghmr_gta4_bridge.cs`;
3. adds or repairs the trilogy's two INI command definitions without changing
   other CLEO extensions; removes obsolete GHMR-only adapter files; and removes
   the old GHMR native plugin and superseded development probes on repair.

If the game is under `Program Files`, Windows may require running the controller
as administrator for this installation step only. Normal runs remain at ordinary
current-user privilege.

## Build on Windows

Install the official .NET 8 SDK, then run from the repository root:

```powershell
dotnet restore .\GHMR.sln
dotnet build .\GHMR.sln --configuration Release
dotnet run --project .\tests\Ghmr.Core.SelfTest\Ghmr.Core.SelfTest.csproj --configuration Release
```

Start the controller:

```powershell
dotnet run --project .\src\Ghmr.Controller\Ghmr.Controller.csproj --configuration Release
```

Click **Test All 5 Games** for the deterministic integration route. See
`docs/FIVE-GAME-HANDOFF-TEST.md`. The fifteen-mission button stays disabled
until every selected game adapter is verified. The UI reveals only the current mission. Normal mode
remains code-locked while any selected mission has an unverified bridge. Chaos
remains explicitly disabled until Normal-mode playtesting is complete and
modifier rules are implemented.

## Simulated bridge

The original named-pipe simulator remains in source for protocol development.
It is not connected to the INI controller in this build. The automated engine
and adapter tests run from the GitHub workflow.

## Local data

Development run state is stored under `%LOCALAPPDATA%\GHMR\controller`. Runtime state and audit logs are excluded from public source packages. While a run is active, the state file contains only completed missions and the currently revealed mission; future mission ids and the internal seed are not written. A controller restart marks any unfinished run as `Interrupted`; it never silently resumes a completed result.

Game launch profiles are stored separately in the same local directory. They
may contain personal installation paths, so the controller never copies them
into an audit log or release package.
