import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import vm from "node:vm";
import { fileURLToPath } from "node:url";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const bridgeSource = fs.readFileSync(
    path.join(root, "mods", "cleo-bridge", "ghmr_sa_bridge.js"),
    "utf8"
);
const transportShim = `
function pipeQuery(key) { return String(native("GHMR_PIPE_QUERY", key)); }
function pipeStatus() { return Number(pipeQuery("status")); }
function pollCommand() { return String(native("GHMR_PIPE_POLL")); }
function transportSend(value) { return Number(native("GHMR_PIPE_SEND", value)) === 1; }
`;

const missionId = "sa.wrong_side_of_the_tracks";
const runId = "11111111111111111111111111111111";
const instanceId = "22222222222222222222222222222222";

assert.match(bridgeSource, /bridgeVersion: "0\.1\.4"/);
assert.match(bridgeSource, /preferredLaunchStabilityMs = 3000/);
assert.match(bridgeSource, /movementFallbackStabilityMs = 5000/);

class StopExecution extends Error {}

class Harness {
    constructor(mode) {
        this.mode = mode;
        this.events = [];
        this.commands = [];
        this.lastCommand = "";
        this.messageSequence = 0;
        this.commandSequence = 0;
        this.waitCount = 0;
        this.progress = (mode === "completionAt100" || mode === "retryAt100")
            ? 100
            : 50;
        this.missionsPassed = 100;
        // Model the real DE startup: the bridge runtime exists before the
        // selected save has handed control to CJ.
        this.control = false;
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
        this.completeAt = null;
        this.finished = false;
        this.sandbox = null;
    }

    enqueue(type) {
        const command = {
            protocol: 1,
            type,
            messageId: `c${++this.commandSequence}`,
            game: "sade",
            bridgeSessionId: instanceId,
            runId,
            missionId
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
                this.enqueue("prepareMission");
                break;
            case "missionPrepared":
                this.enqueue("startMission");
                break;
            case "playerControlGained":
                this.gainedCount += 1;
                if ((this.mode === "completion" ||
                     this.mode === "completionAt100" ||
                     this.mode === "movement") &&
                    this.gainedCount === 1) {
                    this.completeAt = this.waitCount + 1;
                } else if ((this.mode === "retry" ||
                            this.mode === "retryAt100") &&
                           this.gainedCount === 1) {
                    this.failureAt = this.waitCount + 1;
                } else if ((this.mode === "retry" ||
                            this.mode === "retryAt100") &&
                           this.gainedCount === 2) {
                    this.completeAt = this.waitCount + 1;
                } else if (this.mode === "reload" && !this.reloaded) {
                    this.reloadRequested = true;
                } else if (this.mode === "reload" && this.reloaded &&
                           this.gainedCount === 2) {
                    this.finished = true;
                }
                break;
            case "missionFailed":
                this.enqueue("restartMission");
                if (this.mode === "retry" || this.mode === "retryAt100") {
                    this.retryAt = this.waitCount + 1;
                }
                break;
            case "missionCompleted":
                this.enqueue("releaseBridge");
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
            this.failureAt = null;
        }
        if (this.retryAt === this.waitCount) {
            this.setMissionState(true);
            this.control = true;
            this.retryAt = null;
        }
        if (this.completeAt === this.waitCount) {
            if (this.mode !== "completionAt100" &&
                this.mode !== "retryAt100") {
                this.progress += 1;
            }
            this.missionsPassed += 1;
            this.setMissionState(false);
            this.completeAt = null;
        }

        if (this.reloadRequested) {
            this.onMission = true;
            throw new StopExecution("runtime reload");
        }
        if (this.finished || (this.fadeCount > 0 && this.mode !== "reload")) {
            throw new StopExecution("scenario complete");
        }
        if (this.waitCount > 400) {
            throw new Error(`bridge scenario timed out in ${this.mode} mode`);
        }
    }

    executeRuntime() {
        const harness = this;
        const sandbox = {
            HOST: "sa_unreal",
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
                    case "GET_PROGRESS_PERCENTAGE":
                        return harness.progress;
                    case "GET_INT_STAT":
                        assert.equal(args[0], 147);
                        return harness.missionsPassed;
                    case "IS_PLAYER_PLAYING":
                        return true;
                    case "IS_PLAYER_CONTROL_ON":
                        return harness.control;
                    case "CAN_PLAYER_START_MISSION":
                        if (harness.mode === "movement" &&
                            !Boolean(sandbox.ONMISSION)) {
                            // Model continuous movement holding SA's native
                            // mission-ready flag false indefinitely.
                            return false;
                        }
                        return !Boolean(sandbox.ONMISSION);
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
            new vm.Script(transportShim + bridgeSource, { filename: "ghmr_sa_bridge.js" })
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
    const harness = new Harness("completion");
    harness.executeRuntime();
    assert.deepEqual(harness.launches, [29]);
    assert.ok(
        harness.launchWaitCounts[0] >= 49,
        "stationary fast path launched before three stable seconds"
    );
    assert.ok(
        harness.launchWaitCounts[0] < 60,
        "stationary free roam did not use the three-second fast path"
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

function testCompletionAtOneHundredPercent() {
    const harness = new Harness("completionAt100");
    harness.executeRuntime();
    assert.equal(harness.progress, 100, "100% progress must remain capped");
    assert.deepEqual(eventTypes(harness), [
        "bridgeReady",
        "missionPrepared",
        "playerControlGained",
        "playerControlLost",
        "missionCompleted"
    ]);
    assert.equal(
        harness.events.filter(event => event.type === "missionFailed").length,
        0,
        "a passed mission on a 100% save was misclassified as a failure"
    );
}

function testMovementDoesNotResetFreeRoamStability() {
    const harness = new Harness("movement");
    harness.executeRuntime();
    assert.deepEqual(harness.launches, [29]);
    assert.ok(
        harness.launchWaitCounts[0] >= 69,
        "movement bypassed the five-second free-roam safety window"
    );
    assert.ok(
        harness.launchWaitCounts[0] < 80,
        "movement kept resetting the already-proven free-roam window"
    );
    assert.equal(
        harness.events.filter(event => event.type === "missionCompleted").length,
        1
    );
}

function testRetryDoesNotReroll() {
    const harness = new Harness("retry");
    harness.executeRuntime();
    assert.deepEqual(harness.launches, [29], "retry launched a second mission script");
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

function testFailureThenCompletionAtOneHundredPercent() {
    const harness = new Harness("retryAt100");
    harness.executeRuntime();
    assert.equal(harness.progress, 100, "100% progress must remain capped");
    assert.deepEqual(harness.launches, [29], "retry launched a second mission script");
    assert.equal(
        harness.events.filter(event => event.type === "missionFailed").length,
        1,
        "the failed 100% attempt was not reported exactly once"
    );
    assert.equal(
        harness.events.filter(event => event.type === "missionCompleted").length,
        1,
        "the successful 100% retry was not accepted exactly once"
    );
}

function testRuntimeReloadRecovery() {
    const harness = new Harness("reload");
    harness.executeRuntime();
    assert.equal(harness.reloadRequested, true);
    harness.reloadRequested = false;
    harness.reloaded = true;
    harness.executeRuntime();

    assert.deepEqual(harness.launches, [29], "runtime reload relaunched the mission");
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
testCompletionAtOneHundredPercent();
testMovementDoesNotResetFreeRoamStability();
testRetryDoesNotReroll();
testFailureThenCompletionAtOneHundredPercent();
testRuntimeReloadRecovery();
console.log("San Andreas controller-bridge self-test passed.");
