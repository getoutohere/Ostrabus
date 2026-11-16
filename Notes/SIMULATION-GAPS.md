# Simulation gaps

## NPCs can be obliterated by administrative ship cleanup

Confirmed 2026-11-09, game 1.0.1.10. Cleanup can erase living NPCs aboard
undamaged, visible ships, without combat, an accident or another plausible
in-world cause. This undermines persistent characters and emergent encounters.

Two related failures require separate treatment:

- **Active hulls retain obsolete disposal markers.** Reusing a retired hull does
  not cancel IsStale. Advancing time can therefore delete an active crewed ship.
  Fixed 2026-11-11 at crew-validated registration, including saved controllers;
  the background worker rechecks cancelled/refreshed timers before marking.
  Pre-existing committed removal flags are deliberately not revived.
- **Crew preservation is conditional, while destruction is unconditional.**
  TryRecycleCrew needs a station with capacity and a matching hull faction.
  Empty hull factions make relocation impossible, even when the pilot has valid
  affiliations and a home station. A failed relocation does not stop destruction;
  remaining humans/robots are killed. This remains a broader simulation gap even
  after preventing accidental cleanup of active ships.

Follow-up: audit administrative retirement and region cleanup for this pattern.
Distinguish physical destruction from retiring a hull, and preserve legitimate
NPC continuity through native station/population mechanisms where appropriate.
Do not mask failures with arbitrary teleportation, replacement pilots or blanket
immortality. Determine the intended ownership, affiliation and capacity policy
before changing crew relocation behavior.

Evidence and native offline reproduction:
[active-ship cleanup investigation](../MiscBugFixes/ACTIVE-SHIP-STALE-CLEANUP.md)
and [stale lifecycle checks](../Tests/StaleShipDiagnosis/README.md).

## Station retirement can delete a living bounty target

Confirmed 2026-11-16 on native 1.1.0.0 with E-HJPR / It's All Academic,
piloted by bounty target Liam Charles. Live history records a Navy controller
engaging O-PSS6, then firing twelve shots at O-7BV, selecting MVOL, returning
through FlyToPatrol and unregistering. The hull remains hidden in reserve with
`strAIDespawnedAt = MVOL`; this is not a missing hull or a new phantom-thrust case.

The player's `jank` save contains the decisive native ship log:

- Epoch 65634307961.275: `Liam Charles transferred from MVOL.`
- Epoch 65634308632.2372: `Liam Charles vanished.`
- Same epoch: `Docked & Despawned with MVOL.`

`CrewSim.DockAndDespawn` emits that vanished message only on its crew rejection
branch, then directly calls `CondOwner.Destroy()`. It rejects a person when
`TIsValidStationDropOff` fails or the person's summed reputation with station
affiliations is `Dislikes`. The distributed trigger forbids IsDead,
IsDerelictVictim and IsOKLGPirate. MVOL's affiliations are Xinhua and XinhuaCiv.
The exact failed predicate is not established: Liam was created after the
available pre-incident save, and no retained actor record was recovered after
destruction. Do not report faction hostility or a specific condition as proven.

The live crew profile still reports alive but resolves to no CO. The actor is
also absent from the `jank` root CO array. The bounty remains saved as
`ALL_Navy_High`, target region MVOL, wanted dead or alive, payout 370,000 dead /
914,000 alive and expiry 65634975998.5267. It has become an orphaned bounty,
not an expired contract or evidence of a successful arrest.

The destructive decision is native C#. The existing attached-retirement hook
only guards connected civilian visits and does not replace this rejection rule.
No repair or resurrection was performed on the affected actor. Implemented
2026-11-16 as an explicitly requested [simulation policy](../Navigation/BOUNTY-PRESERVATION.md):
active bounty arrivals survive administrative rejection through the existing
station transfer branch, retain existing ownership of their assigned hull, and
keep that hull through regional/stale disposal. Native reboarding, custody,
expiry, hand-ins and physical death retain their normal roles. This is an
intentional exception to administrative drop-off rejection, not a claim that the
authored rejection trigger itself is incorrect. The broader ordinary-NPC cleanup
gap above remains outside this feature.

Private evidence: `obj/HjprAudit/` captures live AI/combat history and saved hull
log/actor absence. `jank` archive SHA-256:
`8E346BEC67853300BBB85E866035DBF36736019371A2DF756ABE7354F842E50D`,
unchanged across read-only inspection. No game data or installed DLL was changed.
