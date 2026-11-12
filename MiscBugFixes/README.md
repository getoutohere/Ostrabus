# Miscellaneous vanilla bug fixes

Always enabled through the plugin's normal Harmony lifecycle. No update loop or saved state.

## AI retirement timestamps

`ActiveShipRetirement` cancels obsolete IsStale markers when valid visible crewed
AI hulls are newly registered, including restored saved controllers. Its worker
write guard prevents a background cleanup scan from committing a cancelled or
refreshed timer. Genuine retirement, later command-failure cleanup and existing
removal flags remain native. Registration extends the existing crew-lifecycle
hook; one additional transpiler handles the final stale mark, with no per-frame
scan or retained game objects. Included with `ai-crew-integrity` in custom builds.
See [evidence and validation](ACTIVE-SHIP-STALE-CLEANUP.md).

## AI controller identity

`AIControllerIdentity.cs` makes unsaved reuse-or-create registration find the
existing controller for the exact hull across regional buckets. Station collision
recovery consequently cannot give an interregional freighter a second local
controller whose regional cleanup destroys its hull and strands cargo. A narrow
load-time repair retains the original cargo record for proven saved duplicates.
Native mission policy, explicit saved/autopilot setup and historical orphaned
stock are unchanged. Included with `ai-crew-integrity` in custom builds.
See [evidence, boundaries and validation](FREIGHT-CONTROLLER-IDENTITY.md).

## Saved inventory below damaged parents

`LoadedInventoryParentPatch` prevents load-time damage culling from skipping a
saved parent before its contents are reconstructed. Native damage, slotting,
destruction recipes and mode-switch transfer/drop behavior remain authoritative.
See [evidence, scope and validation](SAVED-INVENTORY-PARENTS.md).

The separate [plot-reference audit](PLOT-REFERENCE-AUDIT.md) is investigation only;
no plot asset corrections are shipped yet.

## Comms channel window

`CommsDisplayPatch` corrects the native oldest-100 selection to show the newest
100 messages chronologically. Formatting, live arrivals and conversation
interactions remain native. See [comms investigation and protected history
retention](../Optimizations/COMMS-HISTORY.md) for the separate cleanup policy.

## Torch autopilot fuel cache

Included in 0.4.0 following ordinary and braced interplanetary live burns,
including powered SFF, coast and final braking. Module notes distinguish those
observations from automatic-arrival and moored-preview cases tested offline.

`TorchFuelCachePatch` refreshes the reactor's reactant totals and the ship's
normalized fuel budget after native `FusionIC.SetReactants` reconciles the tanks.
It reads the same native tank lists, sums only this ship's inventory, and uses the
limiting reactant rather than assuming the requested inventory fits. No new
collections, polling, save data or retained ship references. Native tank writes,
fuel consumption, route planning and autopilot safety criteria are unchanged.
See [investigation and native-assembly validation](../Navigation/TORCH-FUEL-INVESTIGATION.md).

`TorchCourseFuel.cs` and `TorchCourseFuelPatches.cs` additionally correct fuel
budgeting and reactor control for supported player-plotted torch courses. They
preserve native waypoint playback and skill benefits, idle during true coast, and
cancel safely on unexpected hardware/fuel changes. Profiles are weakly keyed;
normal native waypoint/plot-FLOW data supports save/reload without custom saved
types. See [scope, lifecycle and validation](../Navigation/TORCH-COURSE-FIX.md).

## AI rendezvous and crew lifecycle

`AIShipLifecyclePatches.cs` prevents instant station docking while already attached
to another hull, and prevents reuse of hidden hulls before they detach. Crewless
human-operated AI is stopped at registration/execution/undocking boundaries without
hiding or deleting its hull. Normal transfers, crewed ships and intentionally
uncrewed controllers retain their behavior. See the [investigation and validation](../Navigation/CREWLESS-AI.md).

Police inspection officers may temporarily board the connected target without
losing their patrol controller; navigation waits for their return. An explicit
inspection API can recover patrols stranded by the earlier own-hull crew guard.
See [police inspection correction and recovery](../Navigation/POLICE-INSPECTION-CONTROLLER.md).

