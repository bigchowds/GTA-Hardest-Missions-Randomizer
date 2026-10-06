// GHMR read-only restart/outcome probe v2 for GTA III: The Definitive Edition.
// It observes mission state, wasted/busted state and money. It does not modify missions or saves.

const expectedHost = "gta3_unreal";
const pollIntervalMs = 250;
const heartbeatIntervalMs = 5000;

function readNumber(commandName, argument) {
    try {
        const value = Number(native(commandName, argument));

        if (value !== value) {
            throw new Error(commandName + " returned a non-number");
        }

        return value;
    } catch (error) {
        log("[GHMR] ERROR: " + commandName + " failed: " + error);
        exit();
        return 0;
    }
}

function readCondition(commandName, argument) {
    try {
        return Boolean(native(commandName, argument));
    } catch (error) {
        log("[GHMR] ERROR: " + commandName + " failed: " + error);
        exit();
        return false;
    }
}

function formatSigned(value) {
    return value >= 0 ? "+" + value : String(value);
}

log("[GHMR] GTA III restart/outcome probe v2 started");
log("[GHMR] Host: " + HOST);
log("[GHMR] CLEO Redux: " + CLEO.version);
log("[GHMR] Host version: " + CLEO.hostVersion);

if (HOST !== expectedHost) {
    log("[GHMR] ERROR: expected host " + expectedHost + "; stopping probe");
    exit();
}

let previousMissionState = Boolean(ONMISSION);
let previousMoney = readNumber("STORE_SCORE", 0);
let missionStartMoney = previousMoney;
let previousDead = readCondition("IS_PLAYER_DEAD", 0);
let previousArrested = readCondition("HAS_PLAYER_BEEN_ARRESTED", 0);
let attemptNumber = previousMissionState ? 1 : 0;
let heartbeatPolls = 0;

const heartbeatPollLimit = Math.ceil(heartbeatIntervalMs / pollIntervalMs);

log(
    "[GHMR] Initial state: ONMISSION=" + previousMissionState +
    " money=" + previousMoney +
    " dead=" + previousDead +
    " arrested=" + previousArrested
);
log("[GHMR] Ready; get wasted once, allow the automatic restart, then complete the mission");

while (true) {
    const currentMissionState = Boolean(ONMISSION);
    const currentMoney = readNumber("STORE_SCORE", 0);
    const currentDead = readCondition("IS_PLAYER_DEAD", 0);
    const currentArrested = readCondition("HAS_PLAYER_BEEN_ARRESTED", 0);

    if (currentDead && !previousDead) {
        log(
            "[GHMR] FAILURE SIGNAL: player wasted; ONMISSION=" +
            currentMissionState + " money=" + currentMoney
        );
    }

    if (currentArrested && !previousArrested) {
        log(
            "[GHMR] FAILURE SIGNAL: player busted; ONMISSION=" +
            currentMissionState + " money=" + currentMoney
        );
    }

    if (currentMoney !== previousMoney) {
        log(
            "[GHMR] MONEY CHANGE: " + previousMoney +
            " -> " + currentMoney +
            " (delta " + formatSigned(currentMoney - previousMoney) + ")"
        );
        previousMoney = currentMoney;
    }

    if (currentMissionState !== previousMissionState) {
        if (currentMissionState) {
            attemptNumber += 1;
            missionStartMoney = currentMoney;
            log(
                "[GHMR] MISSION START: attempt " + attemptNumber +
                " baseline money=" + missionStartMoney
            );
        } else {
            const moneyDelta = currentMoney - missionStartMoney;

            log(
                "[GHMR] MISSION END: attempt " + attemptNumber +
                " money delta=" + formatSigned(moneyDelta)
            );

            if (moneyDelta > 0) {
                log(
                    "[GHMR] REWARDED END CANDIDATE: mission ended after a positive " +
                    "money change"
                );
            } else {
                log(
                    "[GHMR] UNREWARDED END: no positive money change " +
                    "(failure/restart/transition candidate)"
                );
            }
        }

        previousMissionState = currentMissionState;
    }

    heartbeatPolls += 1;

    if (heartbeatPolls >= heartbeatPollLimit) {
        log(
            "[GHMR] HEARTBEAT: ONMISSION=" + currentMissionState +
            " money=" + currentMoney +
            " dead=" + currentDead +
            " arrested=" + currentArrested
        );
        heartbeatPolls = 0;
    }

    previousDead = currentDead;
    previousArrested = currentArrested;
    wait(pollIntervalMs);
}
