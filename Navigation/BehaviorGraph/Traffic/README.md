# Cargo and civilian traffic

The shared combat leaf has a separately reproduced native failure:
[SurrenderOffer can clear combat even when sending fails](../../SURRENDER-COMBAT-LOOP.md).
Its production repair and dedicated assembly tests cover message failure,
mixed opponents and native tree continuation. Constructor-tree references and
stubbed priority checks alone do not establish these leaf semantics; this slice
does not claim a complete negotiation or combat model.

The shared native combat leaves have another established gap:
[disabled AI targets are reacquired after native neutralization](../../COMBAT-NEUTRALIZATION.md).
Its separate 66-check native reproduction covers acquisition and neutralization
semantics. The production acquisition repair has 170 combined baseline/contrast
checks, including native cleanup before exclusion and controller reactivation.
This remains external leaf evidence, not a complete battle projection.

The [implemented controller-lifetime boundary](../../UNREGISTERED-COURSES.md)
adds native-tested withdrawal cancellation, exact normal/SFF dispatch admission,
replacement course ordering and terminal retreat status. It preserves regional
migration, native retirement policy and missing-target continuation. See its
machine-readable caller review rather than inferring force ownership from a
successful constructor-tree or lifecycle projection alone.

This reference separates freight, PASS shuttles and civilian visitors, follows
their native control and lifecycle boundaries, and derives the current Ostrabus
overlay without silently repairing known defects in the model.

- [Investigation and certainty of findings](INVESTIGATION.md)
- [Prioritized follow-up investigations](FOLLOW-UP-PRIORITIES.md)
- [Native navigation withdrawal audit and collateral controls](../../CONTROL-OWNERSHIP-AUDIT.md)
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
70 scenarios and 410 consistency checks. It pins 73 source groups and 15 asset
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

Navy/Blockade lost-contact cleanup is recorded in the encounter slice's method
bindings and [native reproduction](../../PATROL-TARGET-HANDOFF.md). It is separate
from this freight/PASS/civilian lifecycle model and its passing counts.

[Unregistered-course playback](../../UNREGISTERED-COURSES.md) is a newly confirmed
numerical/lifecycle boundary: removal does not cancel the course; playback restores
force and can retain it after expiry. The 156-check native investigation is not
yet a production repair. Retirement edges do not prove stopped movement; this
remains outside the projection's numerical flight coverage.
