# Local AI navigation references

Implemented against 1.0.1.10. This is a C# repair to local coordinate creation,
not an alternative AI controller or a change to station orbits.

## Reproduction and owning layer

Panmen (`MSUZ`) and Qiantangmen (`MHNG`) are authored in
`star_systems/star_system.json` as independent solar-orbit stations. MSUZ has
`strNameParent: Sol` and a 1.88-year period. Vesta has its own 3.63-year orbit.
The environment assets already place local nav/field points relative to their
regional station, using `SpawnEnvironment` with placeholders enabled. They do
not instruct patrols to follow Vesta.

Local patrol/wait routines nevertheless call `ShipSitu.LockToBO` with the default
`includePlaceHolder:false`, or explicitly select a non-placeholder nearest body.
`StarSystem.GetNearestBO` excludes the station. `LockToBO` records a fixed
Cartesian offset and replaces velocity with the selected body's velocity. Being
the nearest eligible natural body does not make that body the reference of a
station-centered activity.

Live baseline and follow-up established this at MSUZ: two navy patrols had
Vesta-locked targets and one lurking pirate was Vesta-locked at an approximately
0.9 AU offset. Over 5.34 game hours, including a five-hour SFF, their Panmen ranges
grew from 202/203/336 thousand km to 317/316/455 thousand km. The pirate separated
at 6.216 km/s without using fuel. Both patrols created fresh Vesta-locked targets
after ordinary route completion. This is not confined to stale saved courses or
SFF integration. The patrols retained functional controllers and turned back.

No asset change is needed: the authored station orbit and local activity already
exist. The C# coordinate-to-reference conversion loses that relationship.

## Shared rule

`LocalNavigationFrame` resolves a valid regional station using the same native
regional lookup used by AI target selection. It reads the station's existing
`ShipSitu` orbital reference. For a natural body belonging to that reference's
parent hierarchy, it preserves the native body (Mercury, Venus, Ceres, etc.).
It also preserves a natural body whose gravity at the actual point exceeds
Sol's, even if that body is unrelated to the nearest station. This cheap
`M/r²` comparison uses loaded masses/positions and prevents genuine activity in
a planetary gravity well from being reassigned to a distant station.
Otherwise it uses the station's own reference, including independent solar
stations and synthetic regional anchors. Sol is not used as a stationary local
frame when a moving station reference exists. Missing, retiring, destroyed,
unanchored or unresolvable stations retain the native fallback.

This preserves existing planetary activity while repairing unrelated-body
binding. It is based on loaded reference identities and hierarchy, with no
station-name list or arbitrary range threshold. Patrol positions, candidate
ordering, random choices, fuel evaluators, ambush ranges, reactions and native
command trees remain authoritative. Native pirate/guard locking still supplies
its original station-keeping abstraction; this repair supplies the correct frame.

## Caller audit and implementation boundaries

| Mechanism | Disposition |
| --- | --- |
| `NavyPilot` and `BlockadePilot`: random influence-range points and final jittered patrol goal | Repair all four implicit lock sites, including candidates before fuel evaluation. |
| `Lurk.RunCommand`, `Guard.RunCommand`, `LockPirate.RunCommand` | Repair actual local locking. Preserve natural-body proximity/safety checks and encounter detection. |
| `PiratePilot.GetTarget` | Repair both the ambush placement center and its inherited velocity by resolving the correct local reference before native placement. |
| `PolicePilot.FFWD` | Repair the local placement reference before native random placement; preserve the native return/flight continuation. Its separate GetTarget LOS query remains physical. |
| `AIShipManager.CheckLocalAuthorityScenario`, `SpawnNewBeaconResponse` | Repair nearby encounter target locks; retain authored spawning and response policy. |
| `BeatManager.Pirate`, `FactionShip`, `FactionBattle`, `GenerateTutorialDerelict` | Repair locks for locally placed encounter points/the tutorial wreck. Preserve encounter triggers, positions and actor choices. |
| `FlyToPath.PlanTrip4` short departure leg | Existing departure repair now shares station-reference validation; keeps its exact departure-depot reference and original scope. |
| `SpawnEnvironment`, `HaulerDeployingPilot.CreateDerelictDestination`, `AnchorDockedShip`, `ArenaModePilot` | Already request placeholders or an explicit intended reference. Preserve existing native/hauler/Venus repairs. |
| `FlightCPU.GetDockingNavPoint` | Physical-body obstacle geometry and body-centered detours remain native; do not turn station placeholders into celestial occluders. |
| `FlyToPath.CreateNavPointStatic` | Already locked repaired local goals pass through unchanged. Unlocked free-target/static prediction and interplanetary planning remain native; no blanket regional rebinding. |
| `FlyTo`, `FlyToManual`, `FlyToMissile`, `HaulShip`, `TargetNewIfNone`, `PolicePilot.GetTarget`, manager visibility checks | Natural-body queries serve atmosphere, gravity, LOS or avoidance; unchanged. |
| `KeepAtDistance` | Combat spacing and interception remain native. |
| `Ship.Undock`, Venus re-anchoring, scavenger free-coasting | Separate loaded-state/physics mechanisms, unchanged. |
| Racing, player starting-position coroutine, nav/map/debug drawings | Outside local AI coordinate creation; unchanged. PredictionRectangle implicit locks occur only in DebugVisualize. No static source caller was found for ShipSitu.SetRelativePosition. |
| Player-deployed signal beacons and race-buoy creation | Also contain implicit natural-body locks. These are separate object-placement semantics, not assumed harmless or repaired by this AI patch. Broker placement already supplies an explicit body or enables placeholders for orbital stations. |

Two transpiler classes replace 15 reviewed native call sites, each with an exact
one-call layout check. They do not hook global `GetNearestBO`/`LockToBO`, perform
per-frame corrections, add histories or persist custom state. Regional lookup
occurs only when a native activity creates/locks a point; it retains no registry
or ship references. Normal locked waits do not repeatedly invoke the resolver.

Existing wrongly locked ships/routes are not automatically migrated or moved.
They require an explicit, separate recovery. A code upgrade also cannot relocate
an already distant pirate merely by correcting future coordinate creation.

## Validation

`Tests/LocalNavigationFrameCheck` executes native navy/blockade candidate creation
and final selection, pirate target creation, police SFF placement, and
pirate/lurk/guard locking under bundled Mono. The recorded public MSUZ/Vesta orbit
elements reproduce over 100,000 km of native five-hour drift and a pirate target
beside Vesta. Corrected candidate offsets remain stable over six hours. Controls
cover planetary references, BCRS's synthetic hierarchy, missing/invalid frames,
unscoped locking and composition with the existing navy fuel filter.

Prepared hulls/conditions, regional station lookup, deterministic safe-coordinate
placement, irrelevant fuel/capability checks, Unity presentation and empty nearby
traffic are fixture boundaries. The native orbital calculations, target/command
methods and production transpilers execute. Binding verifies all 15 call sites;
the harness does not construct full BeatManager encounters or replay battles.
The existing departure/planner/Ceres suites provide their separate regressions.

Current results: 61 frame checks, 69 departure checks and 174 Ceres/navigation
checks pass. All 32 custom-builder profiles compile, verify and bind against the
native assembly, including isolated local frames and a combined
local-frame/departure/navigation-budget selection.

The behavior graphs record this as an unexpanded dependency. They do not claim to
simulate orbital geometry or complete military combat behavior.
