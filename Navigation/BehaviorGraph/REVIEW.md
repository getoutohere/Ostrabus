# Coverage, tradeoffs and expansion queue

The first slice contains 22 control points, 38 native edges, eight overlays and
20 conditional cases. It is pinned to game 1.0.1.10 and committed runtime sources
from the 0.2.2 baseline, plus the source-fingerprinted working shared flight-fuel
handoff fix. Uncommitted player torch code is excluded from the model. This is a
reviewed abstraction with explicit holes, not an automatically extracted program
or a proof of all possible Unity/game behavior.

## What the effective graph actually includes

Retriever SFF continuation, the already-attached instant-Dock guard and their
inner/outer call composition, representative crew registration/execution guards,
exact target-reference rebinding, connected-reserve exclusion, VORB depot lookup,
the applied-SFF arrival gate and an explicit operator station-arrival event.

The model deliberately limits its ordinary path to no higher-priority external
reaction/queued directive/StationReturn intent. It summarizes threat response as
conditional authored outcomes; it does not reproduce the full scheduler, health,
social, weapon or navigation-prediction algorithms. Save/load and ATC replacement
are boundary examples, not complete teardown/deserialization models. Numeric
fuel, orbital trajectories and probabilities remain opaque native outcomes.

The graph does not let a successful target string imply successful navigation.
Command, member route and group-anchor motion are separate. Native physics/fuel
reproductions are linked, not reimplemented by these small discrete traces.

November 11 working-tree update: [docked course playback](../Towing/README.md)
now repairs the member-route/group-anchor handoff and torch tow mass. Its explicit
brace-release abort is additional user policy. This numerical movement repair is
outside the baseline graph's discrete transition overlays; do not interpret the
existing vanilla failure trace as the effective motion of a build with this
feature enabled.

## Overlap and behavioral restrictions

- VORB retrieval depot correction preserves native candidate filtering and
  randomness; it changes which authored depot supplies assignments. This is an
  identity correction with a regional intent assumption, not a patrol governor.
- VORB route rejection/manual/fuel assistance and general empty-hauler recovery
  are **deployer-only** in the reviewed source. They do not directly compete with
  this retriever handoff. Keep their separate behavior review on the next slice.
- Bounded SFF travel is a simulation enhancement: partial travel changes the
  time-skip approximation. The gate only suppresses native catchup for a party it
  actually captured/applied that has not reached the handoff. Plotted member routes
  exclude the group from replacement, so the SFF enhancement is not a universal
  protection against a broken native tow route.
- Crew validation is containment at execution/registration boundaries. It does
  not explain why a crew member went missing. Police inspection exceptions must
  not be generalized from this retriever slice without modeling boarding.
- StationReturn and shared fuel-prediction invalidation are recorded boundaries
  requiring further expansion. Do not use this graph to decide their full cross-role
  interaction or conclude those controllers are interchangeable.

## Investigation candidates, not automatic patch requests

Confirmed coverage failure: the [O-ZY3A target-retirement investigation](Scavenger/TARGET-RETIREMENT-INVESTIGATION.md)
reproduces a divergent native route after a retriever destroys a scavenger's
destination. Single-actor slices and opaque flight outcomes omitted that
cross-role transition; all existing fingerprints and scenarios still pass.
Permanent target removal is distinct from the modeled same-ID replacement.
The [target-lifecycle expansion](TargetLifecycle/README.md) now exposes that
interleaving in vanilla, including saved missing targets and finite routes left
assigned to removed hulls. Its effective overlay repairs those lifetimes,
backed by production-hook native tests. The assembly-derived
`coverage` command reports absent role/command source bindings independently of
passing scenario checks. Full semantic coverage remains open.

1. Hidden returned hulls retain angular state; ResetShipState alone does not reset
   it. How do each role's spawn and first steering path handle reuse? Live arrival
   evidence records retained spin but not a subsequent failed reused hull.
2. Failed SFF pickup and wrapper semantics still have fall-through behavior outside
   the handoff patch. Follow the next native sequence cycles before assuming a
   returned failure is either harmless or another runaway.
3. Player-party/unknown-tree exclusions leave modeled conditional control-conflict
   paths. Their existence is a reason for a focused native test, not justification
   to restrict player towing or make AI abandon a legitimate encounter.
4. Native population retirement rejects some crew by deletion and native help
   requests exclude retrievers. These are authored policies; decide whether they
   are acceptable simulation simplifications before changing them.

## Next slices, in order

The [retarget caller audit](../../Notes/AI-RETARGET-CALLER-AUDIT.md) now reproduces
a native police retreat retaining its prior route. Its escape-setter repair
is implemented and natively regression-tested. Complete combat/retreat remains
outside these expanded slices; do not read
the supplied generic retarget event as proof that every caller reaches that state.

