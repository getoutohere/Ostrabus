# Autonomous target selection and disembark permission

## Cause

In 1.0.1.10, `CondOwner.GetMove2()` collects targets across docked ships for
learned needs-driven interactions and random leisure/social interactions. It
chooses a target before `CheckWalkDeferred()` checks movement permissions.
Characters forbidden from disembarking can spend a decision cycle evaluating
and selecting a foreign-ship target they cannot use, even with eligible local
targets available. This is separate from work-order reservation filtering.

## Change

`AutonomousDisembarkPatch` transpiles the generated `GetMove2.MoveNext` method
at two candidate-list merge points, before learned candidate pruning/sampling
and before random fallback sampling. Both points include self, person-spec,
inventory and cached/deferred world-search branches. Unexpected method shapes
fail patch binding rather than silently installing partial coverage.

For an interaction requiring local movement, the filter calls native
`HasShoreLeave()` and removes targets whose known ship differs from the actor's
current ship. It preserves list order and duplicates among remaining entries.
Native emergency, long-draft and visiting-NPC permission exceptions still apply.
Permission changes are read on each filtering call; there is no permission cache.

Manual interactions, remote/no-walk interactions, self and carried items are
preserved. A bounded ownership-chain walk handles carried items with stale ship
references without scanning the actor's inventory. Unknown locations or malformed
ownership chains remain subject to native validation. Airlock permission and
general reachability are deliberately outside this patch's scope.

This is always active, independently of AlternativeWork. It changes no shared
interaction validation, manual command handler, work-manager thread or save data.

## Cost and lifetime

The helper adds no allocations, coroutine wrappers, per-frame hooks, path searches,
cached results or retained game-object references. Unrestricted and remote cases
return immediately. Restricted candidate lists are compacted in place in O(n),
with bounded extra work for ownership chains of foreign targets. Removed tail
references are cleared by `List.RemoveRange`.

Native world/condition searches still run. Only the decision-local combined list
is filtered: modifying the shared condition-search cache could incorrectly hide
targets from a later remote interaction using the same condition trigger.

The expected benefit is fewer wasted candidate evaluations and failed decision
cycles. No live frame-rate improvement has been measured.

## Offline validation

`Tests/AutonomousDisembarkCheck` executes the shipped selection coroutine under
the bundled Mono runtime with adapters for Unity/world services. It first
reproduces forbidden target selection without the patch, then checks both native
selection branches with the production transpiler: inaccessible candidates never
reach interaction/path validation, available local targets are selected, permission
changes and emergencies restore native selection, and cached search results are
not modified. Policy checks cover manual/remote interactions, carried objects,
unknown locations, ordering and malformed ownership chains.

```powershell
dotnet build Tests/AutonomousDisembarkCheck -c Release
dotnet run --project Tests/SaveJsonCompatibility/Host -c Release --no-build -- Tests/AutonomousDisembarkCheck/bin/Release/netstandard2.1/AutonomousDisembarkCheck.dll
```
