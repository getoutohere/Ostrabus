# Alternative crew work selection

Experimental module, namespace `Ostrabus.AlternativeWork`. Default **off**.

Console commands: `altwork on`, `altwork off`, `altwork toggle`, `altwork status`.
`altwork window N` changes the rolling supply window live (1..30; default 8),
persisted as `[AlternativeWork] SupplyWindow`. Lowering it prevents further filling
until outstanding work falls below the limit; it does not delete native tasks.
The command updates `[AlternativeWork] Enabled` in the existing BepInEx configuration;
the choice survives restarting the game. No custom save data is written.

## Integration

The module replaces candidate collection within vanilla `ClaimNextTask`, retaining
the game's interaction validation, build prerequisites/material delivery, hauling,
reloads, suit preparation, claiming, work execution and cancellation. Duty priority
and duty ordering remain vanilla; explicitly owned tasks take precedence over
unowned tasks within a duty. Within each ownership group, supplied work and work
without construction inputs precede queued material deliveries, which precede
unsupplied construction blueprints. Each group sorts by estimated travel cost.
Lot contents are a readiness hint; partial supplies still require native validation.

During alternative selection, required-input validation first checks stored inputs
and then carried inventory, including accessible nested containers, using native
visitors and material matching. If carried materials suffice, the full ship/docked
inventory scan is skipped. Otherwise it continues that search with the same input
requirements, without rolling input loot again. Persistent-item preferences,
delivery tasks and their prerequisite counter remain native. Provisional carried
probes suppress verbose failure messages only while advancing their iterator,
restoring the flag before yielding; wider-search failures retain native messages.
Statistical validation and validation outside alternative selection use the original method.

Native batch-feed lookup also starts from accessible carried contents. Two scoped
call-site redirects inside `CollectBatchFeedTasks` defer its ship inventory scan
until native material matching actually misses. The same candidate list then
expands once with native ship/docked traversal flags, excluding primary materials
and sources already assigned to other deliveries. Loot is evaluated once per
native candidate task. Room/range, condition, capacity, task ownership and vessel
permission checks still apply. There are no global `Ship.GetCOs` hooks; with altwork
off, both redirected calls invoke their original operations. Scope references are
cleared in a finalizer even when native lookup throws.

A separate incremental distance field prepares routes while crew are working.
It uses vanilla tiles, walkability, shore-leave and airlock permissions, without
modifying the character's live pathfinder or reserving a future task. Walk distance
is weighted by 3 on EVA tiles and adds 4 at closed door tiles, making long exterior
detours less attractive. These are selection preferences, not changes to movement.
Use-point range and line of sight participate in scoring; native checks still
decide whether an interaction can execute.

A rolling supply planner also prepares a small batch while existing work executes.
Successful automatic build/feed claims arm it without doing material searches in
the claim hook. Update then scans 32 candidate orders per slice and probes nearby
same-ship blueprints against carried contents only. By default it keeps eight nearby
supplied/delivery/active target groups in its window, choosing the closest eligible
blueprints within the native feed batch's eight-tile radius. It uses native
`QueueMissingInputDeliveries`; native feed batching selects actual materials and
claims deliveries when the character dispatches them. No future build order or
physical material is reserved by the planner. Missing carried materials do not
trigger speculative searches of the wider ship. These are advisory prerequisites,
not proof that tools, routes or the eventual material supply will remain valid.

The first delivery never waits for a batch to fill. The planner yields while
existing work runs; if it cannot get ahead, normal dispatch still works. Another
worker's newly prepared/active target counts against the window on recheck.
Ownership, manual-only orders, permissions, cooldowns, target replacement and
orders removed while a probe yields are checked before adding deliveries. Native
batch-feed candidates also honor explicit task ownership. Work priority still
uses native duties; preparing prerequisites never forces their execution.
Lists exceeding 2,048 pending orders in a duty use normal dispatch without
background preparation. This is not a dependency planner that proves removing a
particular wall unlocks work.

Incomplete, capped or ambiguous routes remain fallback candidates, rather than
being declared impossible; vanilla can find another use point or prepare a suit.
Consequently this is a bounded travel heuristic, not a global optimal schedule.
Changes to worker tile, vessel, permissions or the game's navigation revision
invalidate the field. Finishing construction can therefore change subsequent
selection. Final dispatch repeats native validation and checks that the task is
still unclaimed and its vessels remain eligible.

## Permissions and shared state

- `HasShoreLeave()` filters both the work target and explicit haul destination.
  A worker without shore leave cannot use other ships even as a route shortcut.
- Airlock restrictions constrain route exploration through native `IsWalkable`.
  They do not blanket-ban every job across an open, docked passage.
- Failed alternative selections retain the normal status/error display, but their
  five-second retry cooldown belongs to the worker, not the entire crew.
- Material waits are tagged only when native validation requests prerequisite
  deliveries for that order. After a feed-item interaction successfully adds a
  new item to the target's lot, those tagged cooldowns are cleared for that target
  across workers. Native selection then checks materials, route, ownership and
  permissions again. No orders are forced or characters interrupted. Other
  failure cooldowns, other targets, cancelled/failed deliveries, and vanilla
  behavior with the module off remain unchanged. Partial deliveries allow a
  fresh check; they do not imply all required materials have arrived.
- Batch haul/feed candidates also receive the vessel permission filter.
- Automatic delayed claims (including returning from suit preparation) receive a
  permission and path recheck. Explicit manual direct orders keep vanilla behavior.
