# Saved-world retention study

Read-only offline analysis of a native save ZIP. It does not load the game,
invoke native gameplay methods, rewrite archives, or propose automatic deletion.

```powershell
dotnet run --project Tools/SaveBloatStudy -c Release -- --self-test
dotnet run --project Tools/SaveBloatStudy -c Release -- <save.zip> obj/save-bloat.json [<game-data-directory>]
```

The optional data directory enables comparison against the native `ai_training`
assets. Reports include the training files' hashes. This comparison does not
expand third-party asset overrides: it asks whether each saved condition/action
history has the same iteration count and single-precision total as an entry in
one of those files, with no saved target-specific scores. A match is repeated
baseline data, **not** proof that removing it preserves native loading behavior.

## Coverage and units

- Exact compressed and uncompressed ZIP entry sizes, grouped by payload type.
- Main-world and per-hull top-level field sizes and collection counts.
- Object-state counts, duplicate IDs, definitions, largest records, messages,
  social person-spec copies, relationships, and interaction histories.
- Links from `aItems`, `aCrew`, `aShallowPSpecs`, and `shipCO` into saved object
  states, followed through `aStack` and `aLot`.
- Structural parent chains inside each hull, including missing parents and
  cycles; missing parents are also checked against all other saved layouts and
  object-state tables.
- Market buckets whose IDs lack both a physical hull and a deferred world
  object, retaining actor and stock-entry counts rather than assuming lost cargo
  is disposable.

ZIP entry sizes are bytes. Individual native JSON spans are UTF-16 character
counts; field spans include spacing after the colon. Object subfield totals use
compact JSON character counts. These units are deliberately distinct: a compact
character estimate is **not** a promised reduction in compressed save size.

The reader streams each object record separately instead of retaining the entire
world JSON. It keeps identity/link indexes and one non-CO top-level field at a
time; a large market or system field still needs temporary memory. It does not
extract the archive to disk. SHA-256 verification before and after the scan
rejects a source changed by a concurrent save. The source cannot be the report
path, and duplicate archive member names are rejected.

## Interpretation limits

An item appearing in `aItems` can itself have an invalid parent; therefore a
state-to-layout link alone does not establish that the item is usable. The
separate parent analysis catches that distinction.

Conversely, missing explicit object state is not automatically corruption:
unmaterialized hull layouts and generated template parts require native
load-state/first-visit interpretation. The report labels this as absence of
explicit state, not an error count. Missing parent links also need native review
for possible cross-hull ownership, restoration or future reconstruction.

The analysis does not emulate damage mode switches, slots, asset triggers,
dynamic condition references, future plot queries, or every consumer of saved
IDs. No object becomes safe to delete merely because it is dead, hidden, old,
uncrewed, unloaded, or outside a structural root set. Natural celestial bodies,
manufactured station references, physical hulls and deferred world objects remain
distinct categories.

See [the first findings](FINDINGS-1.1.0.4.md) and the existing
[inventory dependency study](../InventoryLoadAudit/README.md).

## Validation

`--self-test` exercises LF/CRLF spans and Unicode, nested ownership, isolated
cycles, absent and cyclic layout parents, separately recorded state, external
plot references, deferred world objects, absent markets, dead-character
retention, source hashes, and exact versus changed training histories. Fixtures
are temporary archives; cleanup is restricted to their uniquely created
temporary directory.

No plugin code, production configuration or deployment changes are required.
