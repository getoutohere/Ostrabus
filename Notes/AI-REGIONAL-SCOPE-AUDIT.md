# Continued regional scope audit

**Historical pre-repair evidence.** The authorized follow-up removed the VORB
empty-home shortcut, consolidated target observers and generalized refill
accounting. See [implementation and regressions](../Navigation/RCS-REPLENISHMENT.md).
The table below records the earlier defect; current `scope` runs 70 regressions
and retains 95 kg in all seven refill cases. Regional target-route cleanup and
invalid-route containment remain scoped as before; VORB anchoring stays hooked.
The subsequent [retarget caller investigation](AI-RETARGET-CALLER-AUDIT.md)
establishes a native police retreat witness and distinguishes already-cleaned
callers and legitimate intermediate/player plans. Its narrow escape-setter
repair is now implemented; the generic setter witness below is not universal
caller proof.

Working evidence record, 2026-11-09. This pass compares native callers, assets,
patch interactions and cross-home/role fixtures before proposing gameplay
changes. No production refactoring is authorized by these findings alone.

The deploying-hauler graph is under `Navigation/BehaviorGraph/Deployer`.
Native scope fixtures are `Tests/ScavFuelDiagnosis/NavigationScope.cs`, mode
`scope`. Assertions describing present coverage gaps are diagnostic witnesses,
not acceptance tests for desired future behavior.

## What this adds to the earlier audit

The previous conditional-candidate review established that route validation,
target integrity and refill accounting are different duties. That did **not**
establish that their VORB-only scope was correct. Retaining those duties is not
the same decision as retaining the regional classifier or every implementation
detail. The following findings supersede any broader inference from that review.

This is still a bounded audit, not a complete proof of every ship/crew system.
The reviewed continuation specifically closes the deployer/route/fuel scope
gap, maps important callers outside that role and separates natural reachability
from deliberately supplied preconditions.

## Source and asset map

Native source identifiers below are relative to the reviewed 1.0.1.10 decompilation.
They are method references, not claimed original-source line numbers.

| Mechanism | Native owner | Regional data or distinction |
| --- | --- | --- |
| Deploying-hauler mission | HaulerDeployingPilot constructor: Dock, HaulShip, AnchorDockedShip, FlyTo | The same C# tree is used at every home station. No VORB-specific tree exists. |
| Empty deployer target | HaulerDeployingPilot.GetTarget | Assigned HomeStation, then ATC fallback. While towing a derelict it instead creates a placement waypoint. |
| Wreck placement | CreateDerelictDestination | Nearest body including placeholders; radius plus randomized 90–400 km band. Same algorithm, different body inputs. |
| Wreck anchoring | AnchorDockedShip.RunCommand | TIsAllowsAnchoring evaluates the origin station's StationAllowsAnchoring condition. |
| Collector assignment | HaulerRetrievingPilot.GetTarget/FindDerelictTarget | Nearby regional desk, recent assignments and ownership-filtered wrecks; VORB's home depot differs from VNCA's desk. |
| Planned flight | FlyTo.RunCommand, FlyToPath.RunCommand | Shared implementation. An existing plan returns Running without comparing it to a newly assigned goal. |
| Plan fuel consumption | NavData.TimeAdvance | Shared absolute remaining-fuel reconciliation; no faction/station/role distinction. |
| Replenishment | Ship.AIRefuel / RefuelRCS | Shared setters. AIRefuel overwrites shallow contents with capacity; neither native method rebases NavData. |

In the installed NewGame asset, **only OKLG and VORB** have the reviewed derelict
spawner/collector starting conditions. OKLG has StationAllowsAnchoring; VORB
does not. VORB's limits are 16 spawned/9 collected, versus OKLG's 60/40. Those
numbers and the two different orbital environments are data differences; they
do not supply different plan-ownership or fuel-reconciliation algorithms.
HQCH in the tests is a counterfactual home-station control, not a claim that
vanilla naturally dispatches a derelict-deploying fleet there.

## Fresh executable evidence

`Tests/ScavFuelDiagnosis`, mode `scope`, passes **69 assertions** using the shipped
assembly under its bundled Mono runtime and the current production observers.
The test does not modify the installed game or a live save.