- Each selection coroutine owns its `taskUpstream`/`nSpawnedPriorities` scratch
  values. The shared fields are installed only during its `MoveNext` call and
  restored afterward. Vanilla's prerequisite signal is preserved even when a
  delivery task already exists: a positive counter diverts the failed install
  back to prerequisite work instead of suit preparation. The per-worker retry
  cooldown excludes the failed install on restart, and vanilla retains its
  bounded restart loop. `AddTask` is not patched by this module.

## Switching and lifetime

Switching off affects new selections immediately. Already executing work continues;
an in-flight selection drains through its existing validation instead of being
aborted with a borrowed path origin or temporary `IsAirtightFake` condition in place.
Cancellation also restores temporary state owned by the wrapper. Once these
selections finish, alternative scratch state is cleared. A worker's suspended supply
probe is disposed on cleanup, manual control or a switch to unrelated claimed work;
its bounded candidate references are cleared after completion. Existing normal game
tasks and suit pledges remain normal game tasks and pledges.

There are no mod-created threads or tasks. Per-frame planning visits one crew member
and aims for a **0.5 ms cooperative budget**, checking between batches of 16 nodes.
One supply-planning slice runs for that crew member before travel expansion; native
helpers and the bounded outstanding-work checks can exceed the cooperative budget.
At most four cheap null yields from a selection are folded into a frame, also
checking a 0.5 ms budget; explicit yielded objects are preserved. A single native
validation or graph expansion batch can exceed that budget. It is not a hard
frame-time guarantee. No planner work runs when the flag is off or no work orders
are pending.

Scratch storage is reused and bounded to 32 crew, 8,192 settled tiles per field,
and 128 retry keys per worker. Native validation and candidate collection still
allocate; the module is not allocation-free. Crew scratch is swept each wall-clock
second while planning, and cleared on scene unload, work-manager replacement,
plugin teardown or a completed switch off. No persistent static task/ship/crew
history is kept. A task cap or incomplete field falls back to normal validation;
crew beyond the scratch-state cap use vanilla selection.

## Inspection and A/B testing

`GET http://127.0.0.1:18764/workorders` reports the flag, in-flight selections,
selection/claim/failure counts, permission exclusions, carried-input matches and
wider-input searches, `supplyProbes`, `supplyPrepared`, `supplyPlanningMs`,
`supplyWindow`, `carriedOnlyFeedBatches`, `expandedFeedBatches` and per-worker
`supplyPlanning`, accumulated planner and
selection time, per-crew settled tile counts, capped/completed fields and the last
suggested target. Suggestions are advisory; they are not reservations. Counters
reset with the simulation/scene, not with each toggle. Supply planning time is a
subset of total planner time. Supply probes are separate from dispatch input-match
counters; prepared deliveries are counted only when present in native pending work.

`GET http://127.0.0.1:18764/workorders/scheduler` captures the native scheduler
on demand: processed time, active dispatcher owner and nested iterator suspension
states, pending requests, priority debt, company crew clocks/tickers, and current
interaction cancellation/manual/urgent/UI flags. Lists and iterator traversal are
bounded. It reads stored fields on the Unity thread without advancing enumerators,
calling priority updates, or changing scheduler state. There is no background
sampling. Take snapshots before kiosk use, while stuck, and after recovery; keep
the same altwork setting until those captures are complete.

Compare equivalent starting saves with the flag off and on. Useful live cases:

1. Crew with disembark disabled alongside permitted crew; jobs across a dock/tether.
2. Airlock use disabled, testing both an open passage and an exterior route.
3. Nearby deconstruction that opens an interior route versus a long EVA detour.
4. Many ready build orders, and a separate run with missing construction materials.
5. Two workers competing for one order, suit preparation, hauling and cancellation.
6. Toggle off while selecting and while executing a job; unload/reload afterward.

Uninstall/repair and earlier material/helmet corrections have been exercised live.
Live traces confirmed the carried fast path and elimination of unrelated blueprint
detours, but exposed slower steady construction from one-at-a-time supplying.
The original four-target rolling window improved retained steady cadence from
27.7 to 23.0 game seconds/floor. The carried-first native batch lookup and new
eight-target default have offline coverage; their live throughput is unmeasured.

Offline regression checks use the game's bundled Mono and actual Harmony targets:

```powershell
dotnet build Tests/AlternativeWorkCheck -c Release
dotnet run --project Tests/SaveJsonCompatibility/Host -c Release -- Tests/AlternativeWorkCheck/bin/Release/netstandard2.1/AlternativeWorkCheck.dll
```

Checks cover incremental weighted routes against an independent reference on 100
random graphs, disconnected/capped fields, cooldown key identity, coroutine scratch
restoration and binding the patch classes to the shipped game methods. Floor-batch
checks cover supplied/delivery ordering, ownership and cooldowns. Carried-input
checks use the native deferred matcher with controlled inventory/reachability,
covering carried-only, missing, partially carried and persistent inputs, one loot
roll per validation, prerequisite dispatch and disabled/outside-context isolation.
Rolling supply checks cover prompt foreground claims, window limits/refilling,
closest eligible targets, competing planners, revoked/removed tasks, missing
carried inputs, manual/off cancellation, cleanup and queue-exception isolation.
Both four- and eight-target windows are covered. Native batch-feed checks verify
zero ship scans for carried materials, one expansion for partial/world-only
materials, exclusion/source uniqueness, capacity, active tasks, disabled behavior
and exception cleanup. Console/config window controls are covered as well.
They do not emulate Unity movement or full live crew behavior.
