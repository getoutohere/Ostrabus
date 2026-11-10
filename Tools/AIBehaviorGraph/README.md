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

The 49 model checks cover conditional scenario expectations, known native conflict
and bounded patched contrast, unknown-value handling, independent vanilla/overlay
objects, invalid fact rejection and recursive-predicate rejection. They validate
this reference's consistency, not the game. Existing native/live evidence stays
linked separately rather than being inflated into additional game test coverage.

[Assembly revalidation](../../Navigation/BehaviorGraph/REVALIDATION.md) explains
the dependency manifest, immutable accepted snapshots and audit commands. Run
`dotnet run --project Tools/AIBehaviorGraph -c Release -- assembly-self-test`
for synthetic metadata-fixture checks, separate from the graph consistency checks.
