# AI route divergence

Confirmed November 6, 2026 by captured state and offline execution of the shipped
planner. Follow-up lifecycle reproduction and prevention fix are described below.

## Failure

O-CMK and O-QVQ (salvagers), and O-ZWZA and O-GLN (deploying haulers), target OKLG
but coast outward on routes with arrival times between 7.59e18 and 1.03e21 seconds.
They retain substantial RCS fuel. This differs from the earlier orphaned-thrust
failure repaired by Haulers: these ships have active FlyToPath commands and plans.

All four target situations have `bIsBO=true`, nonzero world velocity, and a null
`strBOPORShip`. Their saved target positions do not follow OKLG.

Native source (`<decompiled-source>`):

1. `ShipSitu.UpdateTime` (line 515) advances referenced orbital situations or dock
   groups. An unreferenced snapshot with no dock group is left stationary, even
   when its stored velocity is nonzero.
2. `FlyToPath.CreateNavPointStatic` (line 605) skips its automatic orbital binding
   when `bIsBO` is true, allowing this malformed target through.
3. `FlyToPath.PlanTrip4Sub` uses target velocity multiplied by the estimated trip
   duration to compute the velocity-matched origin's displacement. Because the
   target position does not advance, a longer estimate increases the discrepancy.
4. `PlanTrip4` (line 190) performs an initial calculation and ten refinements,
   feeding the preceding duration into the next calculation. In these captures,
   solar orbital velocity (~20 km/s) greatly exceeds the relative cruise speed
   (~0.6–0.75 km/s), and each refinement magnifies the error. The final endpoint
   discrepancy is only debug-logged; convergence is never required for success.
5. `RunCommand` (line 74) rejects null plans but accepts this non-null result.
   Subsequent runs return Running while a plan exists, unless already inside the
   target handoff distance. Thus the enormous coast leg persists.

This is numerical divergence accepted as a valid route, rather than a deliberate
decision to undertake a very long journey.

## Native offline reproduction

`Tests/AiPlannerDiagnosis` runs actual `FlyToPath.PlanTrip4`, `PlanTrip4Sub`, and
`ShipSitu.UpdateTime` under the game's bundled Mono. Inputs come from captured
origin waypoints, target situations, and cruise limits in `obj/RING-ai-*.json`.

Fixtures supply a clear, atmosphere-free environment, the observed RCS acceleration
limit, and zero fuel bookkeeping. Detour selection is disabled. These isolate
route geometry; they do not simulate an actual return flight or prove fuel budget.

| Ship | Frozen-target native replay (seconds) | Consistent moving-target control (seconds) |
| --- | ---: | ---: |
| O-CMK | 9.4995372e19 | 28,359.54 |
| O-ZWZA | 3.4725640e19 | 25,812.38 |
| O-GLN | 7.5864629e18 | 2,241.25 |
| O-QVQ | 1.0259476e21 | 58,056.76 |

All four divergent durations match the captured live destination durations within
2e-8 relative tolerance. All 16 checks pass. The control supplies the missing
orbital reference backed by a linearly moving body at the captured target velocity;
it demonstrates consistency, not a full curved-orbit interception model.

Commands:

```powershell
dotnet build Tests/AiPlannerDiagnosis/AiPlannerDiagnosis.csproj -c Release --nologo
dotnet run --project Tests/SaveJsonCompatibility/Host -c Release -- Tests/AiPlannerDiagnosis/bin/Release/netstandard2.1/AiPlannerDiagnosis.dll
```

Results: `obj/AI-PLANNER-DIAGNOSIS.json`, including every refinement pass.

## Reproduced upstream cause: replacement during unloading

Native `TargetData(Ship)` shares the destination's live `objSS`. The native
`CrewSim.SaveToShallow` -> `DestroyAndReload` operation destroys that destination
Ship and recreates it under the same registration ID. `Ship.Destroy` calls
`ShipSitu.destroy`, which clears the old situation's orbital reference without
clearing its body flag or velocity. Observers still point to that old situation
and the destroyed Ship. This is sufficient to create exactly the malformed state
consumed by the planner. Native transit and undocking call SaveToShallow when
unloading previously materialized vessels/stations (for example GUITransit:135).

