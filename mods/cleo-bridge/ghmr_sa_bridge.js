// GHMR San Andreas: Definitive Edition bridge v0.1.4
//
// The installer prepends ghmr_ini_transport.js and supplies the user's local
// IPC directory. This script owns all game-specific behavior.

const protocolVersion = 1;
const gameId = "sade";
const expectedHost = "sa_unreal";
const pollIntervalMs = 100;
// CLEO reloads its JavaScript runtime while a save or New Game is entering the
// world. For a few frames the ordinary mission-start predicates can report
// true even though a fade, cutscene, or scripted startup is still taking over.
// Three seconds matches the proven GTA III/VC fast path. SA's
// CAN_PLAYER_START_MISSION flag can remain false merely because CJ is moving,
// so five seconds of uninterrupted player control is the bounded fallback.
// That preserves the older five-second protection against duplicate CJ and a
// stranded black-and-white filter without requiring the player to stand still.
const preferredLaunchStabilityMs = 3000;
const movementFallbackStabilityMs = 5000;
const preferredLaunchStablePolls = Math.ceil(
    preferredLaunchStabilityMs / pollIntervalMs
);
const movementFallbackStablePolls = Math.ceil(
    movementFallbackStabilityMs / pollIntervalMs
);
const outcomeConfirmationPolls = 50;
const recoveryConfirmationPolls = 20;
const progressEpsilon = 0.000001;
const missionsPassedStatId = 147;
const maxMessageBytes = 254;

const missionIndexes = {
    "sa.wrong_side_of_the_tracks": 29,
    "sa.supply_lines": 73,
    "sa.end_of_the_line": 110
};

function nextMessageId() {
    return "b" + pipeQuery("nextId");
}

function parseJson(value) {
    try {
        return JSON.parse(value);
    } catch (error) {
        log("[GHMR] rejected invalid controller JSON: " + error);
        return null;
    }
}

function readProgress() {
    return Number(native("GET_PROGRESS_PERCENTAGE"));
}

function readMissionsPassed() {
    return Number(native("GET_INT_STAT", missionsPassedStatId));
}

function captureOutcomeBaseline(context) {
    context.baselineProgress = readProgress();
    context.baselineMissionsPassed = readMissionsPassed();
}

function playerCanBeControlled() {
    return Boolean(native("IS_PLAYER_PLAYING", 0)) &&
        Boolean(native("IS_PLAYER_CONTROL_ON", 0));
}

function launchBlockReason() {
    if (Boolean(ONMISSION)) {
        return "mission-active";
    }
    if (!Boolean(native("IS_PLAYER_PLAYING", 0))) {
        return "player-not-ready";
    }
    if (!Boolean(native("IS_PLAYER_CONTROL_ON", 0))) {
        return "player-control-off";
    }
    return null;
}

function contextFromCommand(command) {
    if (!command ||
        typeof command.runId !== "string" ||
        typeof command.missionId !== "string" ||
        missionIndexes[command.missionId] === undefined) {
        return null;
    }

    return {
        runId: command.runId,
        missionId: command.missionId,
        launchIndex: missionIndexes[command.missionId],
        baselineProgress: readProgress(),
        baselineMissionsPassed: readMissionsPassed()
    };
}

function commandMatchesContext(command, context) {
    return context !== null &&
        command.runId === context.runId &&
        command.missionId === context.missionId;
}

function validControllerCommand(command, instanceId) {
    return command !== null &&
        command.protocol === protocolVersion &&
        command.game === gameId &&
        command.bridgeSessionId === instanceId &&
        typeof command.type === "string" &&
        typeof command.messageId === "string";
}

function sendEnvelope(type, instanceId, context, extra) {
    const envelope = {
        protocol: protocolVersion,
        type: type,
        messageId: nextMessageId(),
        game: gameId,
        bridgeSessionId: instanceId
    };

    if (context !== null) {
        envelope.runId = context.runId;
        envelope.missionId = context.missionId;
    }

    if (extra !== null) {
        for (const key of Object.keys(extra)) {
            envelope[key] = extra[key];
        }
    }

    const encoded = JSON.stringify(envelope);
    if (encoded.length > maxMessageBytes) {
        log("[GHMR] refused oversized bridge event: " + type);
        return false;
    }

    const accepted = transportSend(encoded);
    if (!accepted) {
        log("[GHMR] transport did not accept bridge event: " + type);
    }
    return accepted;
}

function tryFadeOut() {
    try {
        native("DO_FADE", 500, 0);
    } catch (error) {
        log("[GHMR] fade-out unavailable: " + error);
    }
}

log("[GHMR] San Andreas bridge v0.1.4 loading");
if (HOST !== expectedHost) {
    log("[GHMR] wrong host; expected " + expectedHost + " but found " + HOST);
    exit();
}

