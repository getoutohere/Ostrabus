# Civilian visit completion and asteroid prospecting

## Defect and owning layer

Native civilian Main is Undock -> TargetNewIfNone -> FlyTo -> Dock -> Hold.
An expired Hold starts another wait below 1 kg RCS without lift. It can repeat
forever although peaceful shallow AI Maneuver explicitly permits fuel-free RCS.
Route execution, candidate admission and physical fuel checks remain distinct.

The Ceres investigation found nine empty civilians still at Main/Hold after
2.36 more game hours: six moored to asteroids, three detached. None had an active
help wait. Their targets' Derelict damage flag does not make Asteroid-class
contacts shipwrecks.

The cutoff, CivilianPilot SFF callback, timing and home interface dispatch are
C# mechanisms. Reviewed SOS assets define eligibility, refusal and dispatch;
those remain authoritative. No installed JSON or rescue dispatcher is changed.

## Repair: existing civilian-visits feature

Only ordinary native Main/Hold is registered, weakly associated with its pilot.
At expiry, a peaceful shallow AI with a live nonhostile faction home can finish
despite its abstract empty tank. Actual fuel, planning, flight, docking and native
next-job selection are unchanged. A fueled prospector can select another asteroid;
low fuel takes the native home branch. Missing/hostile faction homes do not
release empty ships toward unsafe destinations; those recovery gaps remain.

CivilianPilot.GetTargetHome is new virtual instead of override, so interface
calls reach BaseCharacter's unfiltered method. The repair uses the existing
civilian faction-home method when it resolves a station, retaining the native
fallback otherwise. Private-owner/no-home cases are covered.

Fresh visits reset the native update timestamp, preventing prior travel time
from consuming a new wait. SFF preserves Main/Hold instead of resetting it or
queuing another Hold. After native SFF docking succeeds and physical attachment
exists, Main enters its own Hold. Failed docking, active routes, unrelated work,
reactions and combat retain their paths. No new teleport/docking shortcut is
introduced. A detached completed visit releases its obsolete goal.

Materialized/combat propulsion, explicit help waits, manual/player navigation,
scripted/other queued Holds and other roles retain their fuel constraints. The
scavenger-specific repair composes at the same native cutoff.

## Simulation choice: civilian-asteroid-visits

Actual direct Asteroid-class mooring draws one uniform duration of **300-7,200
game seconds (5 minutes-2 hours)** per ordinary civilian visit, throughout the
system. Short stays represent failed prospecting; longer ones represent pretend
mining. Civilian-to-civilian, station and detached waits retain native 100-650
second timing. Next-job choice is unchanged. Genuine help retries do not redraw
an hour-scale mining stay.

This separate simulation feature requires civilian-visits. Bugfix-only custom
builds keep native durations. Full/Recommended include the simulation feature;
no PDA control is added.

## Saves and performance

Remaining time stays in JsonShip.fAIDockingExpire. Native Hold payloads carry a
small primitive marker, started/completed flags and last update epoch. Unpatched
native trees can load these payloads: no custom saved type or conversion is
required. Legacy visits retain remaining time; malformed payloads use that
countdown, and foreign payloads are retained.

No Update callback, fleet scan, worker or journal is added. Weak state dies with
the existing Hold/pilot. Warmed bookkeeping allocates zero bytes in the fixture;
payload allocation occurs only during serialization. Home/faction lookup occurs
at expiry for an empty civilian, not every waiting tick. Duration draws occur
once per fresh visit.

## Validation

Run AiPlannerDiagnosis with civilian-waiting through bundled Mono. It executes
native Hold/Main, selection branches, Civilian FFWD, home dispatch, native command
JSON and unpatched-tree restoration. Cases cover bounds, repeat/consecutive visits,
SFF arrival/failure, saved clocks, another asteroid versus low-fuel home, shared
one-shot/Main completion, exclusions and warmed allocations. Physical docking,
faction scores, trajectory proposals, random next target, captain/trigger lookup
and Unity identity are fixture boundaries.

The traffic graph separates cutoff, safe home, interface dispatch, clock and
duration policy. It does not invent completed returns or rescue outcomes.

Live follow-up on 2026-11-14 confirmed that all nine previously sampled empty
Main/Hold civilians resumed FlyTo with active routes and were closing on their
station targets. Stored RCS fuel remained zero. Successful native unmoor events
were observed for the formerly attached visitors, including the final waiter
O-CJ4B. Their saved short visit remainders expired normally. No inspection API
refueling, navigation, unmooring or pruning intervention was used.

That observation validates release of the existing stuck visits, not completed
station arrivals. Fresh 5-120 minute visits, another-asteroid selection and the
new clocks' save/load/SFF behavior remain covered offline; those cases have not
yet been confirmed live.
