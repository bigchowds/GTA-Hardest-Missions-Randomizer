// GHMR GTA Vice City: Definitive Edition bridge v0.1.7
//
// The installer prepends ghmr_ini_transport.js. Demolition Man is launched by
// its original main.scm index and completion requires its exact $1,000 reward.
// A mission-state clear without that reward is treated as failure only after a
// confirmation window, so a transient flag change can never advance the run.

const protocolVersion = 1;
const gameId = "vcde";
const expectedHost = "vc_unreal";
const pollIntervalMs = 100;
// A save reload creates the CLEO runtime before the game has fully settled.
// Require a sustained free-roam window so an internal mission script is never
// launched into a load transition, cutscene, or resumed story mission.
const launchStabilityMs = 3000;
const launchStablePollsRequired = Math.ceil(
    launchStabilityMs / pollIntervalMs
);
const recoveryConfirmationPolls = 20;
const outcomeConfirmationPolls = 20;
const maxMessageBytes = 254;

const missionConfiguration = {
    "vc.demolition_man": {
        launchIndex: 19,
        reward: 1000
    }
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

function readMoney() {
    return Number(native("STORE_SCORE", 0));
}

function playerCanBeControlled() {
    return Boolean(native("IS_PLAYER_PLAYING", 0)) &&
        Boolean(native("CAN_PLAYER_START_MISSION", 0));
}

function launchBlockReason() {
    if (Boolean(ONMISSION)) {
        return "mission-active";
    }
    if (!Boolean(native("IS_PLAYER_PLAYING", 0))) {
        return "player-not-ready";
    }
    if (!Boolean(native("CAN_PLAYER_START_MISSION", 0))) {
        return "game-not-ready";
    }
    return null;
}

function contextFromCommand(command) {
    if (!command ||
        typeof command.runId !== "string" ||
        typeof command.missionId !== "string") {
        return null;
    }

    const configuration = missionConfiguration[command.missionId];
    if (configuration === undefined) {
        return null;
    }

    return {
        runId: command.runId,
        missionId: command.missionId,
        launchIndex: configuration.launchIndex,
        expectedReward: configuration.reward,
        previousMoney: readMoney(),
        rewardObserved: false,
        missionStateDropLogged: false,
        outcomePollsRemaining: 0
    };
}

function resetAttemptEvidence(context) {
    context.previousMoney = readMoney();
    context.rewardObserved = false;
    context.missionStateDropLogged = false;
    context.outcomePollsRemaining = 0;
}

function observeReward(context) {
    const currentMoney = readMoney();
    const increase = currentMoney - context.previousMoney;
    if (increase >= context.expectedReward) {
        context.rewardObserved = true;
        log(
            "[GHMR] completion reward observed for " + context.missionId +
            "; increase=" + increase
        );
    }
    context.previousMoney = currentMoney;
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
    if (type === "bridgeError") {
        log(
            "[GHMR] bridge error: " +
            (extra !== null && extra.reason ? extra.reason : "unknown")
        );
    }
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

function playerFailedAttempt() {
    // VC DE marks the legacy arrest-only opcode as unsupported. This native
    // is the game's supported combined wasted/busted mission outcome check.
    return Boolean(native("HAS_DEATHARREST_BEEN_EXECUTED"));
}

function tryFadeOut() {
    try {
        native("DO_FADE", 500, 0);
    } catch (error) {
        log("[GHMR] fade-out unavailable: " + error);
    }
}

log("[GHMR] Vice City bridge v0.1.7 loading");
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
                recoveryPollsRemaining = recoveryConfirmationPolls;
            }
        }

        if (!readySent) {
            readySent = sendEnvelope(
                "bridgeReady",
                instanceId,
                null,
                {
                    bridgeVersion: "0.1.7",
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
                resetAttemptEvidence(context);
                controlReported = false;
            }
        } else if (command.type === "restartMission") {
            if (!commandMatchesContext(command, context)) {
                sendEnvelope("bridgeError", instanceId, context, {
                    reason: "restart-context-mismatch"
                });
                phase = "released";
            } else {
                // Vice City DE may either resume the native Retry or return an
                // internally launched mission to free roam. Support both: keep
                // a native mission if it is already active, otherwise relaunch
                // this exact locked index after free roam becomes stable.
                phase = Boolean(ONMISSION)
                    ? "retryWaiting"
                    : "retryLaunchPending";
                controlReported = false;
                resetAttemptEvidence(context);
                log("[GHMR] same-mission retry requested after failure");
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
        phase === "launchPending" || phase === "retryLaunchPending" ||
        phase === "recovering" || phase === "failureReported") {
        const blockedReason = launchBlockReason();
        if (blockedReason !== null) {
            launchStablePolls = 0;
            launchStableLogged = false;
            if (blockedReason !== lastLaunchBlockReason) {
                log("[GHMR] waiting for free roam: " + blockedReason);
            }
            lastLaunchBlockReason = blockedReason;
        } else {
            if (lastLaunchBlockReason !== "ready-candidate") {
                log(
                    "[GHMR] free-roam candidate; confirming stability for " +
                    launchStabilityMs + " ms"
                );
            }
            launchStablePolls = Math.min(
                launchStablePolls + 1,
                launchStablePollsRequired
            );
            launchReady = launchStablePolls >= launchStablePollsRequired;
            if (launchReady && !launchStableLogged) {
                log("[GHMR] free-roam state is stable; mission may launch");
                launchStableLogged = true;
            }
            lastLaunchBlockReason = "ready-candidate";
        }
    } else {
        launchStablePolls = 0;
        lastLaunchBlockReason = "";
        launchStableLogged = false;
    }

    if (phase === "recovering") {
        if (recoveryPollsRemaining > 0) {
            recoveryPollsRemaining -= 1;
        } else if (context === null) {
            sendEnvelope("bridgeError", instanceId, null, {
                reason: "retry-context-missing"
            });
            phase = "released";
        } else if (recoveryCommandType === "startMission") {
            resetAttemptEvidence(context);
            if (sendEnvelope("missionFailed", instanceId, context, null)) {
                phase = "failureReported";
                log("[GHMR] runtime reload reported as a missed mission failure");
            }
        } else if (recoveryCommandType === "restartMission") {
            resetAttemptEvidence(context);
            phase = missionState ? "retryWaiting" : "retryLaunchPending";
            log(
                missionState
                    ? "[GHMR] recovered native Retry for locked mission"
                    : "[GHMR] recovered Retry; waiting to relaunch locked mission"
            );
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

    if (phase === "retryLaunchPending" && context !== null && missionState) {
        // The game resumed its own checkpoint while the bridge was waiting for
        // free roam. Do not launch a duplicate mission script.
        resetAttemptEvidence(context);
        phase = "starting";
        log("[GHMR] native Retry resumed; keeping the locked mission");
    }

    if ((phase === "launchPending" || phase === "retryLaunchPending") &&
        context !== null && launchReady) {
        const isRetryLaunch = phase === "retryLaunchPending";
        resetAttemptEvidence(context);
        try {
            ONMISSION = true;
            native("LOAD_AND_LAUNCH_MISSION_INTERNAL", context.launchIndex);
            phase = "starting";
            log(
                "[GHMR] " + (isRetryLaunch ? "relaunched " : "launched ") +
                context.missionId +
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
        context !== null &&
        playerCanBeControlled()) {
        if (sendEnvelope("playerControlGained", instanceId, context, null)) {
            phase = "active";
            controlReported = true;
        }
    }

    if (phase === "active" && context !== null) {
        observeReward(context);
        const controlNow = playerCanBeControlled();
        if (controlReported && !controlNow) {
            if (sendEnvelope("playerControlLost", instanceId, context, null)) {
                controlReported = false;
            }
        } else if (!controlReported && controlNow) {
            if (sendEnvelope("playerControlGained", instanceId, context, null)) {
                controlReported = true;
            }
        }

        if (context.rewardObserved) {
            if (sendEnvelope("missionCompleted", instanceId, context, null)) {
                phase = "completedAwaitCommand";
                log("[GHMR] completion confirmed for " + context.missionId);
            }
        } else if (playerFailedAttempt()) {
            if (sendEnvelope("missionFailed", instanceId, context, null)) {
                phase = "failureReported";
                log("[GHMR] failure confirmed: player wasted or busted");
            }
        } else if (!missionState) {
            if (!context.missionStateDropLogged) {
                context.missionStateDropLogged = true;
                context.outcomePollsRemaining = outcomeConfirmationPolls;
                log(
                    "[GHMR] ONMISSION cleared without the completion reward; " +
                    "confirming failure"
                );
            } else if (context.outcomePollsRemaining > 0) {
                context.outcomePollsRemaining -= 1;
            } else if (sendEnvelope(
                "missionFailed",
                instanceId,
                context,
                null
            )) {
                phase = "failureReported";
                log("[GHMR] failure confirmed: mission ended without reward");
            }
        } else if (context.missionStateDropLogged) {
            context.missionStateDropLogged = false;
            context.outcomePollsRemaining = 0;
            log("[GHMR] transient ONMISSION clear ended; mission is still active");
        }
    }

    if (phase === "released" && releaseFadeRequested &&
        !releaseFadeStarted) {
        releaseFadeStarted = true;
        tryFadeOut();
    }

    wait(pollIntervalMs);
}
