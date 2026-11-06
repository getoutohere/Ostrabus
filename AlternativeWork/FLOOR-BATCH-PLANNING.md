# Large floor batches: planning pauses

Diagnosis and correction, November 5, 2026.

`Collect` ranks ownership, travel cost, then native pending-list position.
Floor installation allows a two-tile interaction range; several targets can
therefore tie at zero walking cost. Native `QueueMissingInputDeliveries` appends
delivery work with the same target and interaction range. Earlier floor blueprints
can repeatedly outrank an already queued delivery, preparing additional orders
instead of supplying one and starting construction.

`Tests/AlternativeWorkCheck/FloorBatchChecks.cs` exercises the actual collector and
comparator with a controlled open 10x10 geometry fixture. Thirteen zero-travel
install preparations precede an existing zero-travel delivery. Geometry, permission
checks and material execution are fixtures; this is an ordering reproduction,
not a full native execution or timing benchmark. Native `ClaimNextTask` permits
six restart passes, so a preparation-only selection can end without claiming work.
The original offline suite passed 2,500 checks.

Implemented correction: within each native duty and ownership group, supplied work
and work without required construction inputs precede deliveries, which precede
unsupplied blueprints. Travel cost sorts within these groups. Lot contents indicate
potential readiness; native validation still rejects insufficient inputs, blocked
routes and unavailable tools. Permissions, cooldowns and the prerequisite counter
are preserved. The carried-input validator uses native inventory visitors and the
native deferred matcher, avoiding the wider scan when carried inputs suffice.
On a miss, it continues the wider native search using the same rolled requirements.

The subsequent live worktrace confirmed five other unsupplied floor validations
between completed material delivery and claiming a supplied floor: 24.24 in-game
seconds, approximately 1.54 wall-clock seconds at 16x. The user confirmed all
materials were in containers carried by Ayden. Source inspection confirmed the
native validator nevertheless traversed ship/docked inventories before matching.
Capture: `obj/FLOOR-LIVE-TRACE.json`, events 177-183. These observations precede
the fix; reduced live pause duration has not yet been measured.

The revised offline suite passes 2,603 checks, including the native deferred
material matcher under controlled inventory/reachability. No game save was edited.

Follow-up live capture (`obj/FLOOR-SLOW-workorders.json`) showed both carried-input
counters at zero despite active floor work. The first fast-path guard excluded
`bVerboseTrigger`, but native WorkManager sets that flag for every selection.
The ordering change was active; the carried-input optimization was not.
Removed that exclusion. Only the provisional carried probe disables verbose
reporting while advancing its native matcher, with a finally block restoring the
flag before each yield/completion/exception. Wider searches retain native reporting.
Regression fixtures now use the WorkManager's actual verbose=true setting and
check restoration at every yield. Live benefit of this correction remains unmeasured.

## Rolling supply window

Later live traces confirmed 28 carried matches and zero wider searches. However,
altwork started consecutive floors about every 27.7 game seconds, versus 18.0 in
the vanilla sample. Vanilla first performed a long preparation phase, then batched
29 deliveries in about 21 game seconds. Immediate single-floor supplying removed
the preparation stall but lost that batching advantage. Samples are steady
installation cadence, not complete batch timings.

`WorkSupplyPlan` now arms after successful build/feed claims and prepares nearby
blueprint material prerequisites during existing work. It yields after 32 candidate
scans or native matcher yields, probes carried contents only, and keeps a four-target
window counting pending supplies and active work. Normal first dispatch does not
wait for it. Native feed batching handles the pending delivery orders and selects
their actual materials; no build task or source item is reserved by the planner.
Native dispatch remains responsible for tools, pathfinding and final availability.

Inspection adds supply probe/prepared counts, planning time and per-worker active
state. Offline checks cover scheduling/window/refill, ownership/permissions,
competing preparation, revoked/removed work, missing carried inputs, coroutine
cleanup and shared scratch restoration after exceptions. Live throughput is not
yet measured for this rolling-window implementation.
