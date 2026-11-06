# Paused hauler inspection: pooled hulls, not active pilots

Epoch 65631764416.3972; simulation paused after reload.
Both O-APJH (VORB Hauler 24) and O-D5GM (VORB Hauler 94) have:
- hiddenFromSystem=true;
- zero crew profiles;
- no registered controller matched to the live Ship instance (18 examined);
- no plotted route, no current thrust, stale scan target VORB.
They have respectively 88.24% and 90.83% RCS fuel and lie 204.12 / 156.35 km
from VORB by stored-coordinate inspection.

AIShip.FindShipForFaction explicitly selects hidden owner-faction hulls for reuse.
HaulerDeployingPilot.CreateAIShip reuses one, resets it, places it at the station,
adds crew and refuels it. Ship.HideFromSystem setter also enables bNoCollisions;
CollisionManager.CheckCollisions rejects bNoCollisions objects before collision
processing. These observations identify the two current objects as inactive pool
hulls, not operating haulers with an omitted active command.

Earlier statements that they were returning to VORB inferred activity from a stale
scan target and were wrong for this paused state. Their SFF impact outcomes should
not be used to infer actual failed piloting or actual collisions/destruction.
The surviving O-APJH observed after the skip may have been reused in the meantime;
that transition was not captured and is not established here.

The appropriate next correction is to exclude hidden/inactive pool hulls from SFF
trajectory and impact replacement. Do not assign pilots or infer travel merely
from their saved scan targets. Capture hidden/collision flags in SFF diagnostics
and add this real inactive-hull case to the offline regressions.
