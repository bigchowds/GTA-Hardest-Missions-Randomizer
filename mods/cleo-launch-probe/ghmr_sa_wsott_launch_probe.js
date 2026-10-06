// GHMR controlled-launch probe for GTA San Andreas: The Definitive Edition.
//
// This development probe starts the game's original "Wrong Side of the Tracks"
// mission only after the player presses F6 in controllable free roam. It does
// not contain or replace Rockstar mission code. Do not save after this test:
// completing an original story mission can change the loaded game's progress.

const expectedHost = "sa_unreal";
const wrongSideOfTheTracksMissionIndex = 29;
const launchKey = 117; // Windows virtual-key code for F6.
const pollIntervalMs = 100;
const outcomeConfirmationMs = 5000;
const progressEpsilon = 0.000001;

function readProgress() {
    return Number(native("GET_PROGRESS_PERCENTAGE"));
}

function canLaunch() {
    if (Boolean(ONMISSION)) {
        return "a mission is already active";
    }

    if (!Boolean(native("IS_PLAYER_PLAYING", 0))) {
        return "the player is not in controllable gameplay";
    }

    if (!Boolean(native("CAN_PLAYER_START_MISSION", 0))) {
        return "the player cannot start a mission in the current state";
    }

    return null;
}

function notify(message) {
    log("[GHMR] " + message);

    try {
        showTextBox("GHMR: " + message);
    } catch (error) {
        log("[GHMR] On-screen message unavailable: " + error);
    }
}

log("[GHMR] SA Wrong Side of the Tracks launch probe started");
log("[GHMR] Host: " + HOST);
log("[GHMR] CLEO Redux: " + CLEO.version);
log("[GHMR] Host version: " + CLEO.hostVersion);

if (HOST !== expectedHost) {
    log("[GHMR] ERROR: expected host " + expectedHost + "; stopping probe");
    exit();
}

let previousKeyDown = false;
let previousMissionState = Boolean(ONMISSION);
let launchedByProbe = false;
let launchProgress = 0;
let pendingOutcomePolls = 0;

notify("ready in free roam; press F6 once to launch Wrong Side of the Tracks");

while (true) {
    const keyDown = Boolean(Pad.IsKeyPressed(launchKey));

    if (keyDown && !previousKeyDown) {
        if (launchedByProbe) {
            notify("launch ignored; this probe permits one mission launch per game session");
        } else {
            const blockedReason = canLaunch();

            if (blockedReason !== null) {
                notify("launch blocked: " + blockedReason);
            } else {
                launchProgress = readProgress();
                log(
                    "[GHMR] LAUNCH REQUEST: missionIndex=" +
                    wrongSideOfTheTracksMissionIndex +
                    " baselineProgress=" + launchProgress.toFixed(6)
                );

                try {
                    // The original main script sets ONMISSION before opcode 0417.
                    ONMISSION = true;
                    native(
                        "LOAD_AND_LAUNCH_MISSION_INTERNAL",
                        wrongSideOfTheTracksMissionIndex
                    );
                    launchedByProbe = true;
                    log("[GHMR] LAUNCH COMMAND ACCEPTED");
                } catch (error) {
                    ONMISSION = false;
                    log("[GHMR] LAUNCH ERROR: " + error);
                    notify("launch failed; exit and send cleo_redux.log");
                }
            }
        }
    }

    previousKeyDown = keyDown;

    const currentMissionState = Boolean(ONMISSION);
    if (currentMissionState !== previousMissionState) {
        log(
            "[GHMR] MISSION STATE: ONMISSION=" + previousMissionState +
            " -> " + currentMissionState
        );

        if (launchedByProbe && !currentMissionState) {
            pendingOutcomePolls = Math.ceil(
                outcomeConfirmationMs / pollIntervalMs
            );
            log("[GHMR] OUTCOME PENDING");
        }

        previousMissionState = currentMissionState;
    }

    if (launchedByProbe && pendingOutcomePolls > 0 && !currentMissionState) {
        const progressDelta = readProgress() - launchProgress;

        if (progressDelta > progressEpsilon) {
            log(
                "[GHMR] OUTCOME PASS: progress advanced by " +
                progressDelta.toFixed(6)
            );
            notify("mission completion detected; exit without saving");
            pendingOutcomePolls = 0;
        } else {
            pendingOutcomePolls -= 1;

            if (pendingOutcomePolls === 0) {
                log(
                    "[GHMR] OUTCOME NO-PROGRESS: mission ended without a " +
                    "story-progress increase"
                );
                notify("no completion signal yet; use Retry if the mission failed");
            }
        }
    }

    wait(pollIntervalMs);
}
