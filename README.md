# Ostrabus

Ostrabus is intentionally an omnibus mod package for **Ostranauts**. I write it for my own enjoyment of the game, and it combines bug fixes, simulation improvements, performance enhancements, and quality-of-life features.

I'm making it public as inspiration, reference material, or a source of code to copy into your own mods. Feel free to borrow a particular module under the repository's [license](LICENSE).

There is no guarantee it will be maintained for new game patches. Issues and pull requests are welcome, but will most likely be ignored. Your best bet is to hit me up on Discord.

**Ostrabus 0.6.0** targets **Ostranauts 1.1.0.0 (`public_test`) with BepInEx 5**.
This snapshot has partial in-game playtesting and extensive offline/native checks.
The latest bounty-preservation, passive-attachment and PASS refinements still need
live acceptance; see the [0.6.0 release notes](Build/RELEASE-0.6.0.md) for the exact scope and evidence.
The [compatibility audit](Build/COMPATIBILITY-1.1.0.0.md) and [per-feature decisions](Build/FEATURE-COMPATIBILITY-1.1.0.0.md) record native/offline evidence and its limits. The [save-content investigation](StandaloneMods/SaveMigration110/FINDINGS.md) confirms native 1.1 already supplies the verified new world content. Its metadata-only migration prototype is unnecessary and is not recommended.
The [preserved 1.0.1.10 baseline](Build/BASELINE-1.0.1.10.md) is commit `be8e282`, tagged `Ostrabus-5.1.0`.
It is an experimental snapshot; compatibility with other game versions is not implied.
See [build/install instructions](Build/README.md) and the [release preparation notes](Build/RELEASE-PREP.md).
For a custom selection of fixes and features, use the [custom plugin builder](CustomBuilder/README.md).

## Inventory of changes

### Bug fixes

- [CO2 scrubber audio](MiscBugFixes/ScrubberAudio/README.md): remove the extra generic pump loops introduced in 1.1 while preserving the scrubber's own hum and native controls; assets only.

