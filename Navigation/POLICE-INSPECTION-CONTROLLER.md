# Police inspection controller regression

Observed on O-ENB (NASS Patrol 88), 2026-11-09, game 1.0.1.10.

Our crewless-AI guard required crew on the controller's own hull. Native police
ShakeDown deliberately moves the sole officer onto the attached inspection target.
The guard could unregister the patrol during that legitimate absence. Returning
aboard did not recreate its controller. O-ENB was found detached with Brody Farmer
aboard, inspection mode still active, ample RCS fuel and no registered controller.
The offline reproduction establishes the regression; the live event's historical
call stack was not recorded.

The guard now recognizes an inspecting captain on a physically connected ship.
It retains the controller and permits ShakeDown, while holding navigation and
instant/SFF travel until conscious crew return aboard. After reload, if the cached
captain is the empty hull CO, it finds an inspecting officer through native ship
ownership. Missing/dead/incapacitated officers and detached empty ships retain the
crewless protection. No permanent registry of officer references is added.

## Explicit recovery API

- `GET /ships/O-ENB/ai/restore`: preview eligibility; no state changes.
- `POST /ships/O-ENB/ai/restore`: restore the native Police controller and home,
  apply native completed-inspection cleanup and mark undocking complete.

Requests run on the existing main-thread inspection queue. The API requires a
visible detached AI local-authority hull, no controller/navigation/thrust, a
living conscious inspection officer aboard and a native LEO owner whose station
matches the current ATC. It rejects player ships, stale hulls and ambiguous roles.
It never replaces an existing controller, spawns/moves crew, changes momentum or
adds fuel. This is intentionally recovery of this demonstrated state, not a
general AI-role guessing mechanism. The running game needs the updated build
before this endpoint exists; no automatic recovery sweep runs at startup.

`Tests/AIShipLifecycleCheck` passes 52 checks against the shipped assembly under
bundled Mono. This includes native lifecycle decisions, inspection absence,
reload fallback, SFF/navigation holds, officer return and recovery refusals.
The recovery fixture substitutes factory and condition-loot boundaries; it does
not claim a full live Unity factory/scene integration test.
