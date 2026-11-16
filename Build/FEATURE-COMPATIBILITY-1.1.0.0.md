# Feature compatibility decisions: Ostranauts 1.1.0.0

This is the source/native-execution verdict for the feature catalog in
`CustomBuilder/features.json`, against native assembly SHA-256
`a79a5081d9c94307a45fe1d0b55ff1aa1d0af6ea8dfce1f9acc5961cf260ef07`.
The [audit narrative](COMPATIBILITY-1.1.0.0.md) preserves the reproductions,
adaptation history, execution counts, live observations and their limits.

Subsequent simulation work adds [decision-aware SFF stepping](../Navigation/SFF-DECISION-GAP.md)
and the [shared PASS adapter](../Orbital/PASS-TIME-SKIPS.md)
to the existing `sff` feature. The subsequent independent
[`bounty-preservation`](../Navigation/BOUNTY-PRESERVATION.md) simulation feature
adds actor/original-hull lifecycle policy beyond the 114-feature compatibility
checkpoint below. Passive attachment adds three further classes; the current
full binding audit has 297 classes;
the 289-class DLL identities and receipts below describe the earlier compatibility
candidate and are preserved as evidence for that candidate.

`Retain` means the reviewed contract and intended intervention still apply;
`Adapt` means production code or shipped native assets were changed for 1.1;
`Add` identifies a newly demonstrated compatibility defect. These are not claims
that every Unity scene, native outcome or possible mod combination was exercised.
No existing feature is retired. Existing bugfix/QoL/simulation classifications and
defaults remain those in the catalog; a compatibility adaptation does not change
their design category.

The decisions use the old/new C# and asset inventories, actual installed Harmony
targets, changed callers and the named offline suites. Merely associating a test
project with a source file is not effect coverage. Named native suites execute
the relevant mechanisms above their documented engine/fixture boundaries. The
ten decorated classes without an associated native suite are addressed explicitly
below. Dynamic sensor/EM and temporary tracing hooks are outside the 289-class
decorated inventory and have their own runtime-harness checks.

## AI control, identity and return paths

