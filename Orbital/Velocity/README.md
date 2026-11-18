# Orbital velocities and RCS coasting handoffs

Independent bugfix feature `orbital-velocity`, included in full builds and bugfix
presets. It does not require the optional orbital gravity/coast/SFF module or
introduce a runtime setting. Two Harmony prefixes repair native C# mechanisms;
the authored star-system JSON supplies orbital elements and atmospheres, not
velocity-cache or conic-fit policy. No installed vanilla assets are edited.

## Defects and repair

Global `BodyOrbit.UpdateTime` updates positions at the requested time but refreshes
velocity only outside a 1.5-second cache window, using a one-second backward
difference. Parent velocities can describe another time. This produces jumping
cross-track velocity and inconsistent reference motion.

`OrbitalVelocityPatch` retains the native `SolveBigE` precision and position
arithmetic. It calculates tangent velocities at the requested epoch throughout
the same parent hierarchy. Position-only prediction queries leave velocity values
and their calculation timestamps untouched. Same-epoch requests use native fields
as their cache. There are no added histories, ship scans, polling callbacks,
worker threads or recurring allocations in the measured solver loop.

Global `BodyOrbit.CreateBOFromShip`, called by `Ship.LockToOrbit` from the nav map,
fits a coasting conic using cached reference velocities and an approximate phase
search. `ShipSitu.LockToOrbit` then replaces the ship's velocity with that orbit's
velocity. Fourteen captured player handoffs introduced roughly 1.7-1.9 m/s steps.

`CoastOrbitFitPatch` fits the bound conic from common-time position/velocity,
preserving native units, constructor metadata, serialization and cleanup. It
retains native input synchronization. A fit is declined through the existing
native null-result path if its velocity error exceeds 0.005 m/s or its positional
offset exceeds 5 m. Native `ShipSitu.LockToOrbit` preserves actual hull position
with its existing offset. Escape and radial trajectories are not forced into
bound orbits. Unsupported ephemerides, including negative-period moons, retain
their native update and factory paths.

## Scope

The velocity correction reaches shared references used by AI RCS/torch planners,
station departures and body-locked hulls. The conic factory's sole native caller
chain is the player's orbital display; ordinary AI planners do not use it.
AI objectives, combat policy, route generation, fuel rules, collision geometry,
attachment ownership and authored body elements are unchanged. The existing
detached `NativeEphemeris` SFF/PASS helper already uses this tangent convention
and is not modified.

Natural bodies, orbital stations, regional references, ground stations and
substations retain their identities and hierarchy. This is not a phase-precision
rewrite or world migration. Correcting reference velocities can change later
navigation decisions and trajectories; identical positions at a sampled epoch
do not imply identical future motion under corrected inputs.

## Validation

The maintained [offline study](../../Tools/OrbitalVelocityStudy/README.md) executes
these production sources under the game's bundled Mono. It includes captured
handoffs, guarded refusals, native register/save/reload/unlock/removal cycles,
future-time cache restoration, and 116 saved references sampled at three epochs.
The same production hooks are composed with orbital/SFF/PASS, AI rendezvous,
route-completion, planetary navigation, docking/attachment and torch/fuel suites.
Custom-build tests execute the selected velocity feature and confirm that its
omission retains native cache behavior.

The captured coasting VCRS step recalculates from 3.7765 to 0.08577 m/s at unchanged
positions. Maximum captured handoff error is 0.001018 m/s; repeated handoffs move
the hull by 0 m and retain native orbit cleanup. All 348 saved-reference position
comparisons are identical to native. These are offline results, not a live GUI,
atmospheric flight or whole-game FPS validation. The separate native phase-solver
approximation remains unchanged; see the [scoped verdict](../../Tools/OrbitalVelocityStudy/POSITION-PRESERVING-VERDICT.md).

## Live Venus check: 1.1.0.1, 2026-11-17

The running game reported MVID `802a99ef-a2fd-470c-9281-4f299c953c7d` and
plugin MVID `e27ccf69-2d43-45be-8ce6-a5415e81ac25`, matching the tested build.
A leased four-minute recording of O-GXD and VORB captured 458 coordinated
snapshots over 8.129 game minutes, 1,118 display events and six orbit transitions.
There were no cursor gaps, overruns or transport/analysis failures. Paused samples
were retained; elapsed wall time is not treated as elapsed simulation time.

- Three RCS-to-coast conversions changed velocity by at most 0.001762 m/s and
  position by 0 m. The three unlocks changed velocity by 0 m/s.
- 1,839 captured body/reference states matched the requested-epoch tangent
  convention within 0.000010 m/s. The older excursion analyzer's backward-
  difference comparison is a legacy control, not the corrected velocity oracle.
- Forty-seven adjacent display pairs during uninterrupted, input-free orbital
  coasting covered 12.34 game seconds. Maximum VCRS text step was 0.01 m/s,
  matching display precision; maximum numeric VREL step was 0.001089 m/s.

Read-only traffic comparisons showed all seven interregional freighters advancing
on stable stored routes with decreasing target range. Fresh local departures
also produced active plans. No duplicate controllers or stale hull references
were found in the compared registry samples. B-23LS completed its VNCA approach:
its hull remained registered and its captain Clinton Miranda was alive at VNCA.
O-HPVPY switched from a torch plan to RCS near 300 km and continued inward.
O-DSXGS replanned a torch leg and was observed closing on VENC; its next approach
handoff was tracked separately rather than assuming a completed arrival.

Follow-up confirmed O-HPVPY completed its VNCA arrival, with Ryan Justin Solis
alive there. O-2PW completed its atmospheric VLA01 approach, with Aaliyah David
alive at VLA01. O-DSXGS subsequently switched from torch to RCS near 300 km and
continued inward to 27.44 km. The final sample was paused with 53 game seconds
remaining on that leg. A subsequent requested watch, after play resumed, confirmed
arrival: Ayden Fuller was alive at VENC by epoch 65634702034.041367. A follow-up
at 65634702083.701607 found O-DSXGS still in the hull registry, not destroyed,
with its route/target cleared and controller unregistered through native parking.
No rescue, thrust cancellation, teleportation or other motion command was used.
The traffic comparison covered 31.89 game minutes. No new navigation failure was
established. The session log
contained no error/exception lines when inspected. Five newly registered local
departures also had active plans, providing evidence beyond playback of saved
pre-patch routes.

This is bounded ordinary-time acceptance for the player's velocity/display and
handoffs, with sampled AI progression and four completed arrivals. It is not a proof of every AI arrival or
atmospheric outcome, a whole-game performance measurement, or a new live SFF/PASS
test. Raw captures and scalar checks are under ignored
`obj/VenusVelocityLive-1.1.0.1-20261117`; no player or AI motion was commanded.
