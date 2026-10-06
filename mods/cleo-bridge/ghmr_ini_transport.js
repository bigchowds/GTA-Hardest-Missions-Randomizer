// GHMR INI transport v0.2.0-verified-event-writes
// This source is prepended to each game adapter by the controller installer.
// CLEO's existing IniFiles plugin is the only native dependency. Each slot uses
// three 100-character keys because CLEO's string interface stops at 127 bytes.
const ghmrDirectory = __GHMR_IPC_DIRECTORY__;
const ghmrGameId = __GHMR_GAME_ID__;
const ghmrControllerFile = ghmrDirectory + "\\" + ghmrGameId + "-controller.ini";
const ghmrGameFile = ghmrDirectory + "\\" + ghmrGameId + "-game.ini";
const ghmrSection = "GHMR";
const ghmrControllerPulseTimeoutMs = 5000;
// The controller rewrites its tiny INI file in place. CLEO can occasionally
// observe the file between truncate and rewrite and see a blank session or
// pulse for one poll. Keep the last healthy transport alive briefly so that a
// torn read cannot erase an active mission and manufacture a retry.
const ghmrTransientReadGraceMs = 2000;
let ghmrSession = "";
let ghmrInstance = "";
let ghmrEventId = 0;
let ghmrSentSequence = 0;
let ghmrReceivedSequence = 0;
let ghmrLastCommand = "";
let ghmrOutgoing = [];
let ghmrLastHealthyAt = 0;
let ghmrPendingCommit = 0;
let ghmrPublicationRetryLogged = false;

function ghmrRead(file, key) {
    return String(native("READ_STRING_FROM_INI_FILE", file, ghmrSection, key) || "");
}

function ghmrWrite(file, key, value) {
    native("WRITE_STRING_TO_INI_FILE", String(value), file, ghmrSection, key);
}

function ghmrFrame(file) {
    return ghmrRead(file, "part0") +
        ghmrRead(file, "part1") + ghmrRead(file, "part2");
}

// Controller frames are written in place because CLEO's INI reader keeps the
// file open without delete sharing. A sequence read before and after the
// chunks cannot, by itself, reject a mixture of an old and new frame. Include
// the byte length and an FNV-1a checksum so a torn read is never acknowledged.
function ghmrChecksum(value) {
    let hash = 0x811c9dc5;
    for (let index = 0; index < value.length; index += 1) {
        hash ^= value.charCodeAt(index);
        hash = Math.imul(hash, 0x01000193);
    }
    return (hash >>> 0).toString(16).padStart(8, "0");
}

function ghmrReadSequence(file) {
    const value = Number(ghmrRead(file, "sequence"));
    return Number.isSafeInteger(value) && value > 0 ? value : 0;
}

function ghmrPublicationFailed() {
    if (!ghmrPublicationRetryLogged && typeof log === "function")
        log("[GHMR IPC] event write did not verify; retaining event for retry");
    ghmrPublicationRetryLogged = true;
}

function ghmrPublicationSucceeded(sequence) {
    ghmrSentSequence = sequence;
    ghmrPendingCommit = 0;
    ghmrOutgoing.shift();
    if (ghmrPublicationRetryLogged && typeof log === "function")
        log("[GHMR IPC] event write recovered at sequence " + sequence);
    ghmrPublicationRetryLogged = false;
}

function ghmrPublishNext() {
    if (ghmrOutgoing.length === 0) return;
    const acknowledgement = Number(ghmrRead(ghmrControllerFile, "ack"));
    const nextSequence = ghmrSentSequence + 1;
    // The commit can succeed while its read-back temporarily fails. The
    // controller's acknowledgement also proves delivery; do not strand that
    // retained queue head waiting for the previous acknowledgement forever.
    if (ghmrPendingCommit === nextSequence && acknowledgement === nextSequence) {
        ghmrPublicationSucceeded(nextSequence);
        return;
    }
    if (acknowledgement !== ghmrSentSequence) return;
    const message = ghmrOutgoing[0];
    ghmrWrite(ghmrGameFile, "sequence", 0);
    // Verify literal zero: a missing/failed read is not proof of invalidation.
    if (ghmrRead(ghmrGameFile, "sequence") !== "0") {
        ghmrPublicationFailed();
        return;
    }
    for (let part = 0; part < 3; part += 1) {
        ghmrWrite(ghmrGameFile, "part" + part,
            message.slice(part * 100, (part + 1) * 100));
    }
    // IniFiles WRITE has no return value. Confirm the whole payload before
    // making it visible to the controller or removing anything from the queue.
    if (ghmrFrame(ghmrGameFile) !== message ||
        ghmrRead(ghmrGameFile, "sequence") !== "0") {
        ghmrPublicationFailed();
        return;
    }
    ghmrPendingCommit = nextSequence;
    ghmrWrite(ghmrGameFile, "sequence", nextSequence);
    if (ghmrRead(ghmrGameFile, "sequence") !== String(nextSequence)) {
        ghmrPublicationFailed();
        return;
    }
    ghmrPublicationSucceeded(nextSequence);
}

