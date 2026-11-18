# Ostrabus

Ostrabus is intentionally an omnibus mod package for **Ostranauts**. I write it for my own enjoyment of the game, and it combines bug fixes, simulation improvements, performance enhancements, and quality-of-life features.

I'm making it public as inspiration, reference material, or a source of code to copy into your own mods. Feel free to borrow a particular module under the repository's [license](LICENSE).

There is no guarantee it will be maintained for new game patches. Issues and pull requests are welcome, but will most likely be ignored. Your best bet is to hit me up on Discord.

**Ostrabus 0.7.0 supports Ostranauts 1.1.0.2 (`public_test`), Windows and BepInEx 5.**
It is experimental and partially playtested; the [release notes](Build/RELEASE-0.7.0.md) separate live observations from offline/native tests and list known limitations.
The [1.1 compatibility audit](Build/COMPATIBILITY-1.1.0.0.md), [feature review](Build/FEATURE-COMPATIBILITY-1.1.0.0.md) and incremental [1.1.0.1](Build/COMPATIBILITY-1.1.0.1.md) / [1.1.0.2](Build/COMPATIBILITY-1.1.0.2.md) reviews document the port.

Use the [build/install instructions](Build/README.md) for the full package or the [custom plugin builder](CustomBuilder/README.md) for selected features.
Normal deployment continues to install the full package.

The optional [Station Kiosk Migration plugin 0.1.2](StandaloneMods/StationMigration/README.md) preserves station interiors during the game's kiosk upgrades; install it separately before loading an affected older save, then remove it after saving.
It is not a general content migration: the [content audit](Build/CONTENT-AVAILABILITY-1.1.0.0.md) explains what 1.1 already adds to continuing saves and why the earlier metadata-only migration prototype is unnecessary.

For the old game, use the [preserved 1.0.1.10 baseline](Build/BASELINE-1.0.1.10.md), commit `be8e282`, tagged `Ostrabus-5.1.0`; this 0.7.0 build does not declare support for other game versions.

## Inventory of changes

Choose categories or individual features with the [custom plugin builder](CustomBuilder/README.md); module links provide implementation details, settings and test evidence.
SFF means Super Fast Forward; PASS shuttle trips also advance the simulation while you travel.

### Bug fixes

#### Navigation, orbits and docking

- [Orbital velocity and coasting handoffs](Orbital/Velocity/README.md): calculate orbital reference velocities at the requested time and preserve ship velocity when RCS gives way to coasting, preventing jumping VCRS readings.
- [Planetary gravity](Orbital/README.md): include the parent body's acceleration when integrating free objects near an orbiting planet.
- [AI collision avoidance](Navigation/PLANETARY-AVOIDANCE-AUDIT.md): detect slow and accelerating collision approaches, reject unsafe course resumption, and prevent repeated evasive reversals while retaining the game's steering.
- [AI departure waypoints](Navigation/DEPARTURE-WAYPOINTS.md): place station departure points relative to the station's own orbit and continue intermediate freight legs during fast-forward.
- [AI rendezvous planning](Navigation/FREIGHT-RCS-APPROACH-INVESTIGATION.md): give NPC RCS its own acceleration limit, reject approach routes that fail to converge, and preserve freight destinations when planning fails.
- [Local AI navigation frames](Navigation/LOCAL-NAVIGATION-FRAMES.md): keep patrol, ambush and encounter locations near their regional stations instead of inheriting a distant celestial body's motion.
- [AI navigation targets](MiscBugFixes/ShipReplacementTargetsPatch.cs): retain destinations through ship reconstruction and asteroid representation changes, and discard obsolete routes when the actual destination disappears.
- [Civilian SFF station selection](MiscBugFixes/CivilianSffCoordinatesPatch.cs): use both position coordinates when choosing a regional station during fast-forward instead of passing X twice.
- [SFF navigation destinations](MiscBugFixes/SFF-NAVIGATION-DESTINATION.md): retain AI mission targets and refresh their positions before instant mooring can save a huge docking offset.
- [Route completion](Navigation/ROUTE-COMPLETION.md): stop finished maneuvers and integrate only the remaining coast time, preventing duplicate movement and residual thrust during PASS travel and normal play.
- [VORB derelicts](Venus/README.md): restore wreck anchoring and use the correct orbital reference after undocking.
- [Docked gravity](MiscBugFixes/DOCKED-GRAVITY.md): restore the anchored docking group's gravity after transit instead of leaving a rebuilt ship at the default 0.3 g.
- [Docking clearance](MiscBugFixes/DockingGridPrecisionPatch.cs): prevent tiny coordinate errors from creating false obstructions in docking-fit checks.
- [Ship-view rotation](MiscBugFixes/ShipRotationPatch.cs): check the ship being rotated for docking restrictions instead of checking an unrelated ship occupied by its captain.

