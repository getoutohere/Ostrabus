# AI ship behavior reference

The aim is to preserve plausible emergent interactions while identifying broken
execution paths and unnecessary restrictions. This is an offline reference, not
a replacement AI controller, runtime monitor or automatic recovery policy.

The [AI behavior patch audit](../../Notes/AI-BEHAVIOR-PATCH-AUDIT.md) reviews the
C#/asset boundary across modules and records a reproduced station-return priority
conflict, since [repaired](../RETURN-PRIORITY.md), outside the currently expanded graph slices. Passing these slice checks
does not establish that all return/combat/queued-command combinations are safe.

Start with the [generated audit](AUDIT.md), [vanilla diagrams](VANILLA.md) or
[effective diagrams](EFFECTIVE.md). Mermaid diagrams render on GitHub. The first
slice is the **OKLG/VORB retriever lifecycle on 1.0.1.10**, with relevant shared
navigation/lifecycle boundaries. It is intentionally not an all-ships/all-patches
model. [Coverage and next steps](REVIEW.md) are part of the reference's contract.

The second independent slice covers [OKLG scavengers](Scavenger/README.md):
docked holds, fuel-selection decisions and emergency assistance eligibility.
Select it with `--model scavenger`; the default remains the retriever reference.

The third slice covers [deploying-hauler scope](Deployer/README.md): ordinary
release/empty recovery, shared refill accounting and VORB-specific route guards.
Select `--model deployer`. It records outstanding coverage gaps explicitly.

The [retarget caller audit](../../Notes/AI-RETARGET-CALLER-AUDIT.md) separately
reproduces stale-route retreat through the native police tree. The implemented
escape-setter repair is catalogued as an unexpanded dependency; full
combat/retreat remains outside these transition slices.

[Shared collision avoidance](../PLANETARY-AVOIDANCE-AUDIT.md) is also catalogued
as an unexpanded dependency in all three slices, including native call sites and
the production geometry/ephemeris sources. Its offline tests reproduce the bad
evasion and execute the patched approach. These transition graphs continue to
treat flight completion as an external outcome; they do not calculate obstacle
geometry or assert that every possible route is safe.

The intentional [station-docked traffic policy](../STATION-DOCKED-COLLISIONS.md)
is another catalogued boundary: parked nonstation hulls connected to stations
are nonhazards, while free-space tows and actual stations remain physical.
The saved PDA setting gates this policy and defaults on; disabling it restores
native hull handling. These slices catalog that configurable boundary rather
than modeling collision geometry or a settings UI as AI transitions.
Native collision and proximity selectors plus the policy/scan sources require future
revalidation. Controlled shared-command replays cover both the departing patrol
and returning hauler; their geometric outcomes are not graph-state proofs.

The optional [hull-shaped physical collision path](../Collisions/README.md) is
outside these graph slices. It changes contact geometry/response, not the shared
AI avoidance predictor, weapon helpers or transition guards. These graphs must
not be interpreted as collision-geometry or swept-motion proofs; that module's
native/numeric harnesses provide its separate evidence.

## Source of truth

- [retriever.vanilla.json](retriever.vanilla.json): native control points, effects,
  orthogonal facts, method/file references, evidence, findings and conditional cases.
- [ostrabus.overlay.json](ostrabus.overlay.json): patch transformations, before/after
  interception phases, changed possibilities, preserved choices and scope exclusions.
- `retriever.effective.json`, `VANILLA.md`, `EFFECTIVE.md` and `AUDIT.md` are generated.
  Do not maintain a second complete patched graph manually.

JSON was chosen for the first implementation because the repository's .NET tools
can validate/query it without adding a YAML dependency. IDs and typed discrete
facts are the machine interface. Human diagrams are projections of that interface.
The [format and phase rules](FORMAT.md) define that interface.

Nodes are **control points**, not complete ship states. `main`, active command,
crew location/readiness, registration, visibility, connection, route ownership,
target identity and inherited controls remain separate facts. In particular the
active leaf and the Main index can disagree legitimately across a callback.
Flee's FlyTo is a different control point from Main's FlyTo.

## Query without rereading the full graph

Run from the repository root with the .NET 8 SDK:

