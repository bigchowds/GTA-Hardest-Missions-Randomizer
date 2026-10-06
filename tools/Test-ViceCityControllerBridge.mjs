import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import vm from "node:vm";
import { fileURLToPath } from "node:url";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const bridgeSource = fs.readFileSync(
    path.join(root, "mods", "cleo-bridge", "ghmr_vc_bridge.js"),
    "utf8"
);
assert.equal(
    bridgeSource.includes('native("HAS_PLAYER_BEEN_ARRESTED"'),
    false,
    "VC bridge called an opcode marked unsupported by vc_unreal"
);
const transportShim = `
function pipeQuery(key) { return String(native("GHMR_PIPE_QUERY", key)); }
function pipeStatus() { return Number(pipeQuery("status")); }
function pollCommand() { return String(native("GHMR_PIPE_POLL")); }
function transportSend(value) { return Number(native("GHMR_PIPE_SEND", value)) === 1; }
`;

const missions = [
    { id: "vc.demolition_man", launchIndex: 19, reward: 1000 }
];
const runId = "11111111111111111111111111111111";
const instanceId = "22222222222222222222222222222222";

class StopExecution extends Error {}

class Harness {
    constructor(mode, mission = missions[0]) {
        this.mode = mode;
        this.mission = mission;
        this.events = [];
        this.commands = [];
        this.lastCommand = "";
        this.messageSequence = 0;
        this.commandSequence = 0;
        this.waitCount = 0;
        this.money = 100000;
        // Model CLEO becoming available before the selected save reaches
        // stable, mission-capable free roam.
        this.control = false;
        this.dead = false;
        this.arrested = false;
        this.controlReadyAt = 20;
        this.onMission = false;
        this.launches = [];
        this.launchWaitCounts = [];
        this.fadeCount = 0;
        this.gainedCount = 0;
        this.reloadRequested = false;
        this.reloaded = false;
        this.failureAt = null;
        this.retryAt = null;
        this.retryFreeRoamAt = null;
        this.missionStateDropAt = null;
        this.missionStateRestoreAt = null;
        this.completeAt = null;
        this.finished = false;
        this.sandbox = null;
    }

    enqueue(type) {
        const command = {
            protocol: 1,
            type,
            messageId: `c${++this.commandSequence}`,
            game: "vcde",
            bridgeSessionId: instanceId,
            runId,
            missionId: this.mission.id
        };
        const encoded = JSON.stringify(command);
        assert.ok(Buffer.byteLength(encoded, "utf8") <= 254);
        this.lastCommand = encoded;
        this.commands.push(encoded);
    }

    receiveEvent(encoded) {
        assert.ok(
            Buffer.byteLength(encoded, "utf8") <= 254,
            `bridge event exceeded transport limit: ${encoded}`
        );
        const event = JSON.parse(encoded);
        this.events.push(event);

        switch (event.type) {
            case "bridgeReady":
                if (this.mode === "bridgeError") {
                    this.enqueue("startMission");
                } else {
                    this.enqueue("prepareMission");
                }
                break;
            case "missionPrepared":
                this.enqueue("startMission");
                break;
            case "playerControlGained":
                this.gainedCount += 1;
                if (this.mode === "completion" && this.gainedCount === 1) {
                    this.completeAt = this.waitCount + 1;
                } else if ((this.mode === "retryFreeRoam" ||
                            this.mode === "retryNative") &&
                           this.gainedCount === 1) {
                    this.failureAt = this.waitCount + 1;
                } else if ((this.mode === "retryFreeRoam" ||
                            this.mode === "retryNative") &&
                           this.gainedCount === 2) {
                    this.completeAt = this.waitCount + 1;
                } else if (this.mode === "reloadFreeRoam" && !this.reloaded) {
                    this.reloadRequested = true;
                } else if (this.mode === "reloadFreeRoam" && this.reloaded &&
                           this.gainedCount === 2) {
                    this.completeAt = this.waitCount + 1;
                } else if (this.mode === "transientMissionStateDrop" &&
                           this.gainedCount === 1) {
                    this.missionStateDropAt = this.waitCount + 1;
                    this.missionStateRestoreAt = this.waitCount + 10;
                    this.completeAt = this.waitCount + 40;
                } else if (this.mode === "outcomeFailure" &&
                           this.gainedCount === 1) {
                    this.missionStateDropAt = this.waitCount + 1;
                }
                break;
            case "missionFailed":
                if (this.mode === "outcomeFailure") {
                    this.finished = true;
                    break;
                }
                this.enqueue("restartMission");
                if (this.mode === "retryFreeRoam" ||
                    this.mode === "retryNative") {
                    this.retryAt = this.waitCount + 1;
                }
                break;
            case "missionCompleted":
                this.enqueue("releaseBridge");
                break;
            case "bridgeError":
                this.finished = true;
                break;
        }

        return 1;
    }

