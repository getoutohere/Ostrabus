# Ostranauts 1.1.0.0: compatibility audit and migration findings

For the later native content/source audit, including narrative access and the
player's named `patch` save, see [Content availability](CONTENT-AVAILABILITY-1.1.0.0.md).
It distinguishes current template versions from eligible ship-design pools.

Status: **source and offline compatibility pass complete for the pinned 1.1.0.0 assembly; no known compatibility blocker remains in the reviewed feature scope**.
The [per-feature ledger](FEATURE-COMPATIBILITY-1.1.0.0.md) accounts for the 114
catalog features at the compatibility checkpoint: 99 retained, 12 adapted and three added compatibility repairs.
The later third addition is the asset-only [CO2 scrubber audio regression](../MiscBugFixes/ScrubberAudio/README.md),
validated separately with 39 native checks and installer/custom-build controls.
Historical audit counts below precede that addition; its assets do not change
the previously audited plugin DLL or add hooks.
This is a code/native-execution verdict, not a guarantee of every Unity scene or
all possible game outcomes. The evidence limits remain explicit below.
The corrected save bridge passes live loading, native-field save persistence, a
two-hour SFF and normal AI resumption. The user confirms docking, rotation,
proximity adjustment and both log scrollers. The native pivot repair fixes course
module placement. The user subsequently requested wheel support alongside native
Shift-drag, so `course-controls` is restored with accumulator synchronization.
The layout inspector remains. The current candidate binds 297 patch classes,
including subsequent [SFF decision-gap repair](../Navigation/SFF-DECISION-GAP.md)
and [shared PASS world stepping/end-turn drain](../Orbital/PASS-TIME-SKIPS.md),
plus the separately requested [bounty preservation simulation policy](../Navigation/BOUNTY-PRESERVATION.md).
The 289-class receipts below remain historical compatibility evidence; the new
iterator hook also passes the full offline binding audit with zero failures or warnings.
No further one-off live validation/reload is required by this pass. Combined
wheel/Shift-drag is supported by native reproduction and source analysis; its
latest candidate has not been observed in the running scene. Release approval,
commit/tag creation and deployment remain separate. Intermediate retirement
decisions and candidate receipts below are historical; the closing section and
feature ledger give the final scope.

The later PASS integration checkpoint builds normal Debug DLL MVID
`5af23260-2b74-437f-a325-b1d5391237f3`, SHA-256
`74189892d94fc8d10f20dac4f1108ca3e54a3686ac69461f60101c047be569ec`.
Its actual bundled-Mono binding receipt is `obj/pass-final-bindings.json`:
291 classes, zero failures/warnings. The existing iterator class now targets
both GUI and PASS callers; the only additional class wraps the native pending
end-turn drain. Existing destination preservation also covers native instant-
moor coordinate refresh. Orbital 5,121, economy 1,494, route/recovery 689, towing
343,871 and reactor-clock 256 checks pass, alongside hull and custom-build tests.
Traffic/retriever graph validation passes 457/49 expectations and safety checks.
See [PASS integration](../Orbital/PASS-TIME-SKIPS.md) for evidence boundaries.
This checkpoint was built and tested offline; no deployment or save edit was made.

The verified 1.0.1.10 source checkpoint is `be8e282`, tagged `Ostrabus-5.1.0`. Compatibility
edits follow that checkpoint. No world-content migration has run. The user deployed the candidate
and loaded a test save; the agent's checks have not written to the installation
or saves. Audit/build receipts are under ignored `obj/Compatibility-1.1.0/`.
Initial findings below describe the pre-adaptation pass; current adaptations,
executed evidence and their boundaries follow them.

## Verified inputs

The preserved 1.0.1.10 installation was verified against the pre-update canonical
installation: 7,616 files, 1,597,576,350 bytes, every SHA-256 equal, no missing or
extra files/directories and no files changing during verification. Six selected
save snapshots were independently verified against their originals, 30 matching
files total. The old installation and both decompiled source trees remain intact.

The installed Steam build changed from 26497584 to 26685993 on `public_test`.
Main assembly SHA-256:

- Previous: `8058f7a07f93d5d44678aaf8d7f749050c0d9b1c9ed1c7419eb521ddcd2097e7`.
- Current: `a79a5081d9c94307a45fe1d0b55ff1aa1d0af6ea8dfce1f9acc5961cf260ef07`.

File-level comparisons identify 85 changed and nine added decompiled C# files
(1,264 unchanged), and 121 changed, 28 added and one removed data file
(498 unchanged). These are byte/content comparisons, not semantic classifications.
The data inventory includes non-JSON sources such as TSV conversation matrices.

## Initial patch compatibility

The unchanged current mod sources compile against 1.1.0.0 with zero warnings or
errors using an isolated candidate output. This is not behavioral validation.

The existing whole-plugin binding harness, using the new native assembly and
isolated candidate DLL, reports **283 classes bound, three failed, zero warnings**:

| Patch | Observed incompatibility |
| --- | --- |
| `Ostrabus.NavConsole.ProximityCollisionPatch` | Native collision-scan visibility pattern changed; its explicit shape guard rejects installation. |
| `Ostrabus.ShipNavigation.Collisions.HullCollisionScanPatch` | The shared collision integration rejects the same changed native pattern. |
| `Ostrabus.Venus.VorbWreckLootPatch` | `StarSystem.AddDerelict` now has a fourth `spawnATC` parameter, so the explicitly selected three-parameter target is absent. Initial environment-ring generation also has a separate new path that needs review. |

Binding success for the remaining classes does not establish correctness. The
traffic graph's assembly audit flags 65 source groups and 275 graph elements for
revalidation, including conservative transitive dependency changes. The accepted
1.0.1.10 baseline was not modified or replaced. Other graph slices and native
regression suites still require their own review.

