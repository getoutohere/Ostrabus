# People-only trigger search metadata

November 10, 2026. Implemented in `TriggerClassificationPatch.cs`.
Custom-builder feature: `trigger-classification` (performance).

## Defect and repair layer

Native `CondTrigger.CheckRequiresHumans` increments an instance `nRecursion`
counter without decrementing or resetting it. Repeated `PostInit` calls eventually
log possible recursion and cache false even for an acyclic direct `IsHuman`
requirement. The captured session emitted 12,032 such startup warnings.

The result controls the search domain in `Ship.GetCOFirstOccurrence` and
`Interaction.GetCOSInRange`. False can cause an unnecessary general-object scan;
an incorrect true can exclude legitimate object targets.

Simply balancing the counter is insufficient. The native metadata routine also
treats `IsHuman OR IsSolid` as human-only and ignores negation. Shipped examples
such as `TIsEmbarkTarget` explicitly permit non-person targets. These are correct
asset expressions; changing their JSON would change authored behavior to work
around a defective C# classifier.

## Scope

One Harmony prefix replaces only `CheckRequiresHumans`, not `Triggered`, trigger
JSON, conditions, interactions, candidate validation, or random rolls.

- AND is people-only if a mandatory direct or nested positive requirement proves
  `IsHuman` or `IsPlayer`.
- OR is people-only only when every positive alternative proves that restriction.
- Negation, numeric alternatives, unknown children, cycles, and nesting at the
  safety bound retain general searches. Robot alternatives also remain general;
  this patch does not assume that every robot is in the native people collection.
- Forbidden predicates do not authorize narrowing. This deliberately favors a
  broad search over an unproven exclusion.

Native property caching and `PostInit` still own metadata publication. Traversal
uses the linked dependency dictionaries, not previously cached child booleans.
A thread-local memo dictionary is reused and cleared in `finally`, releasing all
trigger references after each computation. No polling, scene hooks, or save data.

## Validation

`Tests/TriggerClassificationCheck` runs against the shipped assembly under bundled
Mono. It reproduces both original defects and passes 2,308 checks, including
reinitialization, AND/OR/NOT target preservation against the native evaluator,
nested predicates, cycles, unresolved children, numeric alternatives, deep chains,
and scratch-reference cleanup.

The offline asset pass covers 5,620 raw definitions, classifying 399 as people-only
in approximately 2 ms on this machine. This is metadata processing time, not a
measurement of gameplay FPS or all definitions generated during native loading.
Mono's per-thread allocation counter is unavailable; warm code reuses its memo
storage, but no reliable Mono byte-allocation measurement is claimed.

```powershell
dotnet build Tests/TriggerClassificationCheck -c Release
dotnet run --project Tests/SaveJsonCompatibility/Host -c Release --no-build -- Tests/TriggerClassificationCheck/bin/Release/netstandard2.1/TriggerClassificationCheck.dll
```