    setMissionState(value) {
        this.onMission = value;
        if (this.sandbox !== null) {
            this.sandbox.ONMISSION = value;
        }
    }

    tick() {
        this.waitCount += 1;

        if (this.waitCount === this.controlReadyAt) {
            this.control = true;
        }

        if (this.failureAt === this.waitCount) {
            this.setMissionState(false);
            this.dead = true;
            this.control = false;
            this.failureAt = null;
        }
        if (this.retryAt === this.waitCount) {
            // Vice City DE behavior is deliberately modeled both ways: some
            // retry paths may resume the native mission, while others may
            // return an internally launched mission to free roam.
            this.setMissionState(this.mode === "retryNative");
            this.dead = false;
            this.arrested = false;
            this.control = true;
            this.retryFreeRoamAt = this.waitCount;
            this.retryAt = null;
        }
        if (this.missionStateDropAt === this.waitCount) {
            this.setMissionState(false);
            this.missionStateDropAt = null;
        }
        if (this.missionStateRestoreAt === this.waitCount) {
            this.setMissionState(true);
            this.missionStateRestoreAt = null;
        }
        if (this.completeAt === this.waitCount) {
            this.money += this.mission.reward;
            this.setMissionState(false);
            this.control = false;
            this.completeAt = null;
        }

        if (this.reloadRequested) {
            this.onMission = true;
            throw new StopExecution("runtime reload");
        }
        if (this.finished || this.fadeCount > 0) {
            throw new StopExecution("scenario complete");
        }
        if (this.waitCount > 500) {
            throw new Error(`bridge scenario timed out in ${this.mode} mode`);
        }
    }

    executeRuntime() {
        const harness = this;
        const sandbox = {
            HOST: "vc_unreal",
            CLEO: {
                version: "1.4.2",
                hostVersion: "1.0.112.6680"
            },
            ONMISSION: this.onMission,
            JSON,
            Math,
            Number,
            Object,
            String,
            Boolean,
            log() {},
            exit() {
                throw new Error("bridge exited unexpectedly");
            },
            wait() {
                harness.onMission = Boolean(sandbox.ONMISSION);
                harness.tick();
            },
            native(name, ...args) {
                switch (name) {
                    case "GHMR_PIPE_QUERY":
                        if (args[0] === "status") return "2";
                        if (args[0] === "instance") return instanceId;
                        if (args[0] === "nextId") {
                            return String(++harness.messageSequence);
                        }
                        if (args[0] === "last") return harness.lastCommand;
                        if (args[0] === "error") return "";
                        return "";
                    case "GHMR_PIPE_POLL":
                        return harness.commands.shift() ?? "";
                    case "GHMR_PIPE_SEND":
                        return harness.receiveEvent(String(args[0]));
                    case "STORE_SCORE":
                        return harness.money;
                    case "IS_PLAYER_PLAYING":
                    case "CAN_PLAYER_START_MISSION":
                        return harness.control;
                    case "HAS_DEATHARREST_BEEN_EXECUTED":
                        return harness.dead || harness.arrested;
                    case "LOAD_AND_LAUNCH_MISSION_INTERNAL":
                        harness.launches.push(Number(args[0]));
                        harness.launchWaitCounts.push(harness.waitCount);
                        harness.setMissionState(true);
                        return undefined;
                    case "DO_FADE":
                        harness.fadeCount += 1;
                        return undefined;
                    default:
                        throw new Error(`unexpected native call: ${name}`);
                }
            }
        };

        this.sandbox = sandbox;
        vm.createContext(sandbox);
        try {
            new vm.Script(transportShim + bridgeSource, { filename: "ghmr_vc_bridge.js" })
                .runInContext(sandbox);
            throw new Error("bridge script returned unexpectedly");
        } catch (error) {
            if (!(error instanceof StopExecution)) {
                throw error;
            }
        } finally {
            this.onMission = Boolean(sandbox.ONMISSION);
            this.sandbox = null;
        }
    }
}

function eventTypes(harness) {
    return harness.events.map(event => event.type);
}

function testCompletion() {
    for (const mission of missions) {
        const harness = new Harness("completion", mission);
        harness.executeRuntime();
        assert.deepEqual(harness.launches, [mission.launchIndex]);
        assert.ok(
            harness.launchWaitCounts[0] >= 49,
            "mission launched before free roam was stable for three seconds"
        );
        assert.ok(
            harness.launchWaitCounts[0] < 69,
            "mission still waited for the obsolete five-second window"
        );
        assert.deepEqual(eventTypes(harness), [
            "bridgeReady",
            "missionPrepared",
            "playerControlGained",
            "playerControlLost",
            "missionCompleted"
        ]);
        assert.equal(harness.fadeCount, 1);
    }
}