Of 83 tracked source groups, 15 changed directly, 49 through tracked dependencies,
one is unresolved and 18 remain tracked-unchanged. The unresolved world group
references old `StarSystem.PostShipLoad`, `CreateStellarObject` and
`SpawnShipFromStellarObject` methods, requiring the model to follow the new
environment lifecycle rather than silently treating those transitions as covered.

New native `PrecisionSlider`/`CommandSliderFineAdjust` classes are an upstream
overlap candidate for our course-slider QoL. The new class implements modifier-held
precision dragging. Scene usage and the relationship to our mouse-wheel behavior
have not been established, so retirement is not yet justified.

## Existing-save content migration: confirmed mechanisms

The update introduces `SolarSystem.EnvironmentEntities`, `StellarShip` and
`StellarDerelict`. Environment objects can persist as lightweight stellar objects
and materialize into physical ships when a region loads. New save fields include
`JsonStarSystemSave.aStellarShips`, `aStellarDerelicts` and
`JsonBodyOrbitSave.nEnvironmentMapID`.

Native `StarSystem.Init(JsonStarSystemSave, JsonShip[])` restores the new stellar
arrays when present. If neither array is non-null, it calls
`EnvironmentEntities.SpawnEnvironments()`. Thus an old save can receive some new
content through an existing native fallback; a blanket missing-content injection
would be unsafe.

The fallback processes both:

- Saved regional stations, using their existing ShipCO `EnvironmentMapID`.
- Saved celestial bodies, but only when their new `EnvironmentMapID` is positive.

The new `NewGame` body definitions assign Earth=800, 1036 Ganymed=801 and Venus=802.
The older body/save classes have no such field. A streaming, read-only inspection
of the backed-up `option` save confirms 116 saved bodies, none with the new field,
and neither new stellar array present. Missing integer fields default to zero;
the loader takes the saved bodies rather than replacing them with `NewGame` body
definitions. Those body environments therefore are not covered by that fallback.
However, subsequent review found that `CrewSim`'s existing save-upgrade chain
explicitly handles Earth: `1.0.1.13_Earth_Upgrade` refreshes its atmospheric data,
sets map 800 when missing and calls `SpawnBOEnvironment`, gated by save version
and the native applied-patch list. Earth must not be classified as an unhandled
migration gap. Ganymed/Venus assignments still need separate review. This corrects
the first-pass report, which had examined initialization but not this later chain.

New environment-map JSON adds derelict rings to existing ATC maps, including HQCH,
MSUZ, JFTS and other regions. Those additions may be created automatically on the
first old-save load through `SpawnATCEnvironment`; they must not be unconditionally
created again by a migration. The Earth body map also contains evacuation platforms.

Named environment entities check both the physical-ship and stellar-object
registries for an existing registration before creation. Random derelict rings do
not have an equivalent population deduplication check. Re-running
`SpawnEnvironments()` is therefore not an idempotent migration.

If either stellar array is present, even an empty array, native initialization
skips the whole fallback. A save having passed through 1.1.0 is consequently not
proof that every new environment exists. Migration checks need to inspect actual
content and map metadata, not just the save version or array presence.

## Next review boundaries

1. Compare each new map and its contents with both physical hulls and deferred
   stellar objects in an existing save. Distinguish new locations from old
   Ganymed/Venus populations and preserve player-modified or depleted locations.
2. Produce a content-level dry run: automatically supplied, genuinely missing,
   conflicting/already represented, and unresolved. New suits/parts/ship designs
   also require acquisition-path review; fresh definitions do not prove that
   existing shops, items or loot containers are retroactively regenerated.
3. Develop native-backed tests for selective map assignment/content creation,
   reload persistence and repeated application before writing a migrator.
   Avoid respawning occupied stations or globally regenerating derelict rings.
4. The timing, collision, navigation, crew and economy compatibility review is
   now closed by the adaptations/native checks below and the per-feature ledger.
   The three initial binding failures were only its first integration blockers.

The initial review did not establish new-game content migration or live loading.
Subsequent loading/native checks are recorded below. Content migration remains
deferred; the compatibility verdict does not authorize regenerating world content.

## Upstream overlap with Ostrabus fixes

This is a source/asset comparison plus the explicitly listed native checks below.
The feature ledger records the completed retention decisions. **No entire
Ostrabus bugfix was confirmed redundant.** A temporary course-slider retirement was reversed after
the independent native placement defect was established. Wheel controls now
coexist with native precision adjustment. Other production patches remain.

| Area | New native behavior | Consequence for our fix |
| --- | --- | --- |
| Radio surrender/work stalls | `CondOwner.EndTurnSteps` clears `InSocialCombat` when no replies remain and no nondefault relationship has a loaded participant. | Partial symptom recovery. Radio `Comms.ReceiveMessage`, relevant condition/context effects and original state contamination remain. A loaded participant can keep the new guard blocked. Retain radio/on-foot state isolation, supported by source review and native reproduction/repair tests. |
| Regional environment preservation | `AIShipManager` delegates environment unloading to `EnvironmentEntities`, with player ownership, dock-chain, plot, visitation and crew protections. `DerelictCleanup` additionally checks for player crew aboard. | Overlap with our preservation objective. The separate local-controller `AIShipCleanup` player-connected destruction defect was not repaired by those edits; no corresponding hook is approved for removal. |
| Planetary wreck motion | New circular body-orbit locking, orbit-offset prediction, docking inheritance and orbital-relative relocking. | Our gravity/coast/SFF physical corrections already exclude locked objects. Native free-flight gravity and constant-acceleration integration retain their defects; no orbital hook is superseded. Detached target predictions were adapted to the new circular state. See the concrete boundary in `Orbital/README.md`. |
| RCS tank accounting | `Ship.GetRCSRemain`/`GetRCSMax` deduplicate tanks reachable through multiple distributor sockets. | A real upstream fix, but not the same defect as replenishment erasure or planned/manual double charging. Do not count this as retirement of our flight-fuel fixes. |
| Slider precision | Native modifier-held `PrecisionSlider` dragging. | User retains wheel increments alongside Shift-drag. Synchronize native precision's raw accumulator after a wheel assignment during a drag; retain native pointer ownership and clamping. Separate native placement repair is accepted live. |

