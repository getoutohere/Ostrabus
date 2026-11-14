# PASS and shared time advancement: investigation

Status: diagnosis and integration proposal; production code is unchanged.

## Reproduced failure

Two PASS trips produced two large world updates, the largest about 1,500 seconds.
`/orbital` recorded those updates but zero GUI SFF updates. Ten local Mercury
civilians subsequently appeared 15,000-89,000 km from HQCH while still targeting
HQCH or its nav/security buoys. They had previously been within the local ring.

Replaying O-RRK's saved RCS route through the shipped assembly, without any
Ostrabus production patches, produced an extra **42.673509 km/s**. Its observed
first post-ferry route began with **42.685178 km/s** of excess velocity relative
to the old endpoint. The replay excludes external gravity and the intervening
normal-frame interval. Its acceleration components match the old route's final
braking acceleration multiplied by the entire 1,500-second step.

`NavData.TimeAdvance` writes the completed endpoint pose/velocity and leaves its
last acceleration, then clears NavData and returns false outside GUI SFF.
`ShipSitu.TimeAdvance` consequently performs ordinary integration for the full
step again. The existing SFF destination-preservation patch is inside the native
GUI-SFF completion branch and does not execute on this PASS path.

## Native entry points and authored intent

The native calls to `StarSystem.Update` are the ordinary `CrewSim.Update`,
`GUIFFWD.FFWD`, and `CrewSim._TeleportCO`. The latter handles PASS through:

`ENCFerryPickupAllowTeleport` JSON (`strTeleportRegID = FERRY`) ->
`Interaction` -> `Ferry.TimeChangeForCO` -> `CrewSim.TeleportCO` ->
`_TeleportCO` -> `system.Update(timeJump)` -> destination spawn and crew transfer.

The assets correctly express boarding/travel/arrival. The defect is in C# time
advancement, not the interaction definitions. Local PASS time is range / 0.5 km/s;
long-range PASS time is 80 hours per AU. Long trips can exceed a day.

Native GUI SFF sets its UI flag and drains `Scheduler.CompletePendingEndTurns`
before the world update. PASS does neither. Native AI role catch-up has its own
one-hour threshold (`GUIFFWD.FFWDTIMEUNIT`); a 25-minute ferry jump does not call
individual role callbacks. That threshold must not simply be removed.

## Coverage audit

| Mechanism | Current boundary | Required integration |
| --- | --- | --- |
| Orbital force correction | Applies to native gravity queries independently of UI | Retain unchanged. |
| Fine free coasting | Rejects steps over 60 seconds | Use the bulk propagation path for shuttle jumps. |
| SFF motion / partial AI travel | Started only when GUI SFF is active | Share a real discontinuous-time-advance context. Preserve combat, locks, hidden hulls and player-course exclusions. |
| Planned routes | Deliberately excluded from replacement motion | Fix successful native completion separately; enabling the motion context alone cannot fix the launch bug. |
| Economy and freight timeline | Same GUI-gated world prefix; accepts 60 s-24 h | Reuse for shuttle intervals; handle short and multi-day durations explicitly without duplicate production/dispatch/delivery. |
| Alternative collision history | GUI flag plus an existing 120 s history cutoff | Mark shuttle discontinuities explicitly; do not bridge sweeps across the jump. Preserve native combat/projectile behavior. |
| Torch fuel settlement and braced groups | Independent of GUI flag | Retain ownership, one-step group movement and paid-clock settlement; add ferry-boundary regressions. |
| Recovery Holds | Marked native queue plus SFF role guard | Retain; no automatic release or new target search. |
| Crew loading / transfers | Native teleport coroutine after world update | Preserve authored behavior; drain pending end-turn work before jumping, as native GUI SFF does. |

## Proposed repair

1. Introduce a scoped shared time-advance identity for actual GUI and shuttle
   world jumps. It must not fake `GUIFFWD.Active`, remain active during loading
   yields, or turn ordinary frames into world scans. Honor existing module flags.
2. Repair successful route completion at the shared playback/physics handoff:
   account for elapsed trajectory time once, stop the completed engine command,
   and propagate only the remaining coast interval. Retain the mission target
   and distinguish successful completion from abort/manual-control paths.
3. Start the existing motion/economy machinery and collision-history handling
   from that context. Keep native planning, role decisions and asset evaluators
   authoritative; do not repair velocities afterward or replace role policy.
4. Support long PASS transfers within bounded processing windows. Do not just
   repeat `StarSystem.Update` blindly: native FFWD can enqueue deferred commands
   that must receive a proper AI handoff between windows. Verify behavior against
   equivalent corrected SFF intervals, retaining economy event ordering.
5. Report jump source, duration and completion counts through existing bounded
   inspection reports. No always-on per-contact journal is needed.

Validation must cover short trips, the recorded 1,500-second case, hour-boundary
and multi-day trips, free/docked groups, route completion inside a jump, multiple
ship roles, recovery holds, freight accounting, exceptions/nesting and player
torch/braced courses. The numerical replay proves the defect; it is not yet
validation of this proposed integration.
