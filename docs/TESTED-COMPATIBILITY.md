# Tested compatibility and prerequisites

This table records development environments, not a promise that every store/build/Windows combination has passed. New game updates can require a matching scripting runtime. Check the current upstream compatibility notes before changing a working setup.

| Game | Recorded game build | Tested scripting runtime | Packaged GHMR bridge |
|---|---|---|---|
| GTA III: Definitive Edition | Build must be supported by the installed CLEO runtime; no exact III executable build is claimed here | CLEO Redux 1.5.0 x64 + IniFiles64 | 0.1.5 |
| GTA Vice City: Definitive Edition | Rockstar Launcher 1.0.112.6680 | CLEO Redux 1.4.2 x64 + IniFiles64 | 0.1.7 |
| GTA San Andreas: Definitive Edition | Rockstar Launcher 1.0.112.6680 | CLEO Redux 1.5.0 x64 + IniFiles64 | 0.1.4 |
| GTA IV: Complete Edition | 1.2.0.59 | GTA IV ScriptHookDotNet 1.7.1.9 | 0.1.17-retained-event-delivery |
| GTA V Enhanced | Development API reported 1012 during initial probing; exact later full-run executable build is not independently pinned | Matching Script Hook V + ScriptHookVDotNet Enhanced 1.1.0.6 development baseline | 0.1.13-foreground-restore-confirm |

Vice City's packaged bridge identifies itself as **0.1.7**. Some historical test notes mention an intermediate 0.1.8 milestone; the package's script marker and compatibility guard are authoritative.

The controller is self-contained Windows x64, targeting Windows 10 version 2004 or later. English Windows text recognition is used for the experimental VC Resume and early Derailed pass recognition. The native GTA V bridge remains a fallback. Borderless/capturable English game windows are the known test path; other languages, HDR and exclusive fullscreen need verification.

## Official upstream sources

- [CLEO Redux releases](https://github.com/cleolibrary/CLEO-Redux/releases): obtain the tested 1.5.0/1.4.2 x64 release as appropriate. [Installation documentation](https://re.cleo.li/docs/en/installation.html).
- [Ultimate ASI Loader](https://github.com/ThirteenAG/Ultimate-ASI-Loader/releases): an external loader where the game/runtime needs one. Do not combine loaders blindly.
- [GTA IV ScriptHookDotNet original project](https://github.com/HazardX/gta4_scripthookdotnet) and [Complete Edition fork by Priler](https://github.com/Priler/gta4_scripthookdotnet): follow the runtime's own installation/compatibility instructions. The old original releases are not automatically interchangeable with the tested 1.7.1.9 CE runtime. A version string alone does not prove two downloaded archives are identical.
- [Script Hook V](https://www.dev-c.com/gtav/scripthookv/): native hook compatible with the installed GTA V Enhanced build.
- [ScriptHookVDotNet Enhanced releases](https://github.com/Chiheb-Bacha/ScriptHookVDotNetEnhanced/releases): install one complete matching release. Its runtime dependencies are separate from GHMR's included .NET 8 runtime.
- [LiveSplit](https://livesplit.org/): optional passive timing display. No automatic GHMR integration is bundled.

GHMR does not redistribute these runtimes, game data, trainers or saves. Classic III/VC/SA, GTA V Legacy, console, multiplayer and Linux/Proton are outside this beta's tested support scope. Steam launch routing is implemented; the recorded developer environment does not establish every possible storefront combination.

## Recorded integration check — 6 October 2026

A developer QA run using controller v0.4.14 completed all five missions in random-order mode:

**San Andreas → Vice City → GTA V Enhanced → GTA IV → GTA III**

The controller recorded **Finished, 5/5, zero failures**, with four successful game handoffs. Recorded audio was compared with the supplied transition track and stopped before each destination's main menu. Temporary foreground-activation warnings during VC/III startup recovered. This validates that route on the developer setup; it does not validate every permutation or establish a counted, unassisted competition run.

This beta packaging revision preserves the tested bridge, run-engine and handoff code. A freshly built release must still pass CI and the package inspection before publishing.
