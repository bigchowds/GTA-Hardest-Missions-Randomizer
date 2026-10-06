// GHMR read-only compatibility probe for GTA IV: Complete Edition.
// It verifies the CLEO Redux host and observes mission-state transitions.
// It does not start, finish, fail or modify missions or saves.

const expectedHost = "gta_iv";
const pollIntervalMs = 250;
const heartbeatIntervalMs = 5000;
const heartbeatPollLimit = Math.ceil(heartbeatIntervalMs / pollIntervalMs);

log("[GHMR] GTA IV compatibility probe v1 started");
log("[GHMR] Host: " + HOST);
log("[GHMR] CLEO Redux: " + CLEO.version);
log("[GHMR] API definitions: " + CLEO.apiVersion);
log("[GHMR] Host version: " + CLEO.hostVersion);

if (HOST !== expectedHost) {
    log("[GHMR] ERROR: expected host " + expectedHost + "; stopping probe");
    exit();
}

let previousMissionState = Boolean(ONMISSION);
let heartbeatPolls = 0;

log("[GHMR] Initial state: ONMISSION=" + previousMissionState);
log("[GHMR] Ready; load Story Mode, enter free roam, then start any mission");

while (true) {
    const currentMissionState = Boolean(ONMISSION);

    if (currentMissionState !== previousMissionState) {
        log(
            "[GHMR] MISSION STATE: " + previousMissionState +
            " -> " + currentMissionState
        );
        previousMissionState = currentMissionState;
    }

    heartbeatPolls += 1;

    if (heartbeatPolls >= heartbeatPollLimit) {
        log("[GHMR] HEARTBEAT: ONMISSION=" + currentMissionState);
        heartbeatPolls = 0;
    }

    wait(pollIntervalMs);
}