### Defects confirmed to remain

The new-build, isolated `Tests/ShipSurrenderWorkCheck` default mode passes its
**69 unpatched condition/effect reproduction checks**. Its `combat-loop` mode
passes **32 unpatched acquire/drop-loop reproduction checks**. These are checks
that demonstrate the defect remains, not checks approving native behavior.

`Tests/NpcIdentityCheck` passes **eight unpatched identity-collision reproductions**
and **122 production repair checks** on 1.1.0.0. The changed `AIShip` source only
alters fresh hull spawn-region handling; `AddCrew`'s fresh profile/factory fallback
remains faulty. The native `PersonSpec` source is byte-identical.

`ConsoleToGUI` is byte-identical and `Tests/ConsoleBufferCheck` passes **3,058
baseline/repair/display checks** against the updated assembly. The console buffer
repair remains applicable.

Other byte-identical native sources include `FlyTo`, `SurrenderOffer`,
`AreThreatsNearby`, `Comms`, `MarketManager`, `ShipMarket` and `GUIFFWD`.
This supports retaining their demonstrated defects as open compatibility work;
it does not prove unchanged callers, assets or transitive dependencies.

## Adaptations and native evidence

The current candidate preserves authored selection, permissions, docking, damage,
crew and market policies. No patch has been retired on the basis of compilation,
binding or a similar-sounding upstream change. No save migrator is included.

| Boundary | Review and resulting change | Executed evidence |
| --- | --- | --- |
| Collision scan | Native now has separate ATC and obstacle visibility reads. Replace only the obstacle read; retain native double-precision distance arithmetic and its widened comparison. | Hull geometry/contact/docking suite: 1,194 checks, including both proximity-hook orders. Whole-plugin avoidance composition passes both installation orders. |
| Wreck deployment | `AddDerelict` gains `spawnATC`; the deploying pilot still calls it for both attempts. Adapt the explicit signature and retain that argument. Initial environment generation is a separate native mechanism. | Venus pool/scope/native-IL checks, plus native circular relocking and speed rejection: 28 checks. |
| Environment conversion | New `EnvironmentEntities` publishes a same-ID stellar object and retires its physical hull. Native leaves dangling physical references; the old Ostrabus observer incorrectly cancels the surviving destination. Scope that native teardown as conversion, using the existing replacement mechanism. Deliberate deletion still invalidates the old identity. | `AiPlannerDiagnosis traffic-conversion`: 324 lifecycle checks, including native, legacy-patch and adapted-patch controls; direct/copied goals, existing routes and authored removal. Presentation, asset creation and cleanup bookkeeping are declared fixture boundaries. |
| Circular target motion | Native adds rotating body offsets. Detached SFF/freight predictors previously froze those offsets. Snapshot native angular rate/epoch and include phase, tangential velocity and centripetal acceleration without advancing live caches. | Expanded orbital suite: 5,023 checks. Circular prediction cases compare with native `GetBOOffsetPrediction`, both directions, fixed offsets, route interpolation and freight sampling. |
| Torch/control/towing | Native planner gains an NPC argument and changes RCS plotting; torch reactor/control contracts remain applicable. Adapt reflective test calls, preserve the extra native argument, and keep our player scope. | Full native torch oracle: 402,668 assertions; separate towing suite: 343,871. Includes normal/SFF playback, independent clocks, physical fuel, previews, braced groups and reload. Assertion counts include repeated invariants. |
| Controller withdrawal | Inventory now has 32 direct calls in 27 methods. The extra call is the new RCS-loss abort in `NavData.TimeAdvance`, alongside reactor abort. Existing exact-controller cancellation remains valid. | Existing 410 caller, 84 continuation and 234 production checks pass. New RCS suite: 34 checks covering native exhaust ratios, fuel inversion, a native legacy-cache negative control, and actual intact/damaged playback with/without registered Auto controllers. The legacy control reproduces a migration concern, not a repair. Ordinary completion remains distinct. |
| Patrol/scav/civilian/freight | Role construction, reserve servicing, native handoff, priority and fuel accounting remain authoritative. New environment IDs are reset by native hauling/arrival. | Ceres/priority/traffic/visit/wait/region-cleanup modes pass; 1,524 regional cleanup checks, 187 controller-identity checks, 136 station-arrival checks and 3,622 retriever checks. Fuel/refill/SOS/return/retarget modes pass. |
| Radio/work | New native stale-social-context cleanup is partial. New feed-task promotion occurs before transfer; our completion observer addresses the later state. Keep the existing repairs. | Radio isolation: 3,362 checks; repaired surrender: 85; neutralization: 170; alternative work: 2,815; crew-rest and disembark suites pass. |
| Load optimizations | Native iterator and closure names change. Match compiler-generated owners through their enclosing methods, not numeric suffixes. Native restore-phase labels and saved-CO membership predicate remain applicable. | New `LoadCompatibilityCheck`: 118 checks using the actual native predicate closure and actual `StarSystem.Init` iterator. Preserves restored bodies/hierarchy/maps, progress order and non-null yields; new-game/disabled paths retain scheduling. |
| Save JSON experiment | New ship RCS/orbit fields and deferred-world arrays must survive the optional generic adapter. | Existing 16 integration checks and 34 equivalence cases in two cultures, plus four new native DTO cases in both cultures. Independent invariant-value and null-versus-empty checks prevent vacuous equivalence. This preserves native locale behavior; it is not a new locale correction. |
| Native asset overrides | Rebase the locked PDA and stock/editor records on 1.1. Four native mods now declare 1.1.0.0. | 76 definitions inspected: 55 additions and 21 native overrides. The overrides differ only in the intended PDA action or stock/editor additions. PDA native loader/menu gating: 176 checks. |

