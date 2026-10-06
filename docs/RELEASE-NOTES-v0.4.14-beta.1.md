# GHMR — Five-Mission Beta 1

Five GTA games, five missions, a fresh order each run. The first mission is selected from the Definitive Edition trilogy; complete it and GHMR switches to the next game.

| Game | Mission |
|---|---|
| GTA III DE | Espresso-2-Go! |
| Vice City DE | Demolition Man |
| San Andreas DE | Wrong Side of the Tracks |
| GTA IV Complete Edition | Three Leaf Clover |
| GTA V Enhanced | Derailed |

Download **GHMR-v0.4.14-beta.1-win-x64.zip**, extract everything, and read **START-HERE.txt**. The automatic **Source code** downloads are not the Windows app. All five supported games, compatible scripting runtimes and clean game-specific saves are required; they are not bundled.

**GTA IV requires a clean save from before Three Leaf Clover with Packie's mission marker available and suitable clothing.** An arbitrary 100% GTA IV save is not the tested launch baseline.

The beta includes random-order runs, same-mission retry tracking, gameplay/real timers, creator thumbnails, optional transition music and diagnostic logs. A successful run shows **Finished / 5/5**; the last game intentionally stays open.

This package uses the v0.4.14 mission/handoff logic validated in a recorded developer QA run: SA → VC → V → IV → III, with five accepted completions and zero failures. That does not prove every environment or permutation, and trainer-assisted QA is not a counted competition run.

**Known limitations:** the app is unsigned; some Windows configurations may warn or block it. Manual Resume/Story Mode input can still be needed. Startup logos/black screens can follow the transition overlay. English foreground/capturable GTA V results are the tested early-completion path; other languages and HDR/exclusive fullscreen need testing. GHMR does not automatically copy/restore saves. Single-player only.

**Windows security:** this beta has no digital signature. Microsoft Defender SmartScreen may warn about an unrecognized app. Windows 11 Smart App Control can block unsigned apps and has no per-app allow option. A Microsoft Defender Antivirus threat detection is different: report its exact name, affected file, GHMR version and hash rather than assuming it is harmless. Do not disable protection or add broad exclusions. SHA-256 checksums, readable scripts and build information are provided; they do not bypass Windows protection. See [Windows security details](https://github.com/bigchowds/GTA-Hardest-Missions-Randomizer/blob/main/docs/TRUST-AND-RELEASES.md#unsigned-windows-app).

Please report bugs with the version, game/build, mission, previous game, relevant sanitized logs and a screenshot/clip. Feedback will guide the planned **10-mission update** and **Chaos Mode**, with five additional Chaos missions being considered. No date is promised.

YouTube: https://www.youtube.com/@BigChowds