| Feature ID | Decision | Contract and supporting evidence |
| --- | --- | --- |
| `ai-combat-handoff` | Retain | Native patrol/combat cleanup still needs admission before ordinary patrol resumes; `PatrolTargetHandoffCheck`, `BlockadeTargetHandoffCheck` and planner priority modes preserve urgent/native branches. |
| `ai-neutralized-targets` | Retain | Native reacquisition still admits a neutralized controller reference; surrender and patrol/blockade native matrices retain reactivation and mixed-fight controls. |
| `radio-surrender` | Retain | New `CondOwner.EndTurnSteps` cleanup is conditional on loaded social partners and does not isolate radio effects. Native `Comms` and relevant effects still reproduce contamination; `ShipSurrenderWorkCheck` tests both isolation and on-foot preservation. |
| `ai-surrender-accounting` | Retain | Native `SurrenderOffer` still changes combat state on failed delivery. Native negative/repaired surrender-loop modes preserve refusal and successful-message behavior. |
| `ai-flight-controls` | Retain | New RCS-loss withdrawal is another legitimate controller-removal path. Exact-controller/continuation audits and native intact/damaged playback distinguish loss/abort from completion; RCS commands still release residual torch output. |
| `route-completion` | Retain | Native route completion still returns false before the outer integration, leaving the replay hazard. `AiRouteCompletionCheck`, orbital and towing tests preserve partial intervals and native capability aborts. |
| `ai-patrol-priority` | Retain | Native Navy priority/tree and queue mechanisms are retained. Planner patrol/interaction modes exercise urgent interruption, cleanup, ordinary resumption and queue persistence. |
| `flight-fuel` | Retain | Native planned playback and manual maneuver paths still overlap accounting. Scav fuel controls/queues/scope tests preserve planned fuel debit and real manual consumption. |
| `rcs-refill` | Retain | Upstream tank deduplication does not preserve an added refill in an existing route budget. Native replenish and retarget modes verify exactly that boundary. |
| `navigation-targets` | Adapt | Scope the new `EnvironmentEntities.UnloadEnvironmentForRegion` physical teardown as same-ID conversion. Native lifecycle tests preserve surviving stellar goals/routes while deliberate deletion still invalidates them; native new exhaust-law budget inversion also passes. |
| `ai-crew-identities` | Retain | Native `AIShip.AddCrew` still has the fresh-profile collision fallback; `PersonSpec` is unchanged. `NpcIdentityCheck` reproduces identity theft and verifies dictionary-based repair without altering native reuse. |
| `population-gender` | Retain | The native random-person construction contract remains; `PopulationCheck` verifies the intended 1%/49.5%/49.5% policy, explicit authored genders and RNG consumption. This remains a simulation policy. |
| `ai-crew-integrity` | Retain | New environment-unload protections do not repair separate local-controller cleanup or docked-group crew ownership. Lifecycle, controller-identity, region-cleanup and stale-timer tests retain crew/hull identity and legitimate retirement. |
| `ai-return` | Retain | Ordinary-mission integration still yields to queued, threat and combat priorities. Planner return/priority cases preserve native navigation and station arrival rather than introducing a second controller. |
| `hauler-return` | Retain | Native `HaulShip`/`AnchorDockedShip` now clear environment metadata, but return/target ordering and failed-plan gaps remain. Retriever, general-hauler and fuel/return fixtures preserve the new native statements. |
| `hauler-sff` | Retain | Native hauling stages still need persistence through catchup. Retriever tests exercise both unpatched failure and repaired stage continuation without forcing delivery. |
| `scav-assistance` | Retain | Current authored assistance triggers/pledges remain authoritative; the failed attempt/cooldown and target/controller mechanisms still need repair. Native SOS, recovery, scope and refuel modes cover the chain. |
| `ai-hold-clock` | Retain | Native hold/SOS comparisons retain the deadline/expiry defects. Native wait/sequence and shallow-cooldown cases retain authored durations and failure behavior. |
| `scav-return` | Retain | Failed job selection still needs to reach the existing feasible station return. Scav return/retarget tests retain native emergency/help selection and route feasibility. |
| `passive-docking-attachment` | Added after compatibility audit | Preserve authored body locks during passive visits; release for applied control or an authorized course. Three native targets plus existing towing integration. Production-DLL native hauler, paused-haul save/group restoration, SFF/PASS continuation and exclusion tests pass; no blanket gravity-policy change. |
| `orbital-waiting` | Retain; trajectory-safety review open | Existing exclusion tests preserve mooring/locking, but do not establish arbitrary free Hold safety. A constructed native-accepted Hold falls toward Mars when gravity is restored. This feature was not expanded or changed by the passive-attachment repair. See the attachment investigation for scope. |
| `local-navigation-frames` | Retain | The changed tutorial/authority callers still generate local offsets. Native patrol/ambush and six-hour frame tests retain regional motion while preserving planetary controls. |
| `departure-waypoints` | Retain | New RCS plotting still uses the same station/body departure construction. Native hierarchy/waypoint checks retain authored destinations and incoming RCS capability. |
| `escape-targets` | Retain | Native target replacement still leaves an old escape course valid unless invalidated. Retarget/flight tests preserve same-target continuation and mission intent. |
| `ai-avoidance` | Retain | Native collision scan improvements do not fix the past-encounter or unsafe planetary dodge choices. `PlanetaryNavigationCheck` and whole-plugin collision-order checks preserve native station and nonhazard behavior. |
| `bounty-departure-fuel` | Retain | Changed bounty spawning still reaches the native departure activation. `BountyDepartureCheck` covers activation, low-fuel branches, assets and scope; station servicing does not teleport the target. |
| `bounty-escort` | Retain | Current roster/airlock assets still deny manually escorted arrested targets. Native permission/tile/expiry checks retain the narrow arrested/manual exception. |

## Traffic, environment and encounters

