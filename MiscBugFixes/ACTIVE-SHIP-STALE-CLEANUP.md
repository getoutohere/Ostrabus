# Active ships retain retirement markers and lose living crews

Investigated 2026-11-09; lifecycle repair implemented 2026-11-11, game 1.0.1.10.
The separate crew-recycling policy gap remains open.

## Observed sequence

The player stayed docked to the apartment at OKLG and used two one-hour Super Fast
Forward advances. The current process then logged stale cleanup of O-5FWA and
O-ENB at 16:43:13 local time, immediately followed by the deaths of Ajigbotoso
Vazquez and Hailey Herring. No collision message establishes these deaths.
Recent AI events show both controllers working shortly before removal.

The earlier `home` save has epoch 65632876040.6814. Both hulls already have living
pilots, saved active controllers and these persisted ShipCO conditions:

| Hull | IsStale epoch | Age at home | Expires after home |
|---|---:|---:|---:|
| O-5FWA | 65632869891.4317 | 102.49 minutes | 77.51 minutes |
| O-ENB | 65632871874.2067 | 69.44 minutes | 110.56 minutes |

Thus the second one-hour skip crosses both deadlines. Removal waits for the
cleanup sweep, which is scheduled every 120 real seconds, and for other eligibility
checks such as being detached. Simulation time determines marker age; real time
determines when the sweep gets launched. This explains delayed deletion after
the SFF operation rather than an immediate event during it.

The markers predate the collision patches. The observed deaths happened after
deployment; these are distinct facts. SFF advances time across a native disposal
deadline, rather than providing evidence that our trajectory correction killed
these pilots. The native-only offline reproduction needs no Orbital code.

## Root lifecycle defect

`AIShipManager.UnregisterShip(AIShip)` stamps IsStale on qualifying unlocked hulls.
Normal station retirement uses that method after moving crew and hiding the hull.
`AIShip.FindShipForFaction` can subsequently select the hidden hull for another
voyage. Police/scavenger and other pilot factories call `AIShip.ResetShipState`,
which restores visibility, flags and controls **without cancelling IsStale**.
`AIShipManager.AddAIToDict` also leaves it intact. A successfully reactivated hull
therefore retains the previous retirement's disposal deadline.

`StarSystem.CleanupStaleShips` checks timestamp age (three hours), visit age, load
state, docking and player/plot protection. It does not check active registration
or healthy NPC crew. It sets bRemove even when that controller is working.

Other native writers exist: `BaseCommand.TryInstantCleanup` backdates a marker
after certain command failures; CrewSim docking/undocking refreshes existing
markers, and DestroyAndReload preserves them. We did not recover the original
historical call stack for these two markers. The failed cancellation on reuse
and the sweep's eligibility failure are reproduced using shipped methods.

## Why cleanup killed the pilots

In `StarSystem.Update`, a bRemove hull is passed to `TryRecycleCrew`, then hidden,
unregistered from markets and destroyed with isDespawning:false **regardless of
the recycling return value**.

`TryRecycleCrew` selects a station with spare population capacity and an exact
match between the **hull's** faction list and a station's faction list. Owner IDs,
the AI home station and the pilot's affiliations are not considered. The captured
pre-death AI snapshots show both affected hull faction lists empty. Consequently
neither hull can match any station, regardless of available space or the pilot's
valid affiliations. The routine returns false and leaves the pilots aboard.

`Ship.Destroy(false)` sets Kill on remaining human/robot COs before destroying
them. Their logged deaths are the disposal path, not demonstrated combat,
depressurization or impact damage.

This second behavior needs to be distinguished from the primary defect: a valid
active voyage should never have reached retired-hull disposal in the first place.
Broadly teleporting crews or disabling all retirement would change native policy
and would not repair the lifecycle transition.

## Implemented correction

`ActiveShipRetirement` extends the existing crew-validated AddAIToDict hook.
A newly registered, visible, valid crewed AI hull cancels its obsolete IsStale
marker. Both reserve reuse and saved-controller restoration reach this boundary.
The same controller merely being re-enqueued or moved between region buckets
does not cancel a later command-failure marker. Auto, Station and non-crewed
controller types, hidden reserves, non-AI hulls and pre-existing bRemove flags
are excluded. The existing crew guard still rejects invalid crewless flight.

CleanupStaleShips runs its eligibility scan on a background Task. A narrow
transpiler changes only its final bRemove assignment and associated log: the
worker rechecks the current retirement timestamp under the same short lock used
for cancellation. If it read an obsolete timer before reactivation, it cannot
subsequently commit that cancelled decision. A refreshed, unexpired timestamp
also stops the old decision. The original player/plot/docking/visit protections,
three-hour threshold and selection logic remain native. An unknown worker or
write-site shape fails patch binding rather than modifying unrelated removal.

No blanket bRemove clearing is performed. A hull whose deletion was already
committed before registration remains queued, whether from stale cleanup,
scuttling, arena cleanup or saved state. This is not a queued-deletion rescue.
Normal unregistration writes a new timer, and later deliberate command-failure
cleanup remains effective even for an active controller. The faction matching
and unconditional destruction after failed crew recycling are unchanged.

One additional worker transpiler is used; registration reuses the existing
hook. There is no periodic mod scan, per-frame hook, retained ship reference,
custom save state or added background job. The lock is taken on valid new
registrations and final stale-mark decisions, not on each AI tick. Custom
builds include the repair with `ai-crew-integrity`.

The pre-fix census also found active hull markers on O-BFZ2, O-QH5 and O-YVZC.
O-QH5's marker was expired but docking protected it. Loading the new build
cancels these markers when their valid controllers register; it cannot restore
NPCs already destroyed in the save. No live state was mutated during development.

## Evidence and offline reproduction

[`Tests/StaleShipDiagnosis`](../Tests/StaleShipDiagnosis/README.md) executes the
shipped lifecycle, cleanup and recycling decisions under bundled Mono, first
unpatched and then with the actual production registration/worker hooks (42
checks). It includes deterministic old-read/reactivation and timer-refresh
interleavings, reserve reuse, saved-marker cancellation, role coverage,
same-controller re-enqueueing, legitimate retirement and removal exclusions. Private
native save extracts and live captures remain in ignored obj/stale-* files;
the fixture retains only anonymous scalar ages. Earlier faction evidence is
obj/live-survey/O-5FWA.ai.json and O-ENB.ai.json; cleanup logs and recent events
are in obj/KlegSurvey-20261109-164352. The home save was read directly in its
archive, with no new large JSON extraction or save modification.
