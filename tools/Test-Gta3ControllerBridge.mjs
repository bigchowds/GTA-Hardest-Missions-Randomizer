import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import vm from "node:vm";
import { fileURLToPath } from "node:url";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const bridgeSource = fs.readFileSync(
    path.join(root, "mods", "cleo-bridge", "ghmr_gta3_bridge.js"),
    "utf8"
);
const transportShim = `
function pipeQuery(key) { return String(native("GHMR_PIPE_QUERY", key)); }
function pipeStatus() { return Number(pipeQuery("status")); }
function pollCommand() { return String(native("GHMR_PIPE_POLL")); }
function transportSend(value) { return Number(native("GHMR_PIPE_SEND", value)) === 1; }
`;

const missions = [
    { id: "gta3.espresso_2_go", launchIndex: 72, reward: 40000 },
    { id: "gta3.sam", launchIndex: 73, reward: 45000 },
    { id: "gta3.the_exchange", launchIndex: 79, reward: 1000000 }
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
        this.completeAt = null;
        this.finished = false;
        this.sandbox = null;
    }

    enqueue(type) {
        const command = {
            protocol: 1,
            type,
            messageId: `c${++this.commandSequence}`,
            game: "gta3de",
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
                } else if (this.mode === "retry" && this.gainedCount === 1) {
                    this.failureAt = this.waitCount + 1;
                } else if (this.mode === "retry" && this.gainedCount === 2) {
                    this.completeAt = this.waitCount + 1;
                } else if (this.mode === "reload" && !this.reloaded) {
                    this.reloadRequested = true;
                } else if (this.mode === "reload" && this.reloaded &&
                           this.gainedCount === 2) {
                    this.finished = true;
                } else if (this.mode === "missionStateDrop" &&
                           this.gainedCount === 1) {
                    this.missionStateDropAt = this.waitCount + 1;
                    this.completeAt = this.waitCount + 40;
                }
                break;
            case "missionFailed":
                this.enqueue("restartMission");
                if (this.mode === "retry") {
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
            // GTA III DE returns an internally launched mission to ordinary
            // free roam after Retry. The bridge must launch the locked index
            // again rather than assume the native mission resumed.
            this.setMissionState(false);
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
        if (this.finished || (this.fadeCount > 0 && this.mode !== "reload")) {
            throw new StopExecution("scenario complete");
        }
        if (this.waitCount > 500) {
            throw new Error(`bridge scenario timed out in ${this.mode} mode`);
        }
    }

    executeRuntime() {
        const harness = this;
        const sandbox = {
            HOST: "gta3_unreal",
            CLEO: {
                version: "1.5.0",
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
                    case "IS_PLAYER_DEAD":
                        return harness.dead;
                    case "HAS_PLAYER_BEEN_ARRESTED":
                        return harness.arrested;
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
            new vm.Script(transportShim + bridgeSource, { filename: "ghmr_gta3_bridge.js" })
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

function testMissionStateDropDoesNotFail() {
    const harness = new Harness("missionStateDrop");
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

function testBridgeErrorDoesNotFadeGameToBlack() {
    const harness = new Harness("bridgeError");
    harness.executeRuntime();
    const error = harness.events.find(event => event.type === "bridgeError");
    assert.equal(error?.reason, "start-context-mismatch");
    assert.equal(harness.fadeCount, 0, "error path faded the game permanently");
}

function testRetryDoesNotReroll() {
    const harness = new Harness("retry");
    harness.executeRuntime();
    assert.deepEqual(
        harness.launches,
        [72, 72],
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

function testRuntimeReloadRecovery() {
    const harness = new Harness("reload");
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
        [72, 72],
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
testRetryDoesNotReroll();
testRuntimeReloadRecovery();
testMissionStateDropDoesNotFail();
testBridgeErrorDoesNotFadeGameToBlack();
console.log("GTA III controller-bridge self-test passed.");