The [OKLG scavenger slice](Scavenger/README.md) now expands normal Hold, fuel
selection and assistance eligibility in its own model. Its recovery repair has
since been implemented and live-tested; the historical provenance of fuel exhaustion
remains open. A third [deployer slice](Deployer/README.md) now expands ordinary
empty/release recovery, shared refill accounting and VORB-specific route guards,
with a [continued scope audit](../../Notes/AI-REGIONAL-SCOPE-AUDIT.md).
The queue below still describes unexpanded areas of the retriever
and wider system reference.

1. **Remaining deployer/SFF/loaded and return composition.** Ordinary deploying
   haulers do not use StationReturn; do not conflate the two controllers. The new
   slice maps shared target/route/fuel boundaries; duplicate home recovery has
   been removed and refill accounting generalized with native regressions.
   Complete SFF wrappers, loaded scene transitions and player/cargo plotting remain
   beyond it. Generalization/removal still needs the documented role-specific cases.
2. **Ceres patrol/pursuit/retreat.** Map target affordability, chase decisions,
   homeward reserve and hostile docking. Identify restrictions that erase plausible
   risky decisions versus fixes to invalid references or impossible maneuvers.
3. **Police inspection and boarding.** Represent the officer on either connected
   hull, departure permission, player intervention, shallow/full transitions and
   registration; keep successful visits and legitimate hostilities available.
4. **Population and ATC lifecycle across roles.** Include save/load, retirement,
   reserve reuse, pruning, crew transfer and cargo/passenger travel without assuming
   that a hidden hull is dead or that a failed voyage must end in rescue.

Every expansion should name which existing paths it removes. Desirable emergence
comes from reliable agents making consequential choices, including failure; graph
integrity contracts must not prescribe that all agents come home successfully.

## Cargo and civilian expansion

[Traffic](Traffic/README.md) now maps freight, PASS and civilian visitors. Actual
constructor trees include their priority/combat branches; the lifecycle projection
covers selection, handoff, retirement, reserve reuse, ATC and SFF with explicit
native-result inputs. Its [investigation](Traffic/INVESTIGATION.md) separates
confirmed defects from delivery/policy questions. The effective graph now includes
the two native-tested repairs: civilian SFF X/Y lookup and exception-safe asteroid
Ship-to-stellar conversion. The conversion repair corrects our first removal hook's
regression; the broader delivery and policy questions remain open.

The [player-connected cleanup investigation](Traffic/REGIONAL-CLEANUP.md) now
establishes another native defect through normal region selection, registration
and Update. Departing local buckets destroy connected AI hulls and remaining
aboard COs after skipping relocation. Direct/chained, full/shallow and four
roles reproduce it; unchanged-region, INTERREGIONAL and station-body controls
survive. The runtime repair now extends the exemption to hull destruction and
temporarily schedules retained controllers interregionally, restoring local
membership after detachment. The effective model includes that lifetime, backed
by native queue/save restoration and crewless faction-capture controls.

The assembly inventory now reports 8 of 13 pilot types and 38 of 70 command types
with unbound execution entrypoints, down from 10 and 55. These are binding counts,
not percentages of semantic coverage. Arena, player AutoPilot, blockade, missile,
navy, pirate, police and station roles still lack complete entrypoint bindings.
Police-specific reproductions elsewhere do not amount to a complete police model.
The population/ATC slice above has therefore progressed, not been closed.

Hidden-hull civilian visits now have a native-tested effective repair: eligibility,
retired/restored visit invalidation, saved primitive markers, priority/SFF ownership
and physical encounter protection. Native hauler tow delivery and station-only
parking remain available. The [visit audit](Traffic/HIDDEN-VISIT-INVESTIGATION.md)
records the 82 production assertions and the boundaries of the offline fixture.

## 0.4.0 source-reference review

The retriever lifecycle source fingerprint now includes registration-time stale
timer cancellation. The transition projection is unchanged: this bookkeeping and
its worker interleavings are tested separately by `Tests/StaleShipDiagnosis`.
The registration change preserves crew validation and cancels retirement only
when a valid controller is newly registered, retaining later failure markers.

The retriever, scavenger and deployer station-collision references now include
the extracted `HasStationConnection` helper used by towing departure previews.
`IsExempt` still gates the same station-chain predicate with the existing setting;
the extraction changes neither collision policy nor the modeled transitions.
Native hull and towing checks cover the shared helper and station/free-flight
mass distinction. Source fingerprints and derived graphs were refreshed after
review; accepted native dependency snapshots retain their prior history.

The traffic audit also exposed missing dependency declarations for seven existing
visit/retention predicates. Their mappings now name the owning patch and native
control, queue, hull and blackboard groups. Recursive predicate dependencies
remain transitive. This repairs update-detection coverage; no transition or
runtime behavior changes. All five source/assembly audits are rerun before staging.
