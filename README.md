# Ostrabus

Ostrabus is intentionally an omnibus mod package for **Ostranauts**. I write it for my own enjoyment of the game, and it combines bug fixes, simulation improvements, performance enhancements, and quality-of-life features.

I'm making it public as inspiration, reference material, or a source of code to copy into your own mods. Feel free to borrow a particular module under the repository's [license](LICENSE).

There is no guarantee it will be maintained for new game patches. Issues and pull requests are welcome, but will most likely be ignored. Your best bet is to hit me up on Discord.

The current build targets **Ostranauts 1.0.1.10 (`public_test`) with BepInEx 5**.
It is an experimental snapshot; compatibility with other game versions is not implied.
See [build/install instructions](Build/README.md) and the [release preparation notes](Build/RELEASE-PREP.md).

## Inventory of changes

### Bug fixes

- [Tile helper memory leak](MiscBugFixes/TILE-HELPER-CLEANUP.md): properly retire grid helper COs during tile and async-hull destruction, releasing stale registry entries and old ship references.
- [Shallow refueling](RefuelRCSPatch.cs): replenish RCS fuel instead of incorrectly adding the fuel to ship mass.
- [Torch autopilot fuel (pending playtesting)](Navigation/TORCH-COURSE-FIX.md): documented work in progress on fuel budgets, FLOW/coast control and cache synchronization; related implementation and harness files may be absent from this commit.
- [Ranged self-hits](MiscBugFixes/RangedShooterExclusionPatch.cs): exclude the shooter from character hit processing without blocking damage to intended targets or objects downrange.
- [Meat duplication](MiscBugFixes/MeatScrapPatches.cs): destroy loose meat through the native default recipe instead of spawning another batch of loose meat.
- [PDC ammunition](MiscBugFixes/PDC-CONVERSION-AMMO.md): prevent installation, uninstallation and other PDC conversions from generating another starting magazine before transferring existing ammunition.
- [AI navigation targets](MiscBugFixes/ShipReplacementTargetsPatch.cs): preserve destination references when target ships are unloaded and reconstructed, preventing runaway route calculations.
- [AI crew lifecycle](Navigation/CREWLESS-AI.md): prevent instant station cleanup during ship-to-ship docking and premature hull reuse; stop crewless AI thrust while preserving the visible hull.
- [Hauler returns](Haulers/README.md): restore home targets after derelict release, cancel abandoned thrust, and preserve the retrieval stage after SFF pickups.
- [VORB collection](Venus/HAULER-RETRIEVAL-AUDIT.md): let VORB retrievers use their home depot's assignments and wreck ownership instead of VNCA's regional station.
- [Navy target selection](Navigation/NavyCandidateFuelPatch.cs): reject unaffordable candidates before selection, closing the single-candidate and all-rejected fuel-check bypasses.
- [Propulsion checks](Navigation/NavigationFuel.cs): treat invalid RCS delta-v as zero so ships without usable thrusters cannot bypass low-fuel checks.
- [AI return orders](Navigation/StationReturn.cs): preserve flight and docking stages across save/reload, and discard obsolete trip fuel predictions when retargeting.
- [Planetary gravity](Orbital/README.md): correct missing parent-body acceleration when integrating free objects near an orbiting planet.
- [AI departure waypoints](Navigation/DEPARTURE-WAYPOINTS.md): clear a station relative to its own orbit instead of an unrelated planet, and continue intermediate freight legs during SFF.
- [SFF economy and cargo loading](Orbital/Economy/README.md): preserve production, freight dispatch and arrival ordering during time skips, and stop loaded cargo from being duplicated at its source.
- [VORB derelicts](Venus/README.md): restore native derelict anchoring and correct the orbit lookup used after undocking.
- [Docked gravity](MiscBugFixes/DOCKED-GRAVITY.md): inherit the anchored group's gravity after transit instead of leaving reconstructed ships at the default 0.3 g.
- [Ship-view rotation](MiscBugFixes/ShipRotationPatch.cs): check whether the ship being rotated is docked, rather than an unrelated ship occupied by the captain.
- [Docking clearance](MiscBugFixes/DockingGridPrecisionPatch.cs): eliminate phantom obstructions caused by tiny coordinate errors in docking-fit checks.
- [Remote barter](MiscBugFixes/BarterRemovalPatch.cs): remove sold items from unloaded ship inventories instead of crediting the sale while leaving the item available to sell again.
- [Container mass](MiscBugFixes/STACK-REMOVAL-MASS.md): prevent repeated mass subtraction during nested removals and preserve mass changes made through remote barter copies.
- [Mass propagation](MiscBugFixes/MASS-BOOKKEEPING-AUDIT.md): conserve mass during stack merges and propagate live item/body mass changes to owners and character encumbrance.
- [Door auto-close](MiscBugFixes/DOOR-AUTO-CLOSE.md): preserve the auto-close setting through power loss and restoration.
- [Automatic work selection](MiscBugFixes/AutoTaskScratchPatch.cs): keep patching, repair, and restoration candidate lists separate instead of letting native scratch-list aliasing mix them together.
- [Crew sleeping](MiscBugFixes/CREW-REST.md): prevent autonomous player crew from choosing backpacks and EVA suits as beds.
- [Idle sitting](MiscBugFixes/CREW-REST.md): let autonomously seated crew resume available automatic patching, repair, or restoration work while preserving manual seating orders.
- [Adjacent wall work](MiscBugFixes/WALL-WORK-PATH.md): stop workers trying to walk into a wall when they are already within reach of its work order.