Additional native suites pass for mass, containers/barter/writeback, signal boxes,
sensors, outlines, PDC/micrometeoroids, console/log history, population, identity,
equipment, portraits, rotation and the remaining maintained mechanisms. Historical
`DockingDiagnosis` requires old ignored live captures that are absent; it is not
reported as a passing regression. Current hull/docking mechanisms have maintained
fixtures in `HullOutlinesCheck` and whole-plugin interop.

One rescue API assertion also failed on the untouched old commit with the old
assembly: it expected immediate target binding although the repair deliberately
persists intent until the ordinary mission executes. The corrected test executes
that mission and passes 19 checks; no rescue behavior was changed to satisfy it.

## Hook, graph and packaging receipts

Before the course-slider retirement, the updated binding harness reports **288 omnibus patch classes, zero failures,
zero fallback warnings**, including the save forward-compatibility bridge. Before
that bridge, the independent apartment-terminal combination bound 292 classes.
These counts are classes, not unique native methods or frame costs.

Comparing actual installed target sets with a separately compiled `be8e282`
baseline yields 380 comparison rows: 293 unchanged bodies, 61 changed bodies,
14 new signatures/targets and 12 superseded signatures. There are no unresolved
current targets. Renamed iterator/lambda owners, `InitShip`, `_SpawnShip`,
`PlanTrip`, `PlanTrip4Sub` and `AddDerelict` are explicitly accounted for. Four
Ostrabus-owned targets are compared against each report's actual plugin image.
Unchanged direct bodies are not transitive equivalence claims.

`Tests/PatchBindingCheck --report` records actual originals from Harmony's installed
table. `Tools/PatchInventory compare-hooks` compares normalized IL/contracts;
`evidence` maps those receipts through portable PDBs to source files, feature IDs
and candidate native suites. Suite associations are navigation aids, not proof
that every callback branch was exercised. Keep execution receipts and manual
source review alongside the matrix.

The receipt leaves ten classes without an associated native suite. They are not
silently counted as execution-tested:

| Classes | Evidence and execution boundary |
| --- | --- |
| `ConsoleInfoInspectionPatch` and four docking-transition observers | All bind. Console/mooring targets retain their bodies. Dock/undock additions match native orbit state; linkage transitions, finalizers, bounded recording and sync-only exclusion remain applicable by source review. Individual scene event counts were not all exercised. |
| `Patch` and its three bounty GUI classes | All seven selected native target bodies are unchanged. `PersonSpec`/relationship lookup and text/ID field contracts remain. Retention rests on source/target review; separate filter execution, complete GUI composition and every console command are not claimed as tested. No destructive social pruning was performed. |
| `ShipboardActionPatch` | Native `AIShipManager.Update` adds environment-queue processing while retaining its update/AI cycle. Source review confirms the clock-throttled physical-group snapshot, Shallow/player-connected gates and native return contracts remain applicable. Casualty outcomes are not claimed as newly execution-tested; an idle API response is not encounter coverage. |

All seven behavior slices have updated source/assembly references and pass their
conditional scenarios against the new source. Removed `StarSystem` materialization
selectors now point to `EnvironmentEntities`; target restoration is tracked at
`UpdateShip`/`CheckTargets`. Target-lifecycle scenarios include same-ID environment
conversion and its preservation repair. Previous accepted snapshots are retained.

The independent metadata inventory finds 13 concrete pilot roles and 70 concrete
commands. Previously unbound entrypoints in 40 types are now catalogued in
`Navigation/BehaviorGraph/native-entrypoints.json`, explicitly **not transition
expanded**. The minimum binding inventory has no missing entrypoints; semantic
completeness remains unestablished. Adding references is not adding modeled or
proved behavior.

All seven reviewed assembly baselines are now accepted, retaining their prior
history. Closing audits report zero changed source groups and zero graph elements
requiring revalidation; all seven model validations pass. The invalidation tool's
26 synthetic checks also pass. These are reviewed-reference results, not proof
that every native transition has been modeled or every scene interaction tested.

Custom builder: the initial compatibility pass completed 559 self-checks and 43
compiled/finalized/native-bound profiles, including individual features, all
features and presets, ordering, idempotence and PDA filtering. Adding the save
bridge raises self-checks to 562. Fresh forward-compatibility-only, Recommended
and all-feature builds pass compilation, finalization, idempotence, patch ordering
and native binding (1, 259 and 288 patch classes respectively, zero failures or
fallback warnings). Disposable installation fixtures pass 88 custom
and 56 independent-terminal checks. All 20 development-tool projects compile.
No canonical installation or shared save data was written by these checks.

## Live candidate observations (chronological)

The user deployed and loaded an existing save in 1.1.0.0. `/health` confirmed
plugin MVID `1de911ea-8ed0-4eff-b6ad-fd7e6d7878bc` and native MVID
`8130eff7-382e-4fff-97c5-67c9ade3a42a`, matching the offline binding receipt.
Startup reports successful Harmony installation. The saved-CO optimization
reports 418,074 indexed lookups and zero fallbacks for this load.

The paused baseline contains 305 physical ships. After normal time resumed,
all 85 sampled AI registrations have a live hull matching the registry, with
no null, destroyed or duplicate-controller entries. Fifteen cargo pilots are
executing `FlyTo`; local navy and civilians also resumed native commands.
These observations verify load/registration and command resumption, not completed
voyages or an exhaustive absence of traffic defects. The nav console and hull
worker respond; the first two published hulls report no capture/geometry errors.
Pointer input, detailed outline drawing and physical docking still need scene
acceptance. No repair, pruning or fuel intervention was used for this survey.

