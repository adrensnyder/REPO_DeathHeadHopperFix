# Death Head Hopper Fix

This mod builds on the work of *Death Head Hopper* version **2.1.8** by Cronchy—it's not a full replacement but a compatibility and stability patch that hardens runtime checks, recalculates jump/hop forces, and keeps the charge energy pipeline balanced so the game or server does not lock up while spectating after death.

## Updates
- In case of an update, I suggest resetting the configuration to the default values.

## New functionality
- Adds the ability to tie jumps to the `DeathBattery` function so each jump consumes battery energy, effectively limiting hop/spike forces when the device is drained.
- Introduces the option to recharge stamina using the same baseline that the vanilla game uses, while bumping the ability cost from 40 to 60 to keep it in check.
- Exposes every balance variable in `BepInEx/config/AdrenSnyder.DeathHeadHopperFix.cfg`, covering logging flags, battery thresholds, stamina recharge rates, cost scalars, and multiplier tweaks so you can tune them.

## Fixes
- Protects `SpectateCamera` and `DeathHeadController` routines from missing fields or destroyed game objects so spectating remains stable.
- Tames `HopHandler`, `JumpHandler`, and `ChargeHandler` with diminishing returns and tighter effect control.
- Keeps scene-spanning prefabs (Photon pools, stats/upgrades, etc.) alive so required references never vanish.

## Requirements
- BepInEx-BepInExPack-5.4.2100
- Zehs-REPOLib-2.1.0
- Cronchy-DeathHeadHopper-2.1.8

## Configuration
The configuration file `BepInEx/config/AdrenSnyder.DeathHeadHopperFix.cfg` exposes all of the logging, battery, cost, and multiplier options that this patch applies. Adjust them only while in-game if you want to tweak the energy behavior, stamina recharge pacing, or enable debug logging.

## Credits
Thanks to Cronchy for creating the original *Death Head Hopper* mod that inspired this compatibility fix.  
Original mod: https://thunderstore.io/c/repo/p/Cronchy/DeathHeadHopper/