| Feature ID | Decision | Contract and supporting evidence |
| --- | --- | --- |
| `faction-encounters` | Retain | Current beat assets and native staging still permit a remote player-relative encounter. `FactionEncounterCheck` retains local geometry, eligibility, existing-ship preservation and caps. |
| `civilian-visits` | Retain | New `DockAndDespawn`/stellar conversion does not fix hidden targets or detached attachment retirement by itself. Traffic/visit/wait/conversion tests preserve valid visits and native home returns. |
| `civilian-asteroid-visits` | Retain | Native visit termination still has the shallow 1 kg obstruction; native tests preserve 5-minute-to-2-hour asteroid visits and permit another visit. Ship-to-ship visits retain their separate timing. |
| `civilian-sff-coordinates` | Retain | Native civilian selection still passes the wrong coordinate; native traffic tests retain both axes and station choice. |
| `ceres-navigation` | Retain | Native reserve checks still require the pilot-efficiency/finite-budget correction and reachable homeward framing. Ceres chain/priority modes preserve aggressive combat decisions and native role policy. |
| `boarding` | Retain | `AIShipManager.Update` adds environment-queue processing but retains its native update/AI processing cycle. The postfix's clock-throttled physical-group snapshot, Shallow/player-attachment gates and native return commands retain their contracts. No new native boarding resolver supersedes this intentional simulation policy. Casualty outcomes have source review, not a new end-to-end Unity encounter test. |
| `venus-anchors` | Retain | Native `Undock` now calls `TryRelockToBO`, but still excludes the placeholder needed by eligible VORB wrecks. The existing lookup-only patch feeds the new native relocking method; Venus tests cover circular orbital-relative speed rejection. |
| `venus-depot` | Retain | Current ownership/depot assets and native owner selection still mismatch for the VORB retriever. `VorbRetrievalCheck` verifies the scoped depot correction and unchanged OKLG behavior. Existing-save handling remains a hook as previously decided. |
| `venus-navigation` | Retain | Native new playback capability checks do not replace the invalid target/fuel-plan guard. VORB hold/fuel/retarget modes retain native return selection. |
| `venus-wreck-variety` | Adapt | Bind the four-argument `AddDerelict`, preserving `spawnATC`. Both deploying-pilot attempts still use it. Venus native-IL/scope tests retain the configured deployed-wreck share; new background environment rings keep their native pools. |

## Crew, work and item lifetime

| Feature ID | Decision | Contract and supporting evidence |
| --- | --- | --- |
| `crew-sleep` | Retain | Native bed assets still admit improvised container/EVA targets. `CrewRestCheck` executes native choices and deferred searches; iterator-local scopes restore before yield/disposal and manual choices remain native. |
| `crew-idle-work` | Retain | Native `Interaction.GetReply` and authored chair renewal still bypass ordinary work selection. Crew-rest native cases retain manual/secured/nav sitting, roster/duty ranking and the bounded restoration probe. |
| `work-adjacency` | Retain | Native path speed changes do not repair adjacent wall-work destination selection. `WallWorkDiagnosis` executes the work-range correction with native path decisions. |
| `work-scratch` | Retain | `CondOwner.ProcessAutoTasks` still aliases its three scratch buffers. Crew-rest native progress/exception tests preserve independent buffers and native dispatch. |
| `altwork` | Retain | Native `ValidateRequiredInputsDeferred` still traverses the ship before checking carried sufficiency. New feed promotion occurs before transfer; the later completion observer remains necessary. `AlternativeWorkCheck` covers selection, supply batching, carried inputs and per-iterator state restoration. |
| `disembark-search` | Retain | Native needs/leisure selection still evaluates unreachable other-ship targets for no-disembark crew. Both native selection branches pass; manual/emergency behavior and airlock policy stay native. |
| `trigger-classification` | Retain | Native `CondTrigger.CheckRequiresHumans` retains false-recursion and OR misclassification. Native evaluator truth tables and current asset searches verify conservative people-only metadata; general searches remain available for ambiguous/cyclic triggers. |
| `saved-inventory-parents` | Retain | Native `SpawnItems` still skips damaged parents before reconstructing their saved children. Inventory tests retain native destruction/mode-switch/drop recipes rather than inventing replacement contents. |
| `tile-cleanup` | Retain | Tile helper destruction and async ship cleanup retain the orphan/identity hazards. Native lifecycle tests cover physical destruction order, asynchronous cleanup and replacement-ID safety. |
| `mass-accounting` | Retain | Native RCS bookkeeping additions do not repair live/stack/container mass propagation. New `Slots.UnSlotItem` redirection remains inside nested transfer-depth scopes; no extra mass delta is synthesized. Mass/barter tests retain freeze restoration, native stack transfer and saved writeback. |
| `dolly-rolling` | Retain | Dock/undock gains native orbit matching, not an encumbrance correction. Venus dolly tests retain station-chain eligibility, contained load, late mass changes and the 0.10 hauling multiplier. |
| `door-autoclose` | Retain | Native recovery still fails to rearm the powered door condition; `DoorAutoCloseCheck` retains manual door behavior and ordinary delay. |
| `docked-gravity` | Retain | New orbital lock matching does not harmonize crew gravity through all connected ships. Native docked-gravity tests retain the connected-group calculation. |
| `crew-hotkeys` | Retain | Input is polled without Harmony hooks. New Shift-drag belongs to slider pointer handling and does not replace the configurable crew shortcut. Native equipment/permission tests retain oxygen safety, failed-slot rollback and normal equipment effects. |
| `auto-vents` | Retain | Native pump/respiration changes do not replace the scoped Kang pressure-dip policy. `AutoVentCheck` tests native integration, emergencies and settings. |