`PlayerAttachedRegionPatch.cs` extends native player-connection protection to the
whole ATC cleanup transaction. Connected faction ships survive without ownership
transfer; their existing controllers use native global scheduling until detachment
restores physical regional membership. Unregistered crewless hulls gain no new
controller. Two hooks, no frame callback, native primitive save data. See the
[native reproduction, repair and scheduling checks](../Navigation/BehaviorGraph/Traffic/REGIONAL-CLEANUP.md).

`CivilianVisitsPatch.cs` rejects new rendezvous with hidden reserve/vendor hulls,
revalidates retired/restored ordinary visits and lets native AI choose the next
trip. Combat, queued orders, SFF ownership and physically docked encounters retain
their control; civilian encounter retirement is blocked without preventing native
hauler tow deliveries or station-only parking. No Update callback or new behavior
controller. See [design and native tests](../Navigation/BehaviorGraph/Traffic/HIDDEN-VISIT-INVESTIGATION.md).

## PDC conversion ammunition

`PdcConversionAmmoPatch` suppresses starting loot when a vanilla PDC replaces
an existing PDC. This prevents another 50-100 rounds being generated before
native inventory transfer during installation/uninstallation, while new spawns
retain their normal starting ammunition. See [PDC-CONVERSION-AMMO.md](PDC-CONVERSION-AMMO.md).

## Tile helper memory leak

`TileHelperCleanupPatch` performs native managed cleanup for discarded grid
helpers while preserving the caller's Unity deletion order. `AsyncShipCleanupPatch`
extends it to barter/preview/rating copies and protects newer same-ID objects
from stale-copy registry removal. No polling, GC forcing or registry sweeps.
See [TILE-HELPER-CLEANUP.md](TILE-HELPER-CLEANUP.md) for the live reproduction,
scope and 1,036 offline checks.

## Ranged attacks hitting the shooter

Native `DamageSystem.DamageRay` skips attacker-owned body/equipment hits in its
destructible branch but omits the equivalent check in its character-wound branch.
A ray reporting the shooter's own body can therefore reach `Wound.Damage`, whose
normal armor handling can also destroy their worn EVA suit. The personal shotgun
uses ten straight rays; no ricochet is involved in this path.

`RangedShooterExclusionPatch` changes only the wound-selection call inside that
method. It returns no wound when the reported character is the attacker or has
the attacker as its native RootParent. Ownership means the physical parent
hierarchy, not property or faction ownership. Other hits use native wound
selection unchanged. Skipping the shooter spends no ray damage, so a target
behind that collider still receives the normal hit. Existing destructible
exclusion, spread, penetration, cover, faction effects, cross-ship restrictions
and environmental damage remain native. No global wound hook, polling, retained
game references or additional per-shot collections are introduced.

Always on, with no setting or saved state. Unfamiliar native IL emits a warning
and retains vanilla behavior. `Tests/RangedSelfHitCheck` passes 626 checks under
bundled Mono: original self-hit reproduction; applicable installed ranged attack
assets; a complete ten-ray shotgun blast; target/friendly/environmental hits;
attached parts; worn equipment; ordinary objects and cover; docking restrictions;
native instruction preservation; fallback and allocation. Physics hit reports,
engine access and wound writes are controlled fixture boundaries; the native
hit-processing and damage-budget loop execute unchanged around the production fix.

## Loose meat duplication and projectile cover

`MeatScrapPatches.cs` contains two always-on fixes. The destruction correction
applies to ordinary 0.2 kg `ItmMeat01Loose` scraps. The projectile-cover exclusion
also includes loose meat eyes, hooves and horns. Installed meat and heavy meat
planters keep their native behavior.

**Duplication:** native loose meat uses `ACTMeat01Destroy`, just like installed
meat. This resolves through `MSDestroyMeat` and `ItmMeatDestroyed` to another
1–5 loose meat pieces. `LooseMeatDestructionPatch` corrects the individual
`DestCheck.SetData` result to `ACTDefaultDestroy`, the same native path used by
other loose meat variants; destruction produces ordinary trash. This handles
new objects and reconstruction from saves without changing shared definitions,
the installed-meat recipe, other checks or custom destruction recipes. There
is no recurring scan or new save data.

