// GHMR read-only mission-state probe for GTA Vice City: The Definitive Edition.
// This script observes the game's mission flag. It does not modify missions or saves.

const expectedHost = "vc_unreal";
const pollIntervalMs = 100;

log("[GHMR] VC mission-state probe started");
log("[GHMR] Host: " + HOST);
log("[GHMR] CLEO Redux: " + CLEO.version);
log("[GHMR] Host version: " + CLEO.hostVersion);

if (HOST !== expectedHost) {
    log("[GHMR] ERROR: expected host " + expectedHost + "; stopping probe");
    exit();
}

let previousMissionState = Boolean(ONMISSION);
let transitionCount = 0;

log("[GHMR] Initial ONMISSION=" + previousMissionState);
log("[GHMR] Ready; start a story mission, then pass or fail it normally");

while (true) {
    const currentMissionState = Boolean(ONMISSION);

    if (currentMissionState !== previousMissionState) {
        transitionCount += 1;
        log(
            "[GHMR] Mission transition " + transitionCount +
            ": ONMISSION=" + previousMissionState +
            " -> " + currentMissionState
        );
        previousMissionState = currentMissionState;
    }

    wait(pollIntervalMs);
}
