import fs from "node:fs";

const expected = [
  ["sade", "0.1.4", "mods/cleo-bridge/ghmr_sa_bridge.js"],
  ["gta3de", "0.1.5", "mods/cleo-bridge/ghmr_gta3_bridge.js"],
  ["vcde", "0.1.7", "mods/cleo-bridge/ghmr_vc_bridge.js"],
  ["gta4", "0.1.17-retained-event-delivery", "mods/scripthookdotnet-bridge/ghmr_gta4_bridge.cs"],
  ["gtav_enhanced", "0.1.13-foreground-restore-confirm", "mods/scripthookdotnet-bridge/ghmr_gtav_bridge.3.cs"]
];

const compatibility = fs.readFileSync(
  "src/Ghmr.Controller/Installation/BridgeCompatibility.cs",
  "utf8"
);
const workflow = fs.readFileSync(".github/workflows/build.yml", "utf8");
const transportMarker = "// GHMR INI transport v0.2.0-verified-event-writes";
require(fs.readFileSync("mods/cleo-bridge/ghmr_ini_transport.js", "utf8")
  .includes(transportMarker), "The shared transport must advertise verified event writes.");
require(compatibility.includes(transportMarker) &&
  compatibility.includes("source.Contains(RequiredIniTransportMarker"),
  "Installed Trilogy bridges must be checked for the current shared transport.");
require(workflow.includes("Copy-Item mods/cleo-bridge/ghmr_ini_transport.js"),
  "The build must stage the shared transport.");

for (const [game, version, sourcePath] of expected) {
  const source = fs.readFileSync(sourcePath, "utf8");
  require(
    source.includes(`bridgeVersion`) && source.includes(version),
    `${sourcePath} does not advertise required bridge v${version}.`
  );
  require(
    compatibility.includes(`["${game}"]`) && compatibility.includes(`"${version}"`),
    `BridgeCompatibility does not require ${game} bridge v${version}.`
  );
  require(
    workflow.includes(`Copy-Item ${sourcePath}`),
    `The build workflow does not stage ${sourcePath}.`
  );
}

console.log("Bridge version compatibility self-test passed.");

function require(condition, message) {
  if (!condition) throw new Error(message);
}
