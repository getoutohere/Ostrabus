# Scavenger pursues a retrieved and destroyed derelict

November 11 investigation: O-ZY3A (Soul Thought), captain Brody Morrow,
continued flying to E-XJ83 (Crimson Brain) after a station retriever removed it.
This is an uncovered native target-lifetime defect, not a fuel shortage or an
instruction to prevent scavengers and retrievers from choosing the same wreck.
The initial investigation made no live intervention. The shared lifetime repair
is now implemented; see the implementation section below.

## Evidence

- The earlier `steal` save contains E-XJ83, an anchored derelict referenced to
  `1036 Ganymed`. Later `autosave_3_steal` and `ai` saves contain no such ship.
- O-1Y06's saved ship log records docking with E-XJ83 at epoch
  65633024869.7301, then undocking at 65633025460.9046 while its pilot Finbarr
  Smith transfers to OKLG and the hauler docks/despawns there. Player.log records
  `Destroying ship E-XJ83.` immediately before that pilot transfer.
- Native `Ostranauts.Ships.Commands.DockCargoAndDespawn.HandleDockedShips`
  undocks the tow, calls `CrewSim.DockAndDespawn`, and destroys non-player
  derelicts. The station collector asset trigger `TIsDerelictCollector` requires
  `StationIsDerelictCollector`; ordinary candidate choice and retirement are
  native mechanisms, not a missing asset rescue policy.
- O-ZY3A retains E-XJ83's ID, destroyed Ship reference and orphaned ShipSitu.
  The latter has `bBOLocked=true`, `bIsBO=false`, `boPORShip=null`, a frozen
  position and nonzero velocity. Its current route begins at 65633025471.8658,
  about 11 seconds after the retrieval disposal, and ends approximately
  1.21072053094578e20 seconds later. The ship still has about 489.68 kg of RCS
  fuel at the captured live reading.

## Causal chain

1. Scavenger candidate eligibility is checked during selection. A concurrent
   retriever can subsequently collect and retire that candidate.
2. `Ship.Destroy` removes the target from the system and destroys its situation,
   but does not invalidate observers' retained target references.
   `ShipSitu.destroy` clears only `strBOPORShip`, leaving the lock flags and
   kinematics in that retained object.
3. `TargetData.HasValidTarget` tests only `Situ != null`.
   `FlyTo.HasValidPathTarget` accepts the retained `bBOLocked` situation.
   `CreateNavPointStatic` preserves its lock flag, so it does not obtain a new
   body reference. `ShipSitu.UpdateTime` cannot update its anchored position.
4. Native `FlyToPath.PlanTrip4` therefore receives a fixed position with a
   nonzero velocity. Its repeated moving-target correction diverges. The saved
   result is non-null, so `FlyToPath.RunCommand` keeps returning Running.
   Later Dock target validation is never reached.
5. `ShipReplacementTargetsPatch` handles same-ID replacement during
   `CrewSim.DestroyAndReload`. Permanent retirement has no replacement instance
   and is outside that repair. Rebinding the dead wreck to a body would be the
   wrong production response; it would preserve a nonexistent destination.
6. Native `FlyToManual.RunCommand` already detects a destroyed scan Ship,
   looks for its replacement and clears the destination if none exists. Planned
   dispatch bypasses that mechanism. Native `Ship.CheckTargets` introduces an
   additional boundary: after loading a removed destination, scan Ship is null
   but the named target and saved situation survive. That bypasses the manual
   destroyed-Ship check as well. These are shared target-lifetime concerns,
   not a scavenger-specific rescue policy.

## Offline verification

`Tests/AiPlannerDiagnosis` now accepts:

```powershell
dotnet run --project Tests/SaveJsonCompatibility/Host -c Release --no-build -- Tests/AiPlannerDiagnosis/bin/Release/netstandard2.1/AiPlannerDiagnosis.dll retired-target <captured-ship-layout.json> <saved-speed-limit-AU-per-second>
```

