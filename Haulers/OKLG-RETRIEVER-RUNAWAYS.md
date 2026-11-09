# Distant OKLG retrievers — 2026-11-09

Read-only live inspection and comparison of the `fine`, `build` and `undock`
saves found four active retrievers towing wrecks outside normal OKLG operations.
They are not derelicts or victims of the missing police-controller regression.

| Retriever | Approximate live range from OKLG | Tow |
| --- | ---: | --- |
| O-VT1F | 125,279,245 km | H-TZM |
| O-C8U | 221,822,344 km | J-MQR |
| O-XTD7U | 529,954 km | M-YNX |
| O-R2K | 928,676 km | B-SDA |

All four have registered HaulerRetriever controllers, living pilots, zero RCS
fuel and FlyTo commands. Selected O-VT1F has a valid OKLG target reference and
86 route points, but a negative remaining-fuel budget. Its separating velocity
is approximately 2,357 km/s and angular velocity approximately 721 rad/s.
These are broken operating states, not plausible normal retrieval journeys.

O-VT1F and O-C8U already have implausible velocities in `fine`, before the new
player torch-course consumption correction. Later saves show increasing range
and spin. The saves do not preserve the original onset or its call stack.

## Patch attribution audit

- The crew guard has not removed these controllers: all remain registered and
  their pilots are aboard. Police-controller restoration must refuse them.
- HaulerRecovery is restricted to HaulerDeployer; these are HaulerRetriever.
- Ordinary orbital coast replacement excludes docking groups and plotted routes.
- SFF replacement excludes the whole connected group when any member has NavData,
  not just when the synthetic group anchor has a route.
- Player torch-course correction requires a materialized player-owned reactor
  course; these shallow RCS tow groups are outside its scope.

These exclusions rule out the police regression and several direct patch paths.
They do **not** prove that no earlier mod interaction contributed before the
saved states. Do not label this a confirmed vanilla-only historical incident.

## Native SFF transition defect reproduced

Normal retrieval's Main sequence is Undock -> TargetNewIfNone -> FlyTo -> Dock ->
HaulShip -> DockCargoAndDespawn. HaulShip uses AIIntercept2, whose maneuver
updates reach the group's anchor. The successful ordinary hauling path therefore
does not rely on copying a plotted route to that anchor.

HaulerRetrievingPilot.FFWD can resolve Dock while Main is still at FlyTo (index 2),
then return HaulShip. AIShip.FFWD wraps that command in OneShotWrapperCommand.
The wrapper calls HaulShip once, discards its Running result and finishes. Main
remains at index 2. HaulShip has by then selected home and enabled the tow braces,
so Main's FlyTo can run while towing instead of continuing the normal hauling leg.

`Tests/AIShipLifecycleCheck` with argument `retrievers` reproduces this transition
with no production patches installed: six transition checks cover sustained ordinary hauling,
instant pickup, one-shot queuing, home selection, discarded Running continuation
and next-cycle FlyTo on the connected pair. Scene docking and steering bodies are
substituted; the shipped SFF callback, queue/wrapper, target selection and sequence
transitions execute. A separate native motion fixture executes NavData.TimeAdvance,
ShipSitu.TimeAdvance and DockGroup.TimeAdvance. Only hull mass, gravity UI and tow
wear are substituted there; route interpolation, group input synchronization and
motion integration are native.

The four saved retrievers all have Main=2, active FlyTo, a recorded tow and no
remaining priority queue entries in `build` and `undock`. The earlier `fine` save
contains O-C8U/O-VT1F in that same state. This matches the reproduction's completed
one-shot state; the archives do not prove SFF was the original caller for each.

## Motion consequence reproduced

DockGroup.UpdateAnchor creates a separate ShipSitu and calls CopyFrom, which does
not copy NavData. DockGroup.TimeAdvance advances that anchor, then overwrites its
members' position, velocity, angular velocity and control accelerations. In a
one-hour fixture with one-second updates, native plotted coasting resets the
member's velocity, RCS acceleration and spin every tick, then the group restores
the accelerating anchor. Seeded residual RCS of 1e-10 AU/s² adds 53.855 km/s and
angular acceleration of 0.01 rad/s² produces 36 rad/s spin. No production motion
patches are installed. Native UpdateDockedShips synchronization clears the stale
anchor inputs without deleting the accumulated momentum, confirming why the
ordinary steering path behaves differently.
FlyToPath also keeps a non-null route running without an affordability rejection
equivalent to the player's autopilot fuel check.

## Implemented prevention and scope

HaulerRetrieverSffPatch advances the verified native Main sequence from FlyTo/Dock
to HaulShip after the successful connected-wreck handoff, returning null to prevent
AIShip.FFWD from queuing the one-shot. Ordinary native HaulShip remains responsible
for steering, fuel policy and return completion. No global group-physics rewrite.

`retrievers-patched` runs 3,622 checks including the native baseline/motion fixture,
the production handoff, continued hauling, native sequence save/load, Dock-stage
handoff and exclusions for failed pickup, active approach routes, player-owned tow,
hidden/full ships, already-active haul and unknown tree layout. Steering/docking
boundaries remain fixture substitutes for transition tests; this is not a live
Unity scene/full dispatch-to-retirement test. Run under the existing bundled Mono
host after building Debug Ostrabus and Release AIShipLifecycleCheck.

The replayed mechanisms explain how the native SFF handoff can produce these
symptoms; no archive records the historical first divergent call for each ship.
Any recovery must address already-corrupt velocity/control state without pretending
a corrected command alone makes these extreme-speed ships physically recoverable.

No pruning, teleportation, refueling or controller replacement was performed on
the four live haulers. Prevention needs deployment/restart; their persisted
corruption is not automatically swept or hidden.
