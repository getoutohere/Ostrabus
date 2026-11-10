# Revalidate after game updates

The offline comparator answers which parts of the reference need another look
when game code changes. It does not infer new behavior, approve a patch or modify
the running game. The initial baseline covers the source-reviewed 1.0.1.10
retriever slice, not all ship AI.

## Run an audit

From the repository root, with the .NET 8 SDK and BepInEx installed, set paths to
the game installation being reviewed:

```powershell
$game = $env:OSTRANAUTS_DIR
$assembly = Join-Path $game 'Ostranauts_Data/Managed/Assembly-CSharp.dll'
$data = Join-Path $game 'Ostranauts_Data/StreamingAssets/data'
dotnet run --project Tools/AIBehaviorGraph -c Release -- assembly-audit --assembly $assembly --data-root $data
```

Set `OSTRANAUTS_DIR` to the installation first if it is unset. The build uses that
location for BepInEx's Mono.Cecil. Cecil reads metadata without executing game
code, resolving Unity objects or requiring a running game.

The default outputs are ignored files under `obj/ai-assembly-review`:

- `.json`: source and graph-element statuses, reasons, sample changed members,
  dependency paths and coverage caveats.
- `.md`: concise source/element review list.
- `.diagram.md`: effective Mermaid graph with affected nodes and edges in red.

Use `--output obj/my-review` for another report prefix. `assembly-scan` with the
same input options inventories matched roots, missing selectors and unresolved
dependencies without requiring an accepted snapshot.

## What triggers review

Each slice's manifest binds its catalogued source groups to native methods (41
retriever, 32 scavenger and 24 deployer groups after the station-docked policy). Each selected
method includes its overload set, signature, metadata and normalized IL. Tracked
dependencies include local calls, referenced fields/types, constructors and
conservative virtual-call candidates. Directly referenced external DLL bytes are
also compared when present alongside the game assembly. Explicit JSON dependencies
currently include station drop-off eligibility and removal of help requests.

| Source status | Meaning |
| --- | --- |
| `unchanged` | Tracked dependencies match the accepted snapshot. |
| `direct_changed` | A selected method, overload set or catalogued repo source changed. |
| `dependency_changed` | A tracked callee, field/type, virtual candidate or external DLL changed. |
| `data_changed` | A selected JSON entry changed. |
| `declaration_changed` | The graph/overlay or dependency manifest changed. |
| `unresolved` | A required binding/input is missing or snapshots are incompatible. |

All non-unchanged statuses propagate `requires_revalidation` to dependent graph
elements. Unaffected elements say `tracked_unchanged`, deliberately avoiding a
claim that their behavior was proved correct. Predicate dependencies propagate to
guarded edges; scenarios include sources from their vanilla and effective traces.

Metadata token renumbering, MVID changes, NOPs, equivalent short opcode forms and
JSON object-property order do not cause invalidation by themselves. A changed
assembly hash alone does not invalidate every graph element.

## Review and explicitly accept

1. Inspect changed native methods, callers and relevant assets. Update the native
   graph, patch overlay and evidence where behavior changed; preserve genuine
   choices and failure outcomes. A compiler-only change may need no model edits.
2. Review source fingerprints and the pinned assembly hash in the model. Update
   them only after reviewing the corresponding code. Check new patch files too.
3. Run graph validation/scenarios and appropriate native harness checks. Regenerate
   the effective graph/diagrams and inspect their diffs. A metadata audit alone
   does not validate native behavior.
4. Accept the reviewed snapshot explicitly:

```powershell
dotnet run --project Tools/AIBehaviorGraph -c Release -- assembly-baseline --assembly $assembly --data-root $data --accept-reviewed --reason 'Reviewed the changed methods and updated the model and evidence.'
```

Acceptance requires a nonempty reason, matching reviewed model assembly hash and
available method selectors/data. Snapshots are immutable Brotli-compressed JSON;
the pointer and append-only acceptance records retain history. Audit never writes
these files. Acceptance is a deliberate tool operation, not an automatic outcome
of a clean build, passing tests or report generation.

## Coverage limits and validation

Virtual targets are conservatively matched by name and arity, so an unrelated
implementation can cause extra review flags. Reflection, delegates, indirect
calls and data-selected behavior are not fully resolved. External assembly bodies,
their recursive dependencies and Unity native engine binaries are not analyzed.
Only declared JSON entries and catalogued patch files are covered. A new behavior
or patch outside these declarations can require review without producing a flag.
Changed declarations invalidate the baseline rather than silently accepting a
narrower dependency set.

`assembly-self-test` generates synthetic Cecil assemblies and checks change
classification, normalization, dependency propagation, JSON inputs and baseline
preservation. These are metadata tests, not game execution tests. The initial
installed-assembly audit also checks that identical inputs yield no review flags.
The separate 49 graph consistency checks test the abstraction's internal contract.
No part of this tooling installs runtime hooks or adds game-frame work.
