# Patrol commands starving combat

Investigation against the shipped 1.0.1.10 assembly, 2026-11-13. No production
patch or live intervention was included in the initial investigation. Its repair
is now implemented and documented in [PATROL-PRIORITY.md](PATROL-PRIORITY.md),
with offline validation; it has not yet been live-tested. The original scheduler and serialization
reproductions pass 32 checks in `Tests/AiPlannerDiagnosis`, mode `patrol-queue`.

## The ownership error

`Ostranauts.Ships.AIPilots.NavyPilot` constructs this root selector:

1. PriorityQueue
2. CombatResponse
3. ThreatResponse
4. SupportResponse
5. MainBehavior: Undock -> TargetNewIfNone -> FlyToPatrol -> Dock

`Ostranauts.Ships.Commands.SelectorCommand.RunCommand` returns at its first
Running or Finished child. `QueuedCommandsNode.RunCommand` keeps a Running child
as its current command. Consequently a queued FlyToPatrol blocks all subsequent
branches for the duration of that flight. `AreThreatsNearby`, combat initiation,
damage evaluation, surrender and ordinary retreat cannot run through this tree.
An existing combat flag alone cannot bypass the queue.

This priority is useful for an urgent Dodge. The defect is admitting an ordinary
patrol mission into the same queue without preserving emergency preemption.
Native MainBehavior already implements an interruptible patrol. Reproduction
shows it yields immediately to combat, and to the native damage/surrender branch
when the relevant leaf conditions succeed.

## Two independently reproduced admission paths

### Ambient faction staging

`BeatManager.FactionShip` and `BeatManager.FactionBattle` assign an encounter
waypoint, then call `AIShip.AddCommandLoot("FlyToPatrol")`. That creates a new
patrol and enqueues it ahead of every emergency branch. Fresh and recruited
actors can both be affected. The current local-encounter patch retains these
native calls: its physical eligibility/placement policy does not repair priority.

The counterfactual test retains the assigned waypoint and lets native MainBehavior
execute it, without inserting a priority order. Native TargetNewIfNone retains
that waypoint; flight runs normally and combat can interrupt it. No replacement
threat evaluator or combat policy is needed.

Other callers need separate treatment. `BeatManager.Pirate` queues FlyToPatrol
followed by LockPirate as an ordered sequence; `AIShipManager.CheckLocalAuthorityScenario`
queues a police arrival patrol. Removing every queued FlyToPatrol or globally
reordering every priority queue would change those mechanisms too.

### SFF one-shot serialization

`AIShip.FFWD` asks the pilot for a continuation. `NavyPilot.FFWD` returns the
previous leaf, and AIShip queues `OneShotWrapperCommand(leaf)`. In an uninterrupted
process this calls the leaf once, returns Finished, and releases the queue.

`OneShotWrapperCommand.GetSaveData` delegates to the wrapped command at the same
path. Its inherited SaveData is null, so its base save emits no wrapper record.
For a patrol waiting in the queue the saved path is simply:

`/BehaviorTree/PriorityQueue/Queue/FlyToPatrol`

`QueuedCommandsNode.LoadSaveData` recreates a raw FlyToPatrol from that path.
After a JSON round trip, the formerly one-shot callback keeps running until the
whole patrol finishes. There is no saved wrapper provenance to distinguish it
from an intentionally queued patrol. Merely adding a wrapper constructor would
not repair this representation: the serialized wrapper is absent.

Reproduction also confirms wrapper loss for FlyTo, Hold and FlyToPath. Their
specific gameplay consequences depend on their completion semantics; this is a
general serialization defect, not a Ceres-only combat rule. A repair must retain
the continuation and its one-shot semantics, rather than silently dropping all
pending SFF callbacks.

Native `AIShipManager.FFWD` enqueues these callbacks without running each queue
to completion in that method. This is a save taken with a pending callback, not
concurrent serialization inside a command invocation. Normal AI dispatch later
drains the callback if no save/load intervenes.

## O-P5U evidence and attribution limit

The latest examined save, `apartment`, has O-P5U in MainBehavior at child 2,
with its FlyToPatrol state under MainBehavior and no saved PriorityQueue entries.
O-P65 also has an ordinary patrol in that save.

The live read-only capture at epoch 65633967678.204361 has O-P5U in:

`QueuedCommandsNode -> FlyToPatrol -> FlyToPath`

There are zero waiting commands; the current child is an unwrapped FlyToPatrol.
Its InCombat flag is false, Enemies is empty, and NeedsRepairs is absent despite
recorded damage/casualties. About 169 seconds remain on the sampled plan. This
is a persistent queued patrol, not a pending one-shot wrapper. Available logs
do not identify its historical enqueue caller. The latest save does not prove
that this particular queue was created by wrapper loss on that load.

Native execution tests establish the blocking mechanism without relying on that
historical attribution. Once a whole queued patrol finishes, combat becomes
eligible on the next dispatch; the inhibition is not intrinsically permanent.
P5U has not yet been observed through that release while the game remains paused.

## Layer and repair direction

Reviewed assets: `plot_manager/pm_settings.json` supplies the faction event weight;
`star_systems/star_system.json` supplies regional Navy caps, influence ranges and
faction assignments. These interfaces do not specify the Navy selector order,
queue admission priority or continuation serialization. This is C# ownership,
not an asset logical flaw. Disabling authored faction encounters is not a repair.

The repair should:

- Submit ordinary faction patrol goals through native ordinary behavior, retaining
  the assigned waypoint and native combat/threat/support decisions.
- Preserve one-shot continuation semantics through native queue persistence,
  independently of actor role. Do not discard callbacks or infer their origin
  from an unmarked legacy leaf.
- Define a narrowly justified recovery for existing queued Navy patrols. Preserve
  unrelated queued scripts, urgent dodges, target state and fuel accounting.

Do not move every PriorityQueue below combat, add a parallel combat controller,
force P5U to attack, or broadly erase legacy queues. The existing blank comms
description is a separate reporting defect documented in CERES-COMBAT-FOLLOWUP.md.

## Offline reproduction

Build `Tests/AiPlannerDiagnosis` in Release, then run its `patrol-queue` mode with
the bundled-Mono host in `Tests/SaveJsonCompatibility/Host`.

The fixture runs native Navy construction, selector/sequence dispatch, queue
admission, AIShip.FFWD, wrapper execution, JSON payload round trip and native
tree restoration. World-dependent leaves receive controlled outcomes. It tests
scheduling under those outcomes, not sensor detection or live combat damage.
No production Harmony patch is enabled.

An optional second argument is a save ZIP path. This reads the AI manager and
prints only O-P5U/O-P65 records, without changing the archive. For ZIP reading,
the game's `MonoBleedingEdge/EmbedRuntime` directory must be on that command's
PATH so the embedded runtime can resolve its MonoPosixHelper library.

## Behavior graph coverage

The encounter graph stops at patrol assignment; it does not expand scheduling
inside NavyPilot or SFF callback persistence. Its local-patrol scenarios therefore
do not establish combat responsiveness. These transitions need explicit graph
coverage. The encounter graph now expands those scheduling and persistence
boundaries, including the native failure and patched contrast. Sensor detection,
combat physics and eventual ship survival remain external outcomes.
