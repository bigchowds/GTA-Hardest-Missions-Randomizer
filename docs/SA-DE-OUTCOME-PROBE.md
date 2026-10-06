# San Andreas Definitive Edition: Outcome Probe

This is the final San Andreas Phase 0 diagnostic. It checks that a failed and retried attempt does not produce GHMR's success signal, while completing the mission does. The script only reads `ONMISSION` and `GET_PROGRESS_PERCENTAGE`; it does not finish, restart or modify missions or saves.

## Before launching

1. Exit San Andreas completely.
2. Open `Gameface\Binaries\Win64\CLEO` in the San Andreas installation.
3. Remove `ghmr_sa_mission_state_probe.js` and any other earlier GHMR probe.
4. Copy `ghmr_sa_outcome_probe.js` into that `CLEO` folder.
5. Leave `tsconfig.json`, `.config`, `CLEO_PLUGINS` and generated files unchanged.

Only `ghmr_sa_outcome_probe.js` should remain as a GHMR test script.

## Run the test

1. Launch San Andreas using the same method that successfully loaded CLEO Redux `1.5.0`.
2. Reach free roam and approach an available story-mission marker.
3. Start the mission.
4. Deliberately fail once by getting wasted, getting busted or triggering one of that mission's normal failure conditions.
5. When the game offers a checkpoint retry, choose **Retry**, not Cancel.
6. Complete the same mission successfully.
7. Wait in free roam for at least five seconds after the mission-passed sequence.
8. Exit the game normally and send back `Gameface\Binaries\Win64\cleo_redux.log`.

Tell GHMR how the deliberate failure was triggered. This helps correlate the game's retry behaviour with the log.

## Expected evidence

The exact transition order may differ between missions. Important lines are:

```text
[GHMR] SA outcome probe started
[GHMR] Host: sa_unreal
[GHMR] Initial state: ONMISSION=false progress=...
[GHMR] MISSION START: attempt 1 baseline progress=...
[GHMR] PROGRESS ADVANCE: ... -> ...
[GHMR] OUTCOME PASS: attempt ... advanced progress by ...
```

During the deliberate failure and retry, there must be no `OUTCOME PASS` line. Depending on how the game handles that failure, the probe may report `RETRY/CHAIN CANDIDATE`, `OUTCOME NO-PROGRESS`, or simply continue producing heartbeats with unchanged progress. All three are useful results.

## Interpretation limit

Story-progress advancement is a strong completion signal for the selected story missions, but it is not assumed to identify every side mission in every GTA title. GHMR will validate each of the final fifteen chosen missions individually before Normal mode is released.

This original probe used a save below 100% and therefore could not exercise the
completion-percentage ceiling. Production bridge v0.1.4 additionally observes
the engine's `Missions Passed` stat. That signal is covered by 100%-save success
and failure/Retry simulations and requires live confirmation in the five-game
integration route.
