# Executive prototype: Ostranauts 1.1 rework

Study disposition: **abandoned on 2026-11-19**. See the [postmortem](../POSTMORTEM.md).
This document preserves historical qualification, not an active adoption plan.

Status: offline rework qualified against **1.1.0.0**, 2026-11-16. The combined
runner passes **529 assertions across 101 scenarios**, exercising all 27 declared
model transitions. No production patch, asset, configuration or deployment was
changed. The normative mission model and game-independent reducer are unchanged.

## Baselines and source evidence

The current [test receipt](test-results.json), [conformance receipt](conformance-results.json)
and [authored-input inventory](authored-inputs.json) bind native SHA-256
`a79a5081d9c94307a45fe1d0b55ff1aa1d0af6ea8dfce1f9acc5961cf260ef07`.
The runtime binding is `native-navy-1.1.0.0-prototype-v2`, including the native
and retained-plugin MVIDs. Native executive records now use schema 2. Native
assembly hash checks remain enforced. Stale bindings are refused before world
reconstruction; no automatic checkpoint migration was introduced.

The [1.0.1.10 baseline](Baselines/1.0.1.10/test-results.json) retains its original
475 assertions / 90 scenarios and old native/plugin fingerprints. These are
historical results, not evidence that an old checkpoint can load under 1.1.

Inputs reused from the existing change audit:

- [Compatibility investigation](../../../Build/COMPATIBILITY-1.1.0.0.md).
- [Per-feature decisions](../../../Build/FEATURE-COMPATIBILITY-1.1.0.0.md).
- [Content and native initialization](../../../Build/CONTENT-AVAILABILITY-1.1.0.0.md).
- [Decision-aware SFF stepping](../../SFF-DECISION-GAP.md) and
  [shared GUI/PASS advancement](../../../Orbital/PASS-TIME-SKIPS.md).
- [Circular docking/locking](../../DOCKED-ORBIT-LOCK.md) and
  [legacy propulsion metadata](../../../SaveCompatibility/README.md).

Independent old/new decompiled-file comparisons found identical `NavyPilot`,
`BaseCommand`, `FlyTo`, `TargetNearestStation`, `TargetData`, `TakeFuel`, `Hold`,
`ComeHere`, `Dodge`, `SurrenderConsiderPlayerDemand`, `Comms`, `JsonGameSave` and
`SavingJob` source. Their world, physics, asset and serialization dependencies
still required the adaptations and executable checks below.

## Implemented changes and tested boundaries

| Area | Rework and evidence | Boundary that remains |
| --- | --- | --- |
| Logical identity | Resolve native `TargetData` through physical and deferred registries. A target can retain its ID/mission through physical removal, checkpoint reload and materialization with a new physical reference. Actual deletion returns to policy. A deferred actor cannot be silently adopted or certified as arrived. | Representation publication/materialization is an explicit scene provider in the executive tests. The separate 324-check native lifecycle suite executes the production conversion repair; a live executive across actual Unity region loading is not qualified. |
| Propulsion | Use the actual weighted RCS property and nonzero capability snapshots instead of a constant getter/zero constructor accommodation. Fixture save/load carries raw count, weighted thrust, exhaust metadata and native route snapshots. Native damaged player-autopilot playback withdraws only the proper controller and preserves fuel debit/unrelated AI; legacy zero snapshots remain unknown. | Route geometry and physical storage still have providers. The native 46-check propulsion suite and 281-check legacy reconstruction suite were rerun separately. No new AI damage/fuel policy was invented. |
| Shared time ownership | Install current production `SffAdvance`, PASS pending-turn adapter and route-completion repair in the offline process. Remove the prototype PASS scope opener and jump splitter. Keep the native/production world call; cut only the scene tail. Per-segment admission/observation never recursively advances the world. | Native normal/SFF/PASS updates and queue dispatch execute, but scene effects, human work populations and actual Unity collision history remain outside the fixtures. |
| Decision cadence | A real production preview detects an impending impact without changing clock, fuel, route or policy. Only a capable registered actor is admitted for an earlier decision. The capable fixture runs actual shared SFF/native dispatch with its ordinary outward patrol target. | This qualifies an intervening decision opportunity, not every random target or a forced survival outcome. Uncontrolled impact resolution is not simulated by the executive. |
| Circular references | Preserve native angular rate/epoch in DTOs and checkpoints; allow circular targets through native `UpdateTime`. Native quarter-orbit prediction, tangential velocity, docking-phase inheritance and DTO reconstruction execute. | Body ephemerides are controlled fixture inputs. Full materialized moving-station rendezvous and towing remain production gates. |
| Snapshot completeness | Include deferred ship/derelict arrays and hardware/orbit state. Reject duplicate physical/deferred IDs, omission and population changes during capture. Native threaded ZIP writing and raw-wire validation execute. | Physical ship/crew serialization and final native scene initialization still have providers. No general migration or mod-removal guarantee follows. |

