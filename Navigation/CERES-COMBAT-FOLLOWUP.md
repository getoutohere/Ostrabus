# O-P65 / O-P5U combat observation

Read-only investigation, 1.0.1.10, 2026-11-13. The current process predates the
new faction-encounter and phantom-control patches. No intervention was made.
Evidence is retained locally under ignored `obj/P65-P5U-Investigation`.

At epoch 65633967678.204361 the game is paused. O-P65 (Pizza Time, Galcon Navy)
is running KeepWithinFiringDistance against O-P5U at 9.35 km, with a 9.6 km
engagement range and about 68.5% RCS. Earlier samples show active corrections
maintaining that range, not an abandoned controller.

O-P5U (Nuclear Trooper, CCRE Navy) has a current QueuedCommandsNode child
FlyToPatrol -> FlyToPath, with no waiting commands. Its plan advanced across
samples; ETA fell from 502 to 252 to 169 game seconds. The sampled segment
specifies zero acceleration, explaining the coast. There are no current enemy,
combat, surrender, repair, docking, boarding or retreat states.

Damage is real: ship IsDamaged=1014, Mark Patel dead, Dylan Travis unconscious
with internal bleeding, and Clinton Chen/captain Noomi Wells awake. Native
AmIDamaged flags NeedsRepairs above aItems.Length/10 (about 344 for the prebattle
saved 3443-part hull), or zero RCS count. Current RCS count is ten. Individual
live nav/weapon/ammo condition cannot be certified from a prebattle shallow save.

## Established scheduling mechanism

NavyPilot's root puts PriorityQueue before CombatResponse and ThreatResponse.
A Running queued patrol prevents those later branches, including damage/surrender
evaluation, from running. This explains missing health/combat flags despite damage;
it is not evidence of surrender or destroyed navigation controls. The exact
historical enqueue origin is unproven. BeatManager faction staging is one native
caller of AddCommandLoot("FlyToPatrol"); do not infer every queued command came
from SFF or a mod. The paused game has not yet allowed observation of queue release.

Any repair should distinguish an ambient patrol goal from genuine urgent/scripted
commands. Globally lowering all queue priority could break other authored behavior.

## Independent blank-status defect

Both ships accept requests and send SHIPDataRequestStatusAllow, "Status
transmitted", but the body is literally "Response: ". The asset pipeline is
DataStatus -> GetCurrentAICommandDescription -> Ship.GetCurrentAICommandDescription
-> AIShip.ActiveCommandNameDescription -> active DescriptionFriendly.

KeepWithinFiringDistance never supplies DescriptionFriendly. QueuedCommandsNode
forwards the active command name but not its description, losing FlyToPatrol's
"Patrolling assigned sector" text. These are C# reporting defects, not radio
silence or evidence of damaged comms. No reporting/scheduler patch is implemented
by this investigation.

## Follow-up reproduction

[Patrol priority investigation](PATROL-PRIORITY-INVESTIGATION.md) now establishes
the scheduler defect with 32 offline native-assembly checks, including ordinary
patrol preemption, queued patrol starvation, urgent Dodge priority, and a second
general defect that loses SFF one-shot wrappers across queue save/load. The latest
examined save records P5U's patrol in ordinary MainBehavior; the live ship has a
raw queued patrol. Its historical enqueue caller remains unproven. No scheduler
or serialization repair has been deployed by this investigation.

The [repair is now implemented](PATROL-PRIORITY.md) and built for reload, with
native scheduling, shared callback identity, FIFO and existing Ceres/fuel
interaction regressions. Live post-reload behavior remains to be observed.
