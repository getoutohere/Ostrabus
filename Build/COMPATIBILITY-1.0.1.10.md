# Ostranauts 1.0.1.10 compatibility

Reviewed against the previous 1.0.1.9 assembly, complete decompiled source trees
and vanilla `StreamingAssets/data` JSON trees. Raw diffs and test logs are local
artifacts under `obj/Compatibility-1.0.1.10`; game source/assets are not bundled.

## Upstream changes

There are 19 modified C# files, two new C# files and one modified JSON file.
The JSON change adds the `QuickWaitWork` interaction.

The main change replaces blocking work claims with per-character
`WorkClaimJob` iterators. The native scheduler advances these under a separate
work-manager frame budget. Jobs own tentative task reservations, spawned-task
tracking, prerequisite state, fake airtightness, wait/wakeup, completion and
cancellation. `CondOwner` requests/takes claim results rather than directly
running the old `ClaimNextTask(CondOwner, IteratorResult<Task2>)` iterator.

Pathfinding adds pending-search tracking, explicit origin overrides for haul
route probes, and more complete path-cache keys (origin, destination, range,
target CO and airlock permission). The new asynchronous check restores live
pathfinder fields before awaiting. There is also a correction for movement
within the current impassable tile.

Other changes preserve pending tickers during save serialization, discard stale
scheduler debt/overhang on catch-up, drain pending end turns before Super Fast
Forward, prioritize oxygen/CO2 emergency actions, correct suit lookup across
docked ships, force manual pump updates, and cull irrelevant line-of-sight
blocks. Options gain a work-manager frame-budget slider.

Changed files: `AIShipManager`, `CondOwner`, `Condowner.Utils/Scheduler`,
`CondOwnerVisitorCatchUp`, `CrewSim`, `DataHandler`, `GasPump`, `GUIFFWD`,
`GUIOptions`, `JsonUserSettings`, `Ostranauts.Pathing/PathMemory`,
`Ostranauts.Pathing/Vector2Int`, `Ostranauts.Pledges/PledgeSurviveCO2`,
`Pathfinder`, `PledgeSurviveO2`, `PledgeWearSuit`, `StarSystem`, `Visibility`
and `WorkManager`. New files: `Ostranauts.WorkManager/WorkClaimJob` and
`Ostranauts.WorkManager.Models/TaskCandidate`.

## Mod adaptations

- Retarget altwork's selection hook to the new private
  `ClaimNextTask(WorkClaimJob)` method and obtain the actor from the job.
- Apply alternative ranking **after** native collection, preserving the new
  reservation and time-scale filters. Continue native validation/claiming.
- Remove the wrapper's old scratch/fake-airtight ownership and null-yield
  folding. Native claim scopes and scheduler now own these operations; restoring
  old wrapper state would discard progress before the native job could save it.
- Keep background material preparation away from native-reserved tasks, both
  before probing and after yields.
- Expose native claim jobs and work budget through the existing read-only
  scheduler inspection endpoint.

Altwork off retains the native 1.0.1.10 claim lifecycle. No new settings, save
fields or world-state migrations are introduced. This adaptation targets
1.0.1.10; its new native job type does not exist in 1.0.1.9.

## Validation and limits

Result: clean Debug rebuild with zero warnings/errors; all 110 startup patch
classes bind with zero failures/fallback warnings. All 26 offline test suites
pass, including 2,815 altwork checks, 599 crew-rest checks, 4,850 orbital checks
and 1,175 ranged/self-hit/meat/trace checks. The Debug DLL/PDB are packaged for
deployment; no installed plugin or save was changed by the review.

Use a full `Rebuild` after replacing same-version assembly references. The
Debug library is rebuilt against the installed 1.0.1.10 assembly. The permanent
patch-binding audit processes all attributed patch classes against the real
assembly and rejects binding failures and fallback warnings. An offline shader
ID fixture supplies the Unity-only call needed during native type initialization;
it is not a production patch.

Regression suites run on the game's bundled Mono, except the .NET 8 recorder
test, which runs on its own host. Altwork fixtures now initialize the native
job collections and exercise real reservation filtering, job scope transfer,
fake airtightness and cancellation. The excursion test's wall-clock boundary
is explicitly controlled to avoid dependence on first-call JIT timing; its
production code is unchanged.

Existing bug fixes remain relevant: the meat definitions, ranged damage method,
container mass paths, automatic rest/restore selection and ship navigation/
orbital methods were not replaced by this update. Native scheduling, path-cache
and emergency-action improvements are retained rather than overwritten.

Offline binding and fixtures cannot establish live movement timing, user-facing
planning latency or every multi-character interaction. The first live load should
check the startup log and a normal altwork sequence with multiple workers.
