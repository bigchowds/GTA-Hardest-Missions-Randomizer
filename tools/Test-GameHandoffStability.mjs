import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const here = path.dirname(fileURLToPath(import.meta.url));
const root = path.resolve(here, "..");
const handoff = fs.readFileSync(
  path.join(root, "src", "Ghmr.Core", "Launch", "GameHandoff.cs"),
  "utf8"
);
const coordinator = fs.readFileSync(
  path.join(root, "src", "Ghmr.Controller", "ControllerCoordinator.cs"),
  "utf8"
);
const overlay = fs.readFileSync(
  path.join(
    root,
    "src",
    "Ghmr.Controller",
    "Ui",
    "TransitionOverlayForm.cs"
  ),
  "utf8"
);
const transitionMedia = fs.readFileSync(
  path.join(
    root,
    "src",
    "Ghmr.Controller",
    "Ui",
    "TransitionMedia.cs"
  ),
  "utf8"
);
const optionsForm = fs.readFileSync(
  path.join(root, "src", "Ghmr.Controller", "Ui", "OptionsForm.cs"),
  "utf8"
);
const preferences = fs.readFileSync(
  path.join(root, "src", "Ghmr.Controller", "ControllerPreferencesStore.cs"),
  "utf8"
);
const mainForm = fs.readFileSync(
  path.join(root, "src", "Ghmr.Controller", "Ui", "MainForm.cs"),
  "utf8"
);
const gameWindowFocus = fs.readFileSync(
  path.join(
    root,
    "src",
    "Ghmr.Controller",
    "Launch",
    "GameWindowFocus.cs"
  ),
  "utf8"
);
const processManager = fs.readFileSync(
  path.join(
    root,
    "src",
    "Ghmr.Controller",
    "Launch",
    "WindowsGameProcessManager.cs"
  ),
  "utf8"
);
const initialLaunchBlock = coordinator.slice(
  coordinator.indexOf("private async Task<RunSnapshot> BeginStartedRunAsync"),
  coordinator.indexOf("private async Task WarnIfBridgeMissingAsync")
);

assert.match(
  handoff,
  /_launcherSettleDelay = launcherSettleDelay \?\? TimeSpan\.FromSeconds\(3\)/,
  "cross-game handoffs must default to a three-second cleanup delay"
);

const closeIndex = handoff.indexOf("RequestGracefulCloseAsync(");
const settleIndex = handoff.indexOf(
  "WaitAsync(_launcherSettleDelay, cancellationToken)",
  closeIndex
);
const launchIndex = handoff.indexOf(
  "_processes.LaunchAsync(nextProfile, cancellationToken)",
  closeIndex
);
assert.ok(closeIndex >= 0, "the completed process must be closed");
assert.ok(
  settleIndex > closeIndex && launchIndex > settleIndex,
  "the launcher-settle delay must occur after close and before the next launch"
);

