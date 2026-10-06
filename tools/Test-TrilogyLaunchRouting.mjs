import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const here = path.dirname(fileURLToPath(import.meta.url));
const root = path.resolve(here, "..");
const manager = fs.readFileSync(
  path.join(
    root,
    "src",
    "Ghmr.Controller",
    "Launch",
    "WindowsGameProcessManager.cs"
  ),
  "utf8"
);

const expectedSteamRoutes = new Map([
  ["gta3de", "steam://rungameid/1546970"],
  ["vcde", "steam://rungameid/1546990"],
  ["sade", "steam://rungameid/1547000"]
]);

for (const [game, launchUri] of expectedSteamRoutes) {
  assert.ok(manager.includes(`= "${game}";`), `${game} must be mapped`);
  assert.ok(manager.includes(`= "${launchUri}";`), `${game} must use ${launchUri}`);
}

assert.match(manager, /IsSteamInstall\(profile\.LaunchExecutablePath\)/);
assert.match(manager, /TryGetDefinitiveEditionSteamLaunchUri/);
assert.match(manager, /FileName = steamUri/);
assert.match(manager, /RockstarCommerceProviderArgument = "-scCommerceProvider=4"/);
assert.match(manager, /directStart\.Arguments = RockstarCommerceProviderArgument/);

const steamRouting = manager.indexOf(
  "TryGetDefinitiveEditionSteamLaunchUri(profile.Game"
);
const directStart = manager.indexOf("ProcessStartInfo directStart");
assert.ok(
  steamRouting >= 0 && directStart > steamRouting,
  "Steam Trilogy routing must be selected before the direct-executable fallback"
);

console.log("Trilogy platform launch-routing source checks passed.");