let connected = false;
let readySent = false;
let instanceId = "";
let context = null;
let phase = "idle";
let controlReported = false;
let previousMissionState = Boolean(ONMISSION);
let outcomePollsRemaining = 0;
let recoveryPollsRemaining = 0;
let recoveryCommandType = "";
let releaseFadeStarted = false;
let releaseFadeRequested = false;
let launchStablePolls = 0;
let lastLaunchBlockReason = "";
let launchStableLogged = false;

while (true) {
    const transportConnected = pipeStatus() === 2;

    if (!transportConnected) {
        if (connected) {
            log("[GHMR] controller transport disconnected");
        }
        connected = false;
        readySent = false;
        instanceId = "";
        context = null;
        phase = "idle";
        controlReported = false;
        recoveryPollsRemaining = 0;
        recoveryCommandType = "";
        releaseFadeRequested = false;
        launchStablePolls = 0;
        lastLaunchBlockReason = "";
        launchStableLogged = false;
        wait(pollIntervalMs);
        continue;
    }

    if (!connected) {
        connected = true;
        instanceId = pipeQuery("instance");
        const lastRaw = pipeQuery("last");
        const lastCommand = lastRaw.length > 0 ? parseJson(lastRaw) : null;

        if (validControllerCommand(lastCommand, instanceId) &&
            lastCommand.type !== "ping") {
            context = contextFromCommand(lastCommand);
            readySent = true;
            recoveryCommandType = lastCommand.type;

            if (lastCommand.type === "releaseBridge" ||
                lastCommand.type === "abortRun") {
                phase = "released";
            } else if (lastCommand.type === "prepareMission") {
                phase = "preparing";
            } else if (lastCommand.type === "startMission" ||
                       lastCommand.type === "restartMission") {
                phase = "recovering";
                recoveryPollsRemaining = Boolean(ONMISSION)
                    ? recoveryConfirmationPolls
                    : 0;
            }
        }

        if (!readySent) {
            readySent = sendEnvelope(
                "bridgeReady",
                instanceId,
                null,
                {
                    bridgeVersion: "0.1.4",
                    executableVersion: String(CLEO.hostVersion).slice(0, 32)
                }
            );
        }
    }

    let rawCommand = pollCommand();
    let processedCommands = 0;
    while (rawCommand.length > 0 && processedCommands < 8) {
        processedCommands += 1;
        const command = parseJson(rawCommand);

        if (!validControllerCommand(command, instanceId)) {
            sendEnvelope("bridgeError", instanceId, context, {
                reason: "invalid-controller-command"
            });
            phase = "released";
        } else if (command.type === "prepareMission") {
            const nextContext = contextFromCommand(command);
            if (nextContext === null) {
                sendEnvelope("bridgeError", instanceId, null, {
                    reason: "unknown-mission"
                });
                phase = "released";
            } else {
                context = nextContext;
                phase = "preparing";
                controlReported = false;
                outcomePollsRemaining = 0;
                recoveryPollsRemaining = 0;
                recoveryCommandType = "";
                releaseFadeStarted = false;
                releaseFadeRequested = false;
                launchStablePolls = 0;
                lastLaunchBlockReason = "";
                launchStableLogged = false;
            }
        } else if (command.type === "startMission") {
            if (!commandMatchesContext(command, context)) {
                sendEnvelope("bridgeError", instanceId, context, {
                    reason: "start-context-mismatch"
                });
                phase = "released";
            } else {
                phase = "launchPending";
                captureOutcomeBaseline(context);
                controlReported = false;
            }
        } else if (command.type === "restartMission") {
            if (!commandMatchesContext(command, context)) {
                sendEnvelope("bridgeError", instanceId, context, {
                    reason: "restart-context-mismatch"
                });
                phase = "released";
            } else {
                phase = "retryWaiting";
                controlReported = false;
                outcomePollsRemaining = 0;
            }
        } else if (command.type === "releaseBridge") {
            releaseFadeRequested = command.reason !== "done";
            phase = "released";
        } else if (command.type === "abortRun") {
            releaseFadeRequested = false;
            phase = "released";
        } else if (command.type === "ping") {
            sendEnvelope("heartbeat", instanceId, context, null);
        } else {
            sendEnvelope("bridgeError", instanceId, context, {
                reason: "unsupported-command"
            });
            phase = "released";
        }

        rawCommand = pollCommand();
    }

    const missionState = Boolean(ONMISSION);

    let launchReady = false;
    if (phase === "preparing" || phase === "prepared" ||
        phase === "launchPending") {
        const blockedReason = launchBlockReason();
        if (blockedReason !== null) {
            launchStablePolls = 0;
            launchStableLogged = false;
            if (blockedReason !== lastLaunchBlockReason) {
                log("[GHMR] waiting for free roam: " + blockedReason);
            }
        } else {
            if (lastLaunchBlockReason !== "ready-candidate") {
                log(
                    "[GHMR] free-roam candidate; confirming stability for " +
                    preferredLaunchStabilityMs +
                    " ms (" + movementFallbackStabilityMs +
                    " ms movement fallback)"
                );
            }
            launchStablePolls = Math.min(
                launchStablePolls + 1,
                movementFallbackStablePolls
            );
            const preferredStable =
                launchStablePolls >= preferredLaunchStablePolls;
            const movementFallbackStable =
                launchStablePolls >= movementFallbackStablePolls;
            const nativeMissionReady =
                Boolean(native("CAN_PLAYER_START_MISSION", 0));
            launchReady = preferredStable &&
                (nativeMissionReady || movementFallbackStable);
            if (preferredStable && !launchStableLogged) {
                log(
                    "[GHMR] free-roam state is stable; " +
                    "using a mission-ready frame or bounded movement fallback"
                );
                launchStableLogged = true;
            }
            lastLaunchBlockReason = "ready-candidate";
        }

        if (blockedReason !== null) {
            lastLaunchBlockReason = blockedReason;
        }
    } else {
        launchStablePolls = 0;
        lastLaunchBlockReason = "";
        launchStableLogged = false;
    }

    if (phase === "recovering") {
        if (!missionState) {
            sendEnvelope("bridgeError", instanceId, context, {
                reason: "retry-recovery-lost"
            });
            phase = "released";
        } else if (recoveryPollsRemaining > 0) {
            recoveryPollsRemaining -= 1;
        } else if (context === null) {
            sendEnvelope("bridgeError", instanceId, null, {
                reason: "retry-context-missing"
            });
            phase = "released";
        } else if (recoveryCommandType === "startMission") {
            captureOutcomeBaseline(context);
            if (sendEnvelope("missionFailed", instanceId, context, null)) {
                phase = "failureReported";
            }
        } else if (recoveryCommandType === "restartMission") {
            captureOutcomeBaseline(context);
            phase = "retryWaiting";
        } else {
            sendEnvelope("bridgeError", instanceId, context, {
                reason: "unsupported-recovery-state"
            });
            phase = "released";
        }
    }

    if (phase === "preparing" && context !== null) {
        if (launchReady &&
            sendEnvelope("missionPrepared", instanceId, context, null)) {
            phase = "prepared";
        }
    }

    if (phase === "launchPending" && context !== null &&
        launchReady) {
        captureOutcomeBaseline(context);
        try {
            ONMISSION = true;
            native("LOAD_AND_LAUNCH_MISSION_INTERNAL", context.launchIndex);
            phase = "starting";
            previousMissionState = Boolean(ONMISSION);
            log(
                "[GHMR] launched " + context.missionId +
                " at index " + context.launchIndex
            );
        } catch (error) {
            ONMISSION = false;
            sendEnvelope("bridgeError", instanceId, context, {
                reason: "mission-launch-failed"
            });
            log("[GHMR] mission launch failed: " + error);
            phase = "released";
        }
    }

    if ((phase === "starting" || phase === "retryWaiting") &&
        context !== null && missionState && playerCanBeControlled()) {
        if (phase === "retryWaiting") {
            captureOutcomeBaseline(context);
        }
        if (sendEnvelope("playerControlGained", instanceId, context, null)) {
            phase = "active";
            controlReported = true;
        }
    }

    if (phase === "active" && context !== null) {
        const controlNow = missionState && playerCanBeControlled();
        if (controlReported && !controlNow) {
            if (sendEnvelope("playerControlLost", instanceId, context, null)) {
                controlReported = false;
            }
        } else if (!controlReported && controlNow) {
            if (sendEnvelope("playerControlGained", instanceId, context, null)) {
                controlReported = true;
            }
        }

        if (previousMissionState && !missionState) {
            phase = "outcomePending";
            outcomePollsRemaining = outcomeConfirmationPolls;
        }
    }

    if (phase === "outcomePending" && context !== null) {
        const progressDelta = readProgress() - context.baselineProgress;
        const missionsPassedDelta =
            readMissionsPassed() - context.baselineMissionsPassed;
        if (progressDelta > progressEpsilon || missionsPassedDelta > 0) {
            if (sendEnvelope("missionCompleted", instanceId, context, null)) {
                phase = "completedAwaitCommand";
                log(
                    "[GHMR] completion confirmed for " + context.missionId +
                    "; progress delta=" + progressDelta.toFixed(6) +
                    "; missions-passed delta=" + missionsPassedDelta
                );
            }
        } else if (missionState) {
            // End of the Line uses linked original mission scripts. A brief
            // false -> true transition without a CLEO runtime reload is a
            // linked part, not a failure or reroll.
            phase = "active";
        } else {
            outcomePollsRemaining -= 1;
            if (outcomePollsRemaining <= 0) {
                log(
                    "[GHMR] completion evidence absent for " +
                    context.missionId +
                    "; progress delta=" + progressDelta.toFixed(6) +
                    "; missions-passed delta=" + missionsPassedDelta
                );
                if (sendEnvelope("missionFailed", instanceId, context, null)) {
                    phase = "failureReported";
                }
            }
        }
    }

    if (phase === "released" && releaseFadeRequested &&
        !releaseFadeStarted) {
        releaseFadeStarted = true;
        tryFadeOut();
    }

    previousMissionState = missionState;
    wait(pollIntervalMs);
}
