# Hidden hulls as civilian rendezvous destinations

Status: repaired against 1.0.1.10 by `MiscBugFixes/CivilianVisitsPatch.cs`, including
selection, retired/restored visits, priority/SFF ownership and physical encounters.
Native offline production tests pass; the effective graph includes the repair.

## Defect ownership and meaning of hidden

`CivilianPilot.GetTarget` chooses an asteroid, a nonstation Ship, or a station,
with native fallbacks. Its private `CanDockWith` rejects self, attached targets,
missing ports, registered AI controllers, materialized hulls and tutorial wrecks.
It does not reject `HideFromSystem`. A hidden, unregistered shallow hull is
therefore eligible even when its damage classification is Used, not Derelict.

`CrewSim.DockAndDespawn` hides a retired hull, records its last station and
unregisters its controller. `AIShip.FindShipForFaction` accepts that hull for reuse.
`CivilianPilot.CreateAIShip` can then select it, reset it, move it to a departure
station, assign that station's velocity and subsequently add crew/refuel it.
The visitor's destination reservation is not consulted by the reuse selector.

Hidden is an administrative world-visibility flag, not poor sensor reception or
an inactive transponder. Its setter also disables collisions. The same flag is
used for broker inventory, ship sales, special offers and substations. Those uses
reinforce restricting new random space rendezvous; they are not evidence that all
hidden objects are retired, derelict, or permanently dead. Native station selection
and explicit destinations must retain their own eligibility rules.

Assets reviewed: `RandomCivilianShip`, its CCRE/Galcon pools, `RandomPassShip`
(Medium Ferry), and the existing regional population/traffic predicates. The
civilian target branch and its eligibility helper are C#; these JSON entries do
not express a hidden-hull rendezvous rule. Population redistribution uses the
separate `AIShipManager.SpawnPassShip` station-capacity path. No asset correction
or replacement AI policy is warranted for this omission.

## Selection correction and consequences

The patch adds `if (shipTarget.HideFromSystem) return false;` to the private civilian
roaming eligibility helper through one scoped patch. Keep all remaining native
conditions, random draws, retries, fuel checks and station/asteroid fallback.

- Visible crewless Used hulls and actual Derelict hulls remain visitable.
- Visible asteroid hulls retain their special no-docking-port allowance. The
  separate asteroid-field selection branch is outside the patched helper.
- Active AI, attached, materialized, portless and self targets retain their
  existing exclusions. The correction adds no faction, ownership or crew policy.
- If only hidden ship candidates exist, native selection can fall through to a
  station. Destination distribution changes because unsuitable choices disappear;
  this does not add a new dispatch quota or guarantee a successful voyage.
- Explicit cargo/PASS destinations, remembered docking partners, combat targets
  and player navigation do not use this roaming helper. Their rules are unchanged.
- Native hidden-hull reuse remains available. Reusing a hull already physically
  attached is separately covered by the existing DetachedReserveHullPatch.

The selection guard has one flag read/branch per roaming candidate, no allocation in
the predicate and no polling or Update hook. Skipping hidden candidates also
avoids the remaining native docking/controller eligibility work for them.

## Reproduced reuse consequence and an important qualification

The native creator can move a previously selected hidden hull. The visitor keeps
the same Ship and shared live ShipSitu references, so its target coordinates move
with the reused hull. `FlyToManual.RunCommand` uses the live target coordinates.
Therefore this is not proof of a ship chasing an empty, obsolete coordinate.

Repositioning does not invalidate the visitor's assigned navigation state or
`TripFuelPrediction`. An unintended pursuit and an estimate made for the previous
rendezvous are plausible consequences. The investigation does not establish a
particular failed arrival, amount of wasted fuel or resulting collision.

The selection guard alone cannot cancel existing assignments. Native CheckTargets
restores hidden destinations and TargetNewIfNone retains them. The additional
lifecycle integration below handles those boundaries instead of claiming that
selection filtering solves them.

## Complete visit lifetime

The civilian constructor transpiler replaces only its native five-child Main
sequence with a StickySequence subclass that checks visit validity before native
continuation. All five native child objects, their order, sticky progress, parent
combat/priority arbitration and save paths stay intact. This is a precondition on
the existing sequence, not another behavior controller or route planner.

When a nonstation hull becomes hidden, registered civilian observers with an
ordinary approach record its ID in the primitive blackboard string
`Ostrabus.CivilianVisit.Invalid`. If the native active leaf is that Main's FlyTo
or Dock and no other priority/reaction owns control, the visit is canceled then.
Otherwise the marker waits until native parent selection actually returns to
ordinary Main. It survives rapid reserve reuse and save/reload; a revealed hull
does not make the old trip valid again.