## Barter, markets and freight

| Feature ID | Decision | Contract and supporting evidence |
| --- | --- | --- |
| `preview-market-isolation` | Retain | Native preview/cargo-market ownership still permits identity contamination. `CargoPodDiagnosis` reproduces separation/ghost cases and verifies repair scope. |
| `shallow-refuel` | Retain | Native shallow `RefuelRCS` still alters mass and misreports overflow. Scav refuel tests retain accepted/overflow quantities and actual new native capacity. |
| `remote-barter` | Retain | New base `Ship.RemoveCO` statements add RCS bookkeeping, not virtual dispatch. Base-typed container removals still bypass the subclass JSON; native barter persistence tests verify removal and save-back. |
| `barter-zones` | Retain | Native zone identity/list construction is unchanged; multi-ship tests retain callsign grouping/order and actual zone identities. This remains presentation QoL. |
| `async-ship-loading` | Retain | Native loader cancellation still needs per-ship ownership rather than global coroutine cancellation. Multi-ship tests preserve another ship's in-flight loader. |
| `barter-writeback` | Retain | Native shallow copy writeback still needs the repurchase/coordinate guards. Native multi-ship/inventory fixtures preserve actual manifested items and placement. |
| `barter-yields` | Retain | Native nested barter initialization still bypasses the outer frame budget. Native equivalence/budget tests preserve items, zone order and non-null yields. |
| `barter-index` | Retain | Native lookup/writeback still repeats linear scans. Native index/equivalence tests retain duplicate handling, mutation invalidation and saved results. |
| `economy-sff` | Adapt | Detached freight destinations now sample native rotating body offsets and their velocities. Native economic timeline/stage checks preserve chronological production, route clocks, arrival and market accounting. |
| `cargo-loading` | Retain | New native docking environment flags do not repair market manifest/load/arrival defects. Native economic tests preserve physical cargo backing, capacity/overflow policy, trade accounting and native arrival stages. |

## Navigation, geometry and torch courses