The deployed Settings, Quick PDA and Rotation asset files match the candidate;
the source-only `.gitkeep` placeholder is intentionally absent. The running
session's `Player.log` contains no managed exceptions at the sampled checkpoint.
Repeated missing maintenance-person-spec messages come from native
`AIShipManager.HandleMaint` probing optional specs: it requests `bSuppressLog`,
but native `DataHandler.GetPersonSpec` does not honor that flag. No suppression
or gameplay repair was added as part of this compatibility pass.

Offline Mono executes the shipped managed algorithms under explicitly stated
engine boundaries. It cannot prove Unity scene wiring, pointer routing, layout,
complete save loading or the whole PASS/SFF coroutine. The candidate still needs
a controlled game load and smoke test before a compatibility claim or release.
`GET /health` exposes the actually loaded plugin/game module IDs to distinguish
this candidate from the preserved baseline despite unchanged mod version metadata.

In particular, inspect settings and native-mod loading, physical contact outlines
and docking/proximity, course preview/engagement/control handoff, normal/SFF/PASS
traffic and cargo, and initial save/social UI behavior. Bounty/social display and
inspection-only event hooks also retain scene-dependent acceptance boundaries.

New lightweight `StellarShip`/`StellarDerelict` map glyphs use the native
`StellarObjectDraw` path. Existing sensor-position and exterior-outline patches
continue to act on physical `ShipDraw` contacts. They do not force new background
objects into physical simulation or invent signatures for those native glyphs.
Extending the model to that new representation would be a separate design change.

The initial fuel-harness constructor failures were fixture incompatibilities;
the required sixth `NavDataPoint` argument has since been supplied. The adapted
native fuel/refill/SOS/playback modes pass, as recorded above. Initial failing
receipts are retained rather than relabelled as passing runs.

### Save forward-compatibility bridge: legacy RCS caches

Native `JsonShip` now stores `nRCSCountRaw` and `nRCSExhaustV`; 1.0 saves do not.
In the shallow branch, native `Ship.InitShip` copies their zero defaults into
the runtime caches. Native `Ship.Maneuver` rejects an RCS maneuver when exhaust
is zero, whereas the maximum-acceleration calculation retains a legacy fallback.
Thus a legacy hull can appear capable to planning while refusing an RCS command.
The compatibility suite reproduces that native rejection and contrasts it with
the same hull supplied with valid new fields. The user's pre-SFF quicksave confirms
zero new caches on the two examined navy hulls, with weighted thrust six and twelve.

This is a native old-save upgrade concern, distinct from an Ostrabus regression.
Full materialization rebuilds propulsion from installed parts. The user explicitly
authorized a separate [save forward-compatibility feature](../SaveCompatibility/README.md)
for missing propulsion metadata; the larger world-content migrator remains deferred.
The bridge restores the two missing fields before native shallow loading copies
them, preserving existing thrust, fuel and combat losses. It uses native destroyed
part checks, asset-driven eligibility and a checked data projection, not a parallel
AI controller. Raw count is not assumed equal to weighted thrust.

The initial native harness passed 291 checks, including 256 actual-evaluator comparisons,
two actual native maneuver refusal/recovery pairs and preservation controls.
All 97 affected hulls in the external quicksave reconstruct; derivation took about
0.7 seconds total, excluding fixture parsing. Native shallow damage required a
separate homogeneous-package proof rather than weakening the consistency check.
The first bridge deployment matched the full binding receipt: plugin MVID
`3d4aa568-790b-4308-a82e-a519f06651fa`, SHA-256
`d259a6cae6e67b7009b33f71bac78f731a2ada96f43864e539281ca8cefbd22e`.
Debug and Release compiled without warnings. This was not sufficient load proof:
the subsequent native load of the untouched 1.0 save restored 12 hulls but rejected
168, including both navy hulls. Native `StarSystem.Init` calls `_SpawnShip(false)`
even for unvisited blueprint layouts. The prefix incorrectly used that argument
alone to suppress fallback to definitions; the initial archive derivation used
visit state and therefore missed the real prefix failure.

A new negative control reproduces that exact load argument failure. The corrected
prefix admits definition fallback for persisted `fLastVisit==0`, while retaining
saved-record requirements for previously materialized hulls. It does not use the
runtime visit field before native initialization copies it. The broadened harness
executes the actual prefix for every input with native `false, Shallow` arguments:
all 180 affected hulls in the untouched 1.0 save pass (472 checks), and all 97 in
the later quicksave pass (389 checks). Both preserve cached thrust and RNG state.
The original-save fixture also loads the Ostrabus rotation overlay so the player's
custom module resolves through native definition lookup. Derivation alone takes
about one second for the larger sample. Live acceptance of this correction is
pending a further reload; the first bridge's rejection is not reported as success.
The corrected Debug deployment image has plugin MVID
`2214da1d-dcde-45e7-b7d7-c431c3151ce9` and SHA-256
`23b9aad71f895f2417f886b8223cc2af1e56c532aa96bf3944157a61e666ca55`.
Debug/Release compilation and full 288-class binding pass without warnings.
Fresh bridge-only, Recommended and all-feature custom profiles also pass build,
finalization, ordering, idempotence and native binding after this correction.

The user deployed that corrected image and loaded the untouched 1.0 save, paused
at epoch `65634077393.0412`. Health confirms the expected plugin/native MVIDs.
Startup reports 180 restorations and no rejected reconstructions. Direct live
inspection captures all 180 affected hulls: 178 remain shallow and all have
positive raw count/exhaust with preserved weighted thrust; two materialize fully
under native loading. O-E3SE reports count/exhaust 6/6 and O-Y2L 12/12, with the
same fuel quantities observed in the failed-load control. No game-state repair
API was used. This establishes the load bridge, not complete traffic acceptance.

