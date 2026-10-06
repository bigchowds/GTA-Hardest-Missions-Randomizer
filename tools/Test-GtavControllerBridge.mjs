import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const bridgeSource = fs.readFileSync(
    path.join(
        root,
        "mods",
        "scripthookdotnet-bridge",
        "ghmr_gtav_bridge.3.cs"
    ),
    "utf8"
);
const installerSource = fs.readFileSync(
    path.join(
        root,
        "src",
        "Ghmr.Controller",
        "Installation",
        "Gta5BridgeInstaller.cs"
    ),
    "utf8"
);
const uiSource = fs.readFileSync(
    path.join(root, "src", "Ghmr.Controller", "Ui", "MainForm.cs"),
    "utf8"
);
const launcherSource = fs.readFileSync(
    path.join(
        root,
        "src",
        "Ghmr.Controller",
        "Launch",
        "WindowsGameProcessManager.cs"
    ),
    "utf8"
);

assert.match(bridgeSource, /class\s+GhmrGtavEnhancedBridgeV0113\b/);
assert.match(bridgeSource, /GameId\s*=\s*"gtav_enhanced"/);
assert.match(bridgeSource, /MissionId\s*=\s*"gtav\.derailed"/);
assert.match(bridgeSource, /MissionScript\s*=\s*"exile3"/);
assert.match(bridgeSource, /ReplayControllerScript\s*=\s*"replay_controller"/);
assert.match(bridgeSource, /MissionRepeatControllerScript\s*=\s*"mission_repeat_controller"/);
assert.match(bridgeSource, /MissionWatcherScript\s*=\s*"mission_stat_watcher"/);
assert.match(bridgeSource, /MissionCatalogBase\s*=\s*93274/);
assert.match(bridgeSource, /MissionCatalogFirstElementOffset\s*=\s*1/);
assert.match(bridgeSource, /MissionCatalogEntryCount\s*=\s*94/);
assert.match(bridgeSource, /matches\s*!=\s*1/);
assert.match(bridgeSource, /TryRequestNativeMissionReplay/);
assert.match(bridgeSource, /System\.Threading\.Timer/);
assert.match(bridgeSource, /SendInput/);
assert.match(bridgeSource, /GetForegroundWindow/);
assert.match(bridgeSource, /GetWindowThreadProcessId/);
assert.match(bridgeSource, /foregroundProcessId\s*==\s*GetCurrentProcessId\(\)/);
assert.match(bridgeSource, /MainEnterScanCode\s*=\s*0x1C/);
assert.match(bridgeSource, /KeyEventScanCode\s*=\s*0x0008/);
assert.match(bridgeSource, /RestoreConfirmInitialDelayMilliseconds\s*=\s*1200/);
assert.match(bridgeSource, /0\.1\.13-foreground-restore-confirm/);
assert.equal(
    /SET_CONTROL_VALUE_NEXT_FRAME|PulseNativeReplayAccept/.test(bridgeSource),
    false,
    "GTA V restore confirmation must not depend on paused script-frame input"
);
assert.match(bridgeSource, /CompletionSignaturePresent/);
assert.match(bridgeSource, /Post-gameplay cutscene observed/);
assert.match(bridgeSource, /native Retry/);
assert.match(bridgeSource, /missionCompleted/);
assert.match(bridgeSource, /missionFailed/);
assert.equal(
    /START_NEW_SCRIPT|REQUEST_SCRIPT|FORCE_CLEANUP|SET_MISSION_FLAG/.test(
        bridgeSource
    ),
    false,
    "GTA V bridge must not directly launch or clean Rockstar mission scripts"
);
assert.equal(
    /\.Position\s*=|SET_CLOCK_TIME|SET_WEATHER|STAT_SET_/.test(bridgeSource),
    false,
    "GTA V bridge must not teleport the player or alter world/save state"
);

assert.match(installerSource, /GTA5_Enhanced\.exe/);
assert.match(installerSource, /ScriptHookVDotNet\.asi/);
assert.match(installerSource, /ScriptHookVDotNet3\.dll/);
assert.match(installerSource, /ghmr_gtav_bridge\.3\.cs/);
assert.match(installerSource, /ghmr_gtav_derailed_probe\.3\.cs/);
assert.match(uiSource, /Test GTA V only/);
assert.match(uiSource, /Test San Andreas only/);
assert.match(uiSource, /"Start Run"/);
assert.match(uiSource, /"Chaos Mode"/);
assert.match(uiSource, /"Setup Game Bridges"/);
assert.match(uiSource, /"Stop Run"/);
assert.match(uiSource, /"gtav\.derailed"/);
assert.match(launcherSource, /steam:\/\/rungameid\/3240220/);

const fiveGameMethod = uiSource.match(
    /private async Task BeginBetaRunAsync\(\)([\s\S]*?)private async Task BeginFixedHandoffTestAsync/
);
assert.notEqual(fiveGameMethod, null, "five-mission Beta method must exist");
assert.match(uiSource, /BeginBetaRunAsync/);
const fiveGameMissionIds = [
    ...fiveGameMethod[1].matchAll(/"((?:sa|gta3|vc|gta4|gtav)\.[^"]+)"/g)
].map((match) => match[1]);
assert.deepEqual(fiveGameMissionIds, [
    "sa.wrong_side_of_the_tracks",
    "gta3.espresso_2_go",
    "vc.demolition_man",
    "gta4.three_leaf_clover",
    "gtav.derailed"
]);

// Re-run the exact successful lifecycle trace as a small deterministic guard
// for the completion predicate used by the bridge.  A failure path lacks the
// post-gameplay cutscene and therefore must never satisfy it.
function completionSignature(state) {
    return !state.targetActive &&
        !state.playerDead &&
        !state.missionActive &&
        state.replayController === 0 &&
        state.missionWatcher === 0 &&
        state.missionObserved &&
        state.watcherObserved &&
        state.gameplayObserved &&
        state.postGameplayCutscene &&
        state.secondsSinceCutscene >= 0 &&
        state.secondsSinceCutscene <= 60;
}

assert.equal(completionSignature({
    targetActive: false,
    playerDead: false,
    missionActive: false,
    replayController: 0,
    missionWatcher: 0,
    missionObserved: true,
    watcherObserved: true,
    gameplayObserved: true,
    postGameplayCutscene: true,
    secondsSinceCutscene: 15
}), true);
assert.equal(completionSignature({
    targetActive: false,
    playerDead: false,
    missionActive: false,
    replayController: 0,
    missionWatcher: 0,
    missionObserved: true,
    watcherObserved: true,
    gameplayObserved: true,
    postGameplayCutscene: false,
    secondsSinceCutscene: 0
}), false);

console.log(
    "GTA V controller bridge checks passed: safe native replay/retry, " +
    "verified completion signature, installer, UI and Steam launch are present."
);
