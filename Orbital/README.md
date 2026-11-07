# Orbital physics and Super Fast Forward

Namespace/subdirectory `Ostrabus.Orbital`. Experimental; default off.

[Economic SFF catch-up](Economy/README.md) processes production, freight dispatch and
eligible cargo arrivals in simulation-time order without changing the player's course.

```ini
[Orbital]
Enabled = false
SuperFastForward = true
```

This setting in `BepInEx/config/com.ostranauts.template.cfg` supplies the game-launch
default. Console `orbital on`, `orbital off`, `orbital toggle`, and `orbital status`
override it **for this session only**; they do not rewrite the launch setting.
Scene/save changes retain the session choice and clear diagnostic caches/counters.
`SuperFastForward` enables the SFF replacement when the main Orbital flag is on.
`orbital sff on/off` changes that subfeature for the session. No configuration value
is rewritten by either console toggle.

## Confirmed force-law problem and correction

Analytical `BodyOrbit` motion adds each parent's changing position. Numerical free
ships instead receive only the selected strongest body's gravity. The missing
reference-body acceleration makes those descriptions inconsistent near planets
and moons. The standalone Venus study is in `Tools/OrbitDrift` and
`VORB-DRIFT-ANALYSIS/binding.json`.

The Harmony postfix on private `StarSystem.GetGreatestGravBOFast` adds the selected
body's **absolute analytical acceleration** to the returned force vector. This is
the sum of the second derivatives of its local Kepler trajectory and each ancestor's
trajectory. It uses orbital elements, not cached finite-difference velocities or
nominal parent masses: the game's prescribed solar orbit and its single-precision
gravity constant are not exactly consistent. Root Sol is stationary and adds zero.

The chain is bounded to 32 entries; malformed/nonfinite elements and cycles skip
the correction. Results are cached once per selected body per simulation epoch.
The cache uses weak keys; cache values contain scalars only. No game body positions,
velocities, target references or save data are changed by the toggle itself.
There are no per-frame scans or background threads. The disabled hooks return
immediately. Scene/plugin teardown clears caches. Errors restore the original
query output and disable the runtime correction.

The ordinary numerical ship update and docked-group anchor update consume the
corrected force. Native gravity-ignore behavior is preserved. Body-locked ships,
analytical orbital coasting and body objects are excluded. Public individual-body
gravity and strongest-body queries remain unchanged, preserving their existing
planner, display, lift/drag and atmosphere uses. This patch does not improve the
native constant-acceleration integrator for powered/docked ships or repair previously
corrupted orbits. Eligible free coasters use the integration improvement below.

## Normal free-coast integration

With Orbital enabled, undocked ships with no NavData, thrust, lift, drag or other
external acceleration use velocity Verlet: evaluate gravity at the old ship and
planet positions, advance position, then average old/new accelerations to advance
velocity. The parent-frame correction is included at both epochs. Native angular
motion, fuel accounting and subsequent collision/AI updates remain in place.

`orbital coast off` restores the native integrator while retaining the parent-force
correction, allowing A/B tests. `orbital coast on` re-enables it. This subfeature
starts on; the main module still inherits its launch default from `[Orbital] Enabled`.
All console toggles are session-only. `GET /orbital` exposes a `coast` object with
enabled, integrated/rejected/invalid step counters.

The integration scope is a real `StarSystem.UpdateShip` call, not arbitrary
`ShipSitu.TimeAdvance` prediction calls. It excludes docking groups, SFF, locked
objects, ignored gravity/acceleration, nonpositive steps and steps over 60 seconds.
Atmosphere/surface crossings and steps over 1% of the local gravitational timescale
fall back to the native path. The strongest body remains the native selected body
for that short step; this does not add n-body gravity or alter body selection.

Body positions reproduce the native Kepler solver's tolerance without mutating
its shared caches. Start/end positions and accelerations are cached per body,
epoch and step duration with weak keys; cached preparation allocates no memory.
The current ship reference exists only within the update scope and is restored by
a Harmony finalizer, including on exceptions. Scene/plugin teardown clears it.

Offline checks exercise the actual patched game TimeAdvance under bundled Mono:
a 136.53-minute coast at 5.333-second steps has 0.000239% energy error. They also
cover coordinate consistency, cache immutability, angular motion, scope/exclusion
guards, allocation and existing SFF/NavData behavior. Live validation is pending;
this is not a claim of equivalent accuracy for powered or docked ships.
The additional [readiness audit](LiveTest/COAST-READINESS.md) runs the actual
UpdateShip path, compares exclusions exactly with native motion, checks exception
cleanup and exercises variable-timestep coasts for 48 game hours. All 4,204 checks
pass; a separate controlled live reload is not required before ordinary play.

