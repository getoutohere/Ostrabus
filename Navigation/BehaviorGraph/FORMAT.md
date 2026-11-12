# Format version 1

The typed loader in Tools/AIBehaviorGraph/Program.cs rejects unknown JSON members.
Domain/ID/source/phase validation adds constraints beyond JSON parsing. No source
fragment or guard is evaluated as arbitrary code.

## Native model

- `id`, `gameVersion`, `assemblySha256`, `baselineCommit`: provenance. IDs inside
  a graph are scoped to that graph. `scope` explicitly lists preconditions, opaque
  areas and unsupported conclusions.
- `sources`: relative game/repository paths, symbol names, lexical anchors and
  normalized-text SHA-256 fingerprints. Decompiler line numbers are not identity.
- `evidence`: source review/native offline/live observation, artifact path and
  the limited claim it supports. Presence of evidence does not validate every
  possible edge sharing that source method.
- `variables`: named discrete domains. Values are string codes, including
  `"true"`/`"false"` and Main indices; they are not free-form expressions or numeric
  simulation inputs. `kind` separates state, assumed/derived predicates and native
  results that this model does not calculate.
- `nodes`: control point, layer, source IDs and explanatory detail. A control
  point does not determine every orthogonal fact.
- `edges`: stable ID, `from`, `to`, event, `when`, `set`, sources, evidence and note.
  Only listed facts change. Missing effects preserve the incoming value.
- `findings`: typed review/integrity predicates. They report conditions; they do
  not prohibit transitions, prescribe outcomes or automatically justify a patch.
- `scenarios`: initial node/facts, externally supplied event sequence, explicit
  mode-specific expectations and assumptions. The effective trace may diverge and
  block later requested events; the report retains that fact rather than skipping
  those events or declaring the real simulation stuck.

Scenario expectations can additionally specify `stop`: an empty string requires
every requested event to complete; a nonempty string asserts the exact blocked,
unknown or ambiguous termination. New cross-actor scenarios use this explicitly
so an accidentally truncated trace cannot masquerade as a successful test.

Conditions conjunct `facts` (each value is a set of allowed domain codes), required
predicate IDs and forbidden predicate IDs. Missing input is unknown. Any definite
false term makes the conjunction false; otherwise an unknown term keeps it unknown.
Predicate cycles are rejected. A trace stops at unknown guards or ambiguity.

## Patch overlay

Each patch names its classification, evidence, source references, removed
possibilities and preserved possibilities. Ordered operations are:

- `replace`: replace a native edge with a documented corrected edge.
- `intercept`: preserve a guarded native fallback and add the scoped correction.
- `add`: introduce an explicit edge, such as an operator-requested action.

`set` overrides/adds effects. `removeSet` cancels effects the intercepted operation
does not execute; it is not a runtime state reset. `phase=before` evaluates patch
eligibility on entry. `phase=after` evaluates it on a temporary state after the
original edge's effects. The temporary state is a **guard probe**, not an additional
write: the retriever postfix can see docking completed before deciding to suppress
the one-shot. Generated `guards` retain this phase/probe information.

Overlay operation order expresses the reviewed nested call boundary, not arbitrary
Harmony priority ordering. Do not encode competing hooks merely in a convenient
order without establishing their actual execution relationship. Dependencies that
are not transition-expanded remain visible in the overlay and generated audit.

## What the checker does not establish

It does not interpret C#/IL, infer physical units/probabilities, execute native
predicates, model Unity threads or parse a live inspection response into facts.
Agents must establish facts from source/inspection and preserve unknowns where
authority is uncertain. Bounded search explores modeled event orders under fixed
external outcomes; absence of a witness is not proof that the game is safe.

## Assembly dependency declarations

`assembly-dependencies.json` binds every source group to native type/method
selectors. A method name selects all matching overloads. Predicate bindings also
connect guard logic to its dependencies. Selected JSON entries use a relative
file path, identity key/value, explicit sources and optional consuming methods.
Missing inputs remain unresolved rather than being treated as unchanged.

`assembly-baseline.json` points to an immutable compressed snapshot under
`assembly-baselines/`; acceptance records preserve the preceding pointer. The
snapshot contains symbol identities, dependency hashes and coverage notes, not
game IL bodies or asset contents. Graph/overlay and manifest hashes bind that
snapshot to its reviewed declarations.

The comparator fingerprints normalized IL and metadata rather than decompiled
line numbers. It ignores token numbering, MVID, NOPs and equivalent short opcode
forms. Root, transitive dependency, selected data and declaration changes are
distinguished; unavailable dependencies are unresolved. Review flags propagate
to nodes, transitions, predicates, findings, overlays and modeled scenario traces.
See [revalidation workflow and limits](REVALIDATION.md). This is dependency-change
detection, not semantic equivalence checking or automatic verification of a patch.
