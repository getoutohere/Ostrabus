# SFF implementation

Part of `Orbital`. Main launch flag defaults off; `SuperFastForward` defaults true
under it. `orbital on` activates both when the SFF subflag is enabled. Session-only
`orbital sff off/on` allows comparison with parent correction alone.

## Integration

`StarSystem.Update` prefix prepares corrected trajectories only while `GUIFFWD.Active`
and the interval is at least 60 seconds. Start positions, velocities and controls are
captured before the native epoch increment. Preparation does not advance the live
ephemeris, mutate NavData or execute AI commands. Target waypoint values are copied.

The start-state exclusions are: any NavData on the object/group, body/orbit locks,
explicit gravity-ignore flags, projectiles, active combat targets or combat commands.
Combat and plotted-course behavior remains native. Active plans also get a dispatch
recheck in case another mod installed a route during the update.

Hidden/background hulls (`Ship.HideFromSystem`) are excluded before trajectory
capture and reported as `nativeHidden`. A docking group containing a hidden hull
also stays native. Gravity/motion dispatch and post-jump impact handling recheck
hidden state, so a hull pooled during the update cannot receive a stale plan.
Once reused and visible, it is eligible again on a later skip. No AI controllers,
targets, crew or pool flags are created/changed by this exclusion.

This fixes the two hidden, uncrewed, unregistered haulers found in the paused
November 4 baseline, and avoids replacement impacts for hidden origin-storage
hulls. A stale VORB scan target is not evidence of an active return journey.
The separate invalid-start guard remains for visible objects. `nativeHidden`
counts exclusions, including whole-group exclusions, not unique physical hulls.
Regression checks cover the captured haulers, an origin hull, reuse, late hiding,
and mixed groups; the complete suite passes 4,246 checks.

Nonfinite starts and starts already inside the selected body's surface/collision
radius are also excluded. They are reported as `nativeInvalidStart` with primitive
body/radius details. They retain native handling; this does not repair or prune
invalid ships. It prevents a zero-step replacement impact from moving them to a
body surface and treating them as new collisions during the skip.

Aerodynamic capture follows `Ship.UpdateGravAndAtmo`: only `Loaded.Full` ships
with a nonempty tile list receive new lift/drag models. Shallow ships and empty
hulls receive none. The presence of a planet's atmosphere-data tail alone does
not force an otherwise passive ship into the atmospheric RK4 path. Fully loaded
ships retain atmospheric forces; actual surface crossings remain collisions.
`lastSff.items[].aerodynamicModels` reports the number of active models.

The November 4 docked trial exposed both differences. Regression replay uses the
exact captured M-5N4 start epoch/position/velocity and the shipped Venus atmosphere
with native gas-density calculations. The repaired shallow coast remains bound,
has no impact, and reaches approximately 10,874.56 km altitude after six hours.
The segmented trajectory agrees with a single full-interval conic solution to
less than a millimetre. Three invalid-start variants produce no replacement or
collision check. Results are in `LiveTest/SFF-6H/FIXED-REPLAY.json`; the full offline
suite passes 4,227 checks. The live test ran the preceding build; these fixes
require deployment and restart before further SFF testing.

Only captured physical `ShipSitu` instances are registered. The `TimeAdvance` prefix
replaces their one large numerical translation at the matching final world epoch.
It does not intercept UI copies, hypothetical positions or route-overflow temporaries.
An object entering with NavData remains excluded even if native playback clears it.
No second call to NavData playback is made.

The private numerical gravity selector is supplied with reference/atmosphere data
for the predicted end position, avoiding the native old-position/new-planet mismatch.
Other public gravity/planning methods remain untouched. A group uses one solution
for its anchor; early member updates and the subsequent native group wrapper agree
on formation offsets, rotation and velocity. Native group rotation/tow-load paths
remain in place. This does not replay crew physiology or fuel consumption per substep.

Before native AI catch-up, swept body impacts are passed to `CollisionManager` and
surviving groups resynchronize their anchors. Target arrivals are checked against
the actual end-of-jump target. Only feasible completed travel keeps its native
catch-up; partial travel suppresses that pilot's FFWD call for this jump. Other
pilot/work/combat catch-up continues normally. There is one world update and one
normal AI catch-up phase, not thousands of replayed ledger/encounter/AI updates.

A Harmony finalizer clears all per-jump object references whether the update succeeds
or throws, and preserves the original exception. Inspection retains only primitive
summaries of the latest corrected jump, bounded to 512 rows.

## Propagation

- Conic coasting uses a universal-variable solver supporting elliptical, parabolic
  and hyperbolic states. No persistent BodyOrbit is created and no velocity is snapped.
- Segments are bounded by local orbital time and travel past nearby bodies. Dominant
  body selection is reevaluated at segment boundaries with continuous world position
  and velocity. Reference position/velocity comes from consistent analytical epochs.
- Segment durations align with the representable global clock increment; otherwise
  the large game epoch can accumulate frame-conversion errors over many segments.
- Travel uses bounded RCS acceleration (matching `AIIntercept2`'s nominal half-thrust
  limit), pilot space/atmosphere speed limits and braking. Planetary occlusion uses
  `FlightCPU.GetDockingNavPoint` with detached shadows. Arrival is an estimate based
  on these limits, not exact replay of native steering/turning decisions.
- Powered/atmospheric trajectories use RK4, with shorter steps for aerodynamic
  damping. Gas-density interpolation and aero coefficients follow native formulas.
- A swept conic candidate is checked against the actual curved arc to avoid false
  impacts when a chord cuts inside a surface. Body impacts use native damage/despawn
  handling at the final epoch. Event timing and powered swept paths are approximate.
- On arrival within the interval, the abstracted vessel maintains its arrival offset
  relative to the predicted target until the end; actual target revalidation prevents
  instant catch-up to a materially different destination.

No artificial distance limit forces ships to remain near Venus. Genuine escape is
allowed, and existing escaped/damaged orbits are not retroactively repaired.

## Limits and observability

`GET /orbital` → `lastSff`: modes `coast`, `powered`, `partial`, `arrived`, `impact`,
or `nativeFallback`; exclusions; estimated arrival; native arrival permission;
substeps; gravity switches; detours; atmosphere; budget fallback; impact checks;
preparation/total wall time. `nativeOther` includes intentional gravity-ignore cases.

Malformed data retains native behavior with an explicit failure reason. The 200,000
step cap ends with a coast in the current frame; the remaining interval loses powered
guidance/event checking and is explicitly reported. Rare domain-switch/grazing cases
and live atmosphere/collision effects need game testing. Existing ship-to-ship combat
and collision abstraction is not replaced by a detailed continuous simulation.

Offline checks run on the game's Mono and actual game types/methods. They include
capturing protected/native cases, maintaining the native docking-group formation,
partial versus completed AI travel and catch-up gating, braking, occlusion, domain
switches, impacts, grazing conics, atmospheric damping and cleanup. The captured Venus
wreck remains bound over the six-hour regression that produces native escape energy.
NavData playback is a test double to prove single delegation and preserved returns;
actual live plotted-course fuel/reactor results still require A/B validation.

The former offline prototype and raw numerical comparisons remain under
`Tools/OrbitDrift` and `VORB-DRIFT-ANALYSIS` for reproducibility.