## AI navigation review performed before implementation

Decompilation root: `<decompiled-source>`.

- `FlyTo.RunCommand` chooses `FlyToPath` or resets NavData and executes `FlyToManual`.
- `FlyToPath.PlanTrip4` queries public `GetGreatestGravBO` for the nearby body's
  atmosphere; its returned gravity vector is not used to integrate the route.
- `PlanTrip4Sub`/`PlotBurnLoop` generate acceleration segments using kinematic
  position/velocity formulas. Target waypoints can follow analytical body motion.
- `NavData.TimeAdvance` directly interpolates ship position/velocity and fuel.
  `ShipSitu.TimeAdvance` returns immediately when that playback succeeds. The
  correction therefore does not bend or invalidate an active stored route.
- `FlyToManual` calls `AIShipManager.AIIntercept2`, a position/velocity feedback
  steering controller. It reads live state and commands RCS; it has no hardcoded
  subtraction of the old gravity model. Corrected numerical dynamics therefore
  change its future steering demands and fuel usage. **Success/fuel improvements
  still require live testing**, especially when handing off from NavData to manual
  flight near VORB and other planets. No AI targets, plans or fuel are reset here.
- Collision avoidance and stopping-distance estimates remain their existing
  approximations. This patch does not make all navigation predictions physically
  exact. The legacy `FlightPlan.Advance` path uses its own single-star gravity
  query and is also unchanged.

## Super Fast Forward: failure and replacement

`GUIFFWD.FFWD` calls `CrewSim.system.Update(fTimeDelta)` once for the entire selected
hours. `StarSystem.Update` first advances celestial ephemerides to the END epoch,
then samples gravity at each free ship's OLD absolute position. `ShipSitu.TimeAdvance`
holds that one acceleration constant for the entire interval. Docked free groups
have the equivalent problem through their anchor.

This is worse than omitting parent acceleration: after hours of planetary movement,
the old ship coordinates can be so far from the planet's new position that the Sun
is selected as the strongest body. The planetary acceleration effectively vanishes
for that whole step. Adding the selected reference's acceleration cannot reconstruct
the missing curved trajectory; if Sol is selected it adds zero.

The GUI collision preview is different: ~21.6-second steps summing gravity, or
analytical/NavData projection. It is not a simulation of the actual hours-long
numerical update and cannot guarantee the rest of the world remains safe.

**Reproduced against actual shipped methods:** the captured, initially bound M-5N4
state, advanced six hours through the actual private selector and actual
`ShipSitu.TimeAdvance`, selects Sol and acquires positive Venus-relative escape
energy (~0.43 km²/s²), with parent correction alone. Offline repeated 1h/2h/6h
updates reach a Venus distance of one gigametre in approximately 90–97 game hours.
These repeated jumps model repeated SFF use, not a recording of the user's history.

AI catch-up happens AFTER the world update, via `AIShipManager.FFWD`, which is enabled
for jumps of at least one hour. `AIShip.FFWD` delegates to the pilot and can schedule
an instant-resolution command. Scav/hauler pilots may instant-dock/finish work when
there is no NavData; `NavyPilot.FFWD` simply returns the active command. Thus some AI
ships may subsequently recover or teleport to their goals, while unattended wrecks
have no such recovery. Active NavData and anchored objects bypass the bad free step.

The SFF replacement captures the start state before the clock/body jump and replaces
only registered numerical trajectories. It keeps exactly one world update, preserving
ledger, encounters, crew effects, market processing and combat catch-up.

- Initial NavData, including any plotted course on a docked group member, protects
  the entire object/group for the whole jump. Native playback, fuel accounting,
  arrival overflow and temporary prediction objects are not intercepted.
- Body locks and analytical orbit locks retain vanilla motion. Explicit combat
  targets/combat commands and projectiles retain native SFF behavior.
- Free coasters use two-body conic segments with consistent moving reference frames.
  Dominant gravity sources can change without teleporting or resetting velocity.
- Noncombat AI with an existing FlyTo/HaulShip/Dock target gets bounded simulated
  travel using native pilot speed and RCS acceleration limits, braking and the native
  planetary occlusion waypoint policy. Fuel is abstracted as agreed. This is an
  approximation to ordinary piloting, not a frame-by-frame replay of its decisions.
- Only journeys that actually reach the arrival envelope during that propagation
  can receive native instant-arrival catch-up. Others make partial progress and
  suppress the pilot's instant catch-up for this jump. Arrival is revalidated against
  the actual target's end state before allowing it.