**Cover:** captured floor scraps have full-tile, ten-unit-deep colliders. They
intercept rays at shooting height, and native `DamageRay` subtracts their
remaining durability from the projectile budget. Its penetration parameter is
used for wounds, not this object-damage calculation. Live captures showed
shotgun and carbine rays intersecting the intended meat but spending their
damage on scraps first. One carbine round spent 21 damage on seven scraps.

`LooseMeatCoverPatch` changes only the `DmgLeft` call inside `DamageRay`: a
loose meat piece, eye, hoof or horn with neither a parent nor a slot reports zero effective
cover durability. The existing native branch then continues without spending
environment, blunt or cut damage. Ray-based fire passes over these scraps;
their actual durability, other damage paths, pickup/selection colliders,
inventory, stack behavior and normal cover remain unchanged. Held or contained
meat does not qualify. This is not a general penetration or weapon rebalance.
The check allocates nothing and holds no game references. Unknown native ray
IL warns and retains native behavior.

The bundled-Mono `Tests/RangedSelfHitCheck` harness reproduces the recursive
asset chain, executes native destruction-check initialization and native ray
damage with controlled engine hits, checks installed/custom recipe preservation,
reload reconstruction, installed/heavy/held/container cover, applicable native
ranged modes, unchanged downstream damage, allocation and instruction preservation.
It also verifies that opt-in personnel tracing respects and reports the cover
fix and can be removed without disturbing the permanent patches.

## Navigation target lifetimes

`CivilianSffCoordinatesPatch.cs` separately corrects the two regional lookup
arguments in `CivilianPilot.FFWD`: native code supplies X twice when an Undock
has no remembered partner, or a free positive-fuel Hold chooses a regional station.
A guarded transpiler changes only each second X load to Y. Candidate selection,
instant-docking success/failure, remembered partners, attached/empty-fuel Hold,
and existing flight routes remain native. The `traffic` harness has 64 checks,
including native wrong-station reproduction and corrected branch/outcome controls.
Custom builds expose it as the independent `civilian-sff-coordinates` bugfix.

`ShipReplacementTargetsPatch.cs` fixes native `CrewSim.DestroyAndReload` leaving
other ships targeting the destroyed instance. Its shared ShipSitu has its orbital
reference cleared during teardown, causing subsequent AI route calculations to
diverge. After replacement, exact references to the old ship/situation transfer
to the new instance. Existing routes and independent offset waypoints remain
unchanged; there are no travel distance/duration limits or recurring checks.

The same feature now handles permanent destruction and targets restored from a
save whose named ship is gone. Destroying a registered nonprojectile vessel
clears affected navigation, stationkeeping, combat and undocking references.
An affected flight loses its obsolete route, RCS/torch thrust input and fuel
prediction, then returns control to native AI on its normal update. Position,
velocity, fuel and crew are preserved. A scanned-only contact disappearing does
not cancel an unrelated coordinate course. Native ClearShipTarget also clears
the corresponding optional saved JSON fields, preventing shallow serialization
from restoring the old goal.

Normal same-ID replacement defers this cleanup until the replacement transaction
finishes, then transfers references without cancelling routes. A finally path
restores the suppression scope across nested transactions and exceptions. Hidden
ships, occupied ports, live named destinations and coordinate/stellar-object
goals remain distinct. Projectile teardown and whole-world teardown are excluded
from fleet scans. There is no periodic fleet poll or per-flight-tick validation.

Native asteroid unmooring also replaces representations: `Undock.AttemptToUnMoor`
destroys an unvisited asteroid Ship and recreates its stellar object under the
same ID. The conversion scope defers lifetime cleanup until the native operation
finishes. Successful conversion preserves independent/copied goals and routes,
rebinds references to the old ShipSitu, and drops obsolete Ship/auxiliary references.
Failed conversion still invalidates the actually lost destination and propagates
the native exception. Nested scopes restore their prior context. This corrects a
regression in the first permanent-removal implementation, with no saved schema or
new behavioral policy. `traffic-conversion` now passes 303 native lifecycle checks.

