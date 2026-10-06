// GHMR read-only outcome probe for GTA San Andreas: The Definitive Edition.
// It observes mission state and story progress. It does not finish, restart or modify missions.

const expectedHost = "sa_unreal";
const pollIntervalMs = 250;
const heartbeatIntervalMs = 5000;
const endConfirmationMs = 5000;
const progressEpsilon = 0.000001;

function readProgress() {
    try {
        const value = Number(native("GET_PROGRESS_PERCENTAGE"));

        if (value !== value) {
            log("[GHMR] ERROR: GET_PROGRESS_PERCENTAGE returned a non-number");
            exit();
        }

        return value;
    } catch (error) {
        log("[GHMR] ERROR: could not read progress: " + error);
        exit();
        return 0;
    }
}

function formatProgress(value) {
    return value.toFixed(6);
}

log("[GHMR] SA outcome probe started");
log("[GHMR] Host: " + HOST);
log("[GHMR] CLEO Redux: " + CLEO.version);
log("[GHMR] Host version: " + CLEO.hostVersion);

if (HOST !== expectedHost) {
    log("[GHMR] ERROR: expected host " + expectedHost + "; stopping probe");
    exit();
}

let previousMissionState = Boolean(ONMISSION);
let previousProgress = readProgress();
let attemptBaselineProgress = previousProgress;
let attemptNumber = previousMissionState ? 1 : 0;
let pendingEnd = null;
let heartbeatPolls = 0;

const heartbeatPollLimit = Math.ceil(heartbeatIntervalMs / pollIntervalMs);
const endConfirmationPollLimit = Math.ceil(endConfirmationMs / pollIntervalMs);

log(
    "[GHMR] Initial state: ONMISSION=" + previousMissionState +
    " progress=" + formatProgress(previousProgress)
);
log("[GHMR] Ready; fail once, retry, then complete the same story mission");

while (true) {
    const currentMissionState = Boolean(ONMISSION);
    const currentProgress = readProgress();
    const progressChange = currentProgress - previousProgress;

    if (progressChange > progressEpsilon) {
        log(
            "[GHMR] PROGRESS ADVANCE: " + formatProgress(previousProgress) +
            " -> " + formatProgress(currentProgress)
        );
        previousProgress = currentProgress;
    } else if (progressChange < -progressEpsilon) {
        log(
            "[GHMR] PROGRESS RESET: " + formatProgress(previousProgress) +
            " -> " + formatProgress(currentProgress) +
            " (save/load candidate)"
        );
        previousProgress = currentProgress;
        attemptBaselineProgress = currentProgress;
        pendingEnd = null;
    }

    if (currentMissionState !== previousMissionState) {
        if (currentMissionState) {
            if (pendingEnd !== null) {
                const pendingDelta = currentProgress - pendingEnd.baselineProgress;

                if (pendingDelta > progressEpsilon) {
                    log(
                        "[GHMR] OUTCOME PASS: attempt " + pendingEnd.attemptNumber +
                        " advanced progress by " + formatProgress(pendingDelta)
                    );
                } else {
                    log(
                        "[GHMR] RETRY/CHAIN CANDIDATE: mission resumed without " +
                        "a progress increase"
                    );
                }

                pendingEnd = null;
            }

            attemptNumber += 1;
            attemptBaselineProgress = currentProgress;
            log(
                "[GHMR] MISSION START: attempt " + attemptNumber +
                " baseline progress=" + formatProgress(attemptBaselineProgress)
            );
        } else {
            const attemptDelta = currentProgress - attemptBaselineProgress;

            log(
                "[GHMR] MISSION END CANDIDATE: attempt " + attemptNumber +
                " progress=" + formatProgress(currentProgress)
            );

            if (attemptDelta > progressEpsilon) {
                log(
                    "[GHMR] OUTCOME PASS: attempt " + attemptNumber +
                    " advanced progress by " + formatProgress(attemptDelta)
                );
            } else {
                pendingEnd = {
                    attemptNumber: attemptNumber,
                    baselineProgress: attemptBaselineProgress,
                    pollsRemaining: endConfirmationPollLimit
                };
            }
        }

        previousMissionState = currentMissionState;
    }

    if (pendingEnd !== null && !currentMissionState) {
        const pendingDelta = currentProgress - pendingEnd.baselineProgress;

        if (pendingDelta > progressEpsilon) {
            log(
                "[GHMR] OUTCOME PASS: attempt " + pendingEnd.attemptNumber +
                " advanced progress by " + formatProgress(pendingDelta)
            );
            pendingEnd = null;
        } else {
            pendingEnd.pollsRemaining -= 1;

            if (pendingEnd.pollsRemaining <= 0) {
                log(
                    "[GHMR] OUTCOME NO-PROGRESS: attempt " + pendingEnd.attemptNumber +
                    " ended without progress (failure/cancel/transition candidate)"
                );
                pendingEnd = null;
            }
        }
    }

    heartbeatPolls += 1;

    if (heartbeatPolls >= heartbeatPollLimit) {
        log(
            "[GHMR] HEARTBEAT: ONMISSION=" + currentMissionState +
            " progress=" + formatProgress(currentProgress)
        );
        heartbeatPolls = 0;
    }

    wait(pollIntervalMs);
}
