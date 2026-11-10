# Station-docked traffic

Intentional simulation policy requested 2026-11-09. Enabled by default; toggle
**Station-docked hull exemption** in the PDA Mod Settings app. Saved in BepInEx
as `[Navigation] StationDockedCollisionExemption`. Implemented in
`StationDockedCollisions.cs` and existing AI/proximity
scan transpilers. Deployed; the returning-hauler scenario is confirmed in play.

Live follow-up: the user deployed the DLL, reverted the earlier save and reported
that the returning hauler's lawn-dart was averted. The API also reports the actual
large wreck O-F4C6 as station-docked collision-exempt. This validates that return
scenario/scope in play; it does not add a live weapon-damage or impact-physics
test to the offline coverage below.

Shipbreaking yards should handle moored wrecks without enclosing collision
circles blocking docking approaches. The player chose an exemption for parked
hulls. This is a gameplay policy change, not a claim about vanilla intent.

## Scope

- Nonstation hulls are exempt while a reciprocal docking/tether chain connects
  them to a native station, substation/apartment or ground station. Wreck -> tug
  -> apartment is covered, independent of ownership/faction.
- Exempt hulls cause no ordinary ship impact response, impulse, collision damage
  or collision-triggered AI changes. They are excluded as AI obstacles and ship
  proximity candidates. The actual station remains physical and visible to ATC.
- Free-space tow/docking groups remain hazards. Orbit locking near a station
  alone grants no exemption. Undocking the final station connection immediately
  restores native collision/evasion/proximity behavior.
- Weapon/projectile hits and planetary collision handling remain native. No
  Ship/BodyOrbit or projectile scan patch is installed; the Ship/Ship guard also
  explicitly permits projectile calls.
- Interior movement, docking ports/clearance, sensors, geometry, factions, fuel,
  ownership, targets and orbital state are unchanged.

Native station identity/classifications determine scope, without region or
call-sign lists. Do not set `bNoCollisions` or `HideFromSystem`, whose broader
effects include weapon handling. One additional prefix guards Ship/Ship impact
response; scan filtering reuses the AI avoidance and NavConsole transpilers.
The range dial continues to govern remaining proximity hazards.

Disabling the setting immediately restores ordinary visiting-hull collision,
avoidance and proximity treatment without a re-dock/reload. It changes no world
data or geometry; toggling it back on restores the policy for existing chains.
All three paths use the same early config gate, without additional hooks.

## Lifecycle and cost

Follow live direct links, or native shallow JSON when the runtime dictionary is
absent. Runtime empty links override stale JSON. Each edge must be reciprocal and
resolve to current registered hulls. Destroyed/replaced/unresolved members and
stale DockGroup anchors cannot establish exemption. A visited set handles cycles
without recursive GetAllDockedShips. No graph cache survives calls.

Undocked hulls take a constant-time fast path; connected queries traverse the
group. Thread-local scratch buffers grow only as needed and clear in finally,
releasing all hull references. The warmed traversal uses reusable collections;
the bundled runtime's allocation counter is not reliable proof of zero allocation.
Disabled queries return before traversing the group. No docking observers,
update loops, persistent ship references or custom world-save data.

`GET /ships/{id}` reports read-only `stationDockedCollisionExempt`; actual
stations report false because their own collision behavior remains active.

## Validation

`Tests/PlanetaryNavigationCheck` runs docking
scope, stale links, cycles, station types, immediate undocking, reference release,
allocation and the production impact prefix. Native CheckCollisions executes
with the production proximity transpiler; ordinary warnings and restored warnings
after undocking are checked. The ship-impact body is a counting fixture boundary;
these tests do not replay live Unity impact physics or weapon damage.
The saved-setting gate is tested in both directions, alongside the PDA config
setter tests; actual PDA layout and input remain live Unity boundaries.

Native shared FlyTo/planning/interception/waypoint/body-ephemeris replays retain
explicit Unity/scheduler/pilot boundaries (see the harness README):

| Encounter | Native control | Current policy |
| --- | --- | --- |
| Patrol departing for Minarchist | Body impact | FlyTo finishes after 110 s with original target |
| O-BFZ2 returning on its saved 86-point route | Body impact after about 112 s | Native docking handoff after 105 s; minimum hull/body clearance about 6.45 km |

Production cases succeed for all three plausible station anchors. The hauler ID
is normalized to the harness alias; native speed/role and saved route remain.
Tests exercise shared commands, not complete pilot dispatch, physical docking or
station retirement. The current last-moment live hauler may already be beyond
ordinary thrust recovery; prevention starts from the earlier save. No live
rescue, pruning or refueling was performed for this policy.
