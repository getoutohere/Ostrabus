# Hull-shaped physical collisions

Optional simulation enhancement, enabled by default. **MOD SETTINGS → Hull-shaped
collisions** changes saved BepInEx `[Navigation] HullCollisions` live. Off restores
the native circle branch immediately. No reload or save migration is required.
Disabling also cancels the shared partial capture; already queued numeric work
can finish without retaining world objects.

## Native mechanism and scope

Native `CollisionManager.CheckCollisions(Ship)` decides ordinary ship impacts
from center distance versus the sum of ShipSitu radii. Ship/Ship
`ProcessCollision` uses that circle overlap for separation and a center-line
normal for the response. Adding only a hull-intersection gate would still
produce an excessive circular push when two hull corners actually meet.

This is a deliberate higher-fidelity geometry model, not an asset behavior
repair. C# owns these calculations. The native `TIsCollisionSafe` JSON trigger
was reviewed: it defines new-player/undock grace, not hull geometry. Its native
evaluation, docking protection and special-case branches remain authoritative.

Only ordinary free hulls (`Classification.Default`, excluding actual stations,
substations and body objects) use the alternative. Planetary impacts, station
regions/symbols, asteroids, buoys and projectiles retain native handling. The
separate station-docked hull exemption remains authoritative, including chains.
Free-space towing groups retain native group mass and translation handling.

ShipSitu.Size, GetCollisionDistanceAU, WillCollide, projectile scans, railgun/PDC
hit tests, DamageRay, weapon accuracy, AI avoidance and proximity distances are
unchanged. Native damage, relative-speed severity, crew effects, collision
events, grace and momentum rules remain in ProcessCollision. Contact normal and
separating displacement are its only revised geometric inputs.

## Shared geometry and performance

The [outline worker](../../NavConsole/HULL-OUTLINES.md) builds a rectangle cover
of the same filled exterior. This is a decomposition, not one convex envelope:
bays and separate components remain empty, enclosed holes stay filled, and the
rectangle union matches the perimeter. Coordinates use identical normalization,
native radius and grid/ship rotation. The X-only sizing defect is not silently
changed. Sensor uncertainty affects observation, not physical contact positions.

A vertical-edge sweep merges runs across adjacent bands. Scratch lists/maps are
reused; immutable piece/BVH arrays are produced only for changed raw geometry.
Limits are 8,192 rectangles and 1,048,576 sweep visits. Cover failure preserves
rendering and retains native physical handling. Covers are prepared with nav
geometry even when physical collisions are off, allowing reuse on re-enable.

There is no second world scan or new Update loop. The native scan requests the
shared snapshot service for nearby candidates even with the map closed. A 5 km
prewarm margin and two-second relative-velocity lookahead request geometry before
contact. Collection retains its shared .75 ms/2,048-operation per-frame budget,
one step per Unity frame, and bounded numeric worker. Pending, stale, invalid or
unavailable geometry uses native circles conservatively. A cold first encounter
can therefore retain native clearance temporarily rather than blocking or
assuming a miss.

The narrow phase traverses rectangle bounding-box trees and tests four
separating axes at candidate leaves. For overlap, it finds a contact-face normal
and unions collision intervals along that direction until the entire hull pair
is separated. This avoids clearing one rectangle only to stop inside another.
A 1 mm tolerance accommodates AU-double coordinate round trips. Native bounce
scaling is accounted for in the actual displacement supplied to ProcessCollision.

Numeric solver/interval buffers are reused. A CoreCLR warmed check observes zero
allocations over 10,000 contact queries; bundled Mono's allocation counter is
unreliable, so this is not a measured live Unity GC claim. Six shipped hulls use
5–48 rectangles each. Actual live frame cost is unprofiled. Limits of 262,144
node visits / 65,536 leaf pairs conservatively retain native handling if exceeded.

## Motion and lifetime

Weak ship-keyed tracks contain only numeric poses, velocities, radius/grid and
docking state. Both ships align to their common observed interval. Translation
between samples is approximated linearly, with interpolated rotation. Fixed
orientations use continuous SAT; rotating pairs use conservative advancement
with a closing-speed bound including angular corner speeds. Swept node bounds
cover intermediate rotations, not just endpoint AABBs. Bounded nonconvergence
uses native handling, not a fabricated miss. This is not a new rigid-body
integrator. Swept-only contacts feed incoming-side correction and contact normal
through native group separation/momentum handling. Native damage severity stays.

SFF retains native collision handling. Histories reset across observed SFF
boundaries, save/scene replacement, toggles, non-forward clocks, intervals over
120 game seconds, changed grid/radius/docking state, and displacement inconsistent
with recorded speed (instant docking/teleport). Interplanetary relocation does
not become a swept collision path through everything between planets.
After a geometric response, histories for the affected hulls and their current
docked groups are invalidated: native push-apart movement is not fresh travelled
motion to be replayed as another impact.

Two Harmony classes cover two existing methods. CheckCollisions prefix/finalizer
pump bounded capture and scope a prepared contact; its transpiler changes only
the Ship/Ship overlap branch and coincident ordinary-hull center skip. Ship/Ship
ProcessCollision prefix/finalizer scope its geometric response; its transpiler
replaces the single normal normalization call. Direct/out-of-scope calls retain
native normalization and overlap. IL structure/local types/counts are checked;
unknown versions fail binding explicitly. Shared helpers/body/weapon paths are
unpatched.

Workers own no Ship/CO/Unity objects. Prepared actor references clear/restore in
finalizers, including exceptions. Numeric shape references clear in finally.
Weak caches reset through the existing outline save/scene/shutdown path. No
custom saved fields, recovery controller or persistent actor lists are added.

`GET /ui/hulls` adds enabled/query/hit/rejected-circle/swept-hit/fallback counts
and displayed hull piece counts/cover errors. It is on demand without additional
diagnostic hooks. Collision-only cache entries need not have a nav ship row.

## Validation

[HullOutlinesCheck](../../Tests/HullOutlinesCheck/README.md) links production
sources into .NET 8 and shipped Mono. Independent filled-perimeter/cover checks,
1,000 oriented rectangles against a corner-projection oracle, concave bays and
separation, holes, detached pieces, fast crossings/near misses and rotation
between clear endpoints are covered.

Native CheckCollisions runs with the production patch: tests check false-positive
rejection, unchanged proximity, live toggles, coincident centers, sweeps outside
endpoint circles, station-docked exemptions, SFF/teleport exclusions, released
references and unpatched weapon/body helpers. Native response prefix/transpiler
runs against an explicit downstream scene boundary retaining normalization inputs
and the bounce multiplier; it does not emulate Unity impact VFX, full damage or
the complete crew response. Full-plugin binding checks verify coexistence.
PDA registry tests cover both saved-toggle directions. Live collision feel and
world side effects remain to be observed after deployment.
