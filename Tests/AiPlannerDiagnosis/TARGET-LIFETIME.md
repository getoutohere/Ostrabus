# Target lifetime repair checks

The shipped Mono assembly supplies Ship.Destroy, SaveToShallow/DestroyAndReload,
Ship.CheckTargets, ShipSitu, FlyTo dispatch/manual validation, StopManeuver,
SetThrust and native JSON situation round-tripping. Unity presentation, asset
materialization, item serialization and unrelated inventory setup use the
existing lifecycle fixtures. These checks do not claim a whole-world playtest.

```powershell
dotnet build Tests/AiPlannerDiagnosis -c Release
dotnet run --project Tests/SaveJsonCompatibility/Host -c Release --no-build -- Tests/AiPlannerDiagnosis/bin/Release/netstandard2.1/AiPlannerDiagnosis.dll lifecycle
```

Append an extracted captured ship-layout JSON to the final command to verify
automatic recovery of a real saved missing target. The O-ZY3A capture remains
local under ignored obj/ConsoleIssues; game/save contents are not committed.
With that input the suite passes **276 checks** (272 without the capture).

Vanilla witnesses execute before the new destruction/restoration hooks are
installed. They show that removal leaves observer references, planned eligibility
accepts them, manual flight rejects a live dead-Ship reference, and restored null
Ship references bypass that manual check. The saved-target probe stops at native
steering entry after validation; it does not substitute a successful maneuver.

Production checks install the actual replacement, destruction, restoration and
ClearShipTarget hooks. They cover shared versus independently owned situations,
finite versus trillion-year routes, cached old fuel budgets, native failure
continuation, saved JSON resurrection, dangling auxiliary references, hidden
hulls, coordinate/body/stellar-object targets, unrelated sensor selections,
repeated restoration and repeated/nested/failed replacement transactions.

The actual captured O-ZY3A route and target are cleared without changing its
position, velocity or fuel. Separate weak-reference/collection checks verify
that the removed Ship, ShipSitu and TargetData are no longer rooted. The normal
unchanged-target restore check allocated **0 bytes over 5,000 calls** on the
bundled Mono runtime after warmup.

The separate `retired-target <layout> <speed-limit-AU-per-second>` mode replays
the original numerical divergence in the native planner: six checks compare the
captured route and a coherent-motion diagnostic control under prescribed
acceleration/gravity/fuel/collision fixtures. That artificial moving-body control
is not the production repair; missing destinations are discarded.