If the observer is then saved and reloaded, `Ship.CheckTargets` resolves its scan
ID to the replacement destination but independently restores its broken situation
snapshot. This explains the combination seen live: a valid OKLG scan target and a
frozen target situation with no orbital reference.

`Tests/AiPlannerDiagnosis/LifecycleChecks.cs` executes the shipped SaveToShallow,
DestroyAndReload, Ship.Destroy, ShipSitu destruction/JSON conversion, TargetData
and CheckTargets methods. It reproduces the first invalidation from a healthy
target and the malformed snapshot's survival through JSON restoration. Copying
and native LitJson serialization preserve an independently owned valid reference.

Fixture boundaries are Unity presentation, asset materialization in InitShip,
item serialization/enumeration and condition storage. Ship.GetJSON is a minimal
fixture invoking real ShipSitu.GetJSON; actual tile/crew serialization is omitted.
No target assignment, destruction, replacement orchestration or scan-target
restoration method is replaced. Thus this reproduces a concrete native lifecycle
failure, not every operation performed by a live station transition.

Follow-up save audit: 21 records for these four registration IDs were found across
the available save archives (one inaccessible Windows `aux` directory skipped).
Every recorded OKLG target already has the malformed reference; earlier O-CMK
records target a derelict with a valid Ganymed reference. There is no captured
healthy OKLG-target snapshot followed by a malformed one, so this does not identify
the first writer. Evidence is `obj/AI-TARGET-SAVE-AUDIT.json`. The audit runs on
.NET 8 with `audit-saves` (the bundled Mono host lacks the compression helper):

```powershell
dotnet exec --runtimeconfig Tests/SaveJsonCompatibility/Host/bin/Release/net8.0/MonoHost.runtimeconfig.json Tests/AiPlannerDiagnosis/bin/Release/netstandard2.1/AiPlannerDiagnosis.dll audit-saves
```

The save audit cannot prove which historical unload caused each captured ship's
corruption. The lifecycle reproduction now establishes a concrete cause without
requiring that historical inference.

## Implemented prevention

`MiscBugFixes/ShipReplacementTargetsPatch.cs` captures the old Ship/situation
before DestroyAndReload and transfers references to the replacement afterward.
It updates scan, station-keeping, combat and undock references plus TargetData's
Ship/Situ fields. Only exact object references are transferred; independently
owned offset/border snapshots retain their original situations and offsets.
Existing TargetData objects retain identity so other holders see the transfer.

This is not a planner override. No NavData is cleared or edited, and no duration,
distance, velocity or coordinate limit is applied. Native cargo/player torch
courses, fuel accounting and motion remain untouched. The hook runs once per
native shallow replacement and scans the existing ship dictionary with no retained
global state or per-frame work. Genuine ship destruction outside this replacement
operation does not trigger rebinding.

All 123 lifecycle checks pass, including repeated replacement, seeded cargo/torch
and very long-distance route preservation (serialized route data unchanged),
offset/border waypoints, unrelated targets and JSON restoration. These are route
preservation checks, not a simulation of complete interplanetary cargo flights.
The original 16 full native planner diagnosis checks also remain passing.

```powershell
dotnet run --project Tests/SaveJsonCompatibility/Host -c Release -- Tests/AiPlannerDiagnosis/bin/Release/netstandard2.1/AiPlannerDiagnosis.dll lifecycle
```

## Explicit recovery of previously saved corruption

The prevention hook does not retroactively repair malformed saved targets.
`MiscBugFixes/NavigationTargetRecovery.cs` adds `navfix` (dry run) and
`navfix apply` (explicit recovery). It scans for the confirmed null-reference plus
body-target flag, verifies the existing scan/target IDs agree, and resolves the
same named destination to a valid current orbital situation. It requires an
active registered AI pilot, finite motion and an unambiguous direct target.
Offsets/borders, player control, active combat, hidden/inactive vessels and docked
or anchored groups are reported and skipped. There is no call-sign/faction list
and no trip-distance/duration test.

Recovery calls native StopManeuver, ResetNavData, and SetThrust(0) if an old torch
input remains, then applies native TargetData for the same destination. It leaves
position, linear/angular velocity, fuel, crew, behavior trees and AI scheduling
unchanged. Angular acceleration and commanded linear acceleration are cancelled.
The next normal AI navigation update can replan; the command itself never invokes
the planner, updates orbital time, or installs a new course. No background scan,
automatic target refresh or repeated replanning is added. Healthy targets no
longer match, making repeat execution a no-op.