| Feature ID | Decision | Contract and supporting evidence |
| --- | --- | --- |
| `rotation-module` | Adapt | Rebase stock/editor overrides on 1.1, retaining its original inventory. Native physics/control hooks remain applicable; rotation checks include three-ship groups, one fuel debit, shallow invalidation and native new orbit locks. |
| `sff-navigation-destination` | Retain | Native GUI completion overwrites a mission destination needed by AI continuation. Route tests preserve the authored destination without retaining an expired course. PASS follow-up also refreshes native instant-moor coordinates when no GUI completion precedes placement. |
| `torch-fuel-cache` | Retain | Native cached reactant queries still diverge from physical tanks. The native torch oracle covers fresh tank checks, abort boundaries and successful braking. |
| `torch-fuel` | Retain | Native fusion/plot contracts still have the fixed-thrust accounting/control mismatch. The native oracle covers preview and engaged output, slider values, feeder limits, normal/SFF fuel and the actual reactor methods. |
| `torch-momentum` | Retain | The planner's new NPC parameter is preserved; its new RCS branch does not repair the player torch initial-velocity calculation. Native transfer tests retain incoming inertia, convergence and player/AI scope. |
| `torch-towing` | Retain | Native `SetThrust`/docking changes do not provide braced-course playback/mass. Native tow tests preserve unbraced catastrophic wear, station exclusion, multi-link braces and native abort when a link unclamps. |
| `hull-geometry` | Retain | Native item/grid/rotation contracts still support immutable geometry snapshots. Hull tests retain mutation invalidation, native-coordinate transforms, worker isolation and bounded fallbacks. |
| `hull-outlines` | Retain | Native `GUIOrbitDraw.SaveAndClose` adds stellar-draw state cleanup; the physical outline lifetime remains. Native hull/render-copy tests preserve actual outer contours and useful-zoom/viewport clipping. Stellar objects retain their own native renderer. |
| `hull-collisions` | Adapt | Native collision distances are now doubles and visibility is read separately for ATC and obstacles. Preserve that precision and replace only the free-hull overlap branch; hull/native docking and both installation-order tests preserve docking, stations and combat paths. November 16 follow-up repairs a pre-existing integration omission: docking-clearance responses now measure the same selected-hull clearance as target data, with 37 native-method checks ([details](../Navigation/Collisions/FORCED-DOCK-RANGE.md)). |
| `station-docked` | Adapt | Adapt the shared scan hook to the obstacle visibility read only. The new ATC visibility read stays native. Native chain/proximity tests retain actual station collisions and the configurable visitor-hull exception. |
| `proximity-range` | Adapt | The same shared scan adaptation preserves the new native double distance. Native warning/range tests retain the configured clearance threshold, station candidates, save state and both collision-hook orders. |
| `docking-clearance` | Retain | Native new ring/UI/environment changes do not repair the float grid-roundtrip in docking coordinates. Actual target IL/contracts remain applicable and current hull docking composition passes. The historical `DockingDiagnosis` capture-dependent executable is unavailable, not counted as a passing test. |
| `ship-rotation` | Retain | Native stale dock-protection state still blocks rotation. Miscellaneous native controls preserve legitimately docked protection. |
| `orbital` | Retain | Native circular locking is an improvement but does not replace parent-frame gravity/free-flight corrections. Orbital native cases preserve the new lock state and analytical hierarchy. |
| `coast` | Retain | Native high-time-step integration still needs the coast correction. The new `ShipSitu.TimeAdvance` body is composed with route/tow patches; native `UpdateShip` and coast tests preserve powered/docked paths and exception restoration. |
| `sff` | Adapt | Capture native angular rate/epoch for body-locked targets, including tangent velocity and centripetal acceleration. Native offset-prediction and SFF tests preserve zero-rate legacy offsets and avoid advancing live caches. |
| `course-controls` | Adapt | User retains wheel steps alongside new native Shift-drag. Synchronize only an engaged `PrecisionSlider` raw accumulator after the native clamped assignment; native precision/pointer flags remain authoritative. Native wheel and precision-drag tests cover stale-baseline reproduction and endpoints. |
| `nav-module-placement` | Add | Native `Draggable.MoveRectTransformUsingAnchors` assumes every parent pivot is centered. Replace four center constants with actual parent pivot reads; native projection, snapping and rejection remain. Native negative/repair controls and observed placement confirm the fix. |

## Combat and sensing

| Feature ID | Decision | Contract and supporting evidence |
| --- | --- | --- |
| `ranged-self-hit` | Retain | Native shooter inclusion remains; ranged native tests preserve other targets and damage resolution. |
| `meat-scrap` | Retain | Current destruction assets still permit duplicate meat generation. Native destruction tests retain authored non-meat outcomes. |
| `meat-cover` | Retain | Native loose-meat/plot-part cover remains; native ranged/cover cases retain the deliberate QoL exception without changing other cover. |
| `pdc-ammo` | Retain | Native conversion still regenerates starting rounds. Updated actual factory/loot tests account for 1.1 assets and retain ammunition identity, variants and conversion scope. |
| `stabilizer-creak` | Retain | Native maneuver wear still does not scale this cue by active stabilizers. Native wear tests preserve damage and other audio contracts. |
| `mm-fairness` | Retain | Current `BeatManager` strike selection still needs the eligible-loaded-hull distribution. Micrometeoroid native selection/probability checks preserve authored strike scheduling. |
| `pdc-defense` | Retain | Native strike/PDC methods still support the same ballistic interception boundary. Native firing/probability fixtures retain sensors, firing interlocks, ammunition and failed interception. |
| `em-propagation` | Retain | Native EM receiver methods and signatures remain. Sensor-runtime checks exercise actual dynamic hook installation/removal and native on/off/reference-range behavior. The inverse-square law remains an intentional policy. |
| `sensors` | Retain | Native physical `ShipDraw` offset methods remain distinct from new stellar drawing. Runtime sensor tests preserve unknown contacts, paused redraw stability, celestial/station exclusions and authoritative navigation/weapon coordinates. |

