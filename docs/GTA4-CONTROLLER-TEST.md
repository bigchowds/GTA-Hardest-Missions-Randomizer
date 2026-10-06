# GTA IV Three Leaf Clover Controller Test

The isolated GTA IV route passed in game on 27 September 2026. It validates
one mission, not the other two GTA IV missions or the complete fifteen-mission
run.

## Verified environment

- GTA IV: Complete Edition `1.2.0.59`
- GTA IV ScriptHookDotNet `1.7.1.9`
- GHMR GTA IV bridge `0.1.17-retained-event-delivery`
- save slot `SGTA406`, in-game title **Waste Not Want Knots**
- mission: **Three Leaf Clover** (`Packie3`)

## Verified behaviour

The bridge:

- waits for stable, controllable free roam;
- sets a mission-compatible clock value once;
- finds GTA IV's live green Packie marker;
- works around the tested ScriptHookDotNet build's icon/type blip-enumeration
  bug without using guessed world coordinates;
- moves Niko onto the live marker and lets GTA IV's original mission manager
  launch `Packie3`;
- never directly launches `Packie3` during death or failure cleanup;
- leaves GTA IV's native death screen and phone Retry in control;
- safely reattaches after native Retry; and
- requires the exact `$250,000` reward plus the mission completion cue or
  target-script end before reporting completion.

Live testing passed automatic launch, explosion death without a freeze, native
phone Retry, safe bridge reattachment and full completion. The final log
contained:

```text
Completion reward observed; increase=250000
Completion confirmed for gta4.three_leaf_clover
```

## Install or repair

1. Install a compatible GTA IV ScriptHookDotNet build externally. The GTA IV
   folder must already contain `ScriptHookDotNet.asi` and `ScriptHook.dll`.
2. Open **Setup Games & Bridges** in GHMR.
3. Select **GTA IV: Complete Edition**.
4. Browse to the inner `GTAIV` folder and select `GTAIV.exe`.
5. Choose **Install/Repair Bridge**.

GHMR installs exactly one readable file:

```text
GTAIV\scripts\ghmr_gta4_bridge.cs
```

Repair removes only superseded GHMR GTA IV CLEO bridge/probe files. It does not
remove ScriptHookDotNet, CLEO Redux, Legacy Trainer or unrelated mods.

## Steam launch

For a Steam installation, GHMR launches registered Steam App ID `12210` rather
than invoking `GTAIV.exe` directly. This avoids Steam's **Launch Game with
custom arguments** confirmation. The configured executable remains the source
of the installation directory and expected process name.

## Remaining GTA IV work

**The Snow Storm** and **Out of Commission** still require their own native
launch, failure, Retry and mission-specific completion evidence before the
fifteen-mission Normal mode can be considered release-ready.