- [Radio surrender](MiscBugFixes/SHIP-SURRENDER-WORK-STALL.md): keep ship negotiations from blocking crew work or overwriting face-to-face conversation state.
- [AI surrender accounting](Navigation/SURRENDER-COMBAT-LOOP.md): preserve engagements when an offer cannot be sent, preventing repeated combat lock/release cycles.
- [Disabled combat targets](Navigation/COMBAT-NEUTRALIZATION.md): stop regular and Blockade detectors reacquiring offscreen AI ships that native combat logic considers neutralized, preserving cleanup and pilot reactivation.
- [Combat-to-patrol handoff](Navigation/PATROL-TARGET-HANDOFF.md): let Navy and Blockade pilots finish native combat cleanup after losing contact instead of resuming patrol against an abandoned enemy target.
- [Signal boxes](MiscBugFixes/SignalBoxes/README.md): repair gate updates, effective outputs, disconnections and saved message queues while preserving wiring across temporary reconstruction.
- [Saved inventory](MiscBugFixes/SAVED-INVENTORY-PARENTS.md): restore saved contents beneath damaged parents before native destruction recipes transfer or drop them.
- [Tile helper memory leak](MiscBugFixes/TILE-HELPER-CLEANUP.md): properly retire grid helper COs during tile and async-hull destruction, releasing stale registry entries and old ship references.
- [Comms channel display](Optimizations/COMMS-HISTORY.md): show the newest 100 stored messages chronologically instead of the oldest 100.
- [Console history](MiscBugFixes/CONSOLE-BUFFER.md): discard oldest output reliably and size the scrollable display to its text so long sessions keep showing recent messages.
- [Shallow refueling](RefuelRCSPatch.cs): replenish RCS fuel instead of incorrectly adding the fuel to ship mass.
- [AI flight fuel accounting](MiscBugFixes/AI-FLIGHT-FUEL-HANDOFF.md): prevent navigation-mode and loaded-state handoffs from charging the same travel twice, including across save/load.
- [AI flight control handoffs](Navigation/FLIGHT-CONTROL-HANDOFF.md): release abandoned courses/thrust, wait safely for incapacitated pilots, reject retired controllers, and preserve legitimate autopilot and combat handoffs.
- [AI crew identities](MiscBugFixes/NPC-IDENTITY-COLLISION-AUDIT.md): prevent newly generated crew from regenerating and relocating an existing NPC whose name collides, preserving legitimate station recruitment.
- [AI patrol priority](Navigation/PATROL-PRIORITY.md): keep Navy patrols interruptible by combat and threats, and preserve SFF one-shot callbacks and queued command order across save/load.
- [AI RCS replenishment](Navigation/RCS-REPLENISHMENT.md): preserve actual refills in stored route budgets across AI roles and stations without changing planned consumption.
- [Save forward compatibility](SaveCompatibility/README.md): reconstruct missing 1.1 RCS caches from legacy hull data while retaining native combat losses and existing fuel.
- [Nav module placement](NavConsole/MODULE-PLACEMENT.md): use the actual parent pivot when dragging modules, fixing the course panel moving above the console in 1.1.
- [Scav emergency recovery](MiscBugFixes/SCAV-EMERGENCY-RECOVERY.md): release empty visits, reject unaffordable returns and keep failed selection in a throttled retry instead of a destinationless Hold, preserving native assistance.
- [AI wait clocks](Navigation/SCAVENGER-WAITING.md): keep pause deadlines absolute and expire shallow SOS cooldowns using their native saved timers.
- [Bounty escorts](MiscBugFixes/BOUNTY-ESCORT.md): let temporarily arrested targets follow manual airlock orders without changing their faction roster or autonomous permissions.
- [Torch autopilot fuel checks](MiscBugFixes/TorchFuelCachePatch.cs): synchronize cached fuel with actual tanks so affordable braking legs do not disengage autopilot.
- [Torch course consumption](Navigation/TORCH-COURSE-FIX.md): calculate fuel and reactor controls from the planned burn/coast phases, including previews and SFF without double charging.
- [Torch towing](Navigation/Towing/README.md): execute plotted courses for braced tow groups, account for their mass and preview station departures without counting the station.
- [Ranged self-hits](MiscBugFixes/RangedShooterExclusionPatch.cs): exclude the shooter from character hit processing without blocking damage to intended targets or objects downrange.
- [Meat duplication](MiscBugFixes/MeatScrapPatches.cs): destroy loose meat through the native default recipe instead of spawning another batch of loose meat.
- [PDC ammunition](MiscBugFixes/PDC-CONVERSION-AMMO.md): prevent installation, uninstallation and other PDC conversions from generating another starting magazine before transferring existing ammunition.
- [AI navigation targets](MiscBugFixes/ShipReplacementTargetsPatch.cs): preserve destinations across ship reconstruction and asteroid representation changes, and discard destroyed or missing saved targets before their stale routes can continue.
- [Civilian SFF station selection](MiscBugFixes/CivilianSffCoordinatesPatch.cs): use X/Y position instead of X/X when choosing a regional station during fast-forward.
- [SFF navigation destinations](MiscBugFixes/SFF-NAVIGATION-DESTINATION.md): preserve AI mission targets and refresh their coordinates before fast-forward instant mooring can save enormous docking offsets.
- [Route completion](Navigation/ROUTE-COMPLETION.md): stop completed maneuvers and advance only remaining coast time, preventing phantom thrust and duplicate integration during PASS travel and ordinary non-SFF updates.
- [Civilian rendezvous](Navigation/BehaviorGraph/Traffic/HIDDEN-VISIT-INVESTIGATION.md): exclude hidden hulls, release visits whose destinations retire, and preserve physically docked encounters.
- [Civilian visit completion](MiscBugFixes/CIVILIAN-VISIT-WAITS.md): let peaceful shallow civilians finish empty-tank visits, preserve clocks through SFF/reload and use native faction-home selection.
- [AI crew lifecycle](Navigation/CREWLESS-AI.md): prevent premature cleanup/reuse and crewless thrust; preserve player-connected faction ships and their controllers across ATC changes ([regional cleanup](Navigation/BehaviorGraph/Traffic/REGIONAL-CLEANUP.md)).
- [AI retirement](MiscBugFixes/ACTIVE-SHIP-STALE-CLEANUP.md): cancel obsolete disposal timers on valid hull reactivation and save restoration so active ships and living crews are not deleted as stale.
- [Bounty departures](Navigation/BOUNTY-DEPARTURES.md): restore native station refueling when bounty targets relaunch in existing ships, preventing immediate low-fuel returns.
- [AI controller identity](MiscBugFixes/FREIGHT-CONTROLLER-IDENTITY.md): stop station collisions giving active freighters a second local controller that can destroy their hull and strand cargo; repair demonstrated saved duplicates.
- [Hauler returns](Haulers/README.md): restore home targets after derelict release, cancel abandoned thrust, and preserve the retrieval stage after SFF pickups.
- [VORB collection](Venus/HAULER-RETRIEVAL-AUDIT.md): let VORB retrievers use their home depot's assignments and wreck ownership instead of VNCA's regional station.
- [Navy target selection](Navigation/NavyCandidateFuelPatch.cs): reject unaffordable candidates before selection, closing the single-candidate and all-rejected fuel-check bypasses.
- [Propulsion checks](Navigation/RCS-BUDGET.md): include pilot efficiency in remaining RCS delta-v and reject invalid propulsion budgets in low-fuel checks.
- [AI return orders](Navigation/RETURN-PRIORITY.md): preserve return stages across save/reload, retry failed native retreat approaches/docking, and retain urgent/combat priorities; discard obsolete fuel predictions when retargeting.
- [AI escape destinations](Navigation/ESCAPE-RETARGET.md): discard the previous route and fuel prediction when selecting a new escape station, preserving native retreat decisions.
- [AI collision avoidance](Navigation/PLANETARY-AVOIDANCE-AUDIT.md): stop dodging encounters in the past and reject evasive turns into planetary bodies while retaining ship avoidance and native destinations.
- [Planetary gravity](Orbital/README.md): correct missing parent-body acceleration when integrating free objects near an orbiting planet.
- [AI departure waypoints](Navigation/DEPARTURE-WAYPOINTS.md): clear a station relative to its own orbit instead of an unrelated planet, and continue intermediate freight legs during SFF.
- [Local AI navigation frames](Navigation/LOCAL-NAVIGATION-FRAMES.md): keep patrols, lurking and local encounter points with their regional orbit instead of inheriting a distant body's motion.
- [SFF economy and freight accounting](Orbital/Economy/README.md): preserve time-skip production/dispatch ordering, fix bundled loading and shared market stock accounting, size haulers for the whole load, and complete cargo handoffs before retirement.
- [VORB derelicts](Venus/README.md): restore native derelict anchoring and correct the orbit lookup used after undocking.
- [Docked gravity](MiscBugFixes/DOCKED-GRAVITY.md): inherit the anchored group's gravity after transit instead of leaving reconstructed ships at the default 0.3 g.
- [Ship-view rotation](MiscBugFixes/ShipRotationPatch.cs): check whether the ship being rotated is docked, rather than an unrelated ship occupied by the captain.
- [Docking clearance](MiscBugFixes/DockingGridPrecisionPatch.cs): eliminate phantom obstructions caused by tiny coordinate errors in docking-fit checks.
- [Remote barter](MiscBugFixes/BarterRemovalPatch.cs): remove sold items from unloaded ship inventories instead of crediting the sale while leaving the item available to sell again.
- [Remote repurchases](MiscBugFixes/MULTI-SHIP-BARTER-AUDIT.md): preserve already-marked delivered items during writeback and restore temporary coordinates even if serialization fails.
- [Background ship loading](MiscBugFixes/MULTI-SHIP-BARTER-AUDIT.md): unloading one inventory, preview or rating copy cancels only its own loading job, preserving other ships' barter inventories.
- [Preview cargo markets](MiscBugFixes/PREVIEW-MARKET-ISOLATION.md): prevent presentation copies from registering phantom cargo pods; reconcile verified empty ghosts with `cargopodfix`.
- [Container mass](MiscBugFixes/STACK-REMOVAL-MASS.md): prevent repeated mass subtraction during nested removals and preserve mass changes made through remote barter copies.
- [Mass propagation](MiscBugFixes/MASS-BOOKKEEPING-AUDIT.md): conserve mass during stack merges and propagate live item/body mass changes to owners and character encumbrance.
- [Door auto-close](MiscBugFixes/DOOR-AUTO-CLOSE.md): preserve the auto-close setting through power loss and restoration.
- [Automatic work selection](MiscBugFixes/AutoTaskScratchPatch.cs): keep patching, repair, and restoration candidate lists separate instead of letting native scratch-list aliasing mix them together.
- [Crew sleeping](MiscBugFixes/CREW-REST.md): prevent autonomous player crew from choosing backpacks and EVA suits as beds.
- [Idle sitting](MiscBugFixes/CREW-REST.md): let autonomously seated crew resume available automatic patching, repair, or restoration work while preserving manual seating orders.
- [Adjacent wall work](MiscBugFixes/WALL-WORK-PATH.md): stop workers trying to walk into a wall when they are already within reach of its work order.

