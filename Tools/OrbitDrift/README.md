# Venus wreck orbit analysis

Offline analysis only; no Harmony patches or game state changes. Requires .NET 8.

```powershell
dotnet run --project Tools/OrbitDrift -c Release -- --self-test
dotnet run --project Tools/OrbitDrift -c Release -- .
dotnet run --project Tools/OrbitDrift -c Release -- --binding .
```

Inputs are the root `VORB-DRIFT-*.json` and `VORB-BOUND-*.json` captures. The initial
player response was transcribed from the tool's displayed response; provenance is
explicit in `VORB-DRIFT-INITIAL.json`. Outputs are in `VORB-DRIFT-ANALYSIS`.

The first analysis reconstructs the 66.5-minute salvage interval, comparing force
laws and hypothetical release from a fixed station offset. It does not claim to
know the exact docking instant or intervening control/collision history.

The binding analysis computes Venus-relative energy and osculating elements from
the initial, post-salvage and current M-5N4 states, and runs seven-day forecasts.
It stops at Venus surface crossing or a radius of 1,000,000 km. Atmosphere, thrust,
collisions and subsequent locking are excluded. Forecasts are conditional, not a
claim that the running game has already followed them.

## Findings, 2026-11-04

- All three recorded M-5N4 states are bound. Current speed is 3.136 km/s versus
  6.044 km/s escape speed; periapsis altitude 332 km, apoapsis altitude 11,529 km,
  period 4.048 hours, specific energy -13.34694 km²/s².
- A consistent Venus-relative two-body model preserves that orbit for seven days.
  A Sun-plus-Venus force model also survives seven days, minimum altitude 317 km.
- The source's strongest-body-only force law, integrated accurately, crosses the
  Venus surface after 22.197 hours. This is not just coarse numerical stepping:
  1s and 4s RK4 runs agree. In absolute coordinates it omits the parent acceleration
  inherited by Venus and its analytical satellites.
- A second live capture 259.646 game seconds later agrees with that force law to
  28.65 m and 0.211 m/s. The consistent two-body prediction differs from the live
  state by 383.16 m and 2.957 m/s. This supports the diagnosed force mismatch.
- Native constant-acceleration stepping was separately reproduced with the body's
  end-epoch position sampled against the ship's old position, as in StarSystem's
  update order, including float gravity. Larger fixed timesteps inflate the orbit
  materially: maximum Venus radius ~18,065 km at 0.1s versus ~33,491 km at 16s.
  These are sensitivity experiments, not measurements of actual game frame steps.
- No forecast for this wreck reproduced escape to a gigametre. These results
  establish a bound starting orbit and subsequent orbital corruption, but not the
  cause of every previously observed distant wreck.

Free drift away from VORB is a separate issue: releasing the saved 98 km fixed
offset with VORB's exact velocity produces 488 km separation in 66.5 minutes even
under consistent two-body gravity. The game's fixed-offset anchoring constraint
is not an independent, naturally matching orbit.

## Source reviewed

Decompilation root: `<decompiled-source>`.

- `StarSystem.Update`: advances body ephemerides before updating ship motion.
- `GetGreatestGravBOFast`: selects one strongest body; returns only its acceleration.
- `UpdateShip` and `UpdateDockingGroups`: apply that acceleration to numerical motion.
- `ShipSitu.TimeAdvance`: constant acceleration position/velocity update.
- `BodyOrbit.UpdateTime`: analytical parent position is added to the local orbit.
- `ShipSitu.LockToBO`: fixed inertial position offset and identical body velocity.

Kepler ephemeris reconstruction matches captured game positions below a millimetre
when using the game's solver tolerance. Independent circular/eccentric orbit tests,
bound/unbound classification tests, and 1s/4s timestep comparisons check the analysis.
Actual ship integration timestamps are not exposed by these captures; the API epoch
is used. This limits exact long-interval endpoint reconstruction.
