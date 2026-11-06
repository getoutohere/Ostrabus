# Walking into the wall being worked on

`WorkRangePathPatch` is always on, independent of altwork.

## Evidence and cause

Ayden's captured conduit install on `OKLG|RES_1` stalled at its material delivery
prerequisite. The queued `Walk` targeted tile
`c11e57ae-6c9e-496e-bdf4-e7c303aab88b`, marked `IsWall` and `IsObstruction`.
The installation placeholder was `5ab9a155-f023-4071-8cab-9c5e0424dc55`.
Trace and activity captures are saved under `obj/WALL-*`.

The native `Pathfinder.GetInRangeResult` checks whether the origin is within
`fRangeGoal` of a standing candidate, then can return `[origin, candidate]`.
Its callers treat that as a movement route, including when a fallback candidate
is the solid wall containing the work target. The shortcut bypasses the normal
path search's walkability checks. This was reproduced offline with the native
method. The captured API did not expose the complete live pathfinder internals;
the source defect reproduces the observed invalid walking destination.

There is a second distinction in this method: a candidate is a proposed standing
position near the requested target. Being within working range of that candidate
does not establish that the requested target is within working range.

## Correction

A postfix changes only successful, nontrivial shortcut results for positive-range
interactions with an existing shipboard target and worker. It checks distance to
the actual requested destination (`tilDest`) and native block-based line of sight
to the interaction target. The origin must be passable, neither wall nor fixture,
and not a burning hazard. If valid, it replaces the route with `[origin]`, updating
destination, disembark and path-length metadata. Native `SetGoal2`/`SetGoal2Async`
then retain the current tile as the walking destination; `CheckWalkExecute`
does not queue unnecessary movement. The interaction target remains unchanged.

If those checks fail, the shortcut returns null and its native caller continues
normal pathfinding. Exact zero-range movement, movement without an interaction
target and already stationary results retain their native behavior. The patch
does not bypass native interaction, material, permission or work execution checks.
Both synchronous and asynchronous route generation call the same patched method.

The patch reuses the freshly created native result and tile list. There is no
new background update, retained object reference, save data or configuration.
It does not change an already queued walking route until the game replans it.

## Verification

`Tests/WallWorkDiagnosis` passes 23 offline checks with the bundled game Mono.
The unpatched native method first reproduces the solid-wall route. With the
production Harmony hook installed, checks cover the stationary result and its
metadata, native `SetGoal2` adoption, preserved work target, wall and walkable
candidates, blocked LOS, insufficient range, near-candidate/far-target mismatch,
obstructed/fixture/burning origins, missing destination and direct movement.
Transforms, world tile lookup, visibility, hazard lookup and UI are fixtures;
native shortcut logic, result handling and synchronous route adoption execute.

```powershell
dotnet build Tests/WallWorkDiagnosis/WallWorkDiagnosis.csproj -c Release --nologo
dotnet run --project Tests/SaveJsonCompatibility/Host -c Release -- Tests/WallWorkDiagnosis/bin/Release/netstandard2.1/WallWorkDiagnosis.dll
```