#### Fuel, autopilot and towing

- [Shallow refueling](RefuelRCSPatch.cs): add refilled RCS fuel to a background ship's fuel reserve instead of only increasing its mass.
- [AI flight fuel accounting](MiscBugFixes/AI-FLIGHT-FUEL-HANDOFF.md): prevent navigation and loading transitions from charging AI travel fuel twice, including across save/load.
- [AI RCS replenishment](Navigation/RCS-REPLENISHMENT.md): carry actual RCS refills into an AI ship's stored route budget without changing the route's planned consumption.
- [Propulsion checks](Navigation/RCS-BUDGET.md): include pilot efficiency in remaining RCS delta-v and reject invalid propulsion calculations during low-fuel decisions.
- [Torch autopilot fuel checks](MiscBugFixes/TorchFuelCachePatch.cs): synchronize cached reactor fuel with actual tanks and prevent fuel reconciliation from refunding unmatched or excess reactants.
- [Torch course consumption](Navigation/TORCH-COURSE-FIX.md): keep fuel use, reactor settings and reported acceleration consistent with plotted burns and coasts, including previews, saved courses and fast-forward.
- [Player course destinations](Navigation/TORCH-COURSE-FIX.md#sensor-selection-cancelling-a-player-course-2026-11-17): keep the plotted destination separate from the selected sensor contact so inspecting or losing another ship cannot cancel the course or end it early.
- [Torch towing](Navigation/Towing/README.md): execute plotted courses with braced towing groups, include their mass, and exclude a station's mass when previewing departure.

#### AI activity, recovery and ship lifetimes

- [AI flight control handoffs](Navigation/FLIGHT-CONTROL-HANDOFF.md): release abandoned courses and thrust, wait for incapacitated pilots, and prevent retired controllers from continuing to steer.
- [Scav emergency recovery](MiscBugFixes/SCAV-EMERGENCY-RECOVERY.md): end empty scavenging visits and retry failed destination selection so stranded scavengers can reach the game's emergency-assistance behavior.
- [AI wait clocks](Navigation/SCAVENGER-WAITING.md): preserve absolute waiting deadlines and let background ships' saved emergency-call cooldowns expire correctly.
- [Civilian rendezvous](Navigation/BehaviorGraph/Traffic/HIDDEN-VISIT-INVESTIGATION.md): exclude hidden ships from civilian visit choices and release vanished destinations while preserving visits already physically docked.
- [Civilian visit completion](MiscBugFixes/CIVILIAN-VISIT-WAITS.md): allow background civilians to finish visits with empty fuel tanks and retain their departure timing through fast-forward and reload.
- [AI crew lifecycle](Navigation/CREWLESS-AI.md): prevent premature ship cleanup, reuse and crewless acceleration while preserving player-connected faction ships through changes of ATC region.
- [AI retirement](MiscBugFixes/ACTIVE-SHIP-STALE-CLEANUP.md): clear obsolete disposal timers when a ship becomes active again so cleanup cannot delete an active hull and its living crew.
- [AI return orders](Navigation/RETURN-PRIORITY.md): preserve return stages through reload, retry failed return approaches and docking, and discard stale fuel predictions when the destination changes.
- [AI escape destinations](Navigation/ESCAPE-RETARGET.md): discard the old course and fuel prediction when a retreat selects a different station.
- [Hauler returns](Haulers/README.md): restore the home destination after releasing a wreck, stop abandoned thrust, and preserve the return stage after fast-forward pickups.
- [VORB collection](Venus/HAULER-RETRIEVAL-AUDIT.md): let VORB retrievers use their own depot's assignments and wreck ownership instead of the VNCA regional station's records.

#### Combat, crew and bounty targets

- [Radio surrender](MiscBugFixes/SHIP-SURRENDER-WORK-STALL.md): keep ship surrender negotiations from blocking crew work or overwriting face-to-face conversation state.
- [AI surrender accounting](Navigation/SURRENDER-COMBAT-LOOP.md): retain combat engagements when a surrender offer cannot be sent, preventing repeated targeting lock-and-release loops.
- [AI dodge cleanup](Navigation/AI-GRAVITY-EXEMPTIONS.md#implemented-follow-up): restore gravity and clear completed dodge state when the incoming projectile disappears.
- [Disabled combat targets](Navigation/COMBAT-NEUTRALIZATION.md): stop Navy and Blockade ships repeatedly targeting disabled enemy ships that the game's combat rules already consider neutralized.
- [Combat-to-patrol handoff](Navigation/PATROL-TARGET-HANDOFF.md): let Navy and Blockade pilots finish combat cleanup after losing contact instead of resuming patrol toward an abandoned enemy target.
- [AI patrol priority](Navigation/PATROL-PRIORITY.md): allow combat and threats to interrupt Navy patrols while preserving queued commands and fast-forward callbacks across save/load.
- [Navy target selection](Navigation/NavyCandidateFuelPatch.cs): reject unaffordable targets before selection, including cases with only one candidate or no affordable candidates.
- [AI crew identities](MiscBugFixes/NPC-IDENTITY-COLLISION-AUDIT.md): prevent a newly generated crew member with a matching name from replacing and relocating an existing NPC.
- [Bounty escorts](MiscBugFixes/BOUNTY-ESCORT.md): allow temporarily arrested bounty targets to follow manual airlock orders without granting them unrestricted autonomous movement.
- [Bounty departures](Navigation/BOUNTY-DEPARTURES.md): restore station refueling when bounty targets relaunch in their existing ships, preventing immediate returns for lack of fuel.
- [Ranged self-hits](MiscBugFixes/RangedShooterExclusionPatch.cs): exclude the shooter from character hit checks while preserving damage to intended targets and objects farther along the shot.
- [PDC ammunition](MiscBugFixes/PDC-CONVERSION-AMMO.md): preserve existing ammunition during PDC installation and conversion without generating another starting magazine.

#### Freight, markets and remote trading

- [SFF economy and freight accounting](Orbital/Economy/README.md): preserve production and dispatch order during time skips, conserve cargo and shared market stock, and deliver freight before retiring its hauler.
- [AI controller identity](MiscBugFixes/FREIGHT-CONTROLLER-IDENTITY.md): prevent station collisions from assigning a second local controller to an active freighter and stranding its cargo when that controller deletes the ship.
- [Remote barter](MiscBugFixes/BarterRemovalPatch.cs): remove sold items from an unloaded ship's saved inventory instead of paying for them while leaving them available to sell again.
- [Remote repurchases](MiscBugFixes/MULTI-SHIP-BARTER-AUDIT.md): preserve delivered items when remote inventories are saved and restore temporary coordinates even if saving fails.
- [Background ship loading](MiscBugFixes/MULTI-SHIP-BARTER-AUDIT.md): cancel only the loading job belonging to the inventory or preview being closed, preserving other ships' barter inventories.
- [Preview cargo markets](MiscBugFixes/PREVIEW-MARKET-ISOLATION.md): prevent inventory and rating previews from registering phantom cargo pods, with a command to repair verified empty leftovers.

#### Inventory, mass and saved state

- [Save forward compatibility](SaveCompatibility/README.md): rebuild missing 1.1 RCS performance data from an older save's installed parts while preserving fuel and combat damage.
- [Saved inventory](MiscBugFixes/SAVED-INVENTORY-PARENTS.md): restore contents inside damaged containers before destruction transfers or drops those contents, preventing items from disappearing on load.
- [Ship component loading](MiscBugFixes/MASS-BOOKKEEPING-AUDIT.md): resolve asset component names in the game assembly after patching, preventing missing ship components and stuck loading overlays.
- [Container mass](MiscBugFixes/STACK-REMOVAL-MASS.md): prevent repeated mass subtraction during nested removals and retain mass changes made through remote barter inventories.
- [Mass propagation](MiscBugFixes/MASS-BOOKKEEPING-AUDIT.md): conserve mass during stack merges and pass item and body-mass changes to their containers, ships and character encumbrance.
- [Meat duplication](MiscBugFixes/MeatScrapPatches.cs): destroy loose meat through the default destruction recipe instead of creating another batch of loose meat.

#### Crew work and ship systems

- [Automatic work selection](MiscBugFixes/AutoTaskScratchPatch.cs): keep patching, repair and restoration candidate lists separate so one search cannot overwrite another's results.
- [Idle sitting](MiscBugFixes/CREW-REST.md): let seated crew notice available automatic patching, repair and restoration work while preserving manual seating orders.
- [Adjacent wall work](MiscBugFixes/WALL-WORK-PATH.md): stop workers trying to walk into a wall when they are already close enough to perform its work order.
- [Crew sleeping](MiscBugFixes/CREW-REST.md): stop autonomous player crew from treating backpacks and EVA suits as beds.
- [Signal boxes](MiscBugFixes/SignalBoxes/README.md): correct gate updates, outputs, disconnected wires and saved signal queues while preserving wiring when devices are temporarily rebuilt.
- [Door auto-close](MiscBugFixes/DOOR-AUTO-CLOSE.md): retain a door's auto-close setting through power loss and restoration.
- [CO2 scrubber audio](MiscBugFixes/ScrubberAudio/README.md): remove the extra generic pump sounds added in 1.1 while preserving the scrubber's own hum and controls, using game assets only.

#### Interfaces and long sessions

- [Nav module placement](NavConsole/MODULE-PLACEMENT.md): use the panel's actual parent pivot when dragging modules, preventing the course plotter from disappearing above the nav console in 1.1.
- [Comms channel display](Optimizations/COMMS-HISTORY.md): show the newest 100 stored messages in chronological order instead of leaving the local channel stuck on its oldest messages.
- [Console history](MiscBugFixes/CONSOLE-BUFFER.md): trim old console output and size its scrollable display to the text so long sessions keep showing new messages.
- [Tile helper memory leak](MiscBugFixes/TILE-HELPER-CLEANUP.md): remove obsolete tile helper objects during hull destruction so they stop retaining registry entries and references to old ships.

### Simulation improvements

#### Traffic, encounters and persistence

- [Passive docking attachments](Navigation/DOCKED-ORBIT-LOCK.md): retain a wreck's existing orbital attachment through visits, undocking and tether changes, releasing it for applied movement or an authorized towing course.
- [Civilian visit destinations](Navigation/CIVILIAN-DERELICT-VISITS.md): exclude derelicts from random civilian docking choices while preserving scavenger salvage, ordinary visits and asteroid prospecting.
- [Asteroid prospecting visits](MiscBugFixes/CIVILIAN-VISIT-WAITS.md): give civilian asteroid visits a duration of 5 minutes to 2 hours without changing visits to other ships.
- [Faction encounters](Navigation/Encounters/README.md): stage faction ships near their regional infrastructure at plausible relative speeds without relocating ships already busy with other activity.
- [Scavenger returns](Navigation/SCAVENGER-WAITING.md): try a feasible station return after random scavenging-job selection fails while preserving successful jobs and normal navigation.
- [Orbital waiting](Navigation/SCAVENGER-WAITING.md): let free, unpowered AI ships coast under gravity on supported bound trajectories that clear the body and atmosphere, preserving other native waits.
- [Bounty persistence](Navigation/BOUNTY-PRESERVATION.md): protect active bounty targets and their original ships from administrative cleanup while retaining station stays, later departures and physical death.
- [NPC gender distribution](Population/README.md): generate random NPCs with 49.5% male, 49.5% female and 1% non-binary probabilities without changing authored characters.
- [Ceres navigation](CeresNavigation/README.md): reserve fuel for the trip home when choosing patrols and deciding when to end a chase.

#### Flight, sensing and collision rules

- [Torch course planning](Navigation/TORCH-INITIAL-VELOCITY.md): use the player's existing velocity when plotting a torch transfer instead of cancelling that motion before departure.
- [EM propagation](EMPropagation/README.md): apply inverse-square signal falloff to player and AI EM reception, with an independent PDA setting.
- [Contact position estimates](Sensors/README.md): replace arbitrary distant-contact jitter with uncertainty based on sensor range, bearing and acquired transponder signals.
- [Micrometeoroid detection and defense](Micrometeoroids/README.md): share sensors and PDC defense across a docking group, block shots that threaten connected hulls, and consume ammunition even when a burst misses.
- [Micrometeoroid target fairness](Micrometeoroids/README.md): select exposed loaded ships in proportion to hull area while excluding stations and every ship connected to them.
- [Station docking traffic](Navigation/STATION-DOCKED-COLLISIONS.md): exempt ships connected to a station from ship collisions, AI avoidance and proximity warnings while keeping free-space towing groups as obstacles.
- [Hull-shaped collisions](Navigation/Collisions/README.md): optionally use cached hull geometry for ship impacts, AI clearance, docking ranges and proximity alarms while preserving weapon hits and impact-damage rules.
- [Free coasting](Orbital/README.md): reduce time-step-dependent orbital drift using velocity-Verlet integration and consistent orbital coordinates.
- [Super Fast Forward and PASS](Orbital/SFF-IMPLEMENTATION.md): advance orbital motion, AI decisions, freight and collision checks through time skips without changing the game's passenger-transfer rules.

#### Salvage and boarding

- [Shallow boarding combat](ShipboardAction/README.md): resolve offscreen hostile dockings between Galcon, CCRE and Belt Pirates using crew numbers, weapons, injuries and casualties over time.
- [Prize ships](ShipboardAction/README.md): let victors assign a spare healthy crew member to take a captured, flyable ship home while leaving the captain on the original ship.
- [VORB wreck variety](Venus/README.md): set the share of Venus-specific wrecks delivered to VORB, defaulting to 50% and adjustable from 0-100% in the PDA app.

### Performance enhancements

#### Loading, inventories and saves

- [Remote barter loading](Optimizations/BARTER-FINALIZATION.md): use an item-ID lookup and spread background inventory reconstruction across frames to reduce freezes after transit.
- [Remote trading](Optimizations/INVENTORY-PERFORMANCE.md): reuse an item-ID lookup when saving remote inventory changes instead of repeatedly searching the entire inventory.
- [Contained-item loading](Optimizations/INVENTORY-PERFORMANCE.md): remove processed save records at their known positions while retaining the correct order for restoring containers and item slots.
- [Save cleanup](Optimizations/INVENTORY-PERFORMANCE.md): remove processed item IDs in batches instead of repeatedly searching and shifting the remaining list.
- [Save loading](Optimizations/README.md): look up saved ships by ID instead of repeatedly scanning every ship record.
- [System initialization](Optimizations/README.md): combine small restoration steps within a frame's time budget instead of spending a full frame on each step.
- [Save parsing](Optimizations/README.md): offer an experimental, opt-in Newtonsoft reader that handles the game's LitJson save format.

#### AI, work and displays

- [Trigger searches](Optimizations/TRIGGER-CLASSIFICATION.md): limit people-only trigger searches to people when their conditions permit it, without changing how those conditions are evaluated.
- [Autonomous target selection](Optimizations/AUTONOMOUS-DISEMBARK.md): exclude needs, leisure and social activities on other ships before evaluating them when the character may not disembark.
- [Work planning](AlternativeWork/README.md): reuse route and carried-material searches and prepare upcoming work while the current job runs.
- [Socials portraits](Optimizations/SOCIALS-FIRST-OPEN.md): compose portraits with reused pixel buffers and a single image write, reducing a measured first Socials opening from 5.75 to 0.52 seconds.
- [Comms history](Optimizations/COMMS-HISTORY.md): trim messages older than seven days after load and before save while retaining unread messages, pending deliveries and recent conversation context.

### Quality of life

#### Navigation and console controls

- [RCS rotation module](NavConsole/Rotation/README.md): add a physical 1x1 nav-console module for target-relative turns and bearing hold that releases when the player rotates manually.
- [Nav hull outlines](NavConsole/HULL-OUTLINES.md): draw correctly sized outer hull outlines with corners and recesses intact, updating geometry in the background and showing useful detail at wider zooms.
- [Station-docked hull exemption](Navigation/STATION-DOCKED-COLLISIONS.md): choose in the PDA app whether station-connected ships cause collisions, AI avoidance and proximity warnings.
- [Proximity alarm dial](NavConsole/README.md): select a ship's warning range of 5, 10, 15 or 20 km from the nav console.
- [Nav/comms scrollbacks](NavConsole/README.md#comms-and-diagnostic-scrollbacks): support draggable scrollbars and reliable mouse-wheel scrolling, with comms auto-follow suspended while reading older messages.
- [Course-plot fine controls](QoL/CoursePlot/README.md): adjust ACCEL LIMIT in 0.01 G steps or DRIFT PHASE in 0.01 steps with the mouse wheel while retaining the game's Shift-drag precision control.
- [Hide nav tutorial note](NavConsole/README.md): hide the clickable "Don't crash!" paper through a saved PDA setting so it no longer obscures console controls.

#### Crew and shipboard conveniences

- [Quick PDA hacking](QoL/PdaHacking/README.md): offer PDA unlocking directly in the quick-action menu for characters with Hacking while retaining the game's requirements and effects.
- [Venus dolly handling](Venus/DOLLY-ENCUMBRANCE.md): reduce dolly-and-payload encumbrance by 90% on Venus aerostats and docked ships to approximate wheeled handling.
- [Alternative autotask](AlternativeWork/README.md): favor nearby, reachable work with available supplies and avoid reserving jobs on ships the worker is forbidden to enter.
- [Crew hotkeys](Hotkeys/README.md): toggle airlock permission with Shift+R, disembarking with Shift+T, or helmet/EVA battery equipment with rebindable Shift+F and an oxygen safety check.
- [Kang auto-vents](QoL/AutoVents/README.md): tolerate brief oxygen-pressure dips caused by airlock use while retaining rapid closure during dangerous depressurization.
- [Stabilizer sound cues](MiscBugFixes/StabilizerCreakPatch.cs): account for active stabilizers' damage reduction when deciding whether a maneuver should cause creaking sounds.
- [Meat scrap cover](MiscBugFixes/MeatScrapPatches.cs): let ranged shots pass over loose meat, eyes, hooves and horns on the floor without consuming projectile damage.

#### PDA apps and trading

- [PDA settings](PdaSettings/README.md): toggle included features and adjust their controls through a PDA app that hides settings for features omitted from a custom build.
- [Apartment refuel terminal](StandaloneMods/ApartmentRefuel/README.md): offer a separately installed, expensive terminal for owned station apartments that sells refueling services to directly docked ships, pending live acceptance.
- [Multiple-ship barter zones](QoL/BarterZones/README.md): label sell-list zones with each ship's callsign and sort them by callsign and zone name so identically named zones remain usable.
- [Socials contact filtering](QoL/Socials/README.md): hide construction drones from Socials without changing their relationships or activity in the world.
- [Bounty indicators](QoL/BOUNTY-INDICATORS.md): mark missing targets, including those moved to the offscreen holding pool, as MIA in bounty lists and details.

#### Diagnostics and maintenance

- [Maintenance commands](MiscBugFixes/README.md): repair item and character masses with `massfix`, recover damaged navigation references with `navfix`, and inspect phantom cargo pods with `cargopodfix`.
- [Console noise](MiscBugFixes/README.md): hide routine electrical-audit, missing-glyph and recovered gas-buffer messages from the player console while retaining diagnostic logs.
- [Inspection API](Inspection/INSPECTION-API.md): inspect ships, crew, AI, work, markets and freight through a local API, with explicit recovery commands and removable diagnostic captures.

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
