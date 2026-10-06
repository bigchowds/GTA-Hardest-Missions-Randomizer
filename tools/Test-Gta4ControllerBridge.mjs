import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const bridgePath = path.join(
    root,
    "mods",
    "scripthookdotnet-bridge",
    "ghmr_gta4_bridge.cs"
);
const bridgeSource = fs.readFileSync(bridgePath, "utf8");
const installerSource = fs.readFileSync(
    path.join(
        root,
        "src",
        "Ghmr.Controller",
        "Installation",
        "Gta4BridgeInstaller.cs"
    ),
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

assert.match(bridgeSource, /class\s+GhmrGta4BridgeV017\b/);
assert.match(bridgeSource, /MissionId\s*=\s*"gta4\.three_leaf_clover"/);
assert.match(bridgeSource, /ScriptName\s*=\s*"Packie3"/);
assert.match(bridgeSource, /ExpectedReward\s*=\s*250000/);
assert.match(bridgeSource, /0\.1\.17-retained-event-delivery/);
assert.match(
    bridgeSource,
    /\(BlipType\)\(int\)BlipIcon\.Person_Packie/,
    "GTA IV bridge lost the ScriptHookDotNet v1.7.1.9 Packie-blip workaround"
);
assert.match(bridgeSource, /AttachToNativeRetry\(\)/);
assert.match(bridgeSource, /phone Retry/);
assert.equal(
    /START_NEW_SCRIPT|REQUEST_SCRIPT/.test(bridgeSource),
    false,
    "GTA IV bridge must use the native marker and never directly launch Packie3"
);

assert.match(installerSource, /ScriptHookDotNet\.asi/);
assert.match(installerSource, /ScriptHook\.dll/);
assert.match(installerSource, /ghmr_gta4_bridge\.cs/);
assert.match(installerSource, /scriptsDirectory/);
assert.equal(
    installerSource.includes("CleoBridgeInstaller.Install"),
    false,
    "GTA IV installer must not restore the obsolete CLEO bridge"
);

assert.match(launcherSource, /steam:\/\/rungameid\/12210/);
assert.match(launcherSource, /IsSteamInstall/);

console.log(
    "GTA IV release bridge checks passed: native marker, safe Retry, " +
    "ScriptHookDotNet installer and normal Steam launch are present."
);
