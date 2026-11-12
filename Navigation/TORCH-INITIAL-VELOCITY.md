# Course planning and incoming momentum

November 12, 2026; Ostranauts 1.0.1.10. **Included in 0.4.0**, selectable as
`torch-momentum` in custom builds. The OKLG-to-VORB braced flight exercised the
new trajectory through normal simulation, repeated SFF, coast and final braking;
the player manually disengaged near arrival. Automatic endpoint completion is
covered offline. See the [live flight record](TORCH-COURSE-FIX.md).
The original native reproduction remains below.

## Confirmed cause

The full method is
`Ostranauts.Ships.Commands.FlyToPath.PlanTrip4Sub`.
The nav console passes the real origin velocity into it through
`NavModCoursePlot.PlanTrip` and `FlyToPath.PlanTrip4`.

The inner planner deliberately constructs four phases:

1. Match the destination's entire velocity vector (`:WPV`). Duration is
   `|destination.velocity - origin.velocity| / acceleration`.
2. Accelerate toward the remaining displacement (`:WPXY+`).
3. Coast if the requested speed limit requires it (`:WPXYC`).
4. Brake for the same duration as phase 2 (`:WPXY-`).

It accounts for displacement during phase 1; incoming velocity is not lost
from the input or from integration. It is expensively canceled so that the
remaining calculation can use a symmetric relative-rest-to-relative-rest
transfer. This cancels useful closing speed along with unwanted transverse
speed. The same kernel serves torch and RCS plans, player and AI callers.
Our existing fuel/control corrections do not introduce this behavior.

The relevant asset mappings (`guipropmaps/guipropmaps.json`, NavModCoursePlot)
select a GUI prefab and placement. The strings describe maximum acceleration
and short/long coast controls. The velocity-matching maneuver and trajectory
equations themselves are hardcoded C#, not an authored trigger or pledge
policy. This is a planner improvement at the C# layer.

`PlanTrip4` still owns no-wake departure handling, native AI approach detours,
moving-target ephemeris iterations and approach clearance. Its coast control
reduces peak speed; it is not literally a coast-time fraction. Any replacement
kernel must cooperate with those mechanisms rather than bypassing the wrapper.

## Native assembly reproduction

The `initial-velocity` mode in `Tools/TorchNativeOracle` invokes the shipped
private `PlanTrip4Sub` and its real `PlotBurnLoop`. It does not patch either
method or install our production trajectory/playback corrections. The existing
fixture supplies reactor modules, conditions and Unity boundaries. The native
thrust law supplies approximately 3G for a 26,798 kg, single-pair reactor fit.

Fourteen inertial stationary-target scenarios exercise 10,000 and 100,000 km
distances with resting, closing, receding, overshooting, transverse and oblique
initial velocities. In all cases the first native phase cancels the full
initial relative velocity, and final position and velocity match the target.

Collinear scenarios additionally use an exact bounded-acceleration, minimum-time
solution, emitted through the native waypoint generator. Both positive and
negative initial velocity and overshoot/reversal are included. At rest the
solution agrees with native; receding and unavoidable overshoot examples also
agree. Useful incoming momentum yields shorter courses and less delta-v.

Example: stationary target 10,000 km away; closing at 10 km/s; 3G:

| Measure | Native cancellation first | Momentum-aware analytic course |
| --- | ---: | ---: |
| Initial velocity-matching burn | 339.79 s | None |
| Total duration | 1,401.98 s | 921.19 s |
| Integrated delta-v | 41.260 km/s | 27.111 km/s |

This is 34.3% less time and delta-v for this scenario. Both arrive with zero
relative velocity and below 1 cm positional error. This is an uncapped-speed,
no-coast kernel comparison, not a measured live course or a promise of the same
percentage for every slider setting. Delta-v is not a general fuel percentage
claim: reactor configuration, throttle and idle time also affect fuel.

The 104 checks pass. Numeric output is in ignored
`obj/torch-initial-velocity.csv`.

```powershell
dotnet build Tools/TorchNativeOracle -c Release
dotnet run --project Tests/SaveJsonCompatibility/Host -c Release --no-build -- Tools/TorchNativeOracle/bin/Release/netstandard2.1/TorchNativeOracle.dll initial-velocity
```

## Implementation

`Navigation/TorchCourses/TransferSolver.cs` solves a two-dimensional transfer
using real relative displacement and incoming velocity: one full-acceleration
vector burn, optional coast, and a full-acceleration braking burn. Burn durations
and directions can differ. It solves the boundary conditions, rather than
canceling velocity and assuming a stationary start. This is an exact solution
within this two-burn family, not a claim of globally optimal continuous steering
for every two-dimensional encounter.