## UI, logging and loading

| Feature ID | Decision | Contract and supporting evidence |
| --- | --- | --- |
| `quick-pda-hack` | Adapt | Rebase the locked-PDA override on 1.1 and change only the added action. Native loader/menu/gating tests retain authored attributes and the dialog fallback; no C# hook is added. |
| `comms-window` | Retain | Native transcript-window selection remains; native history/display tests retain current messages and ordering. |
| `console-buffer` | Retain | `ConsoleToGUI` is byte-identical. Native baseline/repair/display tests still reproduce the oversized string and stale visible-buffer defects. |
| `font-console` | Retain | The filtered warnings retain their native message contracts. Console/filter tests retain real errors and ordinary output. |
| `electrical-logs` | Retain | Native connection-audit logging contracts remain; signal audit tests retain diagnostic visibility while removing console noise. |
| `signal-boxes` | Retain | Electrical gate/queue/audit/persistence bodies remain; `Ship.AddCO` adds RCS fields and `InitShip` adds spawn context without replacing wake/owner integration. Native signal tests preserve temporary uninstall/reinstall wiring, lifecycle cleanup and durable queues. |
| `co2-scrubber-audio` | Add | 1.1 removed the `GasPump.Start` audio eligibility gate and exposed stale generic pump-loop entries in the CO2 profile. One asset-only profile override removes those two entries; `ScrubberAudioCheck` reproduces the native emitter creation, verifies the correction and preserves all non-audio fields and other profiles (39 checks). Custom generation/binding contains zero hooks or frame calls. |
| `comms-retention` | Retain | Native comms save structures remain. Native load/save/serialization tests retain unread/pending/latest context while pruning only obsolete history. |
| `save-co-lookup` | Retain | New iterator numbering does not invalidate the enclosing-method/closure selector. `LoadCompatibilityCheck` executes the actual native saved-CO predicate; full membership is preserved. |
| `save-id-lookup` | Retain | Native remaining-item save logic still repeats the scan. Native inventory equivalence tests preserve membership, duplicates and writes. |
| `contained-removal` | Retain | New load/respawn callers retain saved-parent/removal ownership. Native inventory/barter tests preserve exact item removal and lookup invalidation. |
| `portraits` | Retain | Native portrait compositor is unchanged. Portrait tests verify pixels/composition above the declared Unity texture-I/O boundary. |
| `batch-init` | Retain | Native `StarSystem.Init` now restores stellar/environment state. The actual native iterator checks show batching preserves body/hierarchy/map restoration, progress order and non-null yields; new-game/disabled scheduling remains native. |
| `scrollbacks` | Retain | Native MFD content changes do not replace scroll input routing. Native scroll math/IL checks preserve interaction; the user also confirms both log scrollers on 1.1. |
| `hide-nav-note` | Retain | Changed `GUIOrbitDraw.Awake` retains the same tutorial child/lifetime. Native nav checks preserve the optional presentation-only hide. |
| `social-filter` | Retain | Selected native social-list body and `PersonSpec` are unchanged; the filter still resolves the character and checks the same Robot01/Robot02 type/definition fields. Retention rests on source/target review; no separate filter execution or full Unity Socials interaction is claimed. |
| `bounty-info` | Retain | Selected native row/detail/click bodies and private text fields are unchanged; missing/`OKLG_UNK` labels remain applicable. Source/target review supports retention; this is not a new execution test of the complete bounty GUI. |
| `pda-settings` | Adapt | Update native-mod target metadata; preserve custom feature filtering and registry/native app contracts. PDA native checks and built-profile/install fixtures retain only included controls. |
| `save-json` | Retain | Generic adapter preserves new native RCS/orbit/stellar DTO fields, including null versus empty arrays. Native DTO/equivalence cases run in two cultures; optional/default behavior is unchanged. |
| `save-forward-compatibility` | Add | Old saves omit raw RCS count/exhaust caches that new native shallow movement requires. Reconstruct only deterministically derivable hardware using current native eligibility/damage/assets; retain weighted thrust, damage, fuel and commands. Native and real-save prefix tests cover refusal boundaries and persistence. No world content is injected. |