| Prescribed role/home | New goal: old NavData | 100 kg + 20 kg refill − 25 kg travel |
| --- | --- | ---: |
| Deployer / VORB | Cleared | 95 kg |
| Deployer / OKLG | Retained | 75 kg |
| Deployer / HQCH control | Retained | 75 kg |
| Retriever / VORB | Retained | 75 kg |
| Retriever / OKLG | Retained | 75 kg |
| Cargo / OKLG | Retained | 75 kg |
| Scav / OKLG | Retained | 75 kg |

For the retained-route cases, actual NavData.TimeAdvance moves the ship along
the old stored route while the new goal remains selected. The general retarget
hook clears TripFuelPrediction but not that plotted route. Equivalent target
reapplication preserves both route and prediction in every tested row.

The refill experiment calls actual AIRefuel and actual NavData.TimeAdvance.
Without budget translation, reconciliation reduces the new contents to the old
plan's 75 kg prediction, erasing the 20 kg addition. Explicitly translating both
plan endpoints by the addition yields 95 kg in **every** row, preserving the
original planned expenditure. This is an accounting invariant, not a VORB
navigation preference or a reason to supply free fuel.

The test explicitly supplies an active plan at refill/retarget time. It proves
the shared mechanism and patch coverage; it does not prove each role naturally
reaches that exact combination through every native caller.

### Full native sequence, not just isolated leaves

With general HaulShipReturnPatch, AnchorDockedShipReturnPatch and
HaulerIdleManeuverPatch installed, but **without the VORB FlyTo prefix**, the
native selector/Main/AI sequence reaches a home-targeted FlyTo for VORB, OKLG
and the other-home control from:

- Empty HaulShip at Main[1].
- A saved targetless FlyTo at Main[3], within two AI invocations.

Native HaulShip, AnchorDockedShip.CanExecute, SelectorCommand,
StickySequenceCommand, target assignment and AI dispatch execute. A valid-target
FlyTo's physical flight is a Running fixture; a targetless FlyTo executes its
native failure path. Captain/condition/presentation and collision-distance
lookups are fixture boundaries.

An important non-bug: AnchorDockedShip inherits Undock.CanExecute, so it is
**skipped when already empty**. Reasoning from RunCommand/Cleanup alone would
incorrectly predict that it destroys the newly restored home target. The test
checks that inherited eligibility explicitly.

This establishes that the VORB prefix's extra RestoreHomeTarget shortcut is
redundant for eventual recovery in these ordinary states. It can still save a
failure/retry tick for an old targetless save. That timing difference must be
acknowledged; equivalence is not byte-for-byte or same-tick execution.

### Additional VORB ordering finding

The current refill helper validates the **old** route budget before adding new
fuel to its stored levels. With 100 kg initial fuel and a route predicting −10 kg
at arrival, a 20 kg refill would make arrival +10 kg. Actual AIRefuel fills to
120 kg, but current FuelAdded rejects the route first and latches manual fallback.

This is reproduced with a supplied route. It does not prove the frequency of
this combination in ordinary gameplay. It shows that refill accounting and
route-feasibility policy should not be fused in this order. A translated budget
can be judged afterward, or the native planner can be asked to reconsider; this
does not justify promising a feasible or successful rescue.

## Native caller audit: where blanket generalization goes wrong