- A moving target with NavData is predicted from detached waypoint values; its live
  route is never evaluated or changed to predict the pursuer's progress.
- Untargeted AI coasts rather than burning its last steering input for hours. Free
  non-AI manual thrust is integrated in bounded steps. Explicit gravity-ignore flags
  are preserved and reported as native exclusions.
- Docked groups follow one anchor; native formation, rotation and tow-load code
  remain in place. Swept planetary impacts are checked through the native collision
  system, including resynchronizing a surviving group after a bounce.
- Atmosphere uses the game's density interpolation, aerodynamic coefficients and
  lift/drag formulas in bounded numerical substeps. This does not replay every
  shipboard/environmental simulation tick during the skip. New aerodynamic models
  apply only to Full ships with tiles, matching native UpdateGravAndAtmo; shallow
  ships coast through the atmosphere-data tail without invented drag.
- Nonfinite/already-inside-body starts remain native and are reported separately
  as `nativeInvalidStart`, rather than being moved to new impact positions.
- Hidden/background pool hulls remain native and are reported as `nativeHidden`.
  They receive no replacement trajectories or impacts, including when a hidden
  member belongs to a docking group. Reused visible hulls become eligible again.

The per-object solver caps at 200,000 steps. If that cap is hit, it finishes with a
coast in the current reference frame and reports `budgetFallback`; this fallback
does not retain powered guidance or full event detection for the remaining interval.
Invalid inputs/solver failures retain native behavior and appear as `nativeFallback`
with a warning and reason. They are not silently claimed as protected. Ship-to-ship
combat/collision scheduling is not expanded into a full continuous simulation.

See [SFF-IMPLEMENTATION.md](SFF-IMPLEMENTATION.md) for integration points and test scope.

## Observability and verification

`GET /orbital` reports runtime/default flags, correction-query/cache/invalid counters,
last acceleration, largest correction, hierarchy depth, maximum world timestep and
SFF/large-update counts. Query counters do not count unique ships or guaranteed
integration: valid NavData can bypass numerical physics after the gravity query.
`lastSff` adds preparation timing, exclusions/failures, suppressed catch-up counts,
impact checks and up to 512 primitive per-group/ship records: mode, arrival time,
native-arrival eligibility, substeps, gravity switches, detours, atmosphere and budget
fallback. No ship/crew/body references are retained in that report. Per-jump object
references are cleared in the world-update finalizer, including exception paths.

```powershell
dotnet build Tests/OrbitalCheck -c Release
dotnet run --project Tests/SaveJsonCompatibility/Host -c Release -- Tests/OrbitalCheck/bin/Release/netstandard2.1/OrbitalCheck.dll
dotnet build Ostrabus.csproj -c Debug
```

Offline checks cover circular/eccentric/rotated/reversed trajectories, independent
central-force validation, real BodyOrbit derivative comparison, nested hierarchies,
invalid data/cycles, actual Harmony binding and invocation, one correction per query,
excluded locked/ignored situations, unmodified public gravity, epoch cache refresh,
disabled equivalence, cleanup, and the native six-hour SFF escape reproduction.
SFF checks additionally exercise the actual capture, motion and docking-group hooks,
partial/full AI travel, braking, detours, gravity transitions, impacts/grazing orbits,
atmospheric damping, native NavData delegation including arrival/failure, and temporary
prediction exclusion. NavData delegation tests use a playback test double; live
reactor/fuel equivalence still needs an in-game torch-course comparison.
The host supplies a fixed Unity clock for unused path-history timestamps.

NativeEphemeris supplies common native-compatible body positions to normal coast
and SFF. It reproduces BodyOrbit's Kepler tolerance and arithmetic, derives velocity
at the same solved phase, and leaves shared native caches untouched. Boundary tests
compare against actual BodyOrbit across nested/eccentric/retrograde orbits and period
wraps; the captured 15.875 km Venus discrepancy is now zero. Historical study endpoint
expectations were replaced with actual native endpoint coordinates, preserving tight
comparison tolerances. Existing motion, arrival, collision and torch-route dispatch
policies are unchanged by this compatibility correction.
November 6 validation: 4,850 orbital checks pass under the game's bundled Mono,
including 486 ephemeris boundary/cache checks. Debug build has no warnings/errors.

The new build has not yet been loaded in-game. Start with a controlled save, enable
`orbital on`, and inspect `/orbital` before/after a skip. Compare a plotted torch
journey with correction off/on, including a skip past arrival, and check nearby
wrecks, haulers and free/docked formations.