## Inspection and development controls

| Feature ID | Decision | Contract and supporting evidence |
| --- | --- | --- |
| `inspection` | Adapt | Add read-only native cache/build/layout inspection and bounded destruction-caller diagnostics. Docking observers retain linkage transitions and exception transparency after native orbit matching. API/helper/native rescue/journal fixtures cover their stated boundaries; individual rendered transition callbacks are source-reviewed, not all separately scene-tested. |
| `legacy-commands` | Retain | Selected native resolver body is unchanged. Existing mass/refuel maintenance helpers have separate native tests; this does not claim execution of every destructive console command. |
| `social-location` | Retain | Selected native social-click body/ID field and physical lookup contracts are unchanged. Retain existing diagnostic behavior; do not execute destructive double-click cleanup as a compatibility probe. |
| `worktrace` | Retain | Native work/task entrypoints remain; `WorkTraceCheck` retains binding, scope, output and opt-in lifecycle. |
| `torchtrace` | Retain | Temporary observers still bind actual fusion/fuel/thrust/playback methods. Native trace tests preserve enable/disable and fuel/control observations without making trace state persistent. |
| `load-profiling` | Retain | Native generated owners renumber, but enclosing-method selection retains actual restore/load phases. Native load/trace checks and full binding preserve finalizers and do not replace scheduling. |

## Evidence and limits

The ignored working receipts include `wheel-shift-hook-targets.json`,
`wheel-shift-hook-comparison.json`, `wheel-shift-hook-evidence.json`,
`test-run-results.json`, `behavior-mode-results.json` and
`closing-native-results.json` under `obj/Compatibility-1.1.0/`. The closing receipt
records 15 successful invocations, actual native/harness hashes and exit codes;
it closes first-wave fixture failures and checks the final UI/RCS adaptations.
The original failures remain in earlier receipts. An initial mistyped RCS mode
ran the default diagnosis; its log is retained but it is not counted as RCS
coverage. The corrected `rcs-1.1` invocation executes all 46 intended checks.

The current Debug candidate is SHA-256
`c6550e128ce67bca2fed6fa7c0dd62363efb72f1e775d7c4c3eb176b2a8c189d`,
MVID `8ade39e3-e426-4fdd-a3b5-56bac8c59255`. All 289 decorated classes bind with
zero failures/fallback warnings. Native comparison accounts for 381 rows:
294 unchanged bodies, 61 changed, 14 new signatures/targets and 12 superseded
signatures. All 114 compatibility-checkpoint features have a decision in this ledger, including
the subsequently verified asset-only CO2 scrubber audio regression. The three
new classes are environment-conversion scoping, legacy RCS loading and pivot
placement. Seven reviewed behavior slices have no pending assembly-invalidated
elements; unexpanded native entrypoints remain explicitly unexpanded.

The old `DockingDiagnosis` requires missing historical captures. Its unchanged
grid-rounding contract and current composed hull/docking tests support retaining
the feature; do not describe the historical executable as passing. Boarding
casualty outcomes, complete Socials/bounty GUI event routing and Unity rendering
are source-reviewed engine boundaries, not fabricated native execution results.
The two-hour live SFF repeat and normal AI resumption support integration but are
not deterministic A/B proof of every historical loss. In particular the earlier
O-Y2L destruction caller remains unexplained.

No further one-off live test/reload is required by this audit. Combined
wheel/Shift-drag is accepted on its code contract and offline native reproduction;
the latest wheel candidate has not been observed in the running scene. The
previous candidate's placement correction is confirmed live. These distinctions
are recorded instead of treating the player as an exhaustive test harness.

World-content migration is separate from this feature ledger and omnibus build.
The subsequent [standalone migration findings](../StandaloneMods/SaveMigration110/FINDINGS.md)
show native loading already supplies the new regional content. The optional
metadata-only prototype has no demonstrated benefit after that initialization
and is not recommended.
Release/version/commit and deployment decisions remain separate.
