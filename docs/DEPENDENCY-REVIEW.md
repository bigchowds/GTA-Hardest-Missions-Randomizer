# Dependency and Reuse Review

Status: active review. The controller and CLEO transport are independently
written; no third-party mission code or game assets have been imported.

The rule is simple: a public repository is not automatically reusable. Code is copied or modified only when its licence explicitly permits it, and all required notices must be retained.

| Project | Licence / terms | Potential GHMR use | Current decision |
|---|---|---|---|
| [CLEO Redux](https://github.com/cleolibrary/CLEO-Redux) | Project EULA | JavaScript bridge for the Definitive Edition trilogy | External prerequisite. GHMR uses the installed public scripting API and does not redistribute CLEO binaries. VC DE RGL `1.0.112.6680` is pinned to `1.4.2` x64 after a local `1.5.0` initialization failure. SA DE and GTA III DE use tested x64 installations. |
| [GTA IV ScriptHookDotNet](https://github.com/HazardX/gta4_scripthookdotnet) | zlib / bundled ScriptHook terms | Managed GTA IV bridge | External prerequisite; GHMR distributes only its readable `.cs` bridge. Complete Edition `1.2.0.59` with ScriptHookDotNet `1.7.1.9` passed native launch, Retry and completion. The adapter includes a compatibility path for that build's old blip icon/type enumeration bug. |
| [GTA-DE-Cheat-Menu](https://github.com/mateusz-korbut/GTA-DE-Cheat-Menu) | ISC | Known-working Trilogy DE script layout and UI examples | Safe reference; reuse only with notice |
| [ScriptHookVDotNetEnhanced](https://github.com/Chiheb-Bacha/ScriptHookVDotNetEnhanced) | zlib | Managed GTA V Enhanced bridge | External prerequisite; API/reference use approved. Phase 0 pins release `1.1.0.6`; its ASI, v2/v3 API DLLs and INI must be installed as one matching set. |
| [Script Hook V](https://www.dev-c.com/gtav/scripthookv/) | Project SDK terms | Required native GTA V hook | Official external prerequisite; do not mirror |
| [.NET 8](https://dotnet.microsoft.com/) | MIT | Controller runtime and standard library | Controller source targets `net8.0-windows`; release workflow publishes a self-contained Windows build |
| [Windows XInput](https://learn.microsoft.com/windows/win32/xinput/getting-started-with-xinput) | Windows system API | Focus-scoped controller-menu input | Uses the OS `XInputGetState` API only; no controller driver or remapping library is bundled |
| [LiveSplit](https://github.com/LiveSplit/LiveSplit) | MIT | Optional timer and splits integration through its named-pipe protocol | Approved; protocol integration does not require bundling LiveSplit |
| [Ultimate ASI Loader](https://github.com/ThirteenAG/Ultimate-ASI-Loader) | MIT | Loads ASI modules where required | Prefer official installer/download; reassess before redistribution |
| [GTA III Rainbomizer](https://github.com/GTAMadman/GTA-III-Rainbomizer) | GPL-3.0 | Mission and vehicle compatibility research for classic GTA III | Reference only pending GHMR licence decision |
| [Vice City Rainbomizer](https://github.com/GTAMadman/Vice-City-Rainbomizer) | GPL-3.0 | Mission and vehicle compatibility research for classic Vice City | Reference only; it does not support VC DE |
| [SA Rainbomizer](https://github.com/Parik27/SA.Rainbomizer) | GPL-3.0 | Mission shuffle, safe vehicle selection and restart patterns | Reference only; it supports classic SA rather than SA DE |
| [IV/EFLC Rainbomizer](https://github.com/Parik27/IV.EFLC.Rainbomizer) | GPL-3.0 | Mission shuffle and compatibility logic; tested against current Steam GTA IV | Strong reuse candidate if GHMR adopts GPL-3.0 |
| [V Rainbomizer](https://github.com/Parik27/V.Rainbomizer) | GPL-3.0 | Mission/vehicle/weapon randomizer architecture | Reference only; GTA V Enhanced is explicitly unsupported |
| [CLEO Redux Missions Framework](https://github.com/wmysterio/CLEO-Redux-Missions-Framework) | Apache-2.0 | Mission lifecycle and launcher patterns | Reference only; targets classic SA 1.0 and custom missions |

## Material deliberately excluded

- Decompiled Rockstar mission scripts and extracted game assets are not copied into the repository, even if a third-party repository assigns a permissive licence to its collection.
- Unsupported executable offsets and memory signatures are not copied from classic or Legacy editions into Definitive/Enhanced adapters.
- Closed-source trainers and files without a clear redistribution licence are not bundled.

## Licence decision gate

Directly adapting GPL-3.0 Rainbomizer code would require the relevant GHMR derivative work to remain GPL-compatible and publish corresponding source. That is compatible with the project's open-source goal and may save substantial GTA IV work.

Until the project licence is chosen, Rainbomizer code is treated as research material only. New GHMR controller and protocol code remains independently written.
