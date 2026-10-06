import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const source = fs.readFileSync(
    path.join(root, "src", "Ghmr.Core", "Run", "RunEngine.cs"),
    "utf8"
);

assert.doesNotMatch(
    source,
    /_acceptedMessageIds\.Add\(message\.MessageId\)/,
    "RunEngine still de-duplicates bare CLEO message ids across games."
);
assert.match(
    source,
    /MessageDedupeKey\(BridgeEnvelope message\)[\s\S]*?message\.Game[\s\S]*?message\.BridgeSessionId[\s\S]*?message\.MessageId/,
    "RunEngine de-duplication must include game, bridge session and message id."
);
assert.match(
    source,
    /_acceptedMessageIds\.Add\(dedupeKey\)/,
    "RunEngine does not use the scoped key for duplicate detection."
);
assert.match(
    source,
    /_state\.AcceptedMessageIds\.Add\(dedupeKey\)/,
    "Persisted de-duplication state does not use the scoped key."
);
assert.match(
    source,
    /_acceptedMessageIds\.Remove\(dedupeKey\)[\s\S]*?_state\.AcceptedMessageIds\.Remove\(dedupeKey\)/,
    "Rejected messages do not release the scoped de-duplication key."
);

console.log("RunEngine bridge-session message-scope self-test passed.");