The bundled-Mono lifecycle harness reproduces the native failure and checks the
production patch: 276 checks pass with the captured O-ZY3A input, including
permanent removal, native failure continuation, saved recovery, nested/failed
replacement, unchanged-target zero allocation and weak-reference release.
See the [native test instructions](../Tests/AiPlannerDiagnosis/TARGET-LIFETIME.md)
and [cross-actor model](../Navigation/BehaviorGraph/TargetLifecycle/README.md).
Missing destinations in existing saves recover automatically during native
target restoration. Older malformed references to destinations that still exist
retain their explicit recovery command:

- `navfix` or `navfix dryrun`: report eligible and skipped vessels without changes.
- `navfix apply`: rebind eligible targets, discard their old routes, and stop old
  RCS/torch controls. Native AI replans on its normal updates. Repeating is safe.

Selection is by the confirmed cleared orbital-body target reference and a valid,
unambiguous named destination. No registration IDs, factions, distance or duration
limits are used. Hidden/non-AI/player-controlled, connected/anchored, active-combat,
unregistered-pilot, invalid-motion and ambiguous offset/border cases are skipped
and reported. Position, linear/angular velocity, fuel and crew are preserved.
The command does not calculate or install a route; it reports empty stored shallow
RCS tanks rather than granting fuel. Pilot availability, normal AI pauses and
native navigation/fuel decisions still apply.

Recovery tests add 98 checks: full native planner replay from the four captured
current states with native RCS fuel calculations; arbitrary registration IDs;
unchanged dry runs and healthy long routes; rejected cases; native cleanup;
console dispatch and repeat execution. See the
[full diagnosis](../Haulers/AI-PLANNER-DIAGNOSIS.md) for evidence and fixture scope.

## Stabilizer-aware maneuver creaking

`StabilizerCreakPatch.cs` changes only the 0.3 g sudden-acceleration-change
comparison in `Ship.Gravity`. The new threshold is `0.3 / damageMultiplier`,
using the native active-stabilizer count and native wear formula:
`damageMultiplier = count > 0 ? 0.3 + 1 / 2^count : 1`.
Thresholds are approximately 0.300, 0.375, 0.545 and 0.706 g for 0–3 active units.
These are sound-cue thresholds, not wear-free acceleration limits.

Wear still accumulates from absolute acceleration changes, with damage applied
after the native accumulator exceeds 30. Stabilizers reduce that damage; their
first/second-unit discontinuity is preserved. Camera shake, crew effects, creak
severity and other creak sources remain native. Only the sound comparison is
replaced; unfamiliar IL emits a warning and retains vanilla behavior. The helper
reads the existing count without scanning equipment, allocating or retaining ships.

`Tests/ManeuverWearCheck` runs the original and patched native gravity setter and
wear methods under the bundled Mono runtime: 154 checks cover sound boundaries,
positive/negative changes, wear accumulation, damage multipliers and the native
initialization/SFF suppression. Engine/audio output, part enumeration and final
part damage writes are fixtures. No live game or save is modified by these tests.

## Ship view rotation

Vanilla `CommandRotateCW.Execute` and `CommandRotateCCW.Execute` check whether
`CrewSim.coPlayer.ship` is docked, but rotate `CrewSim.shipCurrentLoaded`.
When the captain and selected crew occupy different vessels, an unrelated
docked vessel can block rotation, or an unrelated free vessel can permit it.

`ShipRotationPatch` replaces only that guard with a docking check on the current
loaded ship. Native input restrictions, docking/mooring rules, warning popup,
rotation, camera, navigation invalidation and audio remain intact. It does not
change flight attitude or any docking state. Unknown IL leaves vanilla behavior
intact and emits a warning at startup.

Offline validation in `Tests/MiscBugFixCheck` reads both real game methods,
checks branch/label preservation and unchanged surrounding instructions, and
exercises the replacement against native `Ship.IsDocked` with controlled fixtures.
Deploy the Debug DLL/PDB and restart the game to apply.

## Docking-fit coordinate precision