`RecoveryChecks.cs`: 98 passing checks cover selection/rejection, arbitrary call
signs, native stopping controls, physical state preservation, real console hook
dispatch, dry-run immutability, repeated apply and healthy long-route exclusion.
Captured current ship states and contemporaneous OKLG motion are replayed through
the full native planner after recovery, with native RCSAccelMax and fuel formulas.

| Ship | Return duration (game hours) | Predicted RCS at arrival (kg) |
| --- | ---: | ---: |
| O-CMK | 3.82 | 610.05 |
| O-ZWZA | 3.81 | 2,711.72 |
| O-GLN | 6.70 | 2,976.68 |
| O-QVQ | 15.98 | 970.90 |

The planner fixture uses a clear, atmosphere-free route and linear target motion,
stored shallow mass with no extra contracted-cargo mass, and captured RCS count,
efficiency and remaining fuel. Predicted endpoint errors are below 0.01 km. These
isolate successful recovery/planning and fuel arithmetic; actual orbital curvature,
collision avoidance, later AI decisions and elapsed live-world changes can change
the eventual return times and fuel. No live ship is modified by these tests.

```powershell
dotnet run --project Tests/SaveJsonCompatibility/Host -c Release -- Tests/AiPlannerDiagnosis/bin/Release/netstandard2.1/AiPlannerDiagnosis.dll recovery
```

Detailed route/fuel results: `obj/AI-NAV-RECOVERY.json`.

## Findings from the orbit/navigation audit (November 6)

All three findings have now been corrected offline on November 6.

1. **Navy patrol selection bypasses its own fuel rejection.** Native
   `NavyPilot.GetTarget` returns a lone candidate without its final BingoFuelCheck;
   after rejecting every candidate it can still choose `list.First().Item2`.
   Both branches were reproduced, then corrected with general candidate filtering
   before native single/random/fallback selection. The native decision algorithm
   remains; Ceres additionally retains its 20% round-trip policy. Affordable
   alternatives are preserved. The expanded Ceres audit also reproduced and fixed
   broken return sequencing/persistence, stale manual-flight fuel accounting,
   invalid RCS budgets and short-range combat reserve coverage. Shared fixes live
   in Navigation; boarding returns use the same controller. See
   `CeresNavigation/README.md`. The role of individual defects in historical
   stranded ships has not been proven.
2. **VORB guard confuses replacement with a destination change.**
   `Venus/VorbNavPlanGuard.GetState` clears NavData when the target ShipSitu instance
   changes even if its target registration ID remains the same. By inspection,
   this discarded a valid course after ShipReplacementTargetsPatch correctly
   transferred references during shallow replacement. Reproduced through native
   DestroyAndReload and both production patches, then corrected by comparing
   logical target ID, orbital binding and offsets rather than object identity.
   The 134 lifecycle checks now preserve identical route JSON and fallback across
   equivalent replacement/reapplication, while real destination changes invalidate.
   Target descriptors retain no strong simulation-object references.
   Replaying original VORB divergence remains
   necessary before removing the old rejection/manual-fallback workaround.
3. **SFF/native ephemeris coordinate mismatch.** `OrbitalMotion.BodyAt` uses a
   tighter Kepler solver than native BodyOrbit, whereas normal coast integration's
   `NativePosition` deliberately follows native tolerance. Prior analysis measured
   about 15.87 km difference for Venus. See
   `Orbital/LiveTest/TIMESTEP-FINDINGS.md` and `COAST-READINESS.md`. Corrected by
   sharing NativeEphemeris between normal coast and SFF, matching native position
   arithmetic/tolerance without touching mutable BodyOrbit caches. Velocity is
   derived analytically at the same native-solved phase. Captured discrepancy is
   now zero; nested, eccentric, retrograde and wrap/negative-epoch cases compare
   against actual BodyOrbit, with cache immutability and SFF regression coverage.
   Arrival/collision thresholds are unchanged. A resulting live SFF failure was
   never established; this corrects the reproduced coordinate inconsistency.

Investigation principle: reproduce the first wrong state transition and repair
the responsible operation. Keep explicit recovery and simulation/game-design
policies distinguishable from defect prevention.