### Simulation improvements

- [Passive docking attachments](Navigation/DOCKED-ORBIT-LOCK.md): preserve a wreck's existing body lock during visits and tether changes; release it for applied movement or an authorized towing course, including paused-haul save/load and SFF/PASS continuation.

- [Bounty persistence](Navigation/BOUNTY-PRESERVATION.md): preserve active bounty targets and their original ships across administrative retirement and cleanup, retaining native station stays and later reboarding.
- [Asteroid prospecting visits](MiscBugFixes/CIVILIAN-VISIT-WAITS.md): give civilian asteroid visits one 5-minute-to-2-hour duration while retaining ordinary timing for visits to other ships.
- [Faction encounters](Navigation/Encounters/README.md): stage faction ships only near regional infrastructure at plausible relative speeds, preserving busy actors and existing patrol motion.
- [Scavenger returns](Navigation/SCAVENGER-WAITING.md): try a feasible native station return after random job selection fails, preserving successful jobs and normal navigation/docking.
- [Orbital waiting](Navigation/SCAVENGER-WAITING.md): let free, unpowered AI ships wait under gravity while preserving atmospheric hovering, combat, docking and plotted courses.
- [Torch course planning](Navigation/TORCH-INITIAL-VELOCITY.md): use existing velocity in player torch trajectories instead of cancelling it before starting a new transfer.
- [EM propagation](EMPropagation/README.md): use inverse-square EM falloff for player and AI reception, independently toggleable in PDA settings.
- [Contact position estimates](Sensors/README.md): replace distant-contact jitter with sensor-based range/bearing uncertainty and acquired transponder ranging, independently toggleable in PDA settings.
- [Micrometeoroid detection and defense](Micrometeoroids/README.md): share docked-group sensors and PDC defense, veto bursts whose native projectile paths threaten connected hulls, and spend ammo on missed bursts.
- [Micrometeoroid target fairness](Micrometeoroids/README.md): choose targets among loaded scene vessels by hull area instead of seeking the player's ship, preserving BeatManager event timing and exclusions.
- [Shallow boarding combat](ShipboardAction/README.md): resolve hostile dockings between Galcon, CCRE, and Belt Pirates using crew numbers, weapon loadouts, injuries, and randomized casualties over time.
- [Prize ships](ShipboardAction/README.md): let victors assign a spare healthy crew member to return a captured, flyable ship home for disposal, while keeping captains on their original ships.
- [Ceres navigation](CeresNavigation/README.md): apply a homeward fuel reserve to patrol selection and chase retreat decisions.
- [Station docking traffic](Navigation/STATION-DOCKED-COLLISIONS.md): ships attached to a station through docking/tether chains do not cause ship collisions, AI evasions or proximity warnings; free-space tows remain physical obstacles.
- [Hull-shaped collisions](Navigation/Collisions/README.md): optionally use cached hull geometry for free-ship impacts, AI clearance, consistent docking/range/proximity alarms and safe group placement, preserving native weapon hits and impact damage rules.
- [Free coasting](Orbital/README.md): reduce timestep-dependent orbital drift with velocity-Verlet integration and consistent native orbital coordinates.
- [Super Fast Forward and PASS](Orbital/SFF-IMPLEMENTATION.md): preserve orbital coasting, AI decision turns, freight progression and collision history across time skips; hand completed courses to corrected coasting without changing native passenger transfers.
- [VORB wreck variety](Venus/README.md): default to 50% Venus-specific wrecks, adjustable from 0–100% in the PDA settings app.