`DockingGridPrecisionPatch.cs` corrects tiny position errors (for example
8.499999 instead of 8.5) when the game checks docking fit. Vanilla footprint
classification and integer truncation otherwise produce phantom occupied tiles.

The patch scopes native `Ship.GetAvailableDockingPorts` with a thread-local
nesting counter. Within that operation only, `JsonItem.fX/fY` and
`Transform.position` reads normalize X/Y to the nearest half tile when within
0.0001. Loaded and shallow grids therefore use consistent coordinates without
changing saved items, transforms, flight positions, Z, rotations, or overlap
rules. Exceptions restore the previous scope; there is no retained ship state.

This deliberately leaves `GridUtils.CreateDockingPortGrid` untouched. Offline
checks found that copying this large method through Harmony, even with an
identity transpiler, changed its output. Scoping the outer operation avoids that
problem and was tested through the real native docking-fit call.

Outside docking-fit evaluation, the coordinate hooks do only a thread-local
scope check. They introduce no background work or per-read allocations, but do
add getter interception overhead. Normalization itself runs only during fit
checks. No configuration toggle is required.

Validation: `Tests/DockingDiagnosis` replays the saved O-GXD/OKLG geometry with
installed asset footprints and bundled Mono. Primary fits all three ports;
Secondary still fails the third due to a genuine weapon obstruction. Complete
corrected shallow grids match exact-coordinate vanilla grids. Loaded-path
fixtures cover half/whole-tile errors and unchanged transforms. Native engine
access is substituted in those fixtures; this is not a live docking test.

## Unloaded barter item removal

The [multiple-ship barter audit and repairs](MULTI-SHIP-BARTER-AUDIT.md) cover
scoped async-loader cancellation and item-loss prevention during repurchase.
Callsign grouping/zone row identity is a separate [QoL feature](../QoL/BarterZones/README.md).

`BarterRemovalPatch` redirects `Ship.RemoveCO(CondOwner, bool)` to the native
`BarterZoneShip.RemoveCO` implementation when the instance is a barter copy.
The subclass hides rather than overrides this method, so container and stack
callers typed as Ship otherwise bypass removal from its separate inventory
JSON. This can credit a sale while preserving the sold item in the source hull.

The prefix forwards bForce and skips the base implementation after dispatch.
Ordinary Ship instances continue through vanilla code. The native specialized
method handles the runtime map, item descendants and local JSON removal; native
save-back and payment are unchanged. No persistent state, update loop, or
additional per-call allocations are introduced. Existing duplicated inventory
is not retrospectively reconciled.

`Tests/BarterPersistenceCheck` reproduces the original contained-item failure,
then installs this production prefix. It checks contained and loose item
removal, force forwarding, repeated save-back, a partial three-item stack, and
the saved floor crate with its entire 114-descendant inventory graph. Source
save hashes remain unchanged. See the test README for offline engine fixtures.
Deploy the Debug DLL/PDB and restart; no console toggle is needed.
## Container mass prevention

Two always-active fixes prevent new mass corruption:

- `StackMassPatch` shares one removal charge across nested Container, CondOwner
  and Ship removal methods. Direct ship/stack-head removals still debit once;
  whole stacks, head pops, loaded backpacks and frozen reconstruction retain
  native behavior. Per-thread scratch is reused and cleared at the outermost
  removal's finalizer, including exceptions; no game references survive the call.
- `BarterMassPatch` enables native AddMass only during real container/slot/removal
  transfers on fully loaded barter copies, restoring each original freeze flag
  afterward. Changed mass values (IDs/scalars under weak copy keys) are persisted
  on successful native save-back. Only StatMass and any matching layout mass
  override change: temporary wiring/property maps and other state are not exported.
  Saved DEFAULT conditions are expanded without rolling loot so they cannot
  overwrite current mass. Sold parents are skipped; copy destruction clears state.

No polling or inventory scans run in the background. Ordinary transfers reuse
bounded-by-call-depth charge scratch; remote save-back allocates an item index and
condition arrays only when actual mass changes were recorded. Existing corruption
still requires repair. See [STACK-REMOVAL-MASS.md](STACK-REMOVAL-MASS.md) and
[DOLLY-RETAINED-MASS.md](DOLLY-RETAINED-MASS.md).

