# Corrected hull sizing versus native docking

November 12, 2026; Ostranauts 1.0.1.10. **Repaired in the full Debug build and
custom hull-collision feature; offline validated, pending live validation.**
This was an integration defect in Ostrabus.

## Player report and live evidence

O-GXD Stellar Magnificence encountered the unidentified E-WE5Y Prophet. With
hull-shaped collisions enabled, contact occurred before clamps were in range.
Disabling the feature allowed ordinary docking. After undocking, the now-known
outline visibly overlapped O-GXD.

The live API confirms clean shared geometry for both hulls, with no missing part
definitions or collision-cover error. Native/corrected radii were 500/1129.25 m
for O-GXD and 600/655.13 m for the visited derelict. The unknown-contact rendering
gate does not control physical collision geometry: the collision scan can obtain
it independently. Hiding an unidentified outline concealed the contact shape;
it did not cause the range disagreement.

Recorded docking events identify the selected source port. The derelict's port
CO ID changed during its visit; the earlier save has its single closed port at
90 degrees. The reproduction explicitly uses the saved ID and orientation,
rather than letting native GetAirlockRotation return zero for a missing port.
O-GXD's selected saved port is at 180 degrees.

## Cause

The later consistent-sizing change made ordinary hull outlines and physical
contacts use 40 map metres per installed tile on both axes. It corrected the
X-only native size distortion. However, shared ShipSitu.Size/radius helpers were
deliberately left unchanged to preserve AI avoidance, combat and body collisions.
Docking also reads those helpers; its integration was missed.

- `GUIDockSys.CanDock`: center range must be at most
  `1.1 * CollisionManager.GetCollisionDistanceAU(target, playerShip)`.
- `GUIDockSys.ConvertShipToRing`: ring scaling uses the same native circle sum.
  Its status/range display also reads that old circle boundary.
- `Ship.PlaceAtAirlocks`: places ship centers at the native radius sum plus
  10 m, oriented by port rotations. It does not align actual hull/port geometry.

A resized hull can therefore collide outside the old docking range and overlap
another hull even at a valid native docked placement. All three mechanisms need
to agree with the revised geometry.

## Offline reproduction

`Tests/HullOutlinesCheck` adds a read-only `docking-pair` mode. It reads just the
specified ship records from the native archive, resolves shipped part assets,
uses production snapshot/geometry generation and the numeric collision solver,
and invokes real native `Ship.PlaceAtAirlocks` with existing saved ports.
It checks that the archive SHA remains unchanged.

Using the pre-visit `wtf` save, where the derelict still had native radius 620 m:

| Measure | Center distance |
| --- | ---: |
| First geometric hull contact along native docking orientation | 1720.00 m |
| Maximum native clamp range | 1232.00 m |
| Native docked placement | 1130.00 m |

The production hull solver reports overlap at both clamp range and native docked
placement. Post-visit radius 600 instead of 620 changes the old thresholds
slightly; it cannot account for the roughly 488 m early-contact discrepancy.
Detached diagnostic output is retained privately in `obj/HullPreview/docking-pair.json`.

The original diagnosis passed 917 integration checks. The earlier tests proved
render/contact consistency and unchanged radius/avoidance helpers; they did not
prove docking compatibility. The initial repaired suite passed 1295 integration
checks, including 378 docking/pushback assertions, alongside the existing independent
60687 geometry and 12465 filled-contact checks.

## Implemented repair

`HullDocking.cs` reads the same immutable cached circumscribed radii as rendering
and collisions. Three local native GUI expressions use their sum, and the radius
addition inside `Ship.PlaceAtAirlocks` uses that sum plus its original 10 m gap.
Native selected-port facing, world/BO offsets, rotation and group integration
still execute. Placement deliberately preserves native bounding-circle docking,
rather than inventing exact off-center world-space port layout. Native interior
grid alignment, clearance, fees and fit rules remain authoritative.

Native `BaseCommand.GetHandOffDistance` also receives the corrected pair bound.
Its existing multipliers stay unchanged, so an AI does not have to enter a large
hull's collision envelope before its flight/docking stage can hand off. This
repairs the geometric input, without a new navigation controller or AI policy.

The saved pair now has a 1962.82 m clamp range and 1794.38 m docked separation.
The numeric hull solver confirms no penetration. Native clearance, lateral/ring
alignment, unavailable-port and range failures still reject docking. Tests cover
64 combinations of shallow/full state, cardinal grid rotation and world angle
for each fixture, plus native anchor/removal and repeated placement. The ring's
outer normalization expands for hulls larger than its old 10 km limit.

All changes are scoped to the optional hull collision feature and ordinary free
ships. Off immediately restores the native expressions. Stations, substations,
body objects and station-connected chains stay native, including restored links
before DockGroup reconstruction and with the station-exemption setting off.
Shared helpers consumed by weapons, AI avoidance and planets remain unpatched.
The later consumer audit corrects only AI's local prediction inputs and clearance.

Queries do not inspect sensor identity or change the unknown-outline visibility
rule. They read cached numeric radii without starting snapshots, scanning parts,
waiting for workers or retaining actors. During reconstruction the last published
bound remains available; cold full hulls use current live grid dimensions rather
than a stale saved header. The initial two patch classes covered six native
methods; the [consumer audit](CONSUMER-AUDIT.md) records the expanded integration.
No saved field or on-load migration is added. Existing
formations are updated only when native placement/rebuild occurs.

## Undock follow-up

Live O-840 NASS Patrol 64 docked and undocked during the same session with the
correct first Debug DLL installed. The remaining overlap exposed a missed native
path: `ShipSitu.PushbackFrom` calls `PlaceOrbitPosition`, which recomputed center
separation from the old circles after docking had already used corrected bounds.
The prior group-removal test did not execute this pushback placement.

The same geometry patch now replaces that local radius addition too. It fixes
undock pushback and other callers of the common instant-placement mechanism,
without role-specific police logic. The ordinary-ship/toggle/station exclusions
remain identical. Temporary copied situations without exact live identities
retain native handling. A live group anchor uses the maximum member offset plus
its cached hull radius, protecting attached hulls rather than just the lead ship.
The new radius is never smaller than the native sum because the subsequent
native safe-coordinate scan still uses its original circles.

These anonymous ShipSitu arguments have no native owner backlink. Only during
this explicit placement operation, a single allocation-free registry pass
resolves exact ship/group-anchor identities; group bounds use the existing member
list. No inverse actor registry, persistence or per-frame scan is introduced.
GUI radius queries remain constant time and native pushback already performs
its own registry-based safe-coordinate scan.

Offline tests call real native PushbackFrom, including its unchanged 5 m/s
kick and SetSituToSafeCoords. Planet ephemeris selection is a far-body fixture;
positioning/safe-coordinate code is not replaced. Both saved and synthetic pairs
reproduce inward snapping before the patch and stay separated afterward over
five bearings. Disabled mode and anonymous-point fallback remain native. A
group-anchor test runs real placement and native zero-time group propagation,
verifying that both attached hulls remain clear of the target.

The consolidated audit now also executes complete native group PushbackFrom,
including the safe search, and both sides of a two-group undock and merge.
Native radius reads previously treated a moved anchor's own members as obstacles;
the scoped safe-search radius adapter now excludes those members and checks other
free hulls against cached bounds. Group extents use stable native offsets while
member world positions await propagation. Explicit relocations invalidate swept
motion history, including members whose docked flag has not changed. See the
audit for the additional tether, inspection, range-display and AI cases. The
saved-pair suite now passes 1,356 integration assertions.