The new paused baseline contains 305 physical hulls and 83 controller entries
(15 cargo, 20 civilian, 48 navy). Cargo backing inventories and nine destination
markets are captured. Native aggregate freight stock caches are initially cold;
the backing actor inventories preserve the actual payloads, so aggregate zero
at this checkpoint is not interpreted as lost cargo. The bounded flight journal
is recording for a repeat two-hour SFF, including native destruction callers.

### Two-hour SFF checkpoint

The live SFF advanced exactly 7,200 seconds and reported one scheduled dispatch,
one new freighter carrying 22 media units from BCER to EJDR, 2,309 production
attempts, 1,170 successful cycles, and no economic errors or rejected flights.
All 15 pre-existing freight payloads remain unchanged; there were no completed
deliveries. O-Q6J finished an intermediate local leg and planned its interplanetary
continuation, so the earlier route end was not a promised delivery time.

AI registrations changed from 85 to 77: one new freighter, seven civilian hulls
surviving without their prior controllers, and two removed navy hulls. The current
registrations still have no duplicate/null/destroyed-hull references. No managed
exceptions were logged. This is not a clean traffic acceptance verdict:

- O-E3SE was about 15.8 km above Ceres in the quicksave. Detached coast reproduction
  predicts impact, and the live SFF report records a Ceres impact. Its inability to
  maneuver with the missing caches is independently reproduced; this does not prove
  that restoring propulsion can save an already dangerous orbit.
- O-Y2L's saved course stays clear of Ceres. Isolated native GUI-SFF playback also
  ends clear, and the nearest saved asteroid is about 54 km away. Its destruction
  caller remains unresolved; it is not attributed to the cache defect without proof.

The opt-in flight journal now retains calling stacks for native ship-destruction
messages through its existing temporary log listener, without an additional
Harmony hook or default logging. Its 61 capture/lifecycle checks pass. A repeat
from the saved baseline can distinguish native disposal from physical collision.

### Corrected bridge: repeated SFF and normal resumption

The repeat starts from the untouched 1.0 save at `65634077393.0412` and ends at
`65634084593.0412`, exactly two hours later. This start is earlier than the first
test's later quicksave, so the two runs are not an identical-state causal A/B test
of the earlier navy losses.

All 83 existing controller entries survive; two new registrations bring the
total to 85. There are no null/destroyed/stale hull references. O-E3SE and O-Y2L
both survive with their captains and restored propulsion. Native route playback
consumes their saved courses. After normal time resumes, both execute
`FlyToPatrol`; O-E3SE also has nonzero live RCS acceleration. Their fuel decreases
under native movement rather than being replenished by the bridge.

All 15 pre-existing cargo backing inventories are unchanged. A new freighter,
O-VLHA, departs for OKLG_FLOT with 72 life-support units. There are 2,288 attempted
production cycles, 1,170 successful cycles, no deliveries, no rejected flights and
no economic errors. All 16 cargo pilots resume `FlyTo` during normal time. This
verifies dispatch, cargo preservation and resumption, not a completed delivery.

The journal captures native destruction of O-EY2 through `StarSystem.Update`'s
`bRemove` branch. It already lacks a registered controller in the baseline; it
is not one of the tested navy losses. The caller stack distinguishes this native
removal from a collision without adding a destruction hook. Six C-prefix hulls
also disappear during normal resumption, before the second journal starts.
Their individual caller stacks were not captured; native environment retirement
is a candidate, not an established cause. They are distinct from the retained
AI registrations and are not counted as controller losses.

Both bounded journals stop and drain with zero dropped records, capture errors
or writer errors. Their ship/controller censuses are complete; body snapshots
hit the documented 128-body cap, including Ceres. The first journal also records
the native cloud-save size warning; it does not report a failed local save.
Read-only inspection of that completed quicksave finds 305 hulls, 180 with
positive weighted thrust, and all 180 with complete new raw/exhaust fields. Thus
the restoration persists using native save fields, without a custom format.

Live acceptance still excludes GUI input and actual docking, a completed freight
delivery, PASS travel, and shipboard-action resolution. An idle `/shipboard`
response is not promoted to encounter coverage. The previous later-quicksave
navy removals remain historical unresolved observations; the successful repeat
does not by itself establish their exact cause.

### Remaining DTO defaults and live nav acceptance

The changed JSON classes were reviewed separately from assembly binding:

- `JsonNavDataPoint.RCSCount` defaults to zero on old routes. Native capability-loss
  checks compare current thrust against that historical snapshot. Do not fabricate
  the old plotted hardware from today's installation: zero preserves the old
  route's behavior, and newly plotted routes record the native count. Expanded
  native RCS tests pass 46 checks, including old zero snapshots alongside intact
  and damaged current snapshots with/without a registered autopilot.
- `JsonShipSitu.fOrbitW/fOrbitTimeLast` default to zero, retaining a fixed body
  offset until native code establishes a circular lock. Existing circular-target
  tests cover zero-rate offsets separately from rotating ones. No orbit is invented
  merely because an old field is absent.
- Respiration threshold/charge-profile fields are native definition inputs. Zero
  retains the pump's default threshold; an empty charge-profile name is explicitly
  guarded. The new slot mesh-addition array also has a native null guard.
- Environment IDs, new stellar arrays and derelict-ring definition changes retain
  the migration boundaries described above; no blanket environment regeneration
  is included in compatibility loading.

The user confirms physical docking, rotation-module operation, adjustable
proximity controls and local/message log scrolling. These are now live evidence,
not inferred from harness results. Course-module placement fails: it disappears
from the edit palette without appearing on the panel. No corresponding managed
exception is logged. Native `Draggable`, `NavModWrapper`, `EditMenu` and
`NavModBase` bodies are unchanged; native `PrecisionSlider` is new. This does not
establish the wheel handler as the cause.