`TorchTransferPlanning.cs` replaces only `FlyToPath.PlanTrip4Sub` while the
player's `NavModCoursePlot.PlanTrip` is executing, only for torch plans outside
atmosphere. The existing planning hook enters/leaves a thread-local value-type
scope, including on exceptions; no other thread inherits it. Ordinary AI and RCS
planning remain native. The helper calls native `PlotBurnLoop` via a cached
delegate, preserving ordinary `NavData` storage, easing, fuel correction,
playback, save reconstruction, towing and SFF. No new tick controller is added.

The fixed-iteration numerical kernel uses structs and scalar arithmetic with
zero per-call managed allocations, measured across 10,000 warmed calls. Native
waypoint allocations remain. A normal improved course has at most 67 waypoints,
versus 86 for native velocity-match/burn/coast/brake generation. Nonfinite,
degenerate or unrepresentably short calculations retain the native kernel.

For a collinear approach with distance D, incoming speed u and acceleration a,
when stopping distance does not exceed D:

```
peak = sqrt(a*D + u*u/2)
accelerateSeconds = (peak-u)/a
brakeSeconds = peak/a
```

Unequal burn durations naturally retain incoming momentum. The overshoot case
requires braking through zero, reversing, and braking again. The test harness
includes that branch rather than allowing negative burn durations.

For arbitrary heading, work in the arrival-velocity reference frame. Let u be
incoming relative velocity, v the chosen cruise velocity, a maximum acceleration,
t1 the first burn, t2 the braking burn, and c the coast:

```
t1 = |v-u|/a
t2 = |v|/a
D = (u+v)*t1/2 + v*c + v*t2/2
```

Without a coast, for a trial t1, v points along `D-u*t1/2`. Its magnitude follows
a quadratic, evaluated using the rationalized positive root. A bounded scalar
bisection solves `|v-u|=a*t1`. If the resulting cruise exceeds the native speed
cap, a second bounded scalar bisection solves the same vector relation at that
cap and obtains the nonnegative coast duration from the displacement equation.
Normalization avoids absolute AU-scale tolerances. Existing overspeed can brake
down to the cap; the solver does not pretend inherited velocity never existed.
Already suitable cruise velocity permits an immediate coast and final brake.

The native wrapper still determines target ephemerides, peak-speed/coast control
and the 300 km final approach offset. Full-wrapper tests also reproduce a native
failure: extreme long-coast settings can leave its ten ephemeris iterations
unconverged. In the 45-case orbital matrix, 33 improved courses converge and 12
fail; all 12 also fail with the unchanged native kernel. A final player-plot
check now rejects those bad courses instead of offering them for engagement.
It compares the actual endpoint to the last native approach-offset target at
arrival, allowing 1 km of iteration error; it does not change collision bounds
or remove the 300 km approach clearance. The nav log says to shorten the drift
phase, and the native UI handles the null plot. No ship or fuel state is altered
by that rejection. The final check restores shared `BodyOrbit` ephemerides to
the current epoch in a `finally` block, matching native planner cleanup.

## Validation

`Tools/TorchNativeOracle` links the production implementation and invokes the
shipped game assembly under bundled Mono:

- `transfer-solver`: 4,728 structured/random scenarios covering arbitrary heading,
  receding/closing motion, exact stopping-distance boundaries, overshoot/reversal,
  wide acceleration/distance scales and speed caps. Independent stepped position
  integration, endpoint velocity, limits and collinear analytic controls agree.
  Invalid inputs and zero-allocation checks are included.
- `transfer-planner`: production kernel hook plus native waypoint generation,
  real save-era epochs, Galilean target-frame motion, native RCS/atmosphere and
  unscoped exclusions, immediate coasting, nested/thread-isolated scopes and actual
  Harmony exception unwinding. All 128 representable kernel cases use the solver.
  The 45-case full wrapper uses real `BodyOrbit` ephemerides; its 33 accepted
  courses preserve approach clearance and arrival velocity. Its 12 rejected
  courses reproduce native nonconvergence; no native-success case is rejected.
- `transfer-playback`: 32 native playback scenarios combine straight/oblique
  incoming velocity, capped/uncapped cruise, both pilot fuel modifiers, ordinary
  steps/SFF jumps, solo/braced-tow motion, JSON reconstruction, physical tank
  debits, preview agreement, arrival disengagement and scope cleanup.

BodyOrbit uses one-second finite-difference velocities cached for up to 1.5
seconds; orbital assertions allow that native velocity noise. Waypoint epoch
assertions also account for double timestamp quantization at real save epochs.
The trajectory equations and acceleration limits themselves stay in doubles.

Run each mode after building the oracle, passing its name after the DLL in the
Mono-host command above. The default full oracle additionally runs the existing
reactor/fuel/control/lifecycle regressions and the new playback scenarios.
Live validation of the new trajectory calculation is still pending.