For this capture, use speed limit `3.91980270464387e-9`. Build the diagnosis
project first. The input is the extracted saved ship layout, not the whole save.
The replay executes the shipped Mono assembly's situation constructor/copying,
target-validity predicate, time update and full native planner. Acceleration,
gravity, fuel cost and collision/detour boundaries use the existing diagnosis
fixtures; it does not reproduce the complete original sortie or full Destroy.

All six checks passed. Native broken-route duration was
`1.2107205309457906e20` seconds versus captured `1.2107205309457798e20`.
The consistent linear-motion diagnostic control converged in 4219.32 seconds.
That artificial control isolates the numerical failure; it is not a proposed
repair for a genuinely removed ship. Generated detailed iterations remain in
ignored `obj/ConsoleIssues/retired-target-planner-replay.json`.

Before the permanent-removal repair, the native lifecycle suite passed 163 checks. With the existing
replacement repair installed it executes actual permanent `Ship.Destroy`,
`Ship.CheckTargets`, path eligibility and manual target validation. Shared
situations and independently owned waypoints both retain a dead named target.
The manual saved-target probe aborts at the first destination time update,
after native validation; it does not substitute a full steering outcome.

## Why the behavior graph missed it

This was a modeling omission, not source drift. Validation still reports all
32 source fingerprints/anchors and the assembly fingerprint matching, with all
79 scenario/model checks passing.

- Scavenger and retriever slices describe individual actors. They do not compose
  the retriever's retirement transaction with an observer's in-flight target.
- `scav_fly` has only arrival and fuel-accounting outgoing edges. There is no
  event or state variable for a target being removed during that flight.
- The `plan` variable means only that `GenerateNavData` returned a route.
  Numeric convergence, target liveness and meaningful arrival time are outside
  that boolean. Flight completion is an externally supplied event.
- Existing replacement coverage concerns the same ID getting a replacement
  Ship/ShipSitu. It does not cover permanent removal with surviving references.
- Fingerprints detect changes to declared source dependencies; they cannot
  discover missing transitions in an unchanged abstraction.

Consequently, the graph could not express this execution, much less flag it.
Its passing checks must not be read as target-lifetime safety. The new
[cross-actor target-lifecycle model](../TargetLifecycle/README.md) now represents
that missing execution and detects it in vanilla. The effective overlay now
repairs removal/restoration, backed by production-hook native tests. It also
covers saved missing destinations, finite routes, replacement, independently
owned waypoints, hiding and occupied ports. Native failure remains the baseline.

## Implemented production repair

Shared navigation-target integrity now covers permanent removal and missing
named targets restored from saves. It invalidates the affected goal, route and
fuel prediction so the existing command can fail and native behavior resume.
Preserve the current same-ID replacement repair, live hidden targets,
independent coordinate/stellar-object goals and legitimate moving/long-distance
destinations. Normal unloading uses Destroy inside a replacement transaction;
a blanket Destroy hook that discards courses would regress that repaired path.

This is C# reference/lifetime correctness. No asset needs a new instruction
for scavs to ignore a wreck that no longer exists. Do not ban competing target
selection, force station returns or cap routes as a substitute. The implementation
covers both early planned dispatch and the post-load null-Ship case; copying
the existing manual bDestroyed test alone would be insufficient.

`ShipDestroyedTargetsPatch` captures the old reference before destruction and
completes navigation cleanup afterward. The existing replacement prefix/finalizer
defers that path across a synchronous DestroyAndReload transaction, including
nested loads and native initialization exceptions. Full-system teardown and
projectile teardown do not trigger fleet scans. ShipRestoredTargetsPatch validates
the named destination after native CheckTargets, when the system is populated.
Native ClearShipTarget also clears its saved optional fields through the existing
ClearTargetFuelPatch: shallow GetJSON clones those fields and otherwise only
overwrites them when a live reference is non-null.

The repair adds two boundary patch classes, no recurring fleet poll or AI tick
hook. The suite passes 276 native lifecycle checks with the O-ZY3A capture,
including preserved motion/fuel, native failure continuation, route preservation,
GC release and zero allocation for unchanged restoration. The expanded graph has
20 scenarios and 161 scenario/model checks, with a bounded native failure witness
and no repaired witness within the checked event bound. Full model completeness
is still not established.
