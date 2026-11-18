# Orbital velocity prototype verdict

**Follow-up:** the scoped [position-preserving candidate](POSITION-PRESERVING-VERDICT.md)
avoids the precision rewrite below and passes the preservation tests. That is
the selected proposal for VCRS/cache and player handoff repair. The original
broader experiment and its failed gates remain here as evidence.

**Verdict: the paired mathematical repair works in the offline Venus replay,
but is not a safe drop-in production patch yet.** Tightening the underlying
phase solver changes existing derived body positions. Continuity across that
change remains an explicit integration gate. No production patch source or
installed plugin was changed by this study.

Tested native assembly: Ostranauts 1.1.0.0,
MVID `8130eff7-382e-4fff-97c5-67c9ade3a42a`.

## Reproduced mechanisms

`BodyOrbit.UpdateTime(double, bool, bool)` calculates a one-second backward
position difference for velocity, and retains it until the requested time is
more than 1.5 seconds from `dVelCalcLast`. Positions update independently.
`ShipSitu.TimeAdvance` copies those cached body velocities into analytical
ships. Two nearby ships can therefore report velocities representing different
intervals despite having contemporaneous positions.

`GUIOrbitDraw` changes orbital mode when manual input starts/stops.
`Ship.LockToOrbit` calls `BodyOrbit.CreateBOFromShip`; then
`ShipSitu.LockToOrbit` replaces the ship's velocity with the fitted body's
cached velocity. Keeping the old velocity for one frame would not repair the
following `TimeAdvance` or the fitted trajectory.

The fourteen captured native handoffs reproduce a maximum 1.913 m/s velocity
change. Replacing only the cached velocity convention reduces the maximum to
0.4335 m/s. A common-epoch conic fit alone leaves a maximum 1.4701 m/s error
because the native backward velocity sampling still occurs.

With both prototypes, the maximum captured handoff/next-update error is
0.0009881 m/s. This variant nevertheless fails wider phase/derivative checks.

## Why the broader phase-accuracy objective requires more

Native `SolveBigE` exits when the Kepler residual is below `1e-6`, with at most
ten Newton steps. At some small mean anomalies it returns its initial `E=M`
estimate. This occurs even with Venus's authored eccentricity, `0.0067`.
The local numerical derivative of that approximation is different from the
implicit derivative of the intended Kepler equation. A coherent velocity
formula cannot make that loose position curve accurate by itself.

The separate precision variant uses a `1e-13` residual and a maximum of 32
steps. It retains authored elements, parent hierarchy, orbit directions and
the existing numerical integrators. Its results include:

| Check | Result |
| --- | ---: |
| Maximum error across fourteen captured handoffs | 0.00000961 m/s |
| Maximum error after the next native update | 0.00000961 m/s |
| Maximum position change during the handoff | 0 m |
| Largest conic position-fit offset in those cases | 0.02053 m |
| Maximum handoff error over 64 native lock/unlock cycles | 0.00001005 m/s |
| Body JSON round-trip velocity discrepancy | 0 m/s |
| Maximum central position-derivative discrepancy in the boundary grid | 0.000521 m/s |

The broader grid includes prograde/retrograde orbits, nested references,
stationary manufactured references, eccentricities through 0.9, negative and
large clocks, and future-position queries followed by restoration. Native
relocking with a valid reference accepts matched velocity and rejects an
excessive relative speed. Escaping trajectories are not forced into bound
orbits.

## Concrete compatibility risks caught offline

Changing native phase precision while leaving the current detached
`NativeEphemeris.Evaluate` calculation untouched reproduces a **15.87545 km**
normal-time/SFF coordinate mismatch in an existing orbital fixture. The
composition test fails before proceeding. The precise variant therefore also
adapts that helper's precision inside the test process. Its production source
is untouched.

Agreement between the two implementations does not solve continuity for an
existing world. Re-evaluating today's captured body elements more accurately
changes Venus's derived position by up to **1,706.56 m** and VORB/its auxiliary
by up to **1,705.53 m** in the sampled states. A deliberately constructed phase
grid reaches **101,711.62 m**. That last value is a synthetic boundary case,
not an observed movement in the user's game.

Those differences are derived coordinate changes, not changes to JSON orbit
definitions. Nevertheless, free ships, attached ships, existing route points
and collision consumers cannot safely be assumed to transition together.
An eventual integration must prove continuity at that boundary. The prototype
does not introduce a rebase, migration, saved marker or automatic relocation.

## Existing patch composition

Control, paired native-phase prototype, and coherent precise prototype are
compared with these existing suites:

| Suite | Existing checks |
| --- | ---: |
| Orbital/SFF/PASS/economy | 5,368 |
| AI rendezvous and freight destination retention | 1,531 |
| Route completion and recovery | 689 |
| Planetary navigation and avoidance | 886 |
| Docking, attachment, towing and undock references | 834 |
| Native torch reactor, thermal, thrust and tank debits | 408,488 |

These counts describe the suites' coverage, not that every assertion directly
tests the new velocity path. The run manifest retains every process exit code.
The final reproduction completed all **18 suite processes** (six suites in
three configurations) with exit code zero. The precise standalone study passed
**598 assertions**. The incomplete variants and deliberately unadapted precision
control retain their expected failures separately; they are not counted as
production-ready successes.
Passing these suites does not cover the existing-world transition above or
constitute a complete Unity atmospheric, NPC lifecycle or collision playtest.

One existing docking fixture needed a test-only correction. `World()` supplies
an uninitialized body with arbitrary fixed coordinates/velocity and no orbital
elements. Its missing-reference fallback could pass through native zero-period
NaN arithmetic after preceding physics tests changed the ephemeris boundary.
`UndockReferenceChecks` now explicitly freezes its synthetic body, retaining
all existing assertions. The new study independently tests that fallback with
a valid ephemeris and a rejected high-speed case. No production attachment
rule was changed.

The experimental prefixes also explicitly honor `__runOriginal`; priority
alone is insufficient to assume another prefix's suppression prevents every
later prefix from executing in the bundled Harmony implementation.

## Performance

Bundled-Mono measurements use 64 bodies sharing a Venus/Sol hierarchy, 300
simulated frames, warmup and five repetitions. Typical measured solver cost
was approximately:

| Workload | Native | Precise paired prototype |
| --- | ---: | ---: |
| One query per body per frame | 0.013 ms/frame | 0.018–0.020 ms/frame |
| Ten repeated queries per body per frame | 0.014 ms/frame | 0.023–0.028 ms/frame |

The measured recurring allocation count was zero. The prototype checks an
existing same-time result before validating/re-solving the hierarchy. There
are no ship scans, new simulation ticks, background workers or saved histories.

These are solver microbenchmarks while the separate game may also be running.
They do not establish whole-game frame time or SFF-duration equivalence. The
observed incremental CPU cost is small, not literally zero.

## Layer ownership and next gate

The installed `StreamingAssets/data/star_systems/star_system.json` authors
orbital elements, station references and Venus atmosphere layers. It does not
define the velocity refresh threshold, Newton stopping tolerance or handoff
conversion. The demonstrated defects belong to C# arithmetic and cache
coherency; no AI behavior policy or asset correction is proposed here.

Before production integration, prototype and validate how an existing world's
free ships, attachments and active courses remain continuous when changing
the ephemeris convention. Include close docking/collision geometry and the
atmospheric boundary. Until that gate passes, keep the installed plugin as is.

Private detailed outputs are under `obj/OrbitalVelocityStudy/`; reproduce with
`Run.ps1`. The compact Venus input fixture and experimental sources are kept
with this tool so the result does not depend on the private recording surviving.
