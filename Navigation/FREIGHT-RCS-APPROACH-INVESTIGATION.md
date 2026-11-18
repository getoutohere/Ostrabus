# O-QMJ5: runaway RCS approach to Venus Orbital

Status: reproduced and repaired against Ostranauts 1.1.0.0 after the 0.6.0
checkpoint. Production code and offline checks are complete; the new repair has
not been deployed or used to intervene in the live ship's existing course.

## Observed sequence

O-QMJ5, MHNG Hauler 10, retains HaulerCargoPilot, a living Ryan Justin Solis
aboard, a VORB destination and an empty Food cargo pod. Its ten active RCS
thrusters have valid 1.1 count/exhaust caches. No combat, duplicate controller,
missing pilot or current materialized propulsion damage explains this incident.

The November 14 fleet capture already recorded `AnyFood=0` with matching market
and backing inventory. The November 15 pre-SFF compatibility capture repeats
zero Food. Thus its empty manifest predates this approach and the 1.1 playtest;
the investigation does not establish the original empty-dispatch caller.

Read-only comparison of the `next` and `docked` save archives establishes:

1. The original torch course ends at epoch 65634307014.8142, near VORB.
2. A replacement RCS course starts at 65634307014.8366, approximately 300 km
   from the station. Reconstructed native ephemerides put the initial relative
   speed below 0.001 m/s; it is not arriving with an unavoidable huge velocity.
3. The RCS course ends at 65676003750.6563: **482.60 days after its start**, at
   approximately (-10.6945, 1.62488) AU. It predicts -9,286.87 kg of RCS fuel.
4. Its first long velocity-matching phase alone budgets substantially more than
   the available 1,552.84 kg. Native AI playback continues when real fuel runs
   short; the physical tank reaches zero. This is a consequence of the bad plan,
   not evidence that a bookkeeping operation arbitrarily erased the tank.
5. The live hull is now hundreds of thousands of kilometres from VORB, receding
   at roughly 32 km/s, with current course playback and zero stored RCS fuel.

## Native algorithm defects

`Ostranauts.Ships.Commands.FlyToPath.GetNavSliderValues()` initializes `SliderData`
using `Ostranauts.ShipGUIs.NavStation.NavModTorchDrive.GetLimiterSafetyMax(ship)`.
That helper reduces a **reactor/torch** throttle until predicted torch thrust is
at most 2 G. This hull receives a limiter of about 0.200000688.

`FlyToPath.PlanTrip4()` and `PlanTrip4Sub()` reuse that limiter in the RCS branch
through `GetAcceleration()`. A powerful reactor therefore reduces this NPC's
otherwise independent RCS acceleration to about 20%, before applying the separate
native `LimitAccelSafe()` cap. The actual RCS acceleration at full input is about
18.29 m/s²; the inherited limiter reduces the planner to about 3.66 m/s².

The planner repeatedly estimates the target's future position and velocity and
rebuilds a velocity-match/burn/coast/brake route. VORB's orbital velocity changes
over those estimates. At this low acceleration, those guesses can diverge: the
estimated trip gets longer, the target is sampled at a different orbital phase,
and the next trip estimate becomes still worse. After the fixed ten refinement
iterations, **PlanTrip4 returns the last route without requiring convergence or
checking that its endpoint actually meets its destination**.

This is a numerical planning failure combined with inappropriate coupling of
NPC RCS acceleration to the reactor limiter. No additional force from our
phantom-thrust patches is needed to produce the escaping trajectory.

## Native reproduction and patch comparison

An offline bundled-Mono study reads the saved route origin, mass, propulsion
values and native Sol/Venus/VORB orbital parameters. It executes the native
speed calculation, torch safety limiter, RCS acceleration/fuel calculations,
collision radii, body ephemerides and `PlanTrip4`/`PlanTrip4Sub` algorithms.
There are explicit fixtures for player-control identity, the empty cargo mass,
Venus as the dominant-gravity body and absence of an obstruction detour. It is
not a full Unity scene or an exact replay of the original planner call.

| Case | Result |
| --- | --- |
| Native planner with its native 0.200000688 limiter | Returns a 456.99-day route, misses the future station by about 1.589 billion km, terminal fuel -8,910.84 kg. |
| Same inputs with the relevant Ostrabus planner patches installed | Byte-identical duration, endpoint, iteration history and fuel result. |
| Diagnostic control using full RCS acceleration | Converges in 434.92 seconds (7.25 minutes), with 520.40 kg left and the intended approximately 4.92 km stand-off. |

