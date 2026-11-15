# Completed routes and the playback-to-physics handoff

Native Ostranauts 1.0.1.10 has a completion defect outside GUI SFF. At the end
of a route, `NavData.TimeAdvance` writes the terminal position/velocity and
acceleration, clears the route, and returns false. `ShipSitu.TimeAdvance` then
integrates its complete original interval again. PASS makes this conspicuous
because one call can cover many minutes; ordinary frames can hit the same path.

The controlled 600-second reproduction, with arrival after 300 seconds and a
terminal braking acceleration of -1e-12 AU/s2, gains **89.75872284 m/s** of erroneous
velocity. That value is offline evidence, not a measurement of current ships.
The earlier [Mercury PASS investigation](../Orbital/PASS-TIME-SKIPS.md) also
reproduces the mechanism from a saved route. Ferry interaction JSON correctly
requests native FERRY transport; changing its authored travel conditions would
not repair the integration error.

## Repair

`RouteCompletionPatch.cs` changes two checked native IL sites in one independently
selectable bugfix feature, `route-completion`:

* The successful completion return in `NavData.TimeAdvance` releases the completed
  maneuver. Physical torch completion closes native FLOW/CYCLE and calls native
  `SetThrust(0)`; RCS stops through `StopManeuver(false, false)`. Internal forces
  are cleared while gravity, lift and drag remain available. Borrowed situations
  do not issue a physical stop to the owning ship.
* The existing playback call in `ShipSitu.TimeAdvance` accepts that completion
  and passes only `now - endpoint arrival` to its ordinary physical integrator.
  Its acceleration-ignore flag and native integration body remain authoritative.
  Body-bound endpoints have already been projected to the current ephemeris epoch
  by native `UpdateTime`, so they receive no second translation.

GUI SFF retains its native completion placement, coast and control shutdown.
Unfinished playback, exact-endpoint comparison, rejected/aborted playback, damaged
owners, manual thrust, nonpositive steps, targets, fuel reconciliation and save
formats retain their original paths. The transpiler refuses an unrecognized
playback/return shape. No relocation, velocity clamp, new AI decision or asset
policy is added.

A thread-local value-type scope identifies actual native completion. A true
result plus a cleared route is insufficient: another prefix may have handled the
motion itself. Nested calls restore the previous scope and exceptions clear it
in `finally`; no course/ship reference remains after the outer call. There is no
heap scope allocation, persistent registry, population scan or frame callback.

## Validation and limits

`Tests/AiRouteCompletionCheck` passes **661 checks**, including **295** dedicated
shared-completion checks with actual native playback and physics. Controls cover
the reproduced failure, fractional frames through two-day jumps, residual gravity/
lift/drag, acceleration/gravity ignore flags, projected body/orbit endpoints,
overdue saved routes, exact arrival, manual/borrowed/materialized-player controls,
rejection, native invalid/destroyed paths, nested handlers, exceptions and changed
IL refusal. Engine scene/audio/physical reactor services are explicit fixtures;
the native completion branch, mathematical integration and body ephemerides run.

The linked production patch also runs inside the torch-tow, reactor-clock and
orbital regression harnesses. Tow tests include end-of-course integration against
an equivalent undocked vessel, group ordering, rigid formation, mass, released
braces and ordinary manual controls. The full orbital harness checks that a
different playback prefix's handled result is preserved. A first integration
attempt failed that control; the scoped completion marker repairs that case.
The broader runs pass 343,871 tow checks, 398,200 momentum-aware playback checks
across 32 scenarios, 256 independent-clock checks and 4,855 orbital checks.
Full-plugin binding and an isolated custom build also bind without failures;
the standalone fix has one patch class targeting two methods and zero frame calls.

This repairs the shared completion defect. The wider PASS proposal to share GUI
SFF's motion/economy/discontinuity context remains separate work. It does not
restore excess velocity already acquired in a save. Loaded Unity scenes and real
PASS trips remain live-validation cases; offline coverage is not a claim of
complete game simulation or proof that every possible regression is excluded.