A request-only `GET /ui/nav-layout` reports current/inactive module containers,
anchors, scale, canvas groups, saved placement, palette state and actual slider
component types. It does not load prefabs, evaluate native fit, send input or
change layouts. Its private native field bindings initialize in the shipped Mono
binding harness. The diagnostic build compiles Debug/Release without warnings,
binds all 288 patch classes, and the minimal custom profile compiles/finalizes with
matching patch order. Deployment MVID is `8b0850fe-ae78-4248-a16e-40dbada09276`;
live reproduction with this diagnostic build is pending. No speculative
placement repair has been applied.

### Native precision controls: retired wheel implementation

The user chose native fine adjustment and explicitly retained layout inspection.
The `course-controls` feature, three decorated classes, attached wheel component,
transparent raycast graphics and obsolete `CoursePlotCheck` harness are removed.
The tagged old baseline retains their source/tests. This intentionally relinquishes
exact 0.01 G wheel increments and pre-plot readout augmentation; torch prediction,
fuel, momentum and towing fixes have no code dependency on the retired module.

Current Debug/Release builds compile without warnings. The shipped Mono binding
harness initializes the new layout inspector's private field bindings and reports
285 patch classes, zero failures/warnings. Custom-builder self-tests pass 561 checks;
fresh bridge-only, Recommended and all-feature profiles compile, finalize twice
idempotently, preserve patch ordering and bind 1, 256 and 285 classes respectively
without failures or fallback warnings. All 285 classes map to source/feature owners.
Saved custom profiles containing `course-controls` must drop that retired ID.

Current deployment MVID: `18a1ade9-d270-4bff-b41c-8a6ee3fc08ae`; SHA-256:
`d3426ba9998237017d313f176335a9b4abf6ce806b03d913eb16261007a66132`.
The running game still has the earlier bridge build until user deployment/reload.
Repeat module placement with `/ui/nav-layout` available to establish whether
retirement fixes it; do not infer the result from successful offline builds.

### Native placement defect and repair

Retiring wheel controls does not fix the placement failure. Live inspection on
MVID `18a1ade9-d270-4bff-b41c-8a6ee3fc08ae` confirms both course sliders are native
`PrecisionSlider`s and have no wheel handlers. The course panel becomes active
and enabled but ends at Y anchors approximately `1.02` to `1.42`; its parent
origin is half a panel-height below the centered module origins. It is positioned
offscreen rather than hidden or rejected by a wheel handler.

Native `Ostranauts.ShipGUIs.NavStation.Draggable.MoveRectTransformUsingAnchors()`
adds `0.5` to both normalized local axes. That is correct only for centered
parent pivots. Two conversions during initial palette dragging reproduce the
full panel-height displacement with a bottom-aligned parent. The native module
JSON exposes prefab/default anchors, not the parent's pivot or this conversion.
`ModulePlacementPatch` replaces only four center constants with actual parent
pivot reads, preserving native projection, snap, dimensions, bounds/overlap
rejection and persistence. It is a generalized coordinate bugfix, not a forced
layout outcome. See [the method audit](../NavConsole/MODULE-PLACEMENT.md).

`NavModulePlacementCheck` executes the shipped private method before/after the
production transpiler. Its engine boundaries supply projection and rectangle
properties. The native negative control reproduces the captured `1.02`/`1.42`
bounds; 727 checks cover centered-layout preservation, six pivots, three scales,
repeated conversion, outside bounds, native early returns and IL drift guards.

Current Debug/Release builds compile without warnings. All 286 patch classes
bind without failures or fallback warnings. Custom-builder self-tests pass 563
checks; bridge-only, placement-only, Recommended and all-feature profiles build,
finalize idempotently, retain order and bind 1, 1, 257 and 286 classes respectively.
All 286 classes map to source/feature ownership. Relative to the frozen old build,
the three removed classes are the wheel hooks; the three added classes are the
environment-conversion target observer, legacy RCS loader and module placement
repair. The native layout inspector remains request-only and now includes pivots.

Prepared deployment MVID: `c7a9a9a0-bcf4-44fd-aaa2-31a25c20161c`; SHA-256:
`db8fda95ab4d192502e9bc44bb34dc28b17e4cc3f7a4c2581eafce3a18eea78e`.
Live placement acceptance is pending. No deployment, save modification or pointer
intervention was performed by the agent.

### Placement accepted; wheel/Shift-drag coexistence restored

The user reports placement fixed on MVID `c7a9a9a0-bcf4-44fd-aaa2-31a25c20161c`.
The read-only scene capture confirms the course module active/enabled, its parent
pivot exactly `(0.5, 0)`, anchors approximately `(0,0)` to `(0.25,0.4)`, and native
fit accepted. This directly confirms the pivot inference from earlier geometry.
The other modules' centered layouts retain their native behavior.

The user then requests mouse-wheel support alongside native Shift-drag and keeps
the layout inspector. `course-controls` and its original three hooks are restored;
there is no retired feature ID to remove from saved profiles. The component still
handles scroll/hover, leaving native pointer down/drag/up authoritative. Its value
assignment now updates `PrecisionSlider._rawValue` only while native precision
mode is engaged, using the actual clamped slider value. Without this, the next
native precision drag can revert a wheel step to its stale unsnapped baseline.
The synchronization changes no precision-mode or pointer-ownership flags.

`CoursePlotCheck` passes 12,255 checks, including the stale-accumulator negative
control and actual native precision-drag execution above declared engine/storage
boundaries. Cases cover both drag directions, endpoint clamping, inactive precision,
native pointer-down suppression and ordinary sliders. All 289 classes bind with
zero failures/fallback warnings; both inspectors' private field contracts initialize
in that harness. Debug/Release compile without warnings. Custom-builder self-tests
pass 565 checks. Fresh bridge-only, placement-only, wheel-only, Recommended and
all-feature profiles build, finalize idempotently, preserve order and bind
1, 1, 3, 260 and 289 classes respectively. All classes map to source/feature owners.