### Simulation improvements

- [EM propagation](EMPropagation/README.md): use inverse-square EM falloff for player and AI reception, independently toggleable in PDA settings.
- [Contact position estimates](Sensors/README.md): replace distant-contact jitter with sensor-based range/bearing uncertainty and acquired transponder ranging, independently toggleable in PDA settings.
- [Micrometeoroid detection and defense](Micrometeoroids/README.md): replace the unconditional 50% defense bypass with diminishing sensor contributions and PDC interception based on projectile speed and rounds fired; missed bursts still consume ammo.
- [Micrometeoroid target fairness](Micrometeoroids/README.md): choose targets among loaded scene vessels by hull area instead of seeking the player's ship, preserving BeatManager event timing and exclusions.
- [Shallow boarding combat](ShipboardAction/README.md): resolve hostile dockings between Galcon, CCRE, and Belt Pirates using crew numbers, weapon loadouts, injuries, and randomized casualties over time.
- [Prize ships](ShipboardAction/README.md): let victors assign a spare healthy crew member to return a captured, flyable ship home for disposal, while keeping captains on their original ships.
- [Ceres navigation](CeresNavigation/README.md): apply a homeward fuel reserve to patrol selection and chase retreat decisions.
- [Free coasting](Orbital/README.md): reduce timestep-dependent orbital drift with velocity-Verlet integration and consistent native orbital coordinates.
- [Super Fast Forward](Orbital/SFF-IMPLEMENTATION.md): replace hours-long constant-acceleration jumps with orbital coasting and partial AI travel while retaining native plotted courses and combat handling.
- [VORB wreck variety](Venus/README.md): default to 50% Venus-specific wrecks, adjustable from 0–100% in the PDA settings app.

### Performance enhancements

- [Autonomous target selection](Optimizations/AUTONOMOUS-DISEMBARK.md): exclude needs, leisure and social targets on other ships when disembarking is forbidden, before sampling and evaluating candidates.
- [Remote barter loading](Optimizations/BARTER-FINALIZATION.md): replace repeated inventory scans with an item-ID lookup and spread offscreen reconstruction/finalization across frames to reduce post-transit freezes.
- [Remote trading](Optimizations/INVENTORY-PERFORMANCE.md): reuse an ID lookup during inventory write-back instead of repeatedly searching the saved inventory.
- [Contained-item loading](Optimizations/INVENTORY-PERFORMANCE.md): remove processed records at their known index while preserving parent and slot restoration order.
- [Save cleanup](Optimizations/INVENTORY-PERFORMANCE.md): compact remaining-item ID lists in batches using reusable scratch storage instead of repeatedly searching and shifting them.
- [Save loading](Optimizations/README.md): replace repeated scans of every saved ship with a shared ship-ID lookup.
- [System initialization](Optimizations/README.md): batch cheap coroutine steps within a frame budget instead of spending a frame on each tiny restoration step.
- [Work planning](AlternativeWork/README.md): reuse route and carried-material searches, and prepare upcoming work while the current job runs.
- [Save parsing](Optimizations/README.md): provide an experimental, opt-in Newtonsoft reader with compatibility handling for the game's LitJson save format.

### Quality of life

