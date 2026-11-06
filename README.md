# Ostrabus

Ostrabus is intentionally an omnibus mod package for **Ostranauts**. I write it for my own enjoyment of the game, and it combines bug fixes, simulation improvements, performance enhancements, and quality-of-life features.

I'm making it public as inspiration, reference material, or a source of code to copy into your own mods. Feel free to borrow a particular module under the repository's [license](LICENSE).

There is no guarantee it will be maintained for new game patches. Issues and pull requests are welcome, but will most likely be ignored.

## Inventory of changes

### Bug fixes

- [Shallow refueling](RefuelRCSPatch.cs): replenish RCS fuel instead of incorrectly adding the fuel to ship mass.
- [AI navigation targets](MiscBugFixes/ShipReplacementTargetsPatch.cs): preserve destination references when target ships are unloaded and reconstructed, preventing runaway route calculations.
- [Hauler returns](Haulers/README.md): restore the home target after derelict release and cancel thrust left behind by abandoned commands.
- [Navy target selection](Navigation/NavyCandidateFuelPatch.cs): reject unaffordable candidates before selection, closing the single-candidate and all-rejected fuel-check bypasses.
- [Propulsion checks](Navigation/NavigationFuel.cs): treat invalid RCS delta-v as zero so ships without usable thrusters cannot bypass low-fuel checks.
- [AI return orders](Navigation/StationReturn.cs): preserve flight and docking stages across save/reload, and discard obsolete trip fuel predictions when retargeting.
- [Planetary gravity](Orbital/README.md): correct missing parent-body acceleration when integrating free objects near an orbiting planet.
- [VORB derelicts](Venus/README.md): restore native derelict anchoring and correct the orbit lookup used after undocking.
- [Docked gravity](MiscBugFixes/DOCKED-GRAVITY.md): inherit the anchored group's gravity after transit instead of leaving reconstructed ships at the default 0.3 g.
- [Ship-view rotation](MiscBugFixes/ShipRotationPatch.cs): check whether the ship being rotated is docked, rather than an unrelated ship occupied by the captain.
- [Docking clearance](MiscBugFixes/DockingGridPrecisionPatch.cs): eliminate phantom obstructions caused by tiny coordinate errors in docking-fit checks.
- [Remote barter](MiscBugFixes/BarterRemovalPatch.cs): remove sold items from unloaded ship inventories instead of crediting the sale while leaving the item available to sell again.
- [Container mass](MiscBugFixes/STACK-REMOVAL-MASS.md): prevent repeated mass subtraction during nested removals and preserve mass changes made through remote barter copies.
- [Door auto-close](MiscBugFixes/DOOR-AUTO-CLOSE.md): preserve the auto-close setting through power loss and restoration.
- [Automatic work selection](MiscBugFixes/AutoTaskScratchPatch.cs): keep patching, repair, and restoration candidate lists separate instead of letting native scratch-list aliasing mix them together.
- [Crew sleeping](MiscBugFixes/CREW-REST.md): prevent autonomous player crew from choosing backpacks and EVA suits as beds.
- [Idle sitting](MiscBugFixes/CREW-REST.md): let autonomously seated crew resume available automatic patching, repair, or restoration work while preserving manual seating orders.
- [Adjacent wall work](MiscBugFixes/WALL-WORK-PATH.md): stop workers trying to walk into a wall when they are already within reach of its work order.

### Simulation improvements

- [Shallow boarding combat](ShipboardAction/README.md): resolve hostile dockings between Galcon, CCRE, and Belt Pirates using crew numbers, weapon loadouts, injuries, and randomized casualties over time.
- [Prize ships](ShipboardAction/README.md): let victors assign a spare healthy crew member to return a captured, flyable ship home for disposal, while keeping captains on their original ships.
- [Ceres navigation](CeresNavigation/README.md): apply a homeward fuel reserve to patrol selection and chase retreat decisions.
- [Free coasting](Orbital/README.md): reduce timestep-dependent orbital drift with velocity-Verlet integration and consistent native orbital coordinates.
- [Super Fast Forward](Orbital/SFF-IMPLEMENTATION.md): replace hours-long constant-acceleration jumps with orbital coasting and partial AI travel while retaining native plotted courses and combat handling.
- [VORB wreck variety](Venus/README.md): use Venus-specific wrecks for 70% of VORB wreck spawns.

### Performance enhancements

- [Save loading](Optimizations/README.md): replace repeated scans of every saved ship with a shared ship-ID lookup.
- [System initialization](Optimizations/README.md): batch cheap coroutine steps within a frame budget instead of spending a frame on each tiny restoration step.
- [Work planning](AlternativeWork/README.md): reuse route and carried-material searches, and prepare upcoming work while the current job runs.
- [Save parsing](Optimizations/README.md): provide an experimental, opt-in Newtonsoft reader with compatibility handling for the game's LitJson save format.

### Quality of life

- [Alternative autotask](AlternativeWork/README.md): favor nearby, reachable, supplied work and prevent crew from reserving jobs on vessels their permissions forbid them to enter.
- [Crew hotkeys](Hotkeys/README.md): use Shift+R to toggle Open Airlock and Shift+T to toggle Disembark for the selected controllable crew member.
- [Proximity alarm dial](NavConsole/README.md): choose a ship's warning range from 5, 10, 15, or 20 km using a dial on the nav console.
- [Kang auto-vents](QoL/AutoVents/README.md): tolerate brief airlock-induced oxygen-pressure dips while retaining rapid closure for dangerous depressurization.
- [Stabilizer sound cues](MiscBugFixes/StabilizerCreakPatch.cs): scale the maneuver creaking threshold with active stabilizers' native damage reduction.
- [PDA settings](PdaSettings/README.md): toggle supported mod features and adjust their settings through a native PDA app.
- [Bounty indicators](Patch.cs): mark missing bounty targets as MIA rather than leaving their absence unexplained.
- [Maintenance commands](MiscBugFixes/README.md): repair existing container masses with `massfix` and recover corrupted navigation targets with `navfix`.
- [Console noise](MiscBugFixes/README.md): keep routine electrical-audit and missing-font-glyph spam out of the player console while retaining diagnostic logs.
- [Inspection API](Inspection/INSPECTION-API.md): inspect ships, crew, AI, navigation, work orders, and logs through localhost endpoints, with explicit pruning tools for cleanup.

[Build, install, and run the offline checks](Build/README.md).