### Performance enhancements

- [Trigger searches](Optimizations/TRIGGER-CLASSIFICATION.md): restore conservative people-only search classification without changing authored condition evaluation.
- [Socials portraits](Optimizations/SOCIALS-FIRST-OPEN.md): compose uncached portraits through borrowed pixel buffers, a streamlined blend loop and one bulk write, reducing a measured first opening from 5.75 to 0.52 seconds.
- [Autonomous target selection](Optimizations/AUTONOMOUS-DISEMBARK.md): exclude needs, leisure and social targets on other ships when disembarking is forbidden, before sampling and evaluating candidates.
- [Remote barter loading](Optimizations/BARTER-FINALIZATION.md): replace repeated inventory scans with an item-ID lookup and spread offscreen reconstruction/finalization across frames to reduce post-transit freezes.
- [Remote trading](Optimizations/INVENTORY-PERFORMANCE.md): reuse an ID lookup during inventory write-back instead of repeatedly searching the saved inventory.
- [Contained-item loading](Optimizations/INVENTORY-PERFORMANCE.md): remove processed records at their known index while preserving parent and slot restoration order.
- [Save cleanup](Optimizations/INVENTORY-PERFORMANCE.md): compact remaining-item ID lists in batches using reusable scratch storage instead of repeatedly searching and shifting them.
- [Comms history](Optimizations/COMMS-HISTORY.md): trim transcripts older than seven days after loading and before saving, preserving unread messages, pending deliveries and latest conversation context.
- [Save loading](Optimizations/README.md): replace repeated scans of every saved ship with a shared ship-ID lookup.
- [System initialization](Optimizations/README.md): batch cheap coroutine steps within a frame budget instead of spending a frame on each tiny restoration step.
- [Work planning](AlternativeWork/README.md): reuse route and carried-material searches, and prepare upcoming work while the current job runs.
- [Save parsing](Optimizations/README.md): provide an experimental, opt-in Newtonsoft reader with compatibility handling for the game's LitJson save format.

