import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import vm from "node:vm";
import { fileURLToPath } from "node:url";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const source = fs.readFileSync(
    path.join(root, "mods", "cleo-bridge", "ghmr_ini_transport.js"), "utf8"
).replace("__GHMR_IPC_DIRECTORY__", JSON.stringify("X:\\ipc"))
 .replace("__GHMR_GAME_ID__", JSON.stringify("sade"));

const data = new Map();
let writeFault = null;
let readFault = null;
const diagnostics = [];
const game = "X:\\ipc\\sade-game.ini";
const controller = "X:\\ipc\\sade-controller.ini";
function write(file, key, value) {
    assert.ok(String(value).length <= 127, `${key} exceeded CLEO's 127-byte limit`);
    data.set(`${file}|${key}`, String(value));
}
function read(file, key) { return data.get(`${file}|${key}`) ?? ""; }
function checksum(value) {
    let hash = 0x811c9dc5;
    for (let index = 0; index < value.length; index += 1) {
        hash ^= value.charCodeAt(index);
        hash = Math.imul(hash, 0x01000193);
    }
    return (hash >>> 0).toString(16).padStart(8, "0");
}
function publishController(message, sequence) {
    write(controller, "length", message.length);
    write(controller, "checksum", checksum(message));
    for (let index = 0; index < 3; index += 1)
        write(controller, `part${index}`,
            message.slice(index * 100, (index + 1) * 100));
    write(controller, "sequence", sequence);
}
function runtime() {
    const context = vm.createContext({
        native(name, ...args) {
            if (name === "READ_STRING_FROM_INI_FILE") {
                if (readFault !== null && readFault(args[0], args[2])) return "";
                return read(args[0], args[2]);
            }
            if (name === "WRITE_STRING_TO_INI_FILE") {
                if (writeFault !== null && writeFault(args[1], args[3], String(args[0])))
                    return undefined; // This plugin command has no output parameter.
                write(args[1], args[3], args[0]);
                return undefined;
            }
            throw new Error(`Unexpected native command ${name}`);
        },
        Date, Math, Number, String, log: message => diagnostics.push(message)
    });
    new vm.Script(source).runInContext(context);
    return expression => vm.runInContext(expression, context);
}
write(controller, "session", "1".repeat(32));
write(controller, "pulse", Date.now());
let run = runtime();
assert.equal(run("pipeStatus()"), 2);
const ready = JSON.stringify({
    protocol: 1, type: "bridgeReady", messageId: "b1", game: "sade",
    bridgeSessionId: "2".repeat(32), bridgeVersion: "0.1.0",
    executableVersion: "1.0.112.6680"
});
assert.ok(ready.length > 127 && ready.length <= 254);
assert.equal(run(`transportSend(${JSON.stringify(ready)})`), true);
assert.equal(Number(read(game, "sequence")), 1);
assert.equal(read(game, "part0") + read(game, "part1") + read(game, "part2"), ready);
assert.equal(run('transportSend("second")'), true);
assert.equal(Number(read(game, "sequence")), 1, "second event must await ack");
write(controller, "ack", 1);
run("pipeStatus()");
assert.equal(Number(read(game, "sequence")), 2);
assert.equal(read(game, "part0"), "second");

const command = JSON.stringify({
    protocol: 1, type: "prepareMission", messageId: "controller-1", game: "sade",
    bridgeSessionId: "2".repeat(32), runId: "3".repeat(32),
    missionId: "sa.wrong_side_of_the_tracks"
});
assert.ok(command.length > 127 && command.length <= 254);
write(controller, "sequence", 0);
write(controller, "length", command.length);
write(controller, "checksum", checksum(command));
for (let i = 0; i < 3; i++)
    write(controller, `part${i}`, command.slice(i * 100, (i + 1) * 100));
assert.equal(run("pollCommand()"), "", "uncommitted command must stay hidden");
write(controller, "sequence", 1);
assert.equal(run("pollCommand()"), command);
assert.equal(Number(read(game, "ack")), 1);
assert.equal(run("pollCommand()"), "", "command must not replay");
run = runtime();
assert.equal(run("pipeStatus()"), 2);
assert.equal(run('pipeQuery("last")'), command, "script reload must recover last command");
assert.equal(run('pipeQuery("instance")').length, 32);

const restart = JSON.stringify({
    protocol: 1, type: "restartMission", messageId: "controller-2", game: "sade",
    bridgeSessionId: "2".repeat(32), runId: "3".repeat(32),
    missionId: "sa.supply_lines"
});
// Simulate a read during an in-place controller write: the header and first
// chunk are new while the tail still belongs to the previous command.
write(controller, "sequence", 2);
write(controller, "length", restart.length);
write(controller, "checksum", checksum(restart));
write(controller, "part0", restart.slice(0, 100));
assert.equal(run("pollCommand()"), "", "torn command must not be acknowledged");
assert.equal(Number(read(game, "ack")), 1);
publishController(restart, 2);
assert.equal(run("pollCommand()"), restart);
assert.equal(Number(read(game, "ack")), 2);

