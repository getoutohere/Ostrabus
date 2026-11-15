# Deferred traffic investigations

The [console/SFF roster audit](../../CONSOLE-SFF-ROSTER-AUDIT.md) confirms a
native instant-docking dispatch defect: a materialized portless asteroid takes
the docking branch instead of native mooring, fails and clears target pointers.
Four isolated native checks reproduce the observed civilian warning/signature;
the two ships subsequently replanned home. This is ready for a mechanism repair.
The same audit establishes stale saved crew placements and orphan inventory,
including references already broken before the latest SFF. The original actor
removal/transfer path remains unproven; audit it before changing crew persistence
or suppressing the warning.

November 14: the [Ceres SFF displacement repairs](../../CERES-SFF-DISPLACEMENT.md)
fix stale mooring offsets and unpiloted RCS continuation. O-KGEE's sole crew
member still had `Stunned` across observations despite the asset's short
duration. Establish shallow condition-clock/expiry behavior before proposing
any health catch-up; do not globally tick/heal crews or reinterpret their
incapacitation merely to make ships return. Excess momentum already in the
pre-skip save needs explicit recovery, separate from prevention.

New high-priority native finding:
[unregistered executable courses](../../UNREGISTERED-COURSES.md). O-Y38JN keeps
playing a course after removal, then retains raw force after expiry. A native
baseline/candidates pass 209 checks. The [collateral audit](../../CONTROL-OWNERSHIP-AUDIT.md)
also establishes stale normal/SFF dispatch and an unsafe blanket-cancellation
interaction with native player course engagement. The completed direct-call audit
adds 410 native caller/handoff checks and joint regression runs. The repair is
implemented with another 234 production checks; historical recovery remains
separate. O-71CT has a
related RCS-force phenotype whose exact later write is not historically captured.

The active-invocation follow-up adds 84 checks. LowFuelRetreat's terminal Failure
lets both combat consumers resume control after removing their controller; a local
terminal return repair is included alongside withdrawal cancellation and scheduling
admission. Native missing-target FlyTo/Dock continuation is a negative control,
not another force leak. See the ownership audit for the source/fixture boundary.

Priority agreed after the cargo/civilian audit; this order reflects gameplay
impact, plausible severity and evidence gaps, not graph traversal order.
The civilian SFF X/Y correction and asteroid-conversion regression repair are
already implemented and native-tested. Completed follow-ups are marked below;
the remaining rows are investigations, not authorized behavior changes or
assertions of observed gameplay failures.

| Priority | Item | Established evidence | Outstanding question |
| --- | --- | --- | --- |
| 1 (handoffs repaired) | Freight retirement/unloading | Native tests reproduce and repair ordinary/emergency/SFF handoffs. Historical saves still show three removed hulls with surviving markets. | Exact historical removal caller remains unproved; repaired handoffs do not establish that history. See the [lifecycle repairs](../../../Orbital/Economy/FREIGHT-LIFECYCLE-AUDIT.md). |
| 2 (repaired) | Hidden-hull targeting/reuse | Native-tested selection guard, retired/restored visit invalidation, priority/SFF ownership and civilian encounter protection. | Production offline regression complete: 82 assertions plus 64 native traffic controls; full flight physics remain outside the fixture. See [repair](HIDDEN-VISIT-INVESTIGATION.md). |
| 3 (repaired) | Player-connected regional cleanup | Confirmed through native caller reproduction; repaired cleanup scope, scheduling and primitive saved registration. | Offline regression complete: 91 baseline/repaired cases, including captures, chains, controller restoration and detachment. See [repair](REGIONAL-CLEANUP.md). |
| 4 (repaired boundary) | Missing destination market | Production validates/stages transfer before market removal and gates ordinary/instant/escape/SFF retirement. | Missing station market's ordinary reachability remains conditional; demonstrated loss at this boundary is repaired. |
| 5 | Civilian home dispatch | Native interface calls bypass the owner-filtered method. | Establish correct individual-owner fallback before changing dispatch. |
| 6 (sizing repaired) | Cargo sizing/preflight | Production accumulates native rounded pod requirements across consignments. Ordinary reachability still precedes loading. | Loaded-mass affordability consequences remain unquantified. |
| 7 | Civilian fuel assistance | The shared scavenger SOS mechanism excludes civilian pilots; the inspected civilian tree lacks equivalent recovery. | Does another authored mechanism compensate, or would assistance be a new simulation policy? |

None originates solely in graph speculation. The table distinguishes confirmed
mechanisms from unproved consequences. Graph traces describe conditional paths;
they do not establish that real simulation callers reach them.

The [2026-11-13 live traffic follow-up](../../TRAFFIC-CONTROL-AUDIT.md) confirms
empty civilian Holds in space and separately reproduces a shared execution-time
fuel-admission bypass. The civilian assistance exclusion is a reachable recovery
gap; the exact fuel-exhaustion histories remain unknown. It also reproduces
abandoned-course force leakage, including the incomplete cleanup in our escape
repair. The [implemented force-handoff correction](../../FLIGHT-CONTROL-HANDOFF.md)
repairs that control ownership only; shallow fuel tolerance remains. Flight-leaf
contracts are catalogued as a native-tested unexpanded dependency in the graph.

See [source/asset investigation](INVESTIGATION.md). Each follow-up should retain
native policy, prefer asset repairs when assets own the defect, and reproduce the
specific native mechanism before adding a runtime patch.

The [November 14 freight lifecycle audit](../../../Orbital/Economy/FREIGHT-LIFECYCLE-AUDIT.md)
adds 521 native baseline/production assertions: multi-actor accounting, sizing,
missing-market failure, preserved overflow disposal and normal/SFF escape arrival.
Economic SFF admission now distinguishes actual Main/Flee/priority ownership,
including restored escape checkpoints. These tests expand coverage beyond the
graph's single-consignment projection; they do not establish the historical removal caller.