### Quality of life

- [Apartment refuel terminal](StandaloneMods/ApartmentRefuel/README.md): optional independent plugin and native item mod; buy an expensive terminal for an owned station apartment and use paid station services for directly docked ships. Packaged separately; pending live acceptance testing.

- [RCS rotation module](NavConsole/Rotation/README.md): add a physical 1x1 console module for target-relative turns and persistent bearing hold, canceled by manual rotation.

- [Quick PDA hacking](QoL/PdaHacking/README.md): unlock PDAs directly from the quick-action menu when the controlled character has Hacking, preserving native requirements and effects.
- [Multiple-ship barter zones](QoL/BarterZones/README.md): identify sell-list zones by ship callsign and sort them by callsign and zone name, keeping identically named zones accessible.
- [Nav hull outlines](NavConsole/HULL-OUTLINES.md): draw cached exterior hull perimeters with consistent sizing, corners and recesses intact, background updates, and unpadded detail selected by screen size instead of a fixed zoom cutoff.
- [Socials contact filtering](QoL/Socials/README.md): hide construction drones from the contact list without changing their relationships or simulation.
- [Venus dolly handling](Venus/DOLLY-ENCUMBRANCE.md): reduce dolly-and-payload encumbrance by 90% on Venus aerostats and their docked ships to approximate wheeled handling.
- [Alternative autotask](AlternativeWork/README.md): favor nearby, reachable, supplied work and prevent crew from reserving jobs on vessels their permissions forbid them to enter.
- [Crew hotkeys](Hotkeys/README.md): toggle Airlock (Shift+R), Disembark (Shift+T), or helmet/EVA battery (Shift+F); rebind equipment in the PDA settings app.
- [Station-docked hull exemption](Navigation/STATION-DOCKED-COLLISIONS.md): toggle visiting hulls and station-connected tows as traffic/proximity obstacles in the PDA settings app.
- [Proximity alarm dial](NavConsole/README.md): choose a ship's warning range from 5, 10, 15, or 20 km using a dial on the nav console.
- [Nav/comms scrollbacks](NavConsole/README.md#comms-and-diagnostic-scrollbacks): drag scrollbars, scroll long transcripts consistently, and suspend comms auto-follow while reading older messages.
- [Course-plot fine controls](QoL/CoursePlot/README.md): hover and scroll ACCEL LIMIT in 0.01 G steps or DRIFT PHASE in 0.01 steps, alongside native Shift-drag adjustment.
- [Hide nav tutorial note](NavConsole/README.md): use a saved PDA setting to hide the clickable "Don't crash!" paper that obscures nav-console controls.
- [Kang auto-vents](QoL/AutoVents/README.md): tolerate brief airlock-induced oxygen-pressure dips while retaining rapid closure for dangerous depressurization.
- [Stabilizer sound cues](MiscBugFixes/StabilizerCreakPatch.cs): scale the maneuver creaking threshold with active stabilizers' native damage reduction.
- [PDA settings](PdaSettings/README.md): toggle supported mod features and adjust their settings through a native PDA app.
- [Bounty indicators](QoL/BOUNTY-INDICATORS.md): mark missing targets and those in the offscreen holding pool as MIA in bounty lists and details.
- [Maintenance commands](MiscBugFixes/README.md): repair container, anatomical and character masses with `massfix`, and recover corrupted navigation targets with `navfix`.
- [Console noise](MiscBugFixes/README.md): keep routine electrical-audit, missing-font-glyph and recovered gas-buffer spam out of the player console while retaining diagnostic logs.
- [Inspection API](Inspection/INSPECTION-API.md): inspect ships, crew, AI, navigation, work orders, station economies and freight, with removable combat/memory diagnostics, exact-quantity stranded cargo recovery, explicit ship pruning and a BeatManager MM test-strike endpoint.
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

- [AI ship behavior reference](Navigation/BehaviorGraph/README.md): machine-readable vanilla transitions and patch overlays, generated diagrams, conditional traces and behavioral tradeoff review. Offline; no gameplay changes.
- [Torch drive calculator](Tools/TorchCalculator/README.md): a standalone HTML calculator for reactor fit, sustainable acceleration, He-3/D2O burn rates and fuel/tank mass tradeoffs. Open `calculator.html` directly.

[Build, install, and run the offline checks](Build/README.md).
