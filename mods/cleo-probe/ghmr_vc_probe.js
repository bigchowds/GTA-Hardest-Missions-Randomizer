// GHMR Phase 0 probe for GTA Vice City: The Definitive Edition.
// This script does not read or modify save data.

const probeMessage = "GHMR VC PROBE LOADED";

log("[GHMR] Vice City DE probe started");
log("[GHMR] Host: " + HOST);
log("[GHMR] CLEO Redux: " + CLEO.version);
log("[GHMR] Host version: " + CLEO.hostVersion);
showTextBox(probeMessage);

while (true) {
    wait(1000);
}

