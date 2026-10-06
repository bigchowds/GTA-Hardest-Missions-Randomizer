import fs from 'node:fs';
import path from 'node:path';
import assert from 'node:assert/strict';

const root = path.resolve(process.argv[2] ?? 'artifacts/controller');
const required = [
  'GHMR.Controller.exe', 'START-HERE.txt', 'README.md', 'LICENSE',
  'CHANGELOG.md', 'CONTRIBUTING.md', 'release.json',
  'config/missions.v0.1.json', 'config/cleo/ghmr-ini-extension.v1.json',
  'adapters/cleo/ghmr_ini_transport.js',
  'adapters/cleo/ghmr-ini-extension.v1.json',
  'adapters/sade/ghmr_sa_bridge.js', 'adapters/gta3de/ghmr_gta3_bridge.js',
  'adapters/vcde/ghmr_vc_bridge.js', 'adapters/gta4/ghmr_gta4_bridge.cs',
  'adapters/gtav_enhanced/ghmr_gtav_bridge.3.cs',
  'docs/INSTALL.md', 'docs/TESTED-COMPATIBILITY.md',
  'docs/TROUBLESHOOTING.md', 'docs/SAVE-BASELINE.md',
  'docs/FIVE-MISSION-BETA-RULES.md', 'docs/CONTROLLER-SUPPORT.md',
  'docs/TRUST-AND-RELEASES.md', 'docs/MEDIA-CREDITS.md', 'docs/DEVELOPMENT.md',
  'docs/assets/ghmr-banner.svg',
  'media/transitions/01-every-gta-different.jpg',
  'media/transitions/02-rarest.jpg', 'media/transitions/03-cheats.jpg',
  'media/transitions/04-softlock.jpg', 'media/transitions/05-logic.jpg',
  'media/transitions/06-best-gun.jpg', 'media/transitions/transition-theme.mp3'
];

for (const name of required) {
  const file = path.join(root, name);
  assert(fs.existsSync(file) && fs.statSync(file).isFile(), `Missing release file: ${name}`);
  assert(fs.statSync(file).size > 0, `Empty release file: ${name}`);
}

const metadata = JSON.parse(fs.readFileSync(path.join(root, 'release.json'), 'utf8').replace(/^\uFEFF/, ''));
assert.equal(metadata.packageVersion, '0.4.14-beta.1');
assert.equal(metadata.controllerVersion, '0.4.14');
assert.equal(metadata.runtime, 'win-x64');
assert.equal(metadata.unsigned, true);
assert.match(metadata.sourceCommit, /^[a-f0-9]{40}$/i);
assert.match(metadata.sourceRepository, /^[A-Za-z0-9_.-]+\/[A-Za-z0-9_.-]+$/);
assert.match(metadata.buildRun, /^https:\/\/github\.com\/[^/]+\/[^/]+\/actions\/runs\/\d+$/);
assert(metadata.buildRun.startsWith(`https://github.com/${metadata.sourceRepository}/actions/runs/`));

const bridges = [
  ['sade', '0.1.4', 'adapters/sade/ghmr_sa_bridge.js'],
  ['gta3de', '0.1.5', 'adapters/gta3de/ghmr_gta3_bridge.js'],
  ['vcde', '0.1.7', 'adapters/vcde/ghmr_vc_bridge.js'],
  ['gta4', '0.1.17-retained-event-delivery', 'adapters/gta4/ghmr_gta4_bridge.cs'],
  ['gtav_enhanced', '0.1.13-foreground-restore-confirm', 'adapters/gtav_enhanced/ghmr_gtav_bridge.3.cs']
];
for (const [game, version, file] of bridges) {
  assert.equal(metadata.bridgeVersions[game], version, `Wrong declared bridge version: ${game}`);
  const source = fs.readFileSync(path.join(root, file), 'utf8');
  const normalized = source.replaceAll("\\", "");
  assert(source.includes(`v${version.split('-')[0]}`) && normalized.includes(`"${version}"`), `Wrong packaged bridge marker: ${game}`);
}
const transport = fs.readFileSync(path.join(root, 'adapters/cleo/ghmr_ini_transport.js'), 'utf8');
assert(transport.includes('GHMR INI transport v0.2.0-verified-event-writes'));

const catalog = JSON.parse(fs.readFileSync(path.join(root, 'config/missions.v0.1.json'), 'utf8'));
for (const id of ['gta3.espresso_2_go', 'vc.demolition_man', 'sa.wrong_side_of_the_tracks', 'gta4.three_leaf_clover', 'gtav.derailed']) {
  assert(catalog.missions.some(m => m.id === id), `Missing beta mission: ${id}`);
}

function inspect(directory) {
  for (const entry of fs.readdirSync(directory, { withFileTypes: true })) {
    const file = path.join(directory, entry.name);
    assert(!entry.isSymbolicLink(), `Unexpected link in release: ${entry.name}`);
    if (entry.isDirectory()) {
      assert(!['.git', 'obj', '.vs', '.vscode'].includes(entry.name), `Unexpected private/build directory: ${entry.name}`);
      inspect(file);
    } else {
      assert(!/\.(log|dmp|dump|pdb|pfx|key|sav|tmp|bak)$/i.test(entry.name), `Unexpected private/debug file: ${entry.name}`);
      assert(!/^\.env(?:\.|$)/i.test(entry.name), `Unexpected environment file: ${entry.name}`);
      assert(!/^(game-launch-profiles|run-state|controller-preferences)\.json$/i.test(entry.name), `Unexpected personal settings: ${entry.name}`);
      assert(!/^ScriptHook.*\.(dll|asi)$/i.test(entry.name), `Unexpected third-party runtime: ${entry.name}`);
    }
  }
}
inspect(root);
console.log(`Beta package check passed: ${required.length} required files, five bridge versions, mission catalog and release metadata.`);