Offline validation: 80 container/stack checks and 101 barter checks, including
native transfer and save-back methods, nested ancestors, direct removal, head
pops/full stacks, incomplete loading, exceptions, save defaults/overrides, wiring
preservation and source-save hash checks. Unity I/O and condition storage are
fixtures. Debug DLL/PDB rebuilt; no live save was changed by these checks.

## Mass repair command

A [broader mass bookkeeping audit](MASS-BOOKKEEPING-AUDIT.md) documents two further
preventive patches: stack merge cleanup no longer subtracts cargo that remains
present, and live condition-driven mass changes update ancestors and character
encumbrance exactly once. These cover NPCs and player crew without resetting body
or trait mass. Initial construction, frozen/loading state, shallow inventories,
editor operations and temporary barter copies retain their original accounting.
Existing corruption is not automatically repaired. Validation includes 475 mass
checks, 105 remote-barter checks and 77 Venus dolly checks, with the patches active
together in the compatibility cases.

Run `massfix` (replaces `dollyfix`) to repair eligible containers, reviewed human
anatomical components and `Crew01` characters on materialized world ships
(`Edit`/`Full`). Shallow/unloaded inventories and temporary barter copies remain
excluded. It reports changed aggregates, grouped skip reasons and counts for
containers, anatomy, people and materialized ships.

The command reconstructs reviewed fixed tare plus actual owned contents. It
counts stack members once, excludes secondary slot aliases, preserves missing
or replaced limbs and held items, and reads Strong/Feeble mass adjustments from
the installed loot. It plans an entire ownership tree before changing it, then
applies each affected aggregate once through native `AddMass` so corrupt
intermediate ancestors cannot underflow. Native labels, burden and the Venus
dolly discount stay in use; ship mass caches are invalidated. A write failure
attempts to restore that tree's original masses.

Unreviewed definitions/overlays, explicit mass overrides, ambiguous tare,
variable gas/food/drink/chemical contents, unaudited starting loot, unknown
mass-affecting conditions and invalid/frozen graphs are skipped. A skipped
parent can still receive a corrected cargo aggregate without reconstructing
its own tare. No arbitrary floor/item normalization is performed. See
[character reconstruction](CHARACTER-MASS-RECONSTRUCTION.md) for policy and tests.

This is an on-demand repair, with no background scan or new simulation hooks.
Changes are captured by normal game saving. The command repairs existing errors;
the separate preventive patches above stop the reproduced causes of new errors.
Offline checks are in `Tests/DollyMassCheck`; no manual reproduction is required.
`Tools/CharacterMassAudit` exports read-only saved ownership graphs for the
production repair harness; an optional saved-graph run passes 710 checks and
makes zero corrections on its second pass. `Tools/MassAudit` and
[MASSFIX-COVERAGE.md](MASSFIX-COVERAGE.md) retain the older container-only audit.

## Autonomous sleeping and idle sitting

Player crew's autonomous sleep searches exclude `IsContainer`/`IsEVASuit`
objects without changing manual sleep, NPC behavior or native bed eligibility.
Autonomous unsecured chair/stool sitting yields to usable automatic patch, repair
or restoration work during work shifts, with native duty priorities. Native ProcessAutoTasks scratch
buffers are kept distinct across calls, preventing mixed-category priority errors
after repeated scans. Scratch target references are cleared on exit.
Bounded sitting probes advance past failed candidates instead of repeatedly
checking only the same leading pair; actual work dispatch remains native.
Manual sitting, secured seating and nav use are protected;
native emergencies remain intact. See [CREW-REST.md](CREW-REST.md) for scope,
performance and 696 offline checks. These corrections are always on, independent
of altwork.

## Door auto-close after power loss

`DoorAutoClosePatch` preserves a door's existing auto-close preference during its
four native power-off transitions. Powered-off doors still cannot auto-close;
manual hold-open and close behavior remains native. Always on. Existing affected
doors need one manual close because their disabled preference is indistinguishable
from an intentional hold-open. See [DOOR-AUTO-CLOSE.md](DOOR-AUTO-CLOSE.md) for the
root cause, scope and 50 offline checks.