```powershell
dotnet run --project Tools/AIBehaviorGraph -c Release -- validate
dotnet run --project Tools/AIBehaviorGraph -c Release -- node haul
dotnet run --project Tools/AIBehaviorGraph -c Release -- source retriever
dotnet run --project Tools/AIBehaviorGraph -c Release -- patch handoff
dotnet run --project Tools/AIBehaviorGraph -c Release -- evidence retriever_repro
dotnet run --project Tools/AIBehaviorGraph -c Release -- scenario sff-pickup-conflict
dotnet run --project Tools/AIBehaviorGraph -c Release -- search sff-pickup-conflict risk.route_anchor_conflict 8
dotnet run --project Tools/AIBehaviorGraph -c Release -- generate
```

`node` includes incoming/outgoing transitions and resolved file/method references.
Query commands write one JSON document to standard output; validation/provenance
messages go to standard error, so an agent can capture them separately.
`scenario` compares vanilla and effective traces. `search` looks for a conditional
witness across modeled event orders, capped at 1–16 transitions and 4,096 distinct
states. It keeps external outcomes fixed to the scenario's assumptions. A search
without a witness is **not** a proof of safety or absence of unmodeled behavior.

Default validation checks repository source fingerprints. Add `--game-source`
followed by the local decompiled root to verify native source fingerprints and
method anchors too. Add `--assembly` followed by the installed Assembly-CSharp.dll
path to compare the pinned binary hash. No machine-specific absolute paths are
stored in the graph. Source text hashes normalize line endings to LF.

The tool validates unknown facts/values, missing sources/evidence, duplicate edges,
recursive predicates, overlay references and interception phases. Generation
runs validation/scenario checks before writing outputs. Native source verification
is lexical/fingerprint verification, not a proof that the abstraction is correct.
Fingerprints cover catalogued files only: a new patch in another file can require
an overlay/coverage review even if all existing fingerprints still match. Check
the repository diff since the recorded baseline before treating this as current.

## How to interpret results

- A fact absent from an input is **unknown**, not false. Unknown guards stop a
  scripted trace; searches count unresolved edges/findings instead of inventing
  a value. A blocked event means that requested continuation is unavailable in
  this model under these facts, not that the real game cannot do anything else.
- Events such as `flight_finished`, `flee_reached` or `candidate_found` are
  externally established native outcomes. The graph does not calculate their
  timing, probability, geometry or feasibility. An edge is a conditional path,
  not a promise that its event will occur.
- Graph scenario checks validate **reference-model consistency**. They are not
  additional game-assembly tests. Evidence records separately identify source
  review, existing native reproductions and actual live observations, including
  which Unity/world boundaries were substituted in offline tests.
- The source-grounded inner-Dock-prefix/outer-retriever-postfix composition is
  recorded as a conditional composition. It is not presented as a newly executed
  combined Unity scene. Actual Harmony ordering outside the encoded call phases
  remains a review requirement.

## Useful findings from this slice

The native SFF pickup can leave Main at outbound FlyTo, run HaulShip once and
then allow a member route to compete with the group's inherited controls. The
handoff overlay routes qualifying pickups back into sustained native hauling.
Unknown-tree and player-party exclusions deliberately retain the conditional
mechanism; these are exposed scope limits, not proof those cases failed live.

Fuel exhaustion can still leave a hauler stranded. Threat response, surrender,
escape, failed pickup and candidate randomness remain native possibilities.
The operator's station-arrival API is reachable only through an explicit operator
event. It is never inserted as the universal destination of failed navigation.

Native rejected station drop-off can delete crew. That is recorded as a native
policy requiring design review, rather than automatically declared a bug. Likewise
an already-far-away ship is not itself classified as corrupt: the finding identifies
a specific control-authority mechanism with native reproduction evidence.

## Maintenance

[Assembly revalidation](REVALIDATION.md) detects changes to tracked methods,
dependencies and selected game-data entries and propagates review flags through
the graph. Its report is separate from the accepted model; an audit never accepts
a replacement baseline. Start there when a game update changes the assembly.

1. Review the relevant method and its callers. Add/version source references and
   evidence; do not infer semantics from variable names or a diagram.
2. Change native transitions only for native code changes. Express mod differences
   in the overlay, including exact scope, removed possibilities and preserved ones.
3. State context/assumptions and add a counterexample or exclusion case. Classify
   integrity repair, containment, behavior change, simulation enhancement and
   operator action separately. Do not turn a desirable outcome into an invariant.
4. Verify source/assembly fingerprints, regenerate and inspect the diagram/audit
   diff. Refresh a fingerprint only after reviewing the changed source.
5. Use a native harness or live inspection to establish an actual defect before
   promoting a graph finding into a gameplay patch. This reference itself never
   installs hooks or changes the running game.
