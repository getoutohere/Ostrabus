# AI velocity recovery and native hold

GET /ships/E-KRQD/match-velocity?referenceId=HQCH previews the intervention;
POST applies it. A reference can be a visible ship/nav buoy or a celestial body
such as Mercury. POST matches stored reference velocity and preserves position,
fuel, crew, ownership and controller. It cancels the old route and angular motion
without charging fuel. This is a debug intervention, not a braking burn, approach,
orbit lock or continuous velocity matching.

**POST leaves the ship on a native queued Hold.** It never automatically resumes
target selection. Local pilots lose their obsolete target; haulers retain their
destination and task stage behind the hold. Native priority orders take
precedence over ordinary ship mission and combat decisions. The response includes
held, wouldHold, freshMission, retained destination and velocity/delta-v.

The hold uses native Hold.SaveData, the native priority queue and an indefinite
native docking timer. A scalar marker distinguishes it from unrelated orders
and ensures native command serialization retains the leaf. Native LitJson and
the compatible reader restore the command and timer. No custom command class or
saved mod object is introduced. Normal physical damage and regional lifecycle
rules still apply.

One SFF-only prefix preserves this specific marked hold across role callbacks.
Native police SFF otherwise relocates even a ship with an active priority queue.
The guard retains no ship/crew references and adds no per-frame callback. It
belongs to Custom Builder's inspection feature; without that feature, native
role SFF rules apply to saved native holds.

## Explicit release

GET /ships/E-KRQD/release-hold checks the marked hold without planning or mutation.
POST calls native selection/route-feasibility mechanisms before releasing it.
Failed planning returns 409 and keeps the hold. Accepted target, route and native
fuel prediction are applied before AI resumes. Haulers retain and validate their
current delivery goal. Planning uses native primitives, avoiding the destructive
failure continuation of TargetNewIfNone.RunCommand.

## Eligibility

Connected/tethered, anchored, grounded, hidden, player, station, projectile or
actively fighting vessels are refused, along with ambiguous controllers, active
reactions and unrelated priority orders. The queue must belong to the native
behavior tree. A ship already scheduled for removal is refused. Recovery clears
an obsolete stale timestamp on an otherwise valid registered vessel. Incomplete
controller scans return 503 and can be retried. The existing bounded Unity-thread
request queue owns mutations; there is no background recovery controller.

## Initial recovery failure

The first live E-KRQD intervention matched velocity, then resumed native AI.
It found no feasible target, unregistered and marked the hull stale. Native
cleanup destroyed it and killed bounty captain Landon Olorunleke. Bounty status
did not protect him. The original tests checked immediate state but omitted this
continuation.

Corrected tests reproduce failed selection, native unregistration and cleanup
eligibility, then verify that the queued hold prevents that path across AI
updates, SFF and JSON restoration. Recovering this lost ship now requires a save
taken before the failed intervention.
