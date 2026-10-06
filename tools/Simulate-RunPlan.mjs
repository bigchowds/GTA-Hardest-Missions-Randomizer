import { readFileSync } from "node:fs";
import { randomBytes } from "node:crypto";
import { fileURLToPath } from "node:url";
import { dirname, join } from "node:path";
import assert from "node:assert/strict";

const scriptDirectory = dirname(fileURLToPath(import.meta.url));
const catalogPath = join(scriptDirectory, "..", "config", "missions.v0.1.json");
const catalog = JSON.parse(readFileSync(catalogPath, "utf8"));

function makeRng(seed) {
  let state = seed >>> 0 || 0x6d2b79f5;
  function nextUint32() {
    state ^= state << 13;
    state ^= state >>> 17;
    state ^= state << 5;
    return state >>> 0;
  }

  return {
    nextInt(exclusiveUpperBound) {
      if (!Number.isSafeInteger(exclusiveUpperBound) || exclusiveUpperBound <= 0) {
        throw new RangeError("exclusiveUpperBound must be a positive safe integer");
      }

      const threshold = 0x100000000 % exclusiveUpperBound;
      let value;
      do {
        value = nextUint32();
      } while (value < threshold);
      return value % exclusiveUpperBound;
    }
  };
}

const trilogyGames = new Set(["gta3de", "vcde", "sade"]);

function shuffle(items, seed) {
  const result = [...items];
  const random = makeRng(seed);
  const trilogyMissions = result.filter((mission) => trilogyGames.has(mission.game));
  const openingMission = trilogyMissions[random.nextInt(trilogyMissions.length)];
  result.splice(result.indexOf(openingMission), 1);
  for (let index = result.length - 1; index > 0; index--) {
    const swapIndex = random.nextInt(index + 1);
    [result[index], result[swapIndex]] = [result[swapIndex], result[index]];
  }
  result.unshift(openingMission);
  return result;
}

function generatePlan(seed) {
  return {
    internalSeed: seed >>> 0,
    currentIndex: 0,
    failures: 0,
    missions: shuffle(catalog.missions, seed)
  };
}

function failCurrentMission(plan) {
  plan.failures++;
  return plan.missions[plan.currentIndex];
}

function completeCurrentMission(plan) {
  const completed = plan.missions[plan.currentIndex];
  plan.currentIndex++;
  return completed;
}

function selfTest() {
  assert.equal(catalog.missions.length, 15, "v0.1 must contain exactly 15 missions");
  const gameCounts = new Map();
  for (const mission of catalog.missions) {
    gameCounts.set(mission.game, (gameCounts.get(mission.game) ?? 0) + 1);
  }
  assert.equal(gameCounts.size, 5, "v0.1 must contain exactly five games");
  for (const [game, count] of gameCounts) {
    assert.equal(count, 3, `${game} must contribute exactly three missions`);
  }

  const expectedIds = catalog.missions.map((mission) => mission.id).sort();

  for (let seed = 1; seed <= 10000; seed++) {
    const plan = generatePlan(seed);
    const actualIds = plan.missions.map((mission) => mission.id).sort();
    assert.deepEqual(actualIds, expectedIds, `seed ${seed} changed the mission pool`);
    assert.ok(
      trilogyGames.has(plan.missions[0].game),
      `seed ${seed} did not begin with a Definitive Edition Trilogy mission`
    );

    const beforeFailure = plan.missions[plan.currentIndex].id;
    for (let failure = 0; failure < 3; failure++) {
      const retry = failCurrentMission(plan).id;
      assert.equal(retry, beforeFailure, `seed ${seed} rerolled after failure`);
      assert.equal(plan.currentIndex, 0, `seed ${seed} advanced after failure`);
    }
    assert.equal(plan.failures, 3, `seed ${seed} lost a failure event`);

    const completed = completeCurrentMission(plan).id;
    assert.equal(completed, beforeFailure, `seed ${seed} completed a different mission`);
    assert.equal(plan.currentIndex, 1, `seed ${seed} did not advance after completion`);

    while (plan.currentIndex < plan.missions.length) {
      const lockedMission = plan.missions[plan.currentIndex].id;
      assert.equal(
        failCurrentMission(plan).id,
        lockedMission,
        `seed ${seed} rerolled a later mission after failure`
      );
      assert.equal(
        completeCurrentMission(plan).id,
        lockedMission,
        `seed ${seed} completed the wrong later mission`
      );
    }
    assert.equal(
      plan.currentIndex,
      plan.missions.length,
      `seed ${seed} did not finish the complete mission catalog`
    );
  }

  console.log("Self-test passed: 10,000 plans started in the Trilogy, were deterministic, preserved all missions and never rerolled on failure.");
}

function printPlan(plan) {
  console.log(`Development seed: ${plan.internalSeed}`);
  plan.missions.forEach((mission, index) => {
    console.log(`${String(index + 1).padStart(2, "0")}. [${mission.game}] ${mission.title}`);
  });
}

if (process.argv.includes("--self-test")) {
  selfTest();
} else {
  const seedArgument = process.argv.find((argument) => argument.startsWith("--seed="));
  const seed = seedArgument
    ? Number.parseInt(seedArgument.slice("--seed=".length), 10) >>> 0
    : randomBytes(4).readUInt32LE(0);
  printPlan(generatePlan(seed));
}
