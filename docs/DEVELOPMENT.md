# Development

The public beta runs exactly five selected missions. `config/missions.v0.1.json` also contains research entries for a larger pool; their presence does not mean they are available or verified in the beta. Ten missions and Chaos Mode are planned separately.

## Repository layout

| Path | Purpose |
|---|---|
| `src/Ghmr.Controller` | Windows UI, launch/close orchestration, bridge setup and diagnostics |
| `src/Ghmr.Core` | Run state, timers, event handling and handoff rules |
| `mods/cleo-bridge` | Production Trilogy bridge source and shared INI transport |
| `mods/scripthookdotnet-bridge` | Production GTA IV/V bridge source |
| `mods/*probe*` | Historical development probes, not installed by the beta |
| `config` | Catalog and CLEO definitions |
| `tests`, `tools` | Automated checks and compatibility tooling |
| `docs` | Current player docs plus historical test/update notes |
| `.github/workflows/build.yml` | Windows checks, publish and package |

## Build from source

Use Windows with the .NET 8 SDK and Node.js. The published player app includes its runtime; the SDK is needed only to build.

```powershell
dotnet restore GHMR.sln
dotnet build GHMR.sln --configuration Release
dotnet run --project tests/Ghmr.Core.SelfTest/Ghmr.Core.SelfTest.csproj --configuration Release --no-build
dotnet run --project tests/Ghmr.ControllerLog.SelfTest/Ghmr.ControllerLog.SelfTest.csproj --configuration Release --no-build
node tools/Simulate-RunPlan.mjs --self-test
```

Run the remaining `tools/Test-*.mjs` checks specified by the workflow for affected code. The workflow also checks the GTA IV bridge and produces a complete Windows package; publishing only the EXE locally does not supply the installer adapters.

## Verification boundaries

Automated tests cover state/event/transport/identity/cancellation behavior, not real launcher timing or game compatibility. Record native evidence for launch, Retry, completion and affected cross-game boundaries. Preserve successful behavior and keep cosmetic/packaging work separate from mission logic.

Historical notes such as `V0414-UPDATE-NOTES.md` describe the pre-test state of their update. The current recorded integration result is in [TESTED-COMPATIBILITY.md](TESTED-COMPATIBILITY.md).