| Caller | Existing route handling | Consequence for the audit |
| --- | --- | --- |
| TargetNewIfNone, targetless selection | Resets NavData before choosing/applying target and possibly supplying a new plan | Not every target assignment needs another reset. |
| TargetNearestStation | Resets NavData before selecting station | Normal threat-retreat path already invalidates its plan. |
| TargetEmergencySignal | Writes target directly and resets NavData | Does not require ApplyToShip observer to invalidate it. |
| TargetEscapeStation.SetTarget | Writes target fields directly, without resetting NavData there | ApplyToShip-only generalization would miss this path. Its actual entry state still requires dedicated testing. |
| FlyToPath.GetTarget fallback | Applies a target only after the existing-plan early return | Cannot use that fallback to claim it checks a changed target against an existing plan. |
| GetTarget command | Only obtains a target when shipSituTarget is null | Different precondition from replacing an existing goal. |
| Retriever / deployer FFWD target updates | Generally occur after no-plan checks or native cleanup; hauling has its own stage | Do not invalidate a group route indiscriminately or conflate collector/deployer mission states. |
| Pirate FFWD after Undock/ShakeDown | Uses instant cleanup/target selection then native continuation | Saved stage and cleanup must be represented, not inferred from one setter. |
| Player AutoPilot / FlyToAutoPilot | Shares TargetData/FlyToPath but uses player course requirements and existing targets | Not certified by AI fixtures; blanket application risks changing plotted-course ownership. |
| AIShipManager.GenerateTension | Includes direct police scan-target redirection; its patrol-waypoint branch explicitly resets NavData | Scan/contact selection and navigation intent are not interchangeable. |
| Inspection ShipRefueling | Already snapshots and rebases remaining route levels once across all eligible AI roles | Generalizing observers must not apply its refill twice. |

Native replenishment call sites include pilot creation/reuse, police respawn,
collision handling, station docking of the player, TakeFuel, PirateShakeDown and
debug actions. Many spawn/reuse paths have no active plan. TakeFuel is docked and
timed, and its constructor SyncFuel is another accounting boundary. The primitive
proof does not certify all these high-level flows or removal of their cleanup.

## Disposition by mechanism, rather than by file name

| Mechanism | Evidence-based disposition |
| --- | --- |
| Empty-hauler return and abandoned thrust | Already correctly generalized by role. Preserve general event repairs and compatibility recovery. |
| VORB prefix's extra empty-home shortcut | Concrete consolidation candidate: remove the duplicate call after preserving the full-sequence tests and accepting the bounded retry-tick difference. This need not remove the FlyTo guard hook that also validates plans. |
| Target-change observation | Consolidate duplicate key capture/comparison. For deploying haulers there is no demonstrated station-specific route-ownership rule. Broader AI coverage must include direct setters and callers that already reset/install plans, with player courses excluded until tested. |
| External fuel addition with active plan | General accounting defect; VORB-only ownership is not justified. Move accounting out of regional route validation. Keep API snapshot/idempotency and materialized/shallow boundaries explicit. |
| Finite/chronological plan validity | General data integrity concern. Separate it from arbitrary local return envelopes and establish the producer before expanding/replacing checks. Injected bad-data tests alone do not establish producer defects. |
| Negative predicted fuel | Feasibility plus budget-epoch problem; evaluate against the updated budget, not the pre-refill snapshot. Do not conflate it with NaN/invalid time. |
| 100 AU, 1e9-second, velocity envelopes | Local containment heuristics, not physical invariants. Do not copy to cargo/passenger/interplanetary navigation as universal limits. |
| Sticky manual fallback | Navigation strategy, not accounting. Its retry/reset rules should remain explicit and should not spread automatically with fuel rebasing. |
| VORB retrieval depot identity | Genuinely regional code/data mismatch; preserve native ownership/assignment behavior and narrow home-depot correction. Not an orbital-fuel distinction. |
| VORB anchoring condition | Genuinely asset-owned regional omission; retain the hook per user instruction. |

Thus neither “keep the whole VORB guard unchanged forever” nor “apply the whole
guard to all haulers” follows from the evidence. The shared primitives and local
fallback policy have different owners. This pass identifies separable work;
it does not implement a new controller or demand a product decision now.

## Graph and revalidation

The new deployer slice has 11 control points, 15 native / 23 effective edges and
14 conditional scenarios (59 checks). It shows **current** regional differences,
including still-erased refills outside VORB. Proposed generalized fixes are not
drawn as if implemented. Native movement outcomes and entry preconditions are
supplied facts; graph checks do not replace the native fixture.

It pins C# source fingerprints and native method/asset dependencies. The station
permission fact is supplied separately to isolate route/fuel scope; the graph
does not claim to simulate the anchoring compatibility hook or NewGame save
migration. Other declared gaps include complete SFF wrappers, materialized fuel,
combat scheduling and passenger/player plotting.

No production files, installed assets, DLL or running game state were changed
in this continuation. Tests, tool model selection and audit/reference documents
were extended. The prior priority repair remains separate work already built.
