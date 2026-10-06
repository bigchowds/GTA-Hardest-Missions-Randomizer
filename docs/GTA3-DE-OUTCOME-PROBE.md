# GTA III Definitive Edition: Restart/Outcome Probe v2

GTA III DE already runs CLEO Redux `1.5.0` and CLEO scripts successfully on the test installation. The first GHMR outcome probe exposed an important game-specific difference: GTA III DE does not provide `GET_PROGRESS_PERCENTAGE` through its CLEO command definitions. The v2 probe therefore uses only commands GTA III DE provides.

It observes:

- the read-only `ONMISSION` flag;
- `IS_PLAYER_DEAD` and `HAS_PLAYER_BEEN_ARRESTED`;
- the player's money through `STORE_SCORE`, as supporting evidence for a mission reward.

It does not finish, fail, restart or modify missions or saves.

## Install v2

1. Exit GTA III completely.
2. Open `Gameface\Binaries\Win64\CLEO` in the GTA III installation.
3. Delete the old `ghmr_gta3_outcome_probe.js`.
4. Copy the new `ghmr_gta3_outcome_probe.js` from this package into `CLEO`.
5. The existing cheat-menu folder may remain installed, but do not open or use the menu during this test.
6. Leave `tsconfig.json`, `.config`, `CLEO_PLUGINS` and generated files unchanged.

The first GHMR line must say `GTA III restart/outcome probe v2 started`. If it does not, the old file is still installed.

## Run one controlled test

1. Launch GTA III using the same method that already loads CLEO Redux successfully.
2. Load a save and reach free roam outside a mission.
3. Start any available story mission.
4. Deliberately get Claude **wasted** once. Use a normal in-game cause such as gunfire, fire or an explosion; do not use the cheat menu.
5. Allow Definitive Edition to restart the mission from the beginning. No separate Retry button is required.
6. Complete that restarted mission successfully.
7. Wait in free roam for at least five seconds after the mission-passed sequence.
8. Exit normally and send back `Gameface\Binaries\Win64\cleo_redux.log`.

Tell GHMR which mission was played. The log should show the wasted signal, whether `ONMISSION` remained active through the restart, and whether the successful end included the mission's money reward.

## Expected evidence

```text
[GHMR] GTA III restart/outcome probe v2 started
[GHMR] Host: gta3_unreal
[GHMR] MISSION START: attempt ...
[GHMR] FAILURE SIGNAL: player wasted; ...
[GHMR] MISSION END: attempt ...
[GHMR] REWARDED END CANDIDATE: ...
```

The exact transition order can differ if the automatic restart reloads CLEO's runtime. A new v2 startup line after failure is acceptable and useful evidence.

Money is supporting diagnostic evidence, not a universal completion rule. The three selected GTA III missions will each receive a mission-specific completion check before Normal mode is released.

## Observed result

The controlled normal-mission test produced enough evidence to finish the generic restart investigation:

- the failed attempt collected a small amount of ordinary cash before ending;
- choosing the checkpoint retry reloaded the CLEO runtime and restored the earlier money value;
- the retried mission remained the same mission;
- successful completion ended the mission and produced its much larger mission reward.

This also exposed a deliberate limitation in the v2 diagnostic: any positive money delta was labelled a `REWARDED END CANDIDATE`, so ordinary cash pickups could make a failed attempt look like a possible success. Production code must never advance solely because `ONMISSION` became false or the player's money increased.

The production bridge now treats each selected mission's full scripted reward
jump as completion evidence and explicit wasted/busted state as immediate
failure evidence. It deliberately ignores `ONMISSION` clearing by itself,
because the first internally launched Espresso-2-Go! test showed that flag can
clear while the mission remains playable. A checkpoint reload keeps the
current mission and locked randomizer state; it does not advance or reroll.

## v0.1.5 live verification

The isolated controller test on 2 October 2026 passed cleanly. Bridge v0.1.5
launched Espresso-2-Go! at internal index `72`, retained the locked context
through the transient `ONMISSION` clear, observed the exact `$40,000` scripted
reward and emitted one `missionCompleted` event. The controller acknowledged
that event and sent `releaseBridge` with reason `done`. No transport disconnect,
manufactured failure, restart command or duplicate mission launch occurred.

This closes the transient INI-read regression that v0.3.9 was designed to fix.
The cheat-menu script in that particular test installation was disposed by its
own CLEO runtime and F1 did not open it; GHMR's bridge remained loaded and the
mission result was unaffected.