The A/B installs FreightPlanningClockPatch, DepartureWaypointPatch and
TorchTransferKernelPatch, the production intercepts on these planner methods.
There is no historical freight-planning scope or player torch-planning scope in
this control. The latter two corrections do not replace the NPC RCS kernel.
No shared RCS-acceleration or collision-radius getter is overridden.

The captured route is 482.60 days, not exactly the reproduction's 456.99 days.
Saved precision and reconstructed call context do not reproduce every original
floating-point intermediate. The ten-step divergence amplifies small changes.
The supported conclusion is that the native planner itself produces the same
failure class with this ship's saved approach, and the tested production planner
intercepts do not change that reproduction. It is not proof that all upstream
ship history or every other patch is causally irrelevant.

The native JSON plot/market/ship definitions do not own this convergence loop or
the reuse of the reactor limiter. A correction to those mechanisms belongs in
C#, preserving authored role, destination and cargo semantics.

Local evidence is retained under ignored `obj/QMJ5Study/` and the live survey
under `obj/EconomySurvey-20261116/`. No complete save archive was copied into the
repository; only selected private ship records and small orbital parameters were
extracted for this investigation.

## Implemented repair

[AiRendezvousPatch.cs](AiRendezvousPatch.cs) repairs the shared `FlyToPath`
planner, rather than special-casing this freighter or Venus:

- `AiRcsAccelerationPatch` gives NPC RCS calculations full independent input,
  leaving the native caller's `LimitAccelSafe()` cap authoritative. Player
  control, torch limiting and atmospheric lift-rotor limiting are preserved.
- `AiRendezvousValidationPatch` checks the returned route's finite state,
  chronological points and physical rendezvous with its moving destination.
  It permits native RCS/torch stand-off distances and region-border velocity
  offsets. Recursive obstruction/departure calls are checked against their actual
  intermediate destination, not the final station. Target sampling restores the
  exact position/velocity caches of the body and its parents, including historical
  SFF/PASS scopes with independent position and velocity clocks.
  Free-hull routes retain the native fixed-position target model; the validator
  does not introduce a separate linear forecast that the solver never used.
  Predicting future free-hull interception is outside this repair.
- `FreightRendezvousFailurePatch` preserves a still-valid freight destination
  specifically when the new numerical check rejects a plot, removing its obsolete
  fuel prediction. Native retry counters, stop commands and behavior-tree results
  remain authoritative. Other failures retain native target cleanup.

These are C# numerical/bookkeeping repairs. The reviewed plot-manager, market
and ship assets do not define the faulty iteration or limiter coupling, so no
JSON behavior override is required. There is no new tick, persistent fleet
state, forced arrival, fuel grant or replacement AI controller.

The production repair reproduces the full-RCS control's **434.92-second**
approach with **520.40 kg** remaining. Genuinely weak/divergent plots return
failure rather than publishing a runaway trajectory. This is not a guarantee
that every physical rendezvous is solvable.

## Qualification and remaining live recovery

[Tests/AiRendezvousCheck](../Tests/AiRendezvousCheck/README.md) passes 1,454
native checks: the unpatched divergent control, repaired moving-station approach,
six AI roles, 27 phase/mass/distance combinations, recursive intermediate legs,
native torch and free-hull legs, player/rotor exclusions, safe acceleration caps, historical
clocks and actual native freight retry/delivery behavior. Its small fixture
contains numerical orbital/propulsion inputs, not a saved crew or complete world.

The composed orbital/SFF/PASS suite passes 5,368 checks; its economic suite passes
1,494 with the three new hooks installed. Separate retained-system regressions
pass 689 route-completion/recovery, 449 docking/orbit-lock and 402,668 player torch
oracle checks. These cover different boundaries; their counts are not a claim
of complete game simulation coverage. Full binding verifies 300 patch classes;
custom isolated and combined navigation builds bind successfully.

The Traffic behavior graph records the native acceptance defect and conditional
effective acceptance/rejection. Numerical convergence is supplied by external
native tests, not predicted by the lifecycle graph.

O-QMJ5's already-installed bad course is not silently rewritten by these hooks.
An explicit recovery after deployment/reload is still required to discard that
course and let native navigation resume while preserving the hull and pilot.
