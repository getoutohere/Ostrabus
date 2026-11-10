# Active ships retain retirement markers and lose living crews

Investigation only, 2026-11-09, game 1.0.1.10. **No runtime fix implemented.**

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

## Proposed correction, pending implementation

Cancel an old retirement marker at successful reactivation/registration of a
visible, valid crewed AI ship. Keep actual retirement/unregistration and command
failure disposal intact. Cover loading existing active controllers with bad
saved markers as well as new reserve reuse. Prefer the existing registration
hook if its validated boundary is sufficient; avoid a periodic rescue sweep.

Do not blanket-clear bRemove: a deletion may have another valid cause. Audit
concurrency with the native background sweep before choosing the final integration.
Treat any independent crew-recycling policy change as a separate decision.

The current live census also found active hull markers on O-BFZ2, O-QH5 and O-YVZC.
O-QH5's marker is already expired but docking currently protects it. No live state
has been changed during this investigation.

## Evidence and offline reproduction

[`Tests/StaleShipDiagnosis`](../Tests/StaleShipDiagnosis/README.md) executes the
shipped lifecycle, cleanup and recycling decisions under bundled Mono. Private
native save extracts and live captures remain in ignored obj/stale-* files;
the fixture retains only anonymous scalar ages. Earlier faction evidence is
obj/live-survey/O-5FWA.ai.json and O-ENB.ai.json; cleanup logs and recent events
are in obj/KlegSurvey-20261109-164352. The home save was read directly in its
archive, with no new large JSON extraction or save modification.
