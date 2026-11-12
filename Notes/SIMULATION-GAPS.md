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
