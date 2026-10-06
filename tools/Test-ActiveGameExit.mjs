import assert from "node:assert/strict";
import fs from "node:fs";

const coordinator = fs.readFileSync("src/Ghmr.Controller/ControllerCoordinator.cs", "utf8");
const engine = fs.readFileSync("src/Ghmr.Core/Run/RunEngine.cs", "utf8");
const processManager = fs.readFileSync("src/Ghmr.Controller/Launch/WindowsGameProcessManager.cs", "utf8");
const program = fs.readFileSync("src/Ghmr.Controller/Program.cs", "utf8");
const ui = fs.readFileSync("src/Ghmr.Controller/Ui/MainForm.cs", "utf8");
const workflow = fs.readFileSync(".github/workflows/build.yml", "utf8");
const diagnosticProject = fs.readFileSync("tests/Ghmr.ControllerLog.SelfTest/Ghmr.ControllerLog.SelfTest.csproj", "utf8");

assert.ok(program.includes("engine, bridgeServer, handoff, log, profiles, processes"),
    "The production coordinator must receive profiles and the process exit monitor.");
assert.ok(processManager.includes("IGameProcessExitMonitor") &&
    processManager.includes("await process.WaitForExitAsync(cancellationToken)"),
    "Watch the gameplay process lifetime, not a bridge heartbeat timeout.");
const watch = coordinator.slice(coordinator.indexOf("private async Task WatchActiveGameExitAsync"),
    coordinator.indexOf("private async Task HandleBridgeMessageCoreAsync"));
assert.ok(watch.includes("AbortIfActiveGameExited") && watch.includes("HideTransition()") &&
    watch.includes("TransitionAudioStopRequested?.Invoke") && watch.includes("_eventGate.WaitAsync"),
    "Exit decisions must serialize with bridge events and stop transition media.");
assert.ok(!/LaunchAsync|TransitionAsync|BeginRunAsync|RestartMission/.test(watch),
    "A manual exit must not auto-relaunch or advance to another game.");
assert.ok(coordinator.includes("result.Action == ControllerAction.PrepareMission") &&
    coordinator.includes("Task.WhenAll(_gameExitWatches)"),
    "Arm each accepted mission, including same-game advancement, and cancel/observe watches on shutdown.");
const guard = engine.slice(engine.indexOf("public bool AbortIfActiveGameExited"), engine.indexOf("public RunSnapshot Abort("));
for (const identity of ["_state.RunId != runId", "_state.CurrentIndex != currentIndex",
    "_state.ActiveGame != game", "_state.ActiveBridgeSessionId != bridgeSessionId"])
    assert.ok(guard.includes(identity), `Exit guard must check ${identity}.`);
assert.ok(guard.includes("lock (_sync)") && guard.includes("RunPhase.Preparing or RunPhase.Running or RunPhase.Restarting"),
    "Only the identified active mission may stop; exclude transitions and terminal states.");
assert.ok(ui.includes('_bridgeStatusValue.Text = snapshot.IsTerminal') && ui.includes('? "Inactive"'),
    "Stopped runs must not display an active bridge session.");
assert.ok(workflow.includes("node tools/Test-ActiveGameExit.mjs") &&
    diagnosticProject.includes("ControllerCoordinator.cs"), "Actions must exercise coordinator exit coverage.");
console.log("Active-game exit wiring/safety source checks passed; behavioural C# tests run in Actions.");