Cancellation stops the old commanded maneuver, clears the route, runtime and
saved destination references, prediction and cached manual-trip budget, then
resets the native sticky sequence. Position, velocity, fuel and crew remain
unchanged. Native Undock/TargetNewIfNone choose what happens next, including
normal low-fuel home selection. If the original hull is visible and eligible
again, native AI may choose it with a newly evaluated trip; no blacklist is added.

Registration observes restored targets and native Main also validates hidden
saved visits. A scoped civilian FFWD guard recognizes the actual Main leaf,
including its one-shot wrapper, before instant docking/queuing. Explicit queued
flights and combat/retreat/reaction leaves keep their respective control. Body
stations, cargo/PASS destinations and player navigation are outside this rule.

Once physically docked, the encounter remains intact. DockAndDespawn is blocked
for a nonstation connection with a registered civilian participant, from either
participant's side. Station-only parking and ordinary hauler tow deliveries are
allowed. The existing detached-reserve guard prevents reuse of an attached hull.
This distinction avoids breaking legitimate retrieval while protecting visitors.

## Performance and save compatibility

Six methods are patched: civilian candidate eligibility, constructor Main creation,
the hide setter, AI registration, civilian FFWD and retirement. There is no Update
hook, frame callback or periodic world scan. The hide observer walks registered
controllers only on an actual nonstation/nonprojectile hide transition; valid Main
checks use native references/dictionaries and allocate zero bytes over 5,000 calls.
The Main subclass replaces the allocation native construction would make. Static
fields contain reflection accessors only, with no retained game objects or worlds.

The saved tree still uses the native `/BehaviorTree/Main` path and native child
types/payloads. The pending invalidation is a native primitive blackboard string.
An unpatched civilian constructor loads the saved Main progress in the offline
test, and vanilla can ignore the unknown string key. No custom serialized type or
dependency on the mod is introduced.

## Offline evidence

```powershell
dotnet build Tests/AiPlannerDiagnosis -c Release
dotnet run --project Tests/SaveJsonCompatibility/Host -c Release --no-build -- Tests/AiPlannerDiagnosis/bin/Release/netstandard2.1/AiPlannerDiagnosis.dll hidden-visits
dotnet run --project Tests/SaveJsonCompatibility/Host -c Release --no-build -- Tests/AiPlannerDiagnosis/bin/Release/netstandard2.1/AiPlannerDiagnosis.dll civilian-visits
```

The original investigation retains 49 focused assertions plus 64 native traffic
checks (113 total). It installs only a selection prototype. The production mode
passes 82 lifecycle assertions plus the same 64 traffic checks (146 total). The
original `traffic` mode remains a separate unchanged native baseline.

Production cases execute actual root/sticky/queue/reaction scheduling, target
selection/registration, hide notification, saved paths and native target restore.
They cover immediate retirement, retirement followed by reuse, old hidden saves,
saved invalidation markers, deferred combat/threat/priority/reaction control,
explicit SFF leaves, ordinary SFF/wrappers, both sides of a physical encounter,
station-only parking, hauler tow exclusions, low-fuel home selection and unchanged
visits. Unpatched-tree save restoration and zero-allocation validation are checked.
Physical flight, numerical affordability/planning and terminal retirement crew
movement are controlled fixture boundaries. Native RCS stop runs with audio
notification suppressed; no full gameplay journey is claimed.

Tests execute the native candidate predicate and GetTarget with/without the
candidate, station fallback, visible crewless alternatives, native reserve lookup,
explicit cargo target resolution, remembered-partner FFWD, native saved-target
CheckTargets restoration and existing-target continuation. A matrix compares
Used/Derelict, visible/hidden, materialization, active controller, attachment,
ports, self and asteroid eligibility. Target restore is a DTO/CheckTargets test,
not a complete save-file reload.

The creator reproduction executes actual FindShipForFaction, ResetShipState and
PlaceOrbitPosition. It deliberately stops at the subsequent AddCrew boundary.
Captain/faction/condition values, RNG, fuel affordability, docking ports, damage
presentation and stationary TimeAdvance are controlled inputs/boundaries. Crew
creation, a full spawning Update cycle, navigation physics and actual docking
outcomes are not simulated. These limits distinguish the demonstrated eligibility
and relocation conflict from unproved downstream gameplay failures.

The custom-builder feature is `civilian-visits`, a bugfix depending on existing
navigation-target and crew/connected-hull integrity repairs. It has no runtime
toggle or PDA setting. Normal full-build deployment includes it.