function testTransientMissionStateDropDoesNotFail() {
    const harness = new Harness("transientMissionStateDrop");
    harness.executeRuntime();
    assert.equal(
        harness.events.filter(event => event.type === "missionFailed").length,
        0,
        "a transient or linked ONMISSION clear was mistaken for failure"
    );
    assert.equal(
        harness.events.filter(event => event.type === "missionCompleted").length,
        1
    );
}

function testMissionEndWithoutRewardFails() {
    const harness = new Harness("outcomeFailure");
    harness.executeRuntime();
    assert.equal(
        harness.events.filter(event => event.type === "missionFailed").length,
        1,
        "mission end without Demolition Man's reward was not treated as failure"
    );
    assert.equal(
        harness.events.filter(event => event.type === "missionCompleted").length,
        0
    );
}

function testBridgeErrorDoesNotFadeGameToBlack() {
    const harness = new Harness("bridgeError");
    harness.executeRuntime();
    const error = harness.events.find(event => event.type === "bridgeError");
    assert.equal(error?.reason, "start-context-mismatch");
    assert.equal(harness.fadeCount, 0, "error path faded the game permanently");
}

function testFreeRoamRetryDoesNotReroll() {
    const harness = new Harness("retryFreeRoam");
    harness.executeRuntime();
    assert.deepEqual(
        harness.launches,
        [19, 19],
        "retry did not relaunch the same locked mission index"
    );
    assert.notEqual(harness.retryFreeRoamAt, null);
    assert.ok(
        harness.launchWaitCounts[1] - harness.retryFreeRoamAt >= 29,
        "retry relaunched before free roam was stable for three seconds"
    );
    assert.ok(
        harness.launchWaitCounts[1] - harness.retryFreeRoamAt < 39,
        "retry recovery added a second delay after the stability window"
    );
    assert.equal(
        harness.events.filter(event => event.type === "missionFailed").length,
        1
    );
    assert.equal(
        harness.events.filter(event => event.type === "playerControlGained").length,
        2
    );
    assert.equal(
        harness.events.filter(event => event.type === "missionCompleted").length,
        1
    );
}

function testNativeRetryDoesNotLaunchDuplicate() {
    const harness = new Harness("retryNative");
    harness.executeRuntime();
    assert.deepEqual(
        harness.launches,
        [19],
        "native Retry caused a duplicate mission script launch"
    );
    assert.equal(
        harness.events.filter(event => event.type === "missionFailed").length,
        1
    );
    assert.equal(
        harness.events.filter(event => event.type === "playerControlGained").length,
        2
    );
    assert.equal(
        harness.events.filter(event => event.type === "missionCompleted").length,
        1
    );
}

function testRuntimeReloadRecovery() {
    const harness = new Harness("reloadFreeRoam");
    harness.executeRuntime();
    assert.equal(harness.reloadRequested, true);
    harness.reloadRequested = false;
    harness.reloaded = true;
    harness.onMission = false;
    harness.control = true;
    const recoveredFreeRoamAt = harness.waitCount;
    harness.executeRuntime();

    assert.deepEqual(
        harness.launches,
        [19, 19],
        "runtime reload did not relaunch the same locked mission"
    );
    assert.ok(
        harness.launchWaitCounts[1] - recoveredFreeRoamAt >= 29,
        "runtime recovery relaunched before three stable seconds"
    );
    assert.ok(
        harness.launchWaitCounts[1] - recoveredFreeRoamAt < 39,
        "runtime recovery and stability waits did not overlap"
    );
    assert.equal(
        harness.events.filter(event => event.type === "bridgeReady").length,
        1,
        "runtime reload created a new bridge session"
    );
    assert.equal(
        harness.events.filter(event => event.type === "missionFailed").length,
        1,
        "runtime reload did not recover the missed failure"
    );
    assert.equal(
        harness.events.filter(event => event.type === "playerControlGained").length,
        2,
        "runtime reload did not resume the same mission"
    );
}

testCompletion();
testFreeRoamRetryDoesNotReroll();
testNativeRetryDoesNotLaunchDuplicate();
testRuntimeReloadRecovery();
testTransientMissionStateDropDoesNotFail();
testMissionEndWithoutRewardFails();
testBridgeErrorDoesNotFadeGameToBlack();
console.log("Vice City controller-bridge self-test passed.");
