import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import vm from "node:vm";
import { fileURLToPath } from "node:url";

const repositoryRoot = path.resolve(
    path.dirname(fileURLToPath(import.meta.url)),
    ".."
);
const probePath = path.join(
    repositoryRoot,
    "mods",
    "cleo-launch-probe",
    "ghmr_sa_three_mission_launch_probe.js"
);
const probeSource = fs.readFileSync(probePath, "utf8");

class ScenarioFinished extends Error {}

function runScenario(options) {
    const state = {
        tick: 0,
        onMission: options.initialOnMission,
        progress: options.initialProgress ?? 1,
        launches: [],
        logs: []
    };

    const context = {
        HOST: "sa_unreal",
        CLEO: {
            version: "test",
            hostVersion: "test"
        },
        Pad: {
            IsKeyPressed(key) {
                return options.keyAtTick?.(state.tick, key) ?? false;
            }
        },
        native(name, ...args) {
            switch (name) {
                case "GET_PROGRESS_PERCENTAGE":
                    return state.progress;
                case "IS_PLAYER_PLAYING":
                case "CAN_PLAYER_START_MISSION":
                    return true;
                case "LOAD_AND_LAUNCH_MISSION_INTERNAL":
                    state.launches.push(args[0]);
                    state.onMission = true;
                    return undefined;
                default:
                    throw new Error(`Unexpected native command: ${name}`);
            }
        },
        log(message) {
            state.logs.push(String(message));
        },
        showTextBox() {},
        exit() {
            throw new Error("Probe exited unexpectedly.");
        },
        wait(milliseconds) {
            assert.equal(milliseconds, 100);
            state.tick += 1;
            options.afterWait?.(state);
            if (state.tick >= options.maxTicks) {
                throw new ScenarioFinished();
            }
        }
    };

    Object.defineProperty(context, "ONMISSION", {
        enumerable: true,
        get() {
            return state.onMission;
        },
        set(value) {
            state.onMission = Boolean(value);
        }
    });

    try {
        vm.runInNewContext(probeSource, context, {
            filename: probePath,
            timeout: 2000
        });
    } catch (error) {
        if (!(error instanceof ScenarioFinished)) {
            throw error;
        }
    }

    return state;
}

const startup = runScenario({
    initialOnMission: true,
    maxTicks: 45,
    keyAtTick(tick, key) {
        return tick === 30 && key === 118;
    },
    afterWait(state) {
        if (state.tick === 5) {
            state.onMission = false;
        }
    }
});
assert.deepEqual(startup.launches, [73]);
assert(startup.logs.some(line => line.includes("STARTUP STATE SETTLED")));
assert(startup.logs.some(line => line.includes("Supply Lines...")));

const recovery = runScenario({
    initialOnMission: true,
    maxTicks: 40,
    afterWait(state) {
        if (state.tick === 25) {
            state.progress = 2;
            state.onMission = false;
        }
    }
});
assert.deepEqual(recovery.launches, []);
assert(recovery.logs.some(line => line.includes("RECOVERY:")));
assert(recovery.logs.some(line => line.includes("OUTCOME PASS:")));

const oneLaunch = runScenario({
    initialOnMission: false,
    maxTicks: 35,
    keyAtTick(tick, key) {
        return (tick === 2 && key === 117) || (tick === 25 && key === 118);
    },
    afterWait(state) {
        if (state.tick === 15) {
            state.progress = 2;
            state.onMission = false;
        }
    }
});
assert.deepEqual(oneLaunch.launches, [29]);
assert(oneLaunch.logs.some(line => line.includes("launch ignored")));

console.log("SA three-mission launch probe self-test passed.");
