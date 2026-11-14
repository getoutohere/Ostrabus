# Interruptible Navy patrols and faithful command persistence

Bug fixes for the shipped 1.0.1.10 C# scheduler and queue serialization. Native
combat, threat, damage, surrender, support and travel evaluators retain authority.
The [investigation](PATROL-PRIORITY-INVESTIGATION.md) records the original evidence
and the historical enqueue attribution limit for O-P5U.

## Ordinary patrol admission

`AIShip.AddCommand` now puts a raw native FlyToPatrol assigned to a native
NavyPilot into that pilot's existing MainBehavior patrol slot, instead of its
overriding PriorityQueue. BeatManager's faction staging therefore retains its
waypoint, actor and native movement while allowing the existing root selector to
evaluate combat/threat/support first.

The actual command instance moves; route/fuel progress is not copied or reset.
The normal mission resumes at its patrol stage. An active reference to the old
ordinary patrol is updated if necessary. No target, velocity, fuel or faction is
changed by this admission repair.

The native Navy tree shape is checked. Other pilot classes, derived/custom pilot
trees, ordinary FlyTo commands, OneShotWrapperCommand callbacks and urgent Dodge
orders retain native queue admission. In particular a pirate's FlyToPatrol then
LockPirate script is preserved. Encounter placement and busy-actor recruitment
remain the separate [local encounter policy](Encounters/README.md).

## Existing saves

After the entire native behavior tree loads, an isolated raw Navy FlyToPatrol
is moved from the current or waiting queue slot into ordinary MainBehavior.
Migration after the full tree load matters: otherwise subsequent Main restoration
could overwrite the recovered patrol's trip/fuel state with an older mission.

Only one isolated native Navy patrol is migrated. Mixed scripts, multiple queued
orders, other pilot roles and marked one-shot callbacks are preserved. Older
unmarked data cannot reveal whether a command originally came from SFF; the
migration does not pretend to recover that missing provenance. It repairs the
known ordinary Navy mission category instead. No recurring runtime sweep is used.

This covers O-P5U's observed isolated queued patrol on reload if it remains in
that condition. It does not force a fight or promise survival: the native AI may
fight, surrender or retreat based on its actual sensors, damage and other state.

## SFF continuation persistence

Native AIShip.FFWD queues a OneShotWrapperCommand around its continuation leaf.
The native saver drops the wrapper, so a later load can promote that one-call
callback into a full-length priority command.

The save repair writes the native leaf payload unchanged, followed by a small
versioned `OstrabusOneShot` marker in the existing JsonCommandPayload array. The
queue loader recognizes an adjacent matching marker and reconstructs a native
OneShotWrapperCommand containing the native leaf and its original SaveData.
When the callback wraps a command already in the ordinary tree, the marker also
records its native tree path. Loading reconnects to that same leaf, preserving
shared trip/fuel watermarks instead of constructing a second independent ledger.
Only the loader's temporary input is encoded; saved leaf data is not contaminated
with mod metadata. Native leaf types without saved state are also supported.

This correction applies across pilot roles. Unknown mod-owned nodes and custom
save layouts retain their own/native handling. The repair does not drop pending
callbacks, replay them repeatedly, invent replacement behavior or skip fuel work.

Without this feature, the native loader silently ignores the unknown marker and
loads the original leaf and normal tree state. Saves require no mod-owned CLR
types or new JSON fields to load. Removal returns continuation behavior to the
vanilla limitations; it does not retain the correction in an unpatched game.

The queue loader also restores waiting commands with EnqueueCommand rather than
EnqueueUrgentCommand. Native save traversal writes FIFO order; prepending on load
reverses it. The current command remains distinct from the waiting queue. This
preserves scripted order rather than introducing a new priority policy.

## Runtime and packaging

Five native methods are patched: AIShip.AddCommand, AIShip.LoadBehaviorTreeSave,
AIShip.GetBehaviorTreeSaveData, OneShotWrapperCommand.GetSaveData and
QueuedCommandsNode.LoadSaveData. There is
no new RunAI/frame hook, world scan, actor registry, polling or Blackboard state.
Admission uses bounded tree/identity checks without per-call allocations.
Serialization work and metadata allocations occur during saving/loading only.
A temporary per-thread save context identifies the tree containing a callback;
nested saves restore their caller's context, and a finalizer clears it even if
saving throws. It is not a persistent pilot collection or additional game state.

The custom builder exposes `ai-patrol-priority` as a standalone bugfix, with no
dependency on CeresNavigation, Orbital or the encounter simulation policy. The
queue-loader transpiler validates its native local/factory/enqueue layout and
rejects an incompatible method instead of guessing instruction sites.

## Validation

The health branch's separate native failed-send defect is repaired by
[surrender offer accounting](SURRENDER-COMBAT-LOOP.md). The original priority
tests used controlled leaf results and did not establish that SurrenderOffer
reported failed messages correctly. Its dedicated suite now executes that leaf
and the native selector/sequence continuation with the production repair.

[Lost-contact combat cleanup](PATROL-TARGET-HANDOFF.md) repairs a separate early
return before native neutralization. Its production constructor adapters are
also enabled in the queue and Ceres interaction regression modes below; dedicated
Navy/Blockade suites execute the real detection and cleanup commands.

`Tests/AiPlannerDiagnosis`, mode `patrol-queue`, first runs 32 unpatched native
diagnosis checks, then enables the production patches. The combined 122 checks
cover emergency preemption, damage/surrender, urgent dodge, pirate scripts,
legacy current/waiting migration, stale Main trip protection, repeated one-shot
save/load, commands without saved state, FIFO with repeated command types,
shared leaf identity, failed-save context cleanup, unpatched loading and rejected
IL drift. Native scheduler/factory/serialization
methods execute under the bundled Mono runtime; world-dependent leaves receive
controlled outcomes. These are not live combat/sensor predictions.

Mode `patrol-interactions` runs the existing 174 Ceres/navigation regression
checks with this repair enabled, including pending returns, native combat/urgent
priority, retreat stages, save/load, candidate selection and fuel accounting.
`Tests/ScavFuelDiagnosis`, mode `fuel-queues`, adds 28 mixed-accounting assertions
with flight fuel, phantom-control and queue repairs enabled together. It verifies
the extended saved fuel watermark, shared callback/Main ledger and actual native
fuel debits after continuation and subsequent ordinary flight.

Build the harness in Release, then run either mode with
`Tests/SaveJsonCompatibility/Host`. The updated [encounter graph](BehaviorGraph/Encounters/README.md)
records the admission, starvation and persistence boundaries; its conditional
scenarios complement the native tests rather than substituting for them.

Offline checks pass. Live post-reload behavior has not yet been validated.
