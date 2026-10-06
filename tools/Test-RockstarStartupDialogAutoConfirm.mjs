import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const here = path.dirname(fileURLToPath(import.meta.url));
const root = path.resolve(here, "..");
const confirmerPath = path.join(
  root,
  "src",
  "Ghmr.Controller",
  "Launch",
  "RockstarStartupDialogAutoConfirmer.cs"
);
const processManagerPath = path.join(
  root,
  "src",
  "Ghmr.Controller",
  "Launch",
  "WindowsGameProcessManager.cs"
);
const coordinatorPath = path.join(
  root,
  "src",
  "Ghmr.Controller",
  "ControllerCoordinator.cs"
);

const confirmer = fs.readFileSync(confirmerPath, "utf8");
const processManager = fs.readFileSync(processManagerPath, "utf8");
const coordinator = fs.readFileSync(coordinatorPath, "utf8");

const supportedStart = confirmer.indexOf("SupportedGames = new(");
const supportedEnd = confirmer.indexOf("StringComparer.Ordinal);", supportedStart);
assert.ok(supportedStart >= 0 && supportedEnd > supportedStart);
const supportedBlock = confirmer.slice(supportedStart, supportedEnd);

for (const game of ["sade", "gta3de", "vcde"]) {
  assert.ok(
    supportedBlock.includes(`"${game}"`),
    `${game} must enable the Definitive Edition startup-dialog guard`
  );
}
for (const game of ["gta4", "gtav_enhanced"]) {
  assert.ok(
    !supportedBlock.includes(`"${game}"`),
    `${game} must not enable the trilogy-only startup-dialog guard`
  );
}

assert.match(confirmer, /Connecting to Social Club/);
assert.match(confirmer, /Press OK to continue or Cancel to quit\./);
assert.match(confirmer, /DialogClassName = "#32770"/);
assert.match(confirmer, /IdOk = 1/);
assert.match(confirmer, /IdCancel = 2/);
assert.match(confirmer, /WmGetText = 0x000D/);
assert.match(confirmer, /BmClick = 0x00F5/);
assert.match(confirmer, /PostMessageW\(okButton, BmClick/);
assert.doesNotMatch(
  confirmer,
  /SendInput|SendKeys|keybd_event|SetForegroundWindow/,
  "startup handling must target the native OK button, never global keyboard input"
);

const watcherStart = processManager.indexOf(
  "RockstarStartupDialogAutoConfirmer.Start(profile.Game, cancellationToken);"
);
const processStart = processManager.indexOf("Process.Start(startInfo)");
assert.ok(watcherStart >= 0, "the game process manager must start the dialog watcher");
assert.ok(
  watcherStart < processStart,
  "the dialog watcher must be armed before the game process starts"
);
assert.match(
  coordinator,
  /If Rockstar's Connecting to Social Club dialog is visible, click OK once\./,
  "the no-bridge warning must distinguish a blocked Rockstar dialog from a missing bridge"
);
assert.match(
  coordinator,
  /select Resume or the designated clean save once\./,
  "the no-bridge warning must explain the safe manual Trilogy landing-screen step"
);

console.log("Rockstar startup-dialog auto-confirm source checks passed.");
