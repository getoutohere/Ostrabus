# Mass bookkeeping audit: characters, stacks and dynamic items

Audited against the 1.0.1.10 assembly and installed JSON definitions. The two
preventive fixes below are implemented. The explicit `massfix` command now also
repairs reviewed anatomy and human aggregates; it does not run automatically or
reset every character to a fixed naked weight.

## Confirmed: stack merging loses carried mass

Native path: `Container.AddCO` -> `StackOnInsideItem` -> `CondOwner.StackCO` ->
`SetIsInOurStack` / `StackFromList` -> container list cleanup.

`SetIsInOurStack` accounts for incoming cargo through `AddCOSimple`. After
merging, the original head becomes a member of the new head's stack. The
cleanup loop removes its redundant container-list entry and also subtracts
its mass. That mass still belongs to the container; this is an extra debit.

Offline native reproduction, with our existing `StackMassPatch` enabled:

- A 10 kg crate holds two 20 kg floor pieces: 50 kg total.
- Merge another 20 kg floor into that stack.
- Three pieces exist and individually remain 20 kg each, but the crate stays
  at 50 kg instead of 70 kg.
- A 100 kg character carrying that crate stays at 150 kg instead of 170 kg.
  Native encumbrance follows the incorrect aggregate mass.
- An intermediate 2 kg backpack reproduces the same missing 20 kg through
  both ancestor levels.

This differs from the already-fixed double debit when *removing* a non-head
stack member. The merge cleanup debit occurs outside those removal call sites.
It also explains why `massfix` can legitimately increase a container's mass.

`StackMergeMassPatch` removes that one erroneous mass call when discarding an
alias after a successful merge, preserving native ownership transfer and contents.
The transpiler replaces the call with stack pops, adding no runtime callback.
Partial/full incoming stacks and cross-container merges now conserve mass in
the native offline harness. Existing head-pop and removal regressions still pass.

## Confirmed: condition-driven item mass bypasses its carrier

The installed extinguisher definition `ItmToolFireEx01` has a 2.5 kg base and
11 units of `StatFireChem`. `StatFireChem.aPer` invokes `CONDStatFireChemPer`,
which adds/removes 1 kg per whole unit. This uses:

`Condition.AddAmount` -> `Loot.ApplyCondLoot` -> `CondOwner.ParseCondEquation`
-> `AddCondAmount("StatMass", ...)`.

Unlike `AddMass`, that path does not propagate a mass delta to the owning
container/character or update character encumbrance. Native `UpdateCondRecords`
only maintains the condition collections; it does not repair mass bookkeeping.

Offline reproduction using the installed condition/loot definitions and native
condition, loot and equation methods:

- A 100 kg character carries a full 13.5 kg extinguisher: 113.5 kg aggregate.
- Consume one unit. The extinguisher becomes 12.5 kg; the carrier stays at
  113.5 kg instead of 112.5 kg.
- Applying the native removal debit of 12.5 kg leaves a 101 kg character and
  correspondingly excessive encumbrance. The original body was 100 kg.

The final subtraction demonstrates native removal accounting; this second
fixture does not execute the complete slot/UI operation. The missing propagation
is already established before removal. The fixture adapts scalar condition
storage, not the condition-to-loot-to-equation control flow.

Other condition loot also changes mass (including strength/feebleness and item
overlays). These are legitimate changes, so resetting arbitrary objects or
characters to the base JSON mass would be unsafe.

`LiveMassConditionPatch` adds a mass-only branch immediately after
`Condition.AddAmount` updates its condition records. It uses the actual count
change and calls native `AddMass` on the parent to update the ancestor chain.
`LiveMassAddPatch` marks the native mass write so it does not propagate twice,
and passes the actual before/after delta onward rather than a requested amount
that may have been clamped. Native character encumbrance and mass-label code are
reused for the changed object; its zero-delta refresh skips ancestor recursion.
This preserves the Venus dolly calculation instead of copying its formula.
Affected materialized ships' mass caches are invalidated as part of bookkeeping.

`LiveMassInitializationPatch` scopes synchronous `CondOwner.SetData` calls and
always restores its thread-local depth, including failures. Direct propagation
requires a finished game load, a materialized `Edit`/`Full` ship and unfrozen
conditions/rules, and excludes the editor and temporary `BarterZoneShip` copies.
Unplaced objects, shallow inventories and construction/default/overlay setup
remain on the native path. Our existing barter transfer/save-back fixes retain
their separate frozen-copy bookkeeping.

Cycles or excessively deep parent chains do not start new recursive propagation.
Nonfinite deltas are not propagated. No static collection retains game objects;
the current writer/refresh references are restored in `finally` blocks.

These patches apply to runtime NPC and player-character mass, including direct
body/trait changes and carried equipment. They do not replace saved mass values,
add save fields, or recalculate an arbitrary object's empty mass.

## Runtime cost

There is no polling or inventory scan. Non-mass condition updates add only an
inline name check and branch, not a Harmony prefix/postfix call on every stat.
Native mass writes add bounded scalar bookkeeping and use existing ownership
links. Direct changes check the parent chain before propagation. No scratch
lists, per-object tracking tables or coroutine wrappers are allocated. Native
mass-label string formatting still allocates when bookkeeping refreshes labels;
this is not a claim that the entire native operation is allocation-free.

## Floor pieces and characters

A subsequent [character reconstruction investigation](CHARACTER-MASS-RECONSTRUCTION.md)
verifies the crew core, tests anatomical ownership in native slot code and audits
saved humans. It supersedes the earlier tentative body-weight conclusions;
the resulting repair is implemented in `massfix`.

The confirmed floor-stack case corrupts owner aggregates, not the individual
pieces' mass. No independent intrinsic floor-mass corruption has been established
by this audit. Floor definitions and overlays include legitimate mass differences;
different-looking or damaged variants must not be indiscriminately normalized.

Character `StatMass` includes carried equipment. An apparent body-weight problem
can therefore be stale inventory bookkeeping, including a residual that survives
dropping the offending object. `massfix` now reconstructs reviewed core/trait and
anatomical mass using actual contents; it does not blindly assign a complete body
weight from a character definition.

## Reproduction harness

`Tests/DollyMassCheck/StackRemovalDiagnosis.cs` runs baseline and corrected native
merge paths alongside existing removal regressions. `DynamicMassDiagnosis.cs`
does the same for condition-driven mass and tests nested propagation, direct
body changes, clamped deltas, cache invalidation, exclusions, cycles and exception
cleanup. Unity scene/UI/registry services and scalar condition storage are
fixtures; live-write tests retain native `Condition.AddAmount` and the production
condition observer. The initialization scope is tested with a bounded `SetData`
adapter, not a full offline scene reconstruction.

Validation: 475 standard mass bookkeeping checks (710 with the detached saved
inventory graph), 105 remote-barter checks with source
input SHA-256 unchanged, 77 Venus dolly checks including direct mass changes and
planned tree repair,
and 146 Harmony classes bound with zero failures or warnings. The latter two
suites install the mass and related production patches together. No live reload
or frame-rate claim is implied by these offline results.

```powershell
dotnet build Tests/DollyMassCheck -c Release
dotnet run --project Tests/SaveJsonCompatibility/Host -c Release --no-build -- Tests/DollyMassCheck/bin/Release/netstandard2.1/DollyMassCheck.dll
```

These checks do not modify a live game or a saved archive.
