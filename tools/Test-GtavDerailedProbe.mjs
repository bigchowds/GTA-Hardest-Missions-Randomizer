import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const probePath = path.join(
    root,
    "mods",
    "shvdn-probe",
    "ghmr_gtav_derailed_probe.3.cs"
);
const source = fs.readFileSync(probePath, "utf8");

assert.match(source, /class\s+GhmrGtavDerailedProbeV3\b/);
assert.match(source, /"exile3"/);
assert.match(source, /"mission_repeat_controller"/);
assert.match(source, /"replay_controller"/);
assert.match(source, /"mission_stat_watcher"/);
assert.match(source, /Game\.IsMissionActive/);
assert.match(source, /Game\.IsCutsceneActive/);
assert.match(
    source,
    /GET_NUMBER_OF_THREADS_RUNNING_THE_SCRIPT_WITH_THIS_HASH/
);
assert.equal(
    /START_NEW_SCRIPT|REQUEST_SCRIPT|SET_MISSION_FLAG|FORCE_CLEANUP/.test(source),
    false,
    "GTA V lifecycle probe must remain read-only"
);
assert.equal(
    /\.Position\s*=|SET_CLOCK_TIME|SET_WEATHER|STAT_SET_/.test(source),
    false,
    "GTA V lifecycle probe must not alter world or save state"
);

console.log(
    "GTA V Derailed probe checks passed: exile3 lifecycle and native " +
    "Retry/result observers are present; mutation calls are absent."
);