The historical unmanaged PASS completion control required a source-informed
change: 1.1 explicitly stops player RCS during playback, so that old player-RCS
setup no longer reproduces the defect. An unrelated AI route still demonstrates
about **89.76 m/s** excess speed without the completion repair. With the repair,
excess speed is below `1e-5 m/s`. Both variants execute one native playback in one
600-second ordinary world step. Shared GUI/PASS integration separately tests the
repair with actual jump segmentation.

Two-day PASS now uses production's eight six-hour windows, retains 48 test
production units and 24 scheduled dispatch checks. Short windows are also owned
by the proper production service, changing some old playback-count expectations
without weakening fuel, manifest or market-conservation assertions. Jump entry/
exit invalidation and real empty scheduler draining are explicitly checked.

## Native serialization distinction

`LitJson.JsonMapper.WriteValue` omits empty array/list/dictionary properties;
its reader also normalizes some empty collections to null. This is significant
for 1.1's stellar arrays: native `StarSystem.Init` can generate environments when
both arrays are absent, while explicit empty arrays preserve an empty population.

The existing bounded executive record now carries two empty-array bits, included
in its snapshot digest. The archive reader reconstructs DTO shape from explicit
wire arrays or that same archive's record. Absence, emptiness and populated
arrays have actual native archive round-trip cases. Changing the shape bits or
mixing native/executive generations fails validation before world mutation.
This is an executive checkpoint codec adaptation, not a global serializer patch.
Native-only archives receive no invented shape metadata; safe native continuation
of every possible world and arbitrary mod removal remain unqualified.

The rework also removed a fixture detour of the generic `MonoSingleton<T>` getter:
Mono sharing could contaminate later singleton reads. Fixture data now uses the
actual closed-type static field with restoration. This is a harness repair only.

## Authored policy

Compared preserved/current shipped JSON and the named-definition diff. All
**13 direct CMD alias records are unchanged**, including their arguments. Core
ship-to-ship interactions and ship specifications match the earlier inventory.
Generated surrender interactions add explicit zero/false defaults, and triggers
add false `bNOT`. Fifteen AI-demand/AI-offer/player-offer loot definitions correct
`StatAltuism` to `StatAltruism`, a real authored-effect correction. The current
inventory was regenerated from shipped assets; selected native dialogue/effect
cases were rerun, rather than copying the old asset snapshot or outcomes.

No replacement policy tree, speculative asset correction or forced destination
was added. The descriptive native behavior graph was not modified. The content
audit provides no demonstrated reason for the executive to regenerate this
continuation's world or inject missing designs.

## Reproduction and remaining adoption gates

Run `Tools/ShipExecutiveStudy/Prototype/Run.ps1` from the repository root with a
current full Debug plugin. It builds only the study/Mono host and refuses an
unexpected native assembly. Scratch outputs remain under ignored `obj/`.

Additional native regressions rerun through the bundled Mono host:

- `AiPlannerDiagnosis traffic-conversion`: 324 checks, behind its documented
  presentation/materialization providers; excluded engine-call resolution
  warnings are emitted by this existing harness.
- `AiPlannerDiagnosis rcs-1.1`: 46 propulsion/budget/withdrawal checks.
- `SaveForwardCompatibilityCheck`: 281 legacy reconstruction checks.

The earlier integration gates are addressed within this bounded offline profile.
Remaining runtime gates include actual Unity load/scene lifetimes, materialized
fuel/reactors/weapons/tow groups, populated human end-turn work, active Ceres,
multiple adopted actors and broad native handback/uninstall. No production hook
is removable or performance improvement established by these results.