assert.match(coordinator, /preparing the platform handoff to/);
assert.match(handoff, /GameHandoffStage\.ClosingCompletedGame/);
assert.match(handoff, /GameHandoffStage\.SettlingPlatform/);
assert.match(handoff, /GameHandoffStage\.LaunchingNextGame/);
assert.match(handoff, /GameHandoffStage\.NextGameDetected/);
assert.match(coordinator, /TransitionChanged/);
assert.match(
  coordinator,
  /public sealed class TransitionDisplayEventArgs : EventArgs/,
  "transition event data must use a class because records cannot inherit EventArgs"
);
assert.doesNotMatch(
  coordinator,
  /record TransitionDisplayEventArgs[\s\S]*?: EventArgs/,
  "TransitionDisplayEventArgs must not be declared as an EventArgs-derived record"
);
assert.doesNotMatch(
  initialLaunchBlock,
  /PublishTransition(?:IfWaitingForBridge)?\(/,
  "the media transition must not appear while the first game launches"
);
assert.match(
  coordinator,
  /case ControllerAction\.LaunchNextMission:[\s\S]*?PublishTransition\(/,
  "the media transition must begin only after a mission advances the run"
);
assert.match(
  coordinator,
  /message\.Type == BridgeEventTypes\.PlayerControlGained[\s\S]*?HideTransition\(\)/,
  "the handoff surface must remain until the destination is playable"
);
assert.doesNotMatch(
  coordinator,
  /message\.Type == BridgeEventTypes\.BridgeReady[\s\S]{0,80}?HideTransition\(\)/,
  "an early bridge-ready event must not dismiss the handoff surface"
);
assert.match(coordinator, /TransitionAudioStopRequested/);
const startMissionCase = coordinator.slice(
  coordinator.indexOf("case ControllerAction.StartMission:"),
  coordinator.indexOf("case ControllerAction.RestartMission:")
);
assert.ok(
  startMissionCase.indexOf("TransitionAudioStopRequested?.Invoke") >= 0 &&
    startMissionCase.indexOf("TransitionAudioStopRequested?.Invoke") <
      startMissionCase.indexOf(
        "SendCurrentMissionCommandAsync(ControllerCommandTypes.StartMission)"
      ),
  "audio cutoff must be requested before the mission-start command"
);
assert.match(overlay, /WsExNoActivate/);
assert.match(overlay, /SizeType\.Absolute, 216F/);
assert.match(overlay, /root\.Controls\.Add\(_backdrop, 0, 0\)/);
assert.match(overlay, /root\.Controls\.Add\(footerHost, 0, 1\)/);
assert.match(overlay, /GAMEPLAY TIMER PAUSED DURING HANDOFF/);
assert.match(overlay, /CLOSING CURRENT GAME/);
assert.match(overlay, /LAUNCHING NEXT GAME/);
assert.match(overlay, /CONNECTING BRIDGE/);
assert.match(overlay, /_gameLabel\.Text = state\.Game/);
assert.match(overlay, /_missionLabel\.Text = state\.Mission/);
assert.doesNotMatch(overlay, /NEXT MISSION HIDDEN/);
assert.doesNotMatch(overlay, /RevealNextMission/);
assert.doesNotMatch(overlay, /REVEAL NEXT MISSION/);
assert.match(overlay, /_backdrop\.Advance\(\)/);
assert.match(overlay, /_audioPlayer\.Play\(\)/);
assert.match(overlay, /public void StopMusic\(\)[\s\S]*?_audioPlayer\.Stop\(\)/);
assert.match(overlay, /_audioSafetyTimer/);
assert.match(overlay, /Interval = 60000/);
assert.match(transitionMedia, /class TransitionBackdropPanel/);
assert.match(transitionMedia, /GetContainedDestination/);
assert.doesNotMatch(transitionMedia, /GetCoverSource/);
assert.match(transitionMedia, /class TransitionAudioPlayer/);
assert.match(transitionMedia, /setaudio .* volume to/);
assert.match(transitionMedia, /if \(!_enabled/);
assert.match(transitionMedia, /public void Stop\(\)[\s\S]*?stop \{_alias\}[\s\S]*?close \{_alias\}/);
assert.match(mainForm, /CreateCompactButton\("OPTIONS"\)/);
assert.match(mainForm, /TransitionMusicEnabled/);
assert.match(mainForm, /TransitionAudioStopRequested[\s\S]*?ScheduleTransitionAudioStop/);
assert.match(
  mainForm,
  /ScheduleTransitionAudioStop\(\)[\s\S]*?BeginInvoke\(\(Action\)\(\(\) => _transitionOverlay\?\.StopMusic\(\)\)\)/,
  "audio must be stopped asynchronously on the UI thread"
);
assert.match(mainForm, /_transitionOverlay\.Show\(\)/);
assert.doesNotMatch(
  mainForm,
  /_transitionOverlay\.Show\(this\)/,
  "the handoff overlay must not reactivate the controller through form ownership"
);
assert.match(mainForm, /WindowState = FormWindowState\.Minimized/);
assert.match(mainForm, /MoveControllerAwayFrom\(gameScreen\)/);
assert.match(mainForm, /GameWindowFocus\.TryActivate\(gameWindow\)/);
assert.match(gameWindowFocus, /SetForegroundWindow/);
assert.match(gameWindowFocus, /AttachThreadInput/);
assert.match(mainForm, /Interval = 120000/);
assert.match(optionsForm, /Play music during cross-game handoffs/);
assert.match(optionsForm, /OPEN LOGS/);
assert.match(preferences, /preferences\.json/);
for (const asset of [
  "01-every-gta-different.jpg",
  "02-rarest.jpg",
  "03-cheats.jpg",
  "04-softlock.jpg",
  "05-logic.jpg",
  "06-best-gun.jpg",
  "transition-theme.mp3"
]) {
  assert.ok(
    fs.statSync(
      path.join(
        root,
        "src",
        "Ghmr.Controller",
        "Assets",
        "Transitions",
        asset
      )
    ).size > 0,
    `${asset} must be packaged with the controller`
  );
}
assert.match(mainForm, /_missionValue\.Text = mission\?\.Title \?\? "—"/);
assert.match(mainForm, /ControllerCoordinator\.DisplayGame\(mission\.Game\)/);
assert.doesNotMatch(mainForm, /Locked until gameplay begins/);
assert.doesNotMatch(mainForm, /Next game hidden/);
assert.match(processManager, /CloseRetryInterval = TimeSpan\.FromSeconds\(2\)/);
assert.match(processManager, /while \(closeTimer\.Elapsed < timeout\)/);
assert.match(processManager, /bool requestAccepted = process\.CloseMainWindow\(\)/);
assert.match(processManager, /closeRequestAccepted \|= requestAccepted/);
const completedClose = processManager.slice(
  processManager.indexOf("public async Task<GameProcessCloseResult> RequestCompletedGameCloseAsync"),
  processManager.indexOf("public async Task<GameProcessCloseResult> RequestGracefulCloseAsync")
);
const gracefulClose = processManager.slice(
  processManager.indexOf("public async Task<GameProcessCloseResult> RequestGracefulCloseAsync")
);
assert.match(handoff, /completedGame == "gtav_enhanced" &&[\s\S]*?ICompletedGameProcessManager/);
assert.match(completedClose, /if \(profile\.Game != Gta5EnhancedGameId\)/);
assert.match(completedClose, /CompletedGameClosePolicy\.CanTerminateGtaV/);
assert.match(completedClose, /process\.MainModule\?\.FileName/);
assert.match(completedClose, /process\.Kill\(entireProcessTree: false\)/);
assert.match(completedClose, /await process\.WaitForExitAsync\(exitDeadline\.Token\)/);
assert.equal((processManager.match(/\.Kill\(/g) ?? []).length, 1,
  "only the verified completed-GTA-V path may terminate a gameplay process");
assert.doesNotMatch(gracefulClose, /\.Kill\(/,
  "ordinary game closure must remain graceful");
assert.doesNotMatch(handoff + processManager,
  /entireProcessTree: true|SuspendThread|NtSuspendProcess|SetProcessWorkingSetSize/,
  "handoffs must never terminate launcher trees, suspend or trim games");
assert.match(coordinator, /Waiting for completed GTA V manual exit/);
assert.match(coordinator, /ResumeAfterCompletedGameExitAsync/);
assert.match(coordinator, /CancelHandoff\(\)/);

console.log("Game handoff stability source checks passed.");
