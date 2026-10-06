# v0.4.5 non-blocking audio-cutoff update

v0.4.5 is rebuilt from the proven v0.4.3 source and supersedes v0.4.4.

The v0.4.4 experiment attempted to stop the Windows MCI audio alias directly
inside the serialized bridge-event path. On the live system this left the run
at **Preparing**, prevented the mission text from updating and failed to stop
the music. It must not be released.

## v0.4.5 behaviour

- `missionPrepared` only schedules the audio cutoff and immediately continues
  into the normal mission-start command.
- The actual MCI stop/close operation runs on the controller UI thread that
  opened the track.
- No audio operation can hold up later `playerControlGained`, completion or
  failure events.
- A separate 60-second music-only safety limit prevents endless playback even
  if a bridge-ready signal is missing.
- The visual handoff still remains until actual mission control is confirmed.
- The v0.4.3 ownerless overlay and game-focus fix are unchanged.

No bridge script changed. Do not run **Setup Game Bridges** again if all five
v0.4.2-or-newer bridges already report installed.

## Build and test

1. Upload the update ZIP's contents into the repository root and replace the
   matching files.
2. Commit the upload and wait for **Build and self-test** to turn green.
3. Download `GHMR-controller-win-x64` from that run.
4. Extract `GHMR-v0.4.5-nonblocking-audio-cutoff-win-x64.zip` to a fresh folder.
5. Close every GTA game and the v0.4.4 controller.
6. Run v0.4.5 and select **Diagnostics -> Test all five (fixed order)**.
7. Complete Wrong Side of the Tracks and watch the San Andreas -> GTA III
   handoff. Espresso-2-Go! must start, the controller must change from
   **Preparing** to **Running**, and the music must stop before mission gameplay
   or at the 60-second safety limit, whichever happens first.