- [Venus dolly handling](Venus/DOLLY-ENCUMBRANCE.md): reduce dolly-and-payload encumbrance by 90% on Venus aerostats and their docked ships to approximate wheeled handling.
- [Alternative autotask](AlternativeWork/README.md): favor nearby, reachable, supplied work and prevent crew from reserving jobs on vessels their permissions forbid them to enter.
- [Crew hotkeys](Hotkeys/README.md): use Shift+R to toggle Open Airlock and Shift+T to toggle Disembark for the selected controllable crew member.
- [Proximity alarm dial](NavConsole/README.md): choose a ship's warning range from 5, 10, 15, or 20 km using a dial on the nav console.
- [Course-plot fine controls](QoL/CoursePlot/README.md): hover and scroll ACCEL LIMIT in 0.01 G steps or DRIFT PHASE in 0.01 steps, preserving native plotting and limits.
- [Hide nav tutorial note](NavConsole/README.md): use a saved PDA setting to hide the clickable "Don't crash!" paper that obscures nav-console controls.
- [Kang auto-vents](QoL/AutoVents/README.md): tolerate brief airlock-induced oxygen-pressure dips while retaining rapid closure for dangerous depressurization.
- [Stabilizer sound cues](MiscBugFixes/StabilizerCreakPatch.cs): scale the maneuver creaking threshold with active stabilizers' native damage reduction.
- [PDA settings](PdaSettings/README.md): toggle supported mod features and adjust their settings through a native PDA app.
- [Bounty indicators](Patch.cs): mark missing bounty targets as MIA rather than leaving their absence unexplained.
- [Maintenance commands](MiscBugFixes/README.md): repair container, anatomical and character masses with `massfix`, and recover corrupted navigation targets with `navfix`.
- [Console noise](MiscBugFixes/README.md): keep routine electrical-audit and missing-font-glyph spam out of the player console while retaining diagnostic logs.
- [Inspection API](Inspection/INSPECTION-API.md): inspect ships, crew, AI, navigation, work orders, station economies and freight, with removable combat/memory diagnostics, explicit ship pruning and a BeatManager MM test-strike endpoint.
- [Meat scrap cover](MiscBugFixes/MeatScrapPatches.cs): let ranged shots pass over loose meat scraps, eyes, hooves and horns on the floor without consuming projectile damage.

## Testing strategy

This project uses AI-assisted development. Its defense against "AI slop" is to
require evidence for diagnoses and test the resulting behavior, rather than trusting
a plausible explanation or a successful compile. Testing scales with the change:

- **Establish the defect:** inspect decompiled code and shipped JSON definitions,
  trace the relevant lifecycle, and reproduce the failure where practical. Fix the
  cause rather than repeatedly cleaning up its consequences.
- **Focused tests:** exercise calculations, state transitions, boundaries and
  invariants such as inventory conservation, permission enforcement and cleanup.
  Compare patched behavior with vanilla where behavior should remain equivalent.
- **Offline game-assembly harnesses:** load the shipped game assembly under the
  game's bundled Mono runtime and apply the actual Harmony patches. Regression
  suites exercise native methods without launching the game, using controlled
  fixtures and test doubles for Unity-dependent boundaries. Separate .NET harnesses
  support pure calculations and allocation measurements where appropriate.
- **Compatibility and regression checks:** verify Harmony bindings against the
  installed assembly, run relevant suites, and check save/load, disabled-feature
  behavior and reference cleanup when affected. Performance claims use measurements;
  allocation claims account for limitations of the runtime's counters.
- **Live validation:** use the local inspection API, bounded traces and logs to
  observe actual ships, crew, work orders and economic state during gameplay.
  Before/after snapshots check outcomes, timing and unexpected exceptions. Temporary
  diagnostic hooks are removed when capture ends.

Offline fixtures do not reproduce all of Unity, and passing tests does not prove
every gameplay scenario. Module notes distinguish native-code tests from stubbed
boundaries, record live observations, and identify remaining uncertainties. Not
every change has every kind of coverage; untested behavior is not presented as
verified.

## Companion tools

- [Torch drive calculator](Tools/TorchCalculator/README.md): a standalone HTML calculator for reactor fit, sustainable acceleration, He-3/D2O burn rates and fuel/tank mass tradeoffs. Open `calculator.html` directly.

[Build, install, and run the offline checks](Build/README.md).
