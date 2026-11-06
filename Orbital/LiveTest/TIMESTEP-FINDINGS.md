# Residual normal-simulation drift

Offline investigation of the November 4 live coast, before the first M-5N4
docking. No game patch or deployed DLL changed during this investigation.

## Measurement correction

The original analyzer solved Venus's Kepler equation more tightly than the game.
Native BodyOrbit stops Newton iteration at residual 1e-6. At Venus's solar orbit
scale this produced a 15.87 km coordinate difference. The native-tolerance model
reconstructs the captured native coordinates within 0.000252 metres.

Use native-tolerance positions and a centered 64-second derivative for Venus's
velocity, rather than either the tighter solver or the stale native velocity
cache. The corrected live energy increase is **1.11066%**, replacing 0.56848%.
Inferred periapsis altitude changes 335.969 -> 375.654 km; apoapsis altitude changes
11,527.238 -> 11,756.737 km. Minimum sampled altitude is 357.014 km. The wreck
remained bound. These are osculating elements, not a claim of a completed orbit.

## Isolated integration experiments

Run the same captured starting state over the same 136.224 game minutes with
constant timesteps. Percent change in specific energy relative to its initial
absolute magnitude:

| Step, game seconds | Native timing/integrator | Start-epoch force, native integrator | Velocity Verlet |
|---:|---:|---:|---:|
| 0.1 | +0.171398% | +0.051263% | +0.000000872% |
| 1 | +1.710703% | +0.510703% | -0.00000223% |
| 5.33333 | +9.028918% | +2.675347% | -0.00009035% |

Native timing advances the planets before computing gravity against the ship's
old position (StarSystem.Update). ShipSitu.TimeAdvance then advances position
with v*dt + a*dt*dt/2 and velocity with a*dt, without evaluating the new force.
Moving both force inputs to the start epoch removes the first error; evaluating
the end force and averaging the two acceleration samples removes most of the
remaining integration error in this experiment. The existing parent acceleration
correction is present in these world-coordinate experiments.

Replacing float gravity with double gravity changes the 1-second native result
from 1.710703% to 1.710702%: precision is not the main cause. The relative-frame
frozen-acceleration test closely matches the start-epoch world-coordinate test,
providing a separate check on the force-timing diagnosis.

These sweeps are not an exact replay: the recorder did not capture every frame's
timestep. The actual live run had variable steps, reaching 5.33333 game seconds.
Do not apply the constant maximum step's 9% result to the observed live run.
The improved integrator intentionally diverges from the drifting live endpoint;
endpoint agreement with that run is not the correctness criterion.

## Recommendation

Prototype consistent-epoch gravity with a second-order integrator for free
coasting first. Preserve native NavData playback, locked objects, docking groups,
and all thrust/fuel accounting. Before extending to thrusting ships, audit force
evaluation side effects, atmospheric forces and pilot feedback. This is an
additional integration change, not merely another parent-acceleration term.

The SFF coordinate discrepancy was corrected on November 6. Shared NativeEphemeris
now matches BodyOrbit's bounded Newton solve, stopping tolerance and coordinate
arithmetic for both SFF and normal coast. The captured Venus mismatch is zero
(previously 15.875 km). Velocity is analytic at that solved phase; mutable native
finite-difference velocity caches are neither read nor changed. Offline comparisons
cover nested bodies, both orbit directions, eccentricities through .99, negative
epochs and period boundaries. Full SFF/coast regressions pass, including native
NavData delegation. Arrival and collision thresholds were not enlarged. The old
study remains historical evidence, not a native-coordinate endpoint oracle.
This establishes and corrects a coordinate discrepancy, not a proven live SFF failure.

Reproduce from the workspace root:

```powershell
dotnet run --project Tools/OrbitDrift -c Release -- --timestep-study .
dotnet run --project Tools/OrbitDrift -c Release -- --normal-test . 65631772606.059853
```

Full results: TIMESTEP-STUDY.json. Corrected live analysis:
ANALYSIS-BEFORE-DOCK.json. Original raw samples are unchanged.