function ghmrRefresh() {
    const now = Date.now();
    const session = ghmrRead(ghmrControllerFile, "session");
    const pulse = Number(ghmrRead(ghmrControllerFile, "pulse"));
    if (!session || !pulse ||
        Math.abs(now - pulse) > ghmrControllerPulseTimeoutMs) {
        return ghmrSession !== "" && ghmrLastHealthyAt > 0 &&
            now - ghmrLastHealthyAt <= ghmrTransientReadGraceMs;
    }
    ghmrLastHealthyAt = now;
    if (session !== ghmrSession) {
        const changedRun = ghmrSession !== "";
        ghmrSession = session;
        ghmrOutgoing = [];
        ghmrPendingCommit = 0;
        ghmrPublicationRetryLogged = false;
        const previousSession = ghmrRead(ghmrGameFile, "session");
        if (previousSession !== session) {
            // A fresh controller run cannot consume an old game's events.
            ghmrWrite(ghmrGameFile, "sequence", 0);
            ghmrWrite(ghmrGameFile, "ack", 0);
            ghmrWrite(ghmrGameFile, "eventId", 0);
            ghmrWrite(ghmrGameFile, "instance", "");
            ghmrWrite(ghmrGameFile, "last0", "");
            ghmrWrite(ghmrGameFile, "last1", "");
            ghmrWrite(ghmrGameFile, "last2", "");
            ghmrWrite(ghmrGameFile, "session", session);
        }
        ghmrInstance = ghmrRead(ghmrGameFile, "instance");
        if (!ghmrInstance) {
            ghmrInstance = Math.floor(Math.random() * 0xffffffff)
                .toString(16).padStart(8, "0") +
                now.toString(16).padStart(16, "0") +
                Math.floor(Math.random() * 0xffffffff)
                    .toString(16).padStart(8, "0");
            ghmrWrite(ghmrGameFile, "instance", ghmrInstance);
        }
        ghmrSentSequence = ghmrReadSequence(ghmrGameFile);
        ghmrEventId = Number(ghmrRead(ghmrGameFile, "eventId")) || 0;
        ghmrReceivedSequence = Number(ghmrRead(ghmrGameFile, "ack")) || 0;
        ghmrLastCommand = ghmrReceivedSequence > 0
            ? ghmrRead(ghmrGameFile, "last0") +
              ghmrRead(ghmrGameFile, "last1") +
              ghmrRead(ghmrGameFile, "last2") : "";
        // The adapter must discard its previous mission when a new run begins
        // without the game process exiting.
        if (changedRun) return false;
    }
    ghmrPublishNext();
    return true;
}

function pipeStatus() {
    return ghmrRefresh() ? 2 : 0;
}

function pipeQuery(key) {
    if (key === "status") return String(pipeStatus());
    if (key === "instance") return ghmrInstance;
    if (key === "last") return ghmrLastCommand;
    if (key === "nextId") {
        ghmrEventId += 1;
        ghmrWrite(ghmrGameFile, "eventId", ghmrEventId);
        return String(ghmrEventId);
    }
    return "";
}

function pollCommand() {
    if (!ghmrRefresh()) return "";
    const sequence = ghmrReadSequence(ghmrControllerFile);
    if (sequence <= ghmrReceivedSequence) return "";
    const length = Number(ghmrRead(ghmrControllerFile, "length"));
    const checksum = ghmrRead(ghmrControllerFile, "checksum");
    const message = ghmrFrame(ghmrControllerFile);
    if (ghmrReadSequence(ghmrControllerFile) !== sequence ||
        Number(ghmrRead(ghmrControllerFile, "length")) !== length ||
        ghmrRead(ghmrControllerFile, "checksum") !== checksum ||
        !Number.isSafeInteger(length) || length < 2 || length > 254 ||
        message.length !== length || ghmrChecksum(message) !== checksum ||
        !message.endsWith("}")) return "";
    ghmrReceivedSequence = sequence;
    ghmrLastCommand = message;
    for (let part = 0; part < 3; part += 1) {
        ghmrWrite(ghmrGameFile, "last" + part,
            message.slice(part * 100, (part + 1) * 100));
    }
    ghmrWrite(ghmrGameFile, "ack", sequence);
    return message;
}

function transportSend(message) {
    if (!ghmrRefresh() || message.length > 254 || ghmrOutgoing.length >= 32)
        return false;
    ghmrOutgoing.push(message);
    ghmrRefresh();
    return true;
}
