# Controller v0.4.10: active game exit detection

The v0.4.9 GTA III test reached Running and advanced the gameplay timer, but
closing GTA III manually left the run Running. That controller had no process
lifetime observer after the bridge connected. This follow-up adds one.

## Behaviour

- An accepted mission arms a watch for the identified gameplay process.
- Closing or crashing that process during Preparing, Running or Restarting
  stops the unfinished run and freezes both timers. It does not count a
  mission completion/failure, advance the plan or relaunch the game.
- The controller hides transition media, requests an audio stop, displays
  Stopped with an Inactive bridge and allows starting a fresh run.
- The exit decision is serialized with bridge events and checked atomically
  against run id, mission index, game and bridge session. Late exits cannot
  abort a replacement run, the next mission or a completed run.
- Ambiguous/inaccessible process matches are logged without claiming an exit.
- Controller shutdown cancels and observes outstanding exit watches.

This does not detect a frozen process that remains alive. It does not change
game mission scripts, launch timing or the v0.4.9 INI transport.

## Update and test

1. Upload src, tests, tools, docs and README.md from the source-update archive to
   the existing repository root. Upload build.yml directly to .github/workflows
   last. The separate workflow zip keeps build.yml visible at its root.
2. Use the successful Actions run created by that last commit. Its artifact is
   GHMR-controller-v0.4.10-win-x64. Extract the artifact, then its inner
   GHMR-v0.4.10-game-exit-fix-win-x64.zip into a fresh folder.
3. Confirm Options shows GHMR v0.4.10 game exit fix. With a working v0.4.9
   installation, no bridge repair is required: all game-side files are identical.
4. Test GTA III alone. Once Espresso is playable, close GTA III normally before
   completing it. GHMR should show Stopped/Inactive shortly after process exit,
   both timers should stop and Start Run should become available.
5. Start another test. Complete the mission: the run should remain Complete
   after closing the final game. Then test the five-game handoff route.

If a test fails, retain GHMR-controller.log. The new ProcessExit entries identify
the watched process and whether its exit stopped the run or was ignored after
mission/run advancement.

## Verification

The local Node source checks cover production wiring, atomic identity checks,
serialized exit decisions, terminal UI and absence of automatic relaunch.
C# core tests cover exits in three active phases, timer freezing, persistence,
stale identity rejection, replacement runs, same-game/cross-game advancement
and completed runs. A coordinator integration test drives an injected process
exit through the actual controller and checks stopped state, media and timers.
Windows compilation and these behavioural C# tests must pass in Actions.
