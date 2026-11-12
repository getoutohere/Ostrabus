# Cargo and civilian traffic

This reference separates freight, PASS shuttles and civilian visitors, follows
their native control and lifecycle boundaries, and derives the current Ostrabus
overlay without silently repairing known defects in the model.

- [Investigation and certainty of findings](INVESTIGATION.md)
- [Prioritized follow-up investigations](FOLLOW-UP-PRIORITIES.md)
- [Confirmed player-connected regional cleanup defect](REGIONAL-CLEANUP.md)
- [Hidden-hull visits: candidate correction and lifecycle limits](HIDDEN-VISIT-INVESTIGATION.md)
- [Collision-created duplicate controllers and orphaned freight](../../../MiscBugFixes/FREIGHT-CONTROLLER-IDENTITY.md)
- [Actual native constructor trees](CONTROL-TREES.md)
- [Vanilla transition diagram](VANILLA.md)
- [Current effective diagram](EFFECTIVE.md)
- [Scenario audit](AUDIT.md)

`vanilla.json` and `overlay.json` are authoritative for the lifecycle projection.
`control-trees.json` is exported by the native role harness and preserves complete
constructor child ordering, including combat/priority branches. It is checked
against the declared source inventory and compared with a fresh native export.
The projection does not replace those trees or simulate every physical leaf.

The shared [active-hull retirement repair](../../../MiscBugFixes/ACTIVE-SHIP-STALE-CLEANUP.md)
also applies to freight and civilian controllers: new valid registration cancels
an obsolete IsStale marker, and the background worker rechecks the timer before
marking removal. A later command-failure marker remains authoritative. The
projection's `stale`/`stale_due` variables represent the current conditions;
registration-time timer bookkeeping and worker interleavings are verified by
Tests/StaleShipDiagnosis rather than simulated by this lifecycle projection.

The current reference has 29 control points, 112 vanilla / 123 effective edges,
70 scenarios and 410 consistency checks. It pins 54 source groups and 14 asset
entries. The accepted IL/asset baseline reports no missing selectors or unresolved
members. These counts describe coverage of the reference, not proof that the
entire game simulation is correct.

The registry state now includes `dual`: native collision recovery can attach a
second local controller to an interregional freighter. The new scenario follows
that entry through local cleanup to an orphaned manifest; the effective overlay
reuses the original controller. Native save normalization has separate typed
ownership/ambiguity checks in the shipped-assembly harness. It is not modeled as
an unconditional historical shipment recovery.

```powershell
dotnet run --project Tools/AIBehaviorGraph -c Release -- validate --model traffic
dotnet run --project Tools/AIBehaviorGraph -c Release -- generate --model traffic
dotnet run --project Tools/AIBehaviorGraph -c Release -- scenario civilian-hidden-reserve-selected --model traffic
dotnet run --project Tools/AIBehaviorGraph -c Release -- scenario asteroid-conversion-preserves-goal --model traffic
```

Use `--game-source <decompiled-root>` and `--assembly <Assembly-CSharp.dll>` to
verify fingerprints. The dependency manifest also binds helper commands, shared
market/crew/controller boundaries and selected asset rules for revalidation.

Native checks:

```powershell
dotnet build Tests/AiPlannerDiagnosis -c Release
dotnet run --project Tests/SaveJsonCompatibility/Host -c Release --no-build -- Tests/AiPlannerDiagnosis/bin/Release/netstandard2.1/AiPlannerDiagnosis.dll traffic
dotnet run --project Tests/SaveJsonCompatibility/Host -c Release --no-build -- Tests/AiPlannerDiagnosis/bin/Release/netstandard2.1/AiPlannerDiagnosis.dll traffic-conversion
dotnet run --project Tests/SaveJsonCompatibility/Host -c Release --no-build -- Tests/AiPlannerDiagnosis/bin/Release/netstandard2.1/AiPlannerDiagnosis.dll region-cleanup
```

The second mode includes the shared lifecycle suite and the conversion regression
repair: successful native conversion preserves independent/copied/direct goals
and routes, while failed conversion clears the actual lost destination. Native
exception, nesting, saved restoration and retired-reference controls are included.

Validation: 64 native role/coordinate checks; 303 shared lifecycle/conversion
checks; 1,443 existing economy checks; 136 existing station-arrival checks.
All passed. Native baseline reproductions and corrected behavior are distinguished.
Fixture boundaries are documented in the investigation and test sources. The native
planner fixture build retains its existing unassigned settings-field warning;
the graph tool builds without warnings.

`region-cleanup` establishes and repairs the player-connected cleanup case:
1,524 checks pass (272 shared lifecycle plus 1,252 regional assertions across 91
cases). Vanilla retains the reproduced destruction; the effective graph preserves
the group and models native scheduling, retained membership and detachment.
See the linked reproduction for caller coverage, save restoration, crewless
faction-hull controls and fixture boundaries.

`civilian-visits` passes 146 assertions (82 production visit-lifecycle checks plus
64 shared native traffic controls). New hidden selection, retired/restored visits,
control deferral, SFF, connected encounters and native hauler/station retirement
exclusions are represented in the effective graph. Saved paths load in an
unpatched native tree. See the linked visit investigation for fixture limits.