Prepared deployment MVID: `8ade39e3-e426-4fdd-a3b5-56bac8c59255`; SHA-256:
`c6550e128ce67bca2fed6fa7c0dd62363efb72f1e775d7c4c3eb176b2a8c189d`.
Combined pointer/wheel/Shift behavior has not been observed in the running scene.
The code contract and native negative/repair reproductions support its acceptance;
this is not a request or prerequisite for another one-off reload.

The metadata audit tool also now reports entire removed classes, rather than only
missing targets within retained classes, and rejects receipts whose assembly bytes
no longer match. Four synthetic controls pass: unchanged, whole-class removal,
target removal and stale hash. The frozen old DLL was actually rebound against
the preserved old native assembly to emit a fresh hashed receipt (286 classes,
zero failures/warnings); original receipts remain. The current comparison has
381 rows: 294 unchanged native bodies, 61 changed, 14 new signatures/targets and
12 superseded signatures, with zero unresolved current targets. There are currently
no retired classes; three new classes account for the increase from 286 to 289.

## Closing source/native verdict

The user explicitly rejected repeated one-off live validation. Remaining
decisions were therefore closed using versioned native/source/asset comparisons,
changed-caller review and offline native execution, with engine boundaries
reported instead of converted into player test assignments. The
[feature ledger](FEATURE-COMPATIBILITY-1.1.0.0.md) has exactly one decision for
each of the 113 catalog IDs, including features without decorated patch classes.
Dynamic sensor/EM/trace installation has separate runtime-harness coverage.

The final changed-caller review confirms:

- Native `Ship.AddCO`/`RemoveCO` additions maintain raw RCS count/exhaust; they do
  not fix base-typed barter removal, mass propagation or electrical wake logic.
- `Slots.UnSlotItem` can now redirect to the actual slot owner. Nested transfer
  depth still restores correctly and does not synthesize another mass delta.
- `Ship.Dock`/`Undock` add native orbit matching and orbital-relative relocking.
  VORB lookup correction feeds that native method without bypassing its speed
  rejection; collision, crew and dolly hooks keep their separate scopes.
- Native `WorkManager` promotes dependent work before a feed transfer completes.
  Our later completion observer is still needed. Carried-material scans, aliased
  autotask scratch lists and chair reply loops remain in the native code. Coroutine
  scopes restore before yielding; no worker-thread AI policy is introduced.
- The new environment lifecycle needs same-ID conversion scoping, not deletion
  or forced retargeting. Both native-preserved destinations and authored removals
  pass their contrast fixtures.

A final selective closure rebuild/run records **15 successful native invocations**
with explicit exit codes and native/harness hashes in
`obj/Compatibility-1.1.0/closing-native-results.json`. This closes the first-wave
asset/fixture mismatches and rechecks affected integrations rather than repeating
every previously passing stress assertion. It includes orbital 5,023, rotation
337,520, placement 727, wheel/precision 12,255, legacy RCS default 281, native new
RCS capability/budget 46, load closure/iterator 118 and conversion lifecycle 324
checks. Original 180-hull/472-check archive evidence remains separately recorded.
The initial mistyped RCS mode ran the default diagnosis; its log is retained but
not counted as RCS coverage. The corrected `rcs-1.1` run supplies that coverage.

Earlier failed receipts remain as history. Historical `DockingDiagnosis` still
needs missing private captures and is not counted as passing. The current
composed docking tests and unchanged rounding contract support retaining that
repair. Complete Unity rendering/GUI event routing and boarding casualty outcomes
are declared source-reviewed boundaries, not invented execution results. The
earlier O-Y2L removal caller remains an unresolved historical observation, not a
proven compatibility regression; the repeat has a different start and both navy
crews survive it.

The final prepared DLL remains MVID `8ade39e3-e426-4fdd-a3b5-56bac8c59255`,
SHA-256 `c6550e128ce67bca2fed6fa7c0dd62363efb72f1e775d7c4c3eb176b2a8c189d`.
No game installation, save, deployment, commit, tag or push was performed in this
closing pass. At that checkpoint world-content migration remained unimplemented.
Subsequent [independent migration findings](../StandaloneMods/SaveMigration110/FINDINGS.md)
compare the legacy continuation with a fresh 1.1 reference. Native loading already
supplies the new regional content. A subsequently built metadata-only plugin
does not enable content or improve existing wreck handling; it is unnecessary
for an already-upgraded save and its recommendation has been withdrawn.

Subsequent 2026-11-16 simulation-policy checkpoint: `bounty-preservation` adds
three decorated classes / five native administrative targets. The normal Debug
DLL MVID is `80740b1c-c0b2-4f1f-8766-e258d5d581de`, SHA-256
`14ccd83826e552378102a04936af33d90bb16ad2dc642065aca48efabb886fff`.
`obj/HjprAudit/full-bindings.json` records 294 bound classes, zero failures and
warnings. The new native suite passes 174 checks; existing bounty departure and
stale lifecycle suites pass 166 / 42 checks. Standalone and mixed custom profiles
bind 3 / 35 classes without errors. The expanded bounty graph passes 126
expectations/safety checks and tracks the added native disposal dependencies.
These are offline results; this checkpoint makes no post-deployment claim.

The subsequent passive-attachment review builds normal Debug DLL MVID
`9c07d1f2-3d92-4bca-a15d-f6114b433372`, SHA-256
`af2c501859b147f6ad9ba622c4a23caa689b15a0cbd635fcfee046bef291d5e1`.
`obj/AttachmentReview/full-bindings.json` records 297 bound classes, zero
failures/warnings. The [independent review](../Navigation/DOCKED-ORBIT-LOCK.md#independent-collateral-review)
passes 449 attachment checks and relevant existing regressions. It tightens
permanent-station identity guards and adds the custom towing dependency on the
existing completion repair; neither correction adds a hook. No deployment/live
acceptance is claimed.
