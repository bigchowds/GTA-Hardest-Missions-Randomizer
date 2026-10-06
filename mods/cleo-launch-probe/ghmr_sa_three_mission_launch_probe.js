// GHMR controlled-launch probe for GTA San Andreas: The Definitive Edition.
//
// Test one mission per full game launch:
//   F6 - Wrong Side of the Tracks (mission index 29)
//   F7 - Supply Lines...             (mission index 73)
//   F8 - End of the Line, part 1     (mission index 110)
//
// End of the Line is implemented by the original game as three linked mission
// scripts. This probe starts part 1 and leaves every transition to the game.
// Do not save after testing: original mission success logic can alter progress.

const expectedHost = "sa_unreal";
const pollIntervalMs = 100;
const startupMissionConfirmationMs = 2000;
const outcomeConfirmationMs = 5000;
const progressEpsilon = 0.000001;

const missions = [
    {
        key: 117,
        keyName: "F6",
        missionIndex: 29,
        title: "Wrong Side of the Tracks"
    },
    {
        key: 118,
        keyName: "F7",
        missionIndex: 73,
        title: "Supply Lines..."
    },
    {
        key: 119,
        keyName: "F8",
        missionIndex: 110,
        title: "End of the Line"
    }
];

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

function describeMenu() {
    return missions
        .map(function (mission) {
            return mission.keyName + "=" + mission.title;
        })
        .join("; ");
}

log("[GHMR] SA three-mission launch probe started");
log("[GHMR] Host: " + HOST);
log("[GHMR] CLEO Redux: " + CLEO.version);
log("[GHMR] Host version: " + CLEO.hostVersion);

if (HOST !== expectedHost) {
    log("[GHMR] ERROR: expected host " + expectedHost + "; stopping probe");
    exit();
}

const keyStates = {};
for (const mission of missions) {
    keyStates[mission.key] = false;
}

const initialMissionState = Boolean(ONMISSION);
let previousMissionState = initialMissionState;
let launchLocked = false;
let activeContext = null;
let activeBaselineProgress = readProgress();
let pendingOutcome = null;
let startupMissionPolls = initialMissionState
    ? Math.ceil(startupMissionConfirmationMs / pollIntervalMs)
    : 0;

if (initialMissionState) {
    log(
        "[GHMR] STARTUP OBSERVATION: ONMISSION is true; waiting to distinguish " +
        "normal game startup from a Retry runtime reload"
    );
}

notify("ready in free roam; " + describeMenu());

while (true) {
    const currentMissionState = Boolean(ONMISSION);

    if (startupMissionPolls > 0) {
        if (!currentMissionState) {
            startupMissionPolls = 0;
            log("[GHMR] STARTUP STATE SETTLED: no active mission");
        } else {
            startupMissionPolls -= 1;

            if (startupMissionPolls === 0) {
                launchLocked = true;
                activeContext = "mission active after CLEO runtime reload";
                activeBaselineProgress = readProgress();
                log(
                    "[GHMR] RECOVERY: active mission confirmed after runtime " +
                    "reload; baselineProgress=" +
                    activeBaselineProgress.toFixed(6)
                );
            }
        }
    }

    for (const mission of missions) {
        const keyDown = Boolean(Pad.IsKeyPressed(mission.key));

        if (keyDown && !keyStates[mission.key]) {
            if (launchLocked) {
                notify(
                    "launch ignored; restart the game before testing another mission"
                );
            } else {
                const blockedReason = canLaunch();

                if (blockedReason !== null) {
                    notify("launch blocked: " + blockedReason);
                } else {
                    const baselineProgress = readProgress();
                    log(
                        "[GHMR] LAUNCH REQUEST: title=" + mission.title +
                        " missionIndex=" + mission.missionIndex +
                        " baselineProgress=" + baselineProgress.toFixed(6)
                    );

                    try {
                        ONMISSION = true;
                        native(
                            "LOAD_AND_LAUNCH_MISSION_INTERNAL",
                            mission.missionIndex
                        );
                        launchLocked = true;
                        activeContext = mission.title;
                        activeBaselineProgress = baselineProgress;
                        pendingOutcome = null;
                        log(
                            "[GHMR] LAUNCH COMMAND ACCEPTED: " + mission.title
                        );
                    } catch (error) {
                        ONMISSION = false;
                        log("[GHMR] LAUNCH ERROR: " + error);
                        notify("launch failed; exit and send cleo_redux.log");
                    }
                }
            }
        }

        keyStates[mission.key] = keyDown;
    }

    if (currentMissionState !== previousMissionState) {
        log(
            "[GHMR] MISSION STATE: ONMISSION=" + previousMissionState +
            " -> " + currentMissionState
        );

        if (currentMissionState) {
            if (pendingOutcome !== null) {
                log(
                    "[GHMR] RETRY OR LINKED PART: mission became active again"
                );
                pendingOutcome = null;
                activeBaselineProgress = readProgress();
            }
        } else if (activeContext !== null && startupMissionPolls === 0) {
            pendingOutcome = {
                context: activeContext,
                baselineProgress: activeBaselineProgress,
                pollsRemaining: Math.ceil(
                    outcomeConfirmationMs / pollIntervalMs
                )
            };
            log("[GHMR] OUTCOME PENDING: " + activeContext);
        }

        previousMissionState = currentMissionState;
    }

    if (pendingOutcome !== null && !currentMissionState) {
        const progressDelta =
            readProgress() - pendingOutcome.baselineProgress;

        if (progressDelta > progressEpsilon) {
            log(
                "[GHMR] OUTCOME PASS: " + pendingOutcome.context +
                " progress advanced by " + progressDelta.toFixed(6)
            );
            notify("mission completion detected; exit and restore the backup");
            pendingOutcome = null;
            activeContext = null;
        } else {
            pendingOutcome.pollsRemaining -= 1;

            if (pendingOutcome.pollsRemaining === 0) {
                log(
                    "[GHMR] OUTCOME NO-PROGRESS: " + pendingOutcome.context +
                    " ended without a story-progress increase"
                );
                notify("no completion signal; use Retry if the mission failed");
                pendingOutcome = null;
            }
        }
    }

    wait(pollIntervalMs);
}
