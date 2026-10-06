import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const toolDirectory = path.dirname(fileURLToPath(import.meta.url));
const sourcePath = path.join(
    toolDirectory,
    "..",
    "src",
    "Ghmr.Controller",
    "IniBridgeServer.cs"
);
const source = fs.readFileSync(sourcePath, "utf8");
const readIni = source.slice(source.indexOf("private static Dictionary<string, string> ReadIni"));
if (!readIni.includes("FileShare.ReadWrite | FileShare.Delete") ||
    readIni.includes("File.ReadAllLines(path)")) {
    throw new Error("Controller INI reads must permit simultaneous game-side writes.");
}
const coordinator = fs.readFileSync(path.join(
    toolDirectory,
    "..",
    "src",
    "Ghmr.Controller",
    "ControllerCoordinator.cs"
), "utf8");
const declaration = source.match(
    /private\s+static\s+readonly\s+string\[\]\s+Games\s*=\s*\[([^\]]+)\]/
);

if (declaration === null) {
    throw new Error("Could not find IniBridgeServer.Games.");
}

const configured = [...declaration[1].matchAll(/"([^"]+)"/g)]
    .map((match) => match[1]);
const expected = ["sade", "gta3de", "vcde", "gta4", "gtav_enhanced"];

if (JSON.stringify(configured) !== JSON.stringify(expected)) {
    throw new Error(
        `INI bridge game slots are '${configured.join(",")}'; ` +
        `expected '${expected.join(",")}'.`
    );
}

if (!/string\.IsNullOrWhiteSpace\(raw\)/.test(source) ||
    !/System\.Text\.Json\.JsonException/.test(source) ||
    /Rejected incomplete game bridge message/.test(source)) {
    throw new Error(
        "Controller must silently retry a game frame observed during an in-place write."
    );
}

if (!/public\s+void\s+PrepareForLaunch\(string game\)/.test(source) ||
    !/slot\.Session\s*=\s*Guid\.NewGuid\(\)\.ToString\("N"\)/.test(source)) {
    throw new Error(
        "Every cross-game launch must rotate the destination INI session."
    );
}

const handoffStart = coordinator.indexOf(
    "case ControllerAction.LaunchNextMission:"
);
const handoffEnd = coordinator.indexOf(
    "case ControllerAction.FinishRun:",
    handoffStart
);
const handoff = coordinator.slice(handoffStart, handoffEnd);
if (handoffStart < 0 || handoffEnd < 0 ||
    !handoff.includes("_bridgeServer.PrepareForLaunch(next.Game);") ||
    !handoff.includes("_handoffTasks.Add(PerformHandoffAsync(") ||
    /await\s+PerformHandoffAsync\s*\(/.test(handoff)) {
    throw new Error(
        "Cross-game process discovery must not block the serialized bridge dispatcher."
    );
}

if (!/private\s+async\s+Task\s+PerformHandoffAsync[\s\S]*?await\s+Task\.Yield\(\)/.test(
        coordinator)) {
    throw new Error(
        "Background handoff must yield before process discovery begins."
    );
}

if (!/new\s+BridgeConnectionEventArgs\([\s\S]*?game:\s*slot\.Game\)/.test(source) ||
    !/args\.Game[\s\S]*?current\?\.Game/.test(coordinator)) {
    throw new Error(
        "Bridge transport status must identify and filter by the current game."
    );
}

console.log(
    `Controller INI game-slot/read-race/handoff self-test passed: ${configured.join(", ")}.`
);
