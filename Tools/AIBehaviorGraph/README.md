# AI behavior graph tool

Offline .NET 8 tool. Assembly review uses Mono.Cecil bundled with BepInEx to read
metadata and IL; it never loads or executes the game assembly in the CLR. No live
inspection requests are issued and no runtime hook is installed. Set
`OSTRANAUTS_DIR` before building if the game is outside the default Steam library.

See [the model, commands and evidence contract](../../Navigation/BehaviorGraph/README.md).
Run `dotnet run --project Tools/AIBehaviorGraph -c Release -- validate` from the
repository root. Native source/assembly fingerprints can be verified with optional
`--game-source` and `--assembly` paths. `generate` derives diagrams and effective
JSON from vanilla JSON plus the patch overlay; query commands avoid rereading all
model files into an agent's context.

Add `--model scavenger` to select the separate OKLG scavenger reference. The
default retriever files and accepted baseline are retained. Each slice generates
its own diagrams/audit and maintains its own assembly baseline. The current
`assembly-self-test` fixture suite uses the default retriever model.

`--model deployer` selects the cross-home deploying-hauler route/fuel scope
reference. Its model-safety checks are selected by scenario capabilities, rather
than assuming every graph with a node named `fly` is the retriever slice.

`--model target-lifecycle` adds interacting actors, permanent destination removal,
save/load ghosts, manual/planned dispatch and the existing same-ID replacement
repair. Its permanent-removal/restoration contrast executes no replacement AI
policy and is backed by the shipped-assembly production-hook tests.

`--model traffic` adds separate freight, PASS and civilian lifecycle paths plus
actual constructor trees exported by the native harness. `CONTROL-TREES.md` shows
their ordered priority/combat branches. Native role entrypoints, helper commands,
market handoffs, ATC cleanup and SFF ownership have explicit evidence limits.
The effective model includes native-tested civilian coordinate, asteroid
conversion and player-connected regional cleanup/scheduling repairs. Other findings retain their documented uncertainty; the
graph does not invent successful delivery or rescue outcomes.

The civilian visit overlay additionally models new hidden-candidate exclusion,
retired/restored visit cancellation, deferred control ownership and civilian
encounter retirement. Native station-only parking and hauler tow delivery remain
separate. The native visit harness preserves original tree save paths and checks
that an unpatched constructor can load them.

`coverage --assembly <Assembly-CSharp.dll>` inventories native concrete pilot
types, directly constructed command types, role entrypoints and command execution
entrypoints against all declared source bindings. It reads metadata only.
`--require-bound` fails if bindings are missing; this is a minimum reference
coverage gate, not a semantic completeness assertion. Reports contain no machine
paths. The full behavior model is incomplete even when every scenario passes.

The default retriever's 49 model checks cover conditional scenario expectations, known native conflict
and bounded patched contrast, unknown-value handling, independent vanilla/overlay
objects, invalid fact rejection and recursive-predicate rejection. They validate
this reference's consistency, not the game. Existing native/live evidence stays
linked separately rather than being inflated into additional game test coverage.
Other models report their own counts. Target-lifecycle expectations additionally
assert exact trace termination so a blocked event cannot silently truncate a
scenario that was expected to finish.

[Assembly revalidation](../../Navigation/BehaviorGraph/REVALIDATION.md) explains
the dependency manifest, immutable accepted snapshots and audit commands. Run
`dotnet run --project Tools/AIBehaviorGraph -c Release -- assembly-self-test`
for synthetic metadata-fixture checks, separate from the graph consistency checks.