## Missing font glyph console spam

`MissingGlyphConsoleFilter` hides TextMeshPro's standard missing-character
warnings for `robotocondensed SDF` and `robotocondensed SDF No Outline` from the
player-facing console only. The observed spam was U+9997 (`馗`) in the vanilla
`钟馗` CIWS name: 140 warnings in the inspected session. The supplied font and
fallbacks lack that glyph, so TextMeshPro substitutes a space. Correct rendering
requires an appropriate font asset/fallback; this filter leaves rendering intact.

The same filter also hides the standard missing-ellipsis warning, observed with
`Jura-Bold SDF`, when TextMeshPro explicitly falls back to Truncate overflow.
Original warnings remain in `Player.log` and inspection logs. Other warning
formats and all errors/exceptions remain visible. Always on, no setting,
save data or retained objects. `Tests/MissingGlyphCheck` passes 21 checks,
including native console output and warning-badge behavior.

## Walking into an adjacent work target

`WorkRangePathPatch` corrects the native in-range path shortcut that can tell a
worker to walk onto a wall containing a conduit work order. A valid position
already within reach produces a stationary route; otherwise native pathfinding
continues. Always on for vanilla and altwork. See
[WALL-WORK-PATH.md](WALL-WORK-PATH.md) for scope and 23 offline checks.

## Docked gravity after transit

`DockedGravityPatch` repairs reconstructed docked ships retaining the constructor's
0.3 g in an anchored group. It inherits the anchored member's native gravity and
refreshes crew encumbrance/status without causing maneuver wear. Always on; no
new polling loop, saved data or retained ship references. See
[DOCKED-GRAVITY.md](DOCKED-GRAVITY.md) for the cause, scope and 47 offline checks.

## Signal-box behavior

`SignalBoxes/SignalBoxPatches.cs` repairs native gate scheduling, effective output,
disconnect delivery, queue persistence and endpoint reconstruction boundaries.
Native gate math, power checks, delays and conversion policy remain authoritative.
The fixes are always on in the full build and selectable as `signal-boxes` in
custom builds. They add no Update hook or saved schema. See
[SignalBoxes/README.md](SignalBoxes/README.md) for scope, lifetime, compatibility
and 4,980 offline assertions, including actual production hooks.

## Electrical connection audit logging

`ElectricalAuditLogging` routes the native `Removed N faulty connections!` message
to Unity's `Player.log` using the `[ElectricalAudit]` tag, instead of the player
console. The existing `/logs` listener also captures it. An exact-tag GUI filter
prevents these ordinary log entries being echoed when Unity log display is enabled;
warnings, exceptions and unrelated logs are unaffected.

Each invalid endpoint decision records source device ID/type, input/output
direction, cached/actual source ship, load state, destroyed flag, endpoint ID,
missing-runtime versus missing-Electrical reason, stored ship-layout presence and
parent/force-load metadata, global CO lookup state/ship, saved-CO existence, and
gate/override/wire state. UTC and world epoch identify the event. Stored JSON and
saved-CO presence are evidence, not proof that an endpoint is still live; saved
representations can be stale. Layout lookup is capped and reports unknown if capped.

The patch changes only three call sites inside native `AuditConnections`: two
lookups gain observation and the summary gains log routing. Original lookup
exceptions, pruning, GPM persistence, scheduling and gate behavior are retained.
Unknown call-site shapes keep vanilla behavior and issue a warning. No background
recorder or additional game-object retention is introduced. Successful lookups
do not emit logs. These logging hooks are functional routing, independent of
the optional `worktrace` diagnostic switch.

`Tests/SignalBoxAudit` verifies native traversal/removal/persistence remain intact,
records both missing and moved endpoints, and checks log-only routing with GUI
Unity logging enabled. This feature only changes logging; the independent
[signal-box repairs](SignalBoxes/README.md) now address the behavior findings in
[SIGNAL-BOX-AUDIT.md](SIGNAL-BOX-AUDIT.md). Both features are tested together.

