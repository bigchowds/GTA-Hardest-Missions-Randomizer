# v0.4.7 controller diagnostics and completion delivery

This update preserves the v0.4.6 visible mission labels, normal-close retries,
focus handling, audio cutoff and San Andreas bridge v0.1.4. It adds controller
diagnostics and corrects one identified completion-delivery failure path.

## Controller log

GHMR automatically appends to:

```text
%LOCALAPPDATA%\GHMR\controller\logs\GHMR-controller.log
```

Open **Options -> Open Logs** while playing or after stopping a run. Send
`GHMR-controller.log` when reporting a problem. The log preserves the status
that preceded "Run aborted", so a stopped-run screenshot is no longer the only
evidence. It records:

- controller version, process ID and runtime;
- bridge-reported versions and session IDs;
- incoming events, accepted/rejected decisions, mission progress and failures;
- release commands and acknowledgement timeouts;
- each normal close attempt and whether Windows accepted the request;
- close/launch outcomes, handoff stages and elapsed times;
- focus and transition-overlay operations;
- music operations and multimedia error codes;
- exceptions, including native Windows error codes where available.

Writes run on a background task with a bounded queue. Log storage failure does
not throw into gameplay or wait on the bridge dispatcher. The current log is
limited to approximately 2 MiB, with three backups named
`GHMR-controller.1.log` through `GHMR-controller.3.log`. Exception text is bounded
and common user-profile paths are redacted. The log remains local.

## Completion-delivery correction

Previously the INI reader advanced its received sequence, then wrote the
acknowledgement, then queued the event for the run engine. If that file write
failed, the event was never queued and the next poll skipped it as already
received. A bridge could therefore log "completion confirmed" while the
controller stayed on the same mission.

The reader now queues the event before advancing/writing the acknowledgement.
If writing fails, the accepted event still reaches the dispatcher; the
acknowledgement can recover on a subsequent pulse without dispatching a second
completion. This is a confirmed code defect, but the user's earlier SA log and
stopped-run screenshot do not prove it caused that specific run.

## Build and install

1. Close GHMR and the GTA games. Upload the ZIP's contents into the existing
   repository root and replace matching files, including `.github` and `GHMR.sln`.
2. Wait for **Build and self-test** to turn green. The workflow includes a new
   compiled controller diagnostic test and a Windows test that deliberately
   blocks the acknowledgement file during a completion event.
3. Download `GHMR-controller-win-x64`, then extract
   `GHMR-v0.4.7-controller-logs-win-x64.zip` into a fresh folder.
4. Open v0.4.7. The supplied SA log showed bridge v0.1.2, so select
   **Setup Game Bridges -> San Andreas DE -> Install / Repair Bridge** once to
   install the packaged v0.1.4 startup adapter. Replacing the EXE alone does not
   update the script inside SA.
5. Select **Diagnostics -> Test all five (fixed order)**. Initially verify only
   Wrong Side of the Tracks -> Espresso-2-Go!; continue the route after that
   handoff succeeds.
6. If switching stalls, open **Options -> Open Logs** and send
   `GHMR-controller.log` before starting another test. If the file has rotated,
   include the `.1.log` backup as well. Stop Run and the controller retain the
   earlier diagnostic lines.

The existing thirteen JavaScript simulations/source checks pass in the source
workspace. The new C# tests and Windows build run in GitHub Actions; there is no
.NET SDK in the source workspace. A real game handoff remains the final check.
