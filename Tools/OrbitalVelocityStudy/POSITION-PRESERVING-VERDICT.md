# Position-preserving VCRS and handoff candidate

**Current verdict: use the position-preserving candidate for the reported
velocity-cache and RCS handoff defects. The broader phase-precision rewrite is
not required for that repair and is not selected.** The
[production implementation](../../Orbital/Velocity/README.md) now exists and is
tested by the study's `-Production` mode. The subsequent
[live Venus check](../../Orbital/Velocity/README.md#live-venus-check-1101-2026-11-17)
confirms ordinary-time player handoffs and stable coasting readouts on 1.1.0.1;
its bounded AI observations do not establish every navigation outcome. Building
the plugin does not deploy it.

## Scope correction

The first investigation broadened the task from jumping VCRS into improving the
native position solver's precision. That introduced the kilometre-scale
coordinate differences documented in [FINDINGS.md](FINDINGS.md).

The narrower candidate retains the original position solver, stopping tolerance,
authored orbital elements and parent references. It refreshes velocities at a
common requested time using the same tangent-velocity convention already used
by the unmodified production `NativeEphemeris.State` helper. It also repairs
newly requested player coasting-orbit fits.

This does not claim to make the approximate native position curve a perfect
Kepler solution. The existing phase/derivative counterexamples remain in the
original experiment and are not deleted or reclassified as passing. They are
a separate precision limitation, explicitly retained in this candidate.

## Results

| Check | Result |
| --- | ---: |
| Captured coasting cross-track step, original stored velocities | 3.77650 m/s |
| Same snapshots, positions unchanged, synchronized velocities | 0.08577 m/s |
| Maximum handoff error across fourteen captured cases | 0.001018 m/s |
| Maximum handoff error across 64 repeated native conversions | 0.001019 m/s |
| Position change at those handoffs | 0 m |
| Body JSON round-trip velocity error | 0 m/s |
| Native registration/unlock/removal cycles | 64 passed |
| Saved body references tested | 116 |
| Saved-world coordinate comparisons | 348 |
| Maximum coordinate change in that saved-world grid | 0 m |
| Maximum difference from existing SFF tangent velocity, supported grid | 0.000000000004 m/s |

The VCRS comparison is an offline recalculation of coordinated captured states,
not a live GUI playtest with an experimental DLL. Native inputs and positions
are identical on the two sides of that comparison.

`Fixtures/LegacyBodies.json` contains only saved native body elements and parent
links from the `coast` save: no people, cargo, plots or physical item records.
The comparisons include natural bodies, manufactured stations, ground-station
references and substations without treating them as interchangeable categories.

Five saved retrograde moons use negative periods. The existing detached helper
does not support that representation. This candidate deliberately falls back
to native behavior for them; all fifteen sampled fallback velocities and their
positions remain unchanged. The existing limitation is not repaired here.

## Handoff admission

The new conic fit uses a common-epoch parent state. After constructing it, the
candidate evaluates the result using the retained native phase solver. It
declines conversion if the fit's velocity error exceeds 0.005 m/s or its native
position offset would exceed 5 m.

Returning no fit is already supported by `Ship.LockToOrbit` and
`GUIOrbitDraw.ToggleOrbitalMode`. The ship stays in free flight; no Hold,
controller, route, destination, refuel or station outcome is imposed. This
factory has only one native caller, `Ship.LockToOrbit`, reached by the player's
nav-console orbital display. Ordinary NPC behavior trees are not converted to
new coasting orbits by this guard.

The replacement uses the native `BodyOrbit` constructor and preserves its ship
draw flags, radius conversion, mass, rotation period and parent identity. The
lifecycle checks register each generated body through `StarSystem.AddBO`, save
and reload its native JSON, then invoke `Ship.UnlockFromOrbit` with native
removal enabled. All 64 cycles remove both the temporary orbit and its hierarchy
entry while retaining the parent. These are offline world-dictionary checks;
Unity map rendering itself is not exercised.

All fourteen observed player handoffs are accepted. A separate 100-case
conic/phase/direction grid accepts 80 and declines 20. Every accepted case stays
below the velocity tolerance and preserves position; every declined case leaves
position, velocity and free-flight status unchanged. Those percentages describe
the deliberately broad synthetic grid, not an observed in-game refusal rate.

## Existing-patch and performance checks

The six regression suites pass with the position-preserving hooks and their
current production patches composed in the same offline process. No detached
ephemeris adapter or precision hook is installed in `compose-preserve` mode.
The retained run manifest reports twelve successful control/candidate processes.

The additional preservation study passes 2,631 assertions covering captured readouts, handoffs, JSON
round trips, the 116-reference saved hierarchy, manufactured references,
unchanged native fallbacks and guarded-fit refusal. Detailed assertions and
measurements are retained in the generated report.

The production-source mode additionally checks detached-query cache immutability,
subsecond hierarchy refresh, position-only future queries, prior-prefix
cancellation and unsupported-factory fallback. It passes 2,644 focused assertions.
Its normal constructor retains ship radius, mass, draw flags and rotation metadata;
it preserves the native factory's incoming-situation synchronization. The
production mode retains the same captured handoff, refusal and position results
as the prototype.

Solver-only bundled-Mono measurements remain approximately 0.016-0.025 ms per
simulated frame for 64 bodies, versus approximately 0.013-0.014 ms natively,
depending on query repetition. The measured recurring allocation count is zero.
Whole-game FPS, atmospheric lift/drag, and the cost/frequency of declined
conversions in actual play have not been measured. No live performance claim
is inferred from these microbenchmarks.

## Intended integration boundary

The candidate has two native entry points: `BodyOrbit.UpdateTime` for common-time
orbital velocities and `BodyOrbit.CreateBOFromShip` for the guarded coasting fit.
The first operates on shared orbital references, so its corrected velocities
also reach station/body-locked NPC hulls. It is not a player-only readout patch.
The second affects newly requested player coasting conversions. Neither changes
AI target selection, route policy, docking permissions, collision geometry,
authored orbital elements or the existing detached SFF/PASS helper.

Coordinate preservation means identical evaluated body positions at the same
epoch, not identical future trajectories after a vessel uses corrected reference
velocities to maneuver. There is no claim that all downstream motion remains
numerically identical. The composed suites check specific existing mechanisms;
they do not prove every possible Unity scene interaction.

## Reproduce

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Tools/OrbitalVelocityStudy/Run.ps1 -Preserve -OutputDirectory obj/OrbitalVelocityStudy/preservation-reproduction
# Production implementation, instead of the preserved prototype:
powershell -NoProfile -ExecutionPolicy Bypass -File Tools/OrbitalVelocityStudy/Run.ps1 -Production -OutputDirectory obj/OrbitalVelocityStudy/production-reproduction
```

The original precision experiments remain available by omitting `-Preserve`.
Their wider precision correction and its coordinate changes are not part of
this candidate. No new save fields, migration, world rebase or authored asset
changes are required by the position-preserving proposal.
