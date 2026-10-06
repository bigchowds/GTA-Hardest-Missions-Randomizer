// GHMR GTA III: Definitive Edition bridge v0.1.5
//
// The installer prepends ghmr_ini_transport.js. Completion requires the exact
// mission's scripted reward jump; ONMISSION ending by itself never advances.

const protocolVersion = 1;
const gameId = "gta3de";
const expectedHost = "gta3_unreal";
const pollIntervalMs = 100;
// A save reload creates the CLEO runtime before the game has fully settled.
// Require a sustained free-roam window so an internal mission script is never
// launched into a load transition, cutscene, or resumed story mission.
const launchStabilityMs = 3000;
const launchStablePollsRequired = Math.ceil(
    launchStabilityMs / pollIntervalMs
);
const recoveryConfirmationPolls = 20;
const maxMessageBytes = 254;

const missionConfiguration = {
    "gta3.espresso_2_go": {
        launchIndex: 72,
        reward: 40000
    },
    "gta3.sam": { launchIndex: 73, reward: 45000 },
    "gta3.the_exchange": { launchIndex: 79, reward: 1000000 }
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
        missionStateDropLogged: false
    };
}

function resetAttemptEvidence(context) {
    context.previousMoney = readMoney();
    context.rewardObserved = false;
    context.missionStateDropLogged = false;
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
    return Boolean(native("IS_PLAYER_DEAD", 0)) ||
        Boolean(native("HAS_PLAYER_BEEN_ARRESTED", 0));
}

function tryFadeOut() {
    try {
        native("DO_FADE", 500, 0);
    } catch (error) {
        log("[GHMR] fade-out unavailable: " + error);
    }
}

log("[GHMR] GTA III bridge v0.1.5 loading");
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
                    bridgeVersion: "0.1.5",
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
                // GTA III DE's Retry UI reloads the CLEO runtime but does not
                // recreate a mission that GHMR launched internally. Wait for
                // stable free roam, then explicitly launch the same locked
                // mission index again. This is a retry, never a reroll.
                phase = "retryLaunchPending";
                controlReported = false;
                resetAttemptEvidence(context);
                // Preserve any stable-free-roam time already observed while
                // the Retry-time CLEO reload was being confirmed. This keeps
                // the total delay near three seconds after control returns.
                log("[GHMR] same-mission relaunch requested after failure");
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
            phase = "retryLaunchPending";
            log("[GHMR] recovered retry; waiting to relaunch locked mission");
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

    if (phase === "starting" && context !== null &&
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

        if (!missionState && !context.missionStateDropLogged) {
            context.missionStateDropLogged = true;
            log(
                "[GHMR] ONMISSION cleared while mission is active; " +
                "retaining the locked mission context"
            );
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
        }
    }

    if (phase === "released" && releaseFadeRequested &&
        !releaseFadeStarted) {
        releaseFadeStarted = true;
        tryFadeOut();
    }

    wait(pollIntervalMs);
}