// Simulate CLEO reading the controller INI between its truncate and rewrite.
// A single blank frame must not disconnect the adapter, discard its mission
// context or make the last restart command look like a fresh runtime reload.
write(controller, "session", "");
write(controller, "pulse", "");
assert.equal(run("pipeStatus()"), 2, "transient blank read must stay connected");
write(controller, "session", "1".repeat(32));
write(controller, "pulse", Date.now());
assert.equal(run("pipeStatus()"), 2);
assert.equal(run('pipeQuery("last")'), restart, "transient read lost last command");

write(controller, "session", "4".repeat(32));
write(controller, "pulse", Date.now());
assert.equal(run("pipeStatus()"), 0, "new run disconnects the old adapter session");
assert.equal(run("pipeStatus()"), 2);
assert.equal(Number(read(game, "sequence")), 0, "new session must clear stale event");
assert.equal(Number(read(game, "ack")), 0, "new session must clear stale command");

// A native write can fail without throwing. Do not drop the queued event or
// advance an in-memory sequence past the sequence that actually reached disk.
for (const failedKey of ["invalidate", "part0", "part1", "part2", "commit"]) {
    data.clear();
    write(controller, "session", "5".repeat(32));
    write(controller, "pulse", Date.now());
    write(controller, "ack", 0);
    const retryRun = runtime();
    assert.equal(retryRun("pipeStatus()"), 2);
    assert.equal(retryRun(`transportSend(${JSON.stringify(ready)})`), true);
    write(controller, "ack", 1);
    let injected = false;
    writeFault = (file, key, value) => {
        const match = failedKey === "invalidate" ? key === "sequence" && value === "0"
            : failedKey === "commit" ? key === "sequence" && value === "2"
            : key === failedKey;
        if (file === game && match && !injected) {
            injected = true;
            return true;
        }
        return false;
    };
    const pendingEvent = JSON.stringify({ type: "playerControlGained", detail: "x".repeat(180) });
    assert.ok(pendingEvent.length <= 254);
    assert.equal(retryRun(`transportSend(${JSON.stringify(pendingEvent)})`), true);
    assert.ok(injected, `${failedKey} fault was not injected`);
    writeFault = null;
    retryRun("pipeStatus()");
    assert.equal(Number(read(game, "sequence")), 2,
        `${failedKey}: failed publication must be retried at the same sequence`);
    assert.equal(read(game, "part0") + read(game, "part1") + read(game, "part2"), pendingEvent,
        `${failedKey}: retry must restore the complete original event`);
    write(controller, "ack", 2);
    retryRun('transportSend("after-recovery")');
    assert.equal(Number(read(game, "sequence")), 3,
        `${failedKey}: later events must continue after the recovered acknowledgement`);
    assert.equal(read(game, "part0"), "after-recovery");
}

// A sustained write failure must keep the controller-visible frame uncommitted
// and retain every event in order, without flooding the CLEO diagnostic log.
data.clear();
diagnostics.length = 0;
write(controller, "session", "6".repeat(32));
write(controller, "pulse", Date.now());
write(controller, "ack", 0);
const blockedRun = runtime();
assert.equal(blockedRun("pipeStatus()"), 2);
writeFault = (file, key) => file === game && key === "part1";
const blockedEvent = JSON.stringify({ type: "playerControlGained", detail: "y".repeat(180) });
blockedRun(`transportSend(${JSON.stringify(blockedEvent)})`);
blockedRun('transportSend("queued-behind-blocked")');
for (let attempt = 0; attempt < 5; attempt += 1) blockedRun("pipeStatus()");
assert.equal(Number(read(game, "sequence")), 0, "partial payload must never be committed");
assert.equal(diagnostics.length, 1, "repeated failure must log only once");
writeFault = null;
blockedRun("pipeStatus()");
assert.equal(Number(read(game, "sequence")), 1);
assert.equal(read(game, "part0") + read(game, "part1") + read(game, "part2"), blockedEvent);
assert.equal(diagnostics.length, 2, "recovery must be logged");
write(controller, "ack", 1);
blockedRun("pipeStatus()");
assert.equal(Number(read(game, "sequence")), 2);
assert.equal(read(game, "part0"), "queued-behind-blocked");

// A successful commit with a transient failed read-back must also recover,
// including when the controller has already acknowledged that commit.
for (const alreadyAcknowledged of [false, true]) {
    data.clear();
    write(controller, "session", "7".repeat(32));
    write(controller, "pulse", Date.now());
    write(controller, "ack", 0);
    const commitRun = runtime();
    commitRun("pipeStatus()");
    let missedReadBack = false;
    readFault = (file, key) => {
        if (file === game && key === "sequence" && read(game, key) === "1" && !missedReadBack) {
            missedReadBack = true;
            return true;
        }
        return false;
    };
    commitRun('transportSend("committed-event")');
    assert.ok(missedReadBack);
    assert.equal(Number(read(game, "sequence")), 1);
    readFault = null;
    if (alreadyAcknowledged) write(controller, "ack", 1);
    commitRun("pipeStatus()");
    write(controller, "ack", 1);
    commitRun('transportSend("next-event")');
    assert.equal(Number(read(game, "sequence")), 2, "missed commit read-back must not strand the queue");
    assert.equal(read(game, "part0"), "next-event");
}

console.log("CLEO INI bridge transport self-test passed: silent-write failures, sustained failures, ordered recovery and missed commit read-back.");
