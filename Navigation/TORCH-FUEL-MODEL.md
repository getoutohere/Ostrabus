# Torch course fuel accounting: pre-patch study

**Pending in-game playtesting.** This investigation is retained as documentation;
the related autopilot patch and native harness sources may not be in the same
commit. Results below describe offline modeling and the local Debug implementation.

November 8, 2026; shipped Ostranauts 1.0.1.10. This records the offline investigation
and model. Its [production implementation](TORCH-COURSE-FIX.md) now also runs through
the native harness. The cache synchronization fix remains separate.

## Confirmed accounting mismatch

`FlyToPath.PlotBurnLoop` charges `Ship.CalculateTorchFuelConsumption(deltaV,
limiter)`. That divides delta-v by the limiter's acceleration and multiplies by
the pilot fuel modifier. For a constant powered segment this is its duration
times the pilot modifier, independent of throttle. Drift has zero delta-v and
therefore no plotted fuel cost.

The waypoints store remaining normalized fuel seconds. Native playback
interpolates those values and SetReactants enforces the result on the tanks.
Maximum reactant consumption converts normalized seconds into kilograms. This
is real fuel accounting, not merely an inaccurate UI estimate.

Native manual operation instead derives fuel from pellet throughput and core
power. At ideal core temperature its normalized rate is pellet rate / effective
pellet capacity. At thermal equilibrium, heat balance requires approximately
`pelletRate = 5 * CYCLE`, with a small native idle-feed floor. Thus the equilibrium
budget multiplier is approximately `5 * CYCLE / pelletCapacity`, not CYCLE itself
and not the fraction of maximum G. G versus CYCLE is nonlinear.

This equilibrium formula applies only to a running, thermally balanced reactor
within capacity. It does not describe arbitrary cold cores, excess FLOW, startup,
missing cooling, changing hardware or the native feedback controller.

## Native-assembly model

`Tools/TorchNativeOracle/FuelStudy.cs` extends the existing native oracle. It runs
the shipped `FusionIC.Run` and `FusionIC.Fusion` with explicit condition, module,
inventory, ownership and Unity-side-effect fixtures. It compares their thermal
and fuel outputs against `TorchModel.Step` and `ManualHe3Rate`.

There are 36 operating cases: one, two and four pairs with adequate regulators
and capacitors; zero, quarter, half and 95% of thermally sustainable CYCLE; one
healthy cryopump; and three FLOW policies:

- `balanced`: calculate FLOW for ideal temperature at native 0.27-second ticks.
- `nativeFeedback`: execute native GetAdjustedFLOW once per reactor tick,
  starting from native GetFLOWforCYCLE; actual thrust is the native rated thrust
  at the current temperature. This is a controlled cadence experiment, not a
  claim about every rendering/frame schedule.
- `heldSffFallback`: hold native GetFLOWforCYCLE at normal tick cadence. This
  isolates the fallback's thermal behavior; it is **not** a full SFF execution.

Each case runs 6,000 reactor steps (~27 minutes), measuring fuel rate over the
last 3,000. The model tracks thermal abort eligibility but deliberately continues
the isolated reactor experiment afterward. Rows crossing a safety threshold are
not viable game flights. Initial transients are excluded from settled hourly
rates but included in minimum temperature and first-abort measurements.

Representative He-3 kg/hour, healthy reactor, unskilled pilot:

| Pairs | CYCLE | Vanilla plotted | Balanced native | Native feedback experiment |
| --- | --- | --- | --- | --- |
| 1 | 0.10 | 398.58 | 99.64 | 142.15 |
| 1 | 0.20 | 398.58 | 199.29 | 356.47 |
| 1 | 0.38 | 398.58 | 378.65 | 398.58 |
| 2 | 0.20 | 797.16 | 199.29 | 213.84 |
| 2 | 0.40 | 797.16 | 398.58 | 450.65 |
| 4 | 0.50 | 1,594.32 | 498.22 | 730.25 |

D2O is the native ~0.667 times He-3. The existing 0.75 skilled-pilot planning
modifier must be preserved explicitly; physical manual Fusion does not itself
apply that modifier. The native idle floor consumes about 0.20 kg He-3/hour in
these healthy fixtures, whereas the plot assigns no cost to drift.

The table demonstrates both the maximum-rate overcharge and why simply replacing
it with ideal equilibrium consumption would not accurately describe the current
autopilot controller.

## Controller and catch-up findings

1. `GetFLOWforCYCLE` returns `CYCLE * theoreticalPelletCapacity /
   effectivePelletCapacity * 0.9`. This does not generally match the native heat
   balance. For one healthy pair at CYCLE 0.20 it selects FLOW 0.18; balanced FLOW
   is approximately 0.492. Holding 0.18 at normal ticks eventually drives core
   temperature to about 62.5% of ideal, outside the autopilot's thermal tolerance.
2. Normal `GetAdjustedFLOW` raises FLOW when the reactor runs cold. Once cryogenic
   cooling clamps an overfed core back to ideal temperature and thrust matches,
   that controller has no signal requiring it to remove the excess feed. The
   experiment therefore settles above minimum required consumption in many cases.
3. In `FusionIC.Run`, thermal integration uses `min(elapsedSeconds, 1)`, while
   Fusion fuel consumption uses the full elapsed time. A single catch-up call is
   not equivalent to executing those settings over normal ticks.
4. Playback's reconciliation overwrites that physical fuel result with the
   waypoint budget. Changing the waypoint formula alone could simply refund the
   controller's excessive consumption, rather than making the reactor itself
   operate consistently with the new estimate.
5. Coast transitions deserve explicit handling: playback enters the reactor-control
   branch when waypoint fuel decreases or IsTorching was already true. A flat
   fuel segment subsequently leaves that branch; it does not continuously command
   a zero-flow coast. Its exact transition settings depend on adjacent waypoints.

Another 24 tests compare 600-second and six-hour held-control intervals at normal
ticks versus one native catch-up, across three fits and two CYCLE settings. They
confirm the thermal/fuel model against native math in both regimes. Safety and
shutdown side effects are fixtures here; they do not establish that an unsafe
held-control experiment would survive in game.

CSV outputs are regenerated in ignored `obj/torch-fuel-study.csv` and
`obj/torch-fuel-step-study.csv`. Before the transition extension, the oracle passed
2,026 checks; with the playback study below it passes 3,612. Build and
run it using the native validation command in Tools/TorchCalculator/README.md.
This study does not execute a complete plotted route, full Unity SFF coroutine,
electrical network, hardware wear, or save/reload. Existing oracle checks separately
cover worn capacity and cold-core single-step behavior.

## Recommended patch boundary

A planner-only equilibrium discount would conceal the FLOW mismatch. Prefer a
coherent player-course correction:

1. Define the running reactor's intended operating point for each powered/coast
   segment from native heat balance and actual hardware, including the idle floor.
   Control FLOW consistently with that operating point; retain native temperature,
   equipment and fuel safety checks.
2. Integrate the corresponding reactant cost into remaining waypoint budgets,
   preserving the skill modifier and native tank conversion. Adjacent changes in
   CYCLE need an explicit transient policy rather than silently assuming all cores
   are already at equilibrium.
3. During SFF, integrate across all crossed segment boundaries; do not treat an
   entire skip as the reactor operating at whichever controls happen to be selected
   at its endpoint. Keep native trajectory playback and reconciling tank mechanics.
4. Keep the model bounded by waypoint/transition count rather than running thousands
   of reactor ticks inside a game frame. The fine-step model is the offline oracle,
   not the proposed runtime implementation. Stable powered segments have an
   inexpensive closed-form cost.
5. Restrict the first implementation to player-plotted courses on materialized
   reactors. Shallow NPC cargo has separate permissive fuel semantics; changing its
   affordability or routing must not be an incidental consequence.

The follow-up playback study below completes accelerate/coast/brake, partial-jump
and differently partitioned catch-up modeling. A production implementation still
needs its own integration and lifecycle validation, especially hardware/tank
changes and reconstruction of a saved course. Genuine shortage behavior remains
covered by the separate native guard/cache regression matrix.

The investigation revises the earlier suggestion that fuel budgeting could simply
be corrected while leaving FLOW behavior untouched. The code and experiments show
that those two pieces need a deliberate agreement; changing the accounting alone
does not make the underlying operation physically consistent.

## Completed transition and playback study

`Tools/TorchNativeOracle/PlaybackStudy.cs` now executes native
`FlyToPath.PlotBurnLoop`, including easing, followed by native `NavData.TimeAdvance`,
`FusionIC.CatchUp`/`Update`/`Run`/`Fusion`, `SetReactants`, and the native autopilot
requirements check. The representative course has 1,200 seconds of acceleration,
four hours of coast, and 1,200 seconds of braking. It uses native-generated
waypoints rather than a second implementation of the interpolation loop.

Only engine/environment boundaries are fixtures: reactor component lookup inside
TimeAdvance, clock, condition and GPM storage, module/tank lookup, world undocking,
thrust-vector application, electrical billing, gradual wear, presentation and
shutdown side effects. The lookup adapter replaces exactly one GetComponent call
and asserts that match count. Native shutdown selection is recorded and fails the
case; it is not silently treated as a successful flight. The full GUIFFWD coroutine,
automatic repairs and station/no-wake interaction are not executed.

The 96 cases compare native behavior and the candidate across one/two/four pairs,
half and 95% sustainable CYCLE, ideal and 90%-ideal starting temperature, and four
schedules: normal reactor ticks, hourly jumps, a single whole-course jump, and
irregular jumps crossing waypoint/phase boundaries. All 96 complete with native
fuel and thermal checks active. Completed position and velocity endpoints match
the original native course; candidate changes never edit its motion data.

### Transition defect found

Vanilla interpolates CYCLE from the last powered waypoint to the first coast
waypoint over that entire coast interval, even though both endpoints have the
same velocity. With ten coast waypoints, a four-hour coast can interpolate throttle
down over its first **24 minutes**. The trajectory is already coasting; reactor
commands do not reliably describe that trajectory. The final braking waypoint's
zero CYCLE similarly represents a shutdown endpoint, not zero thrust throughout
the preceding braking interval.

This disproved the first candidate's assumption that interpolated waypoint CYCLE
was always an appropriate basis for physical fuel cost. At high CYCLE it could
charge more than vanilla by honestly charging for unintended coast throttling.

### Validated candidate

- Identify a powered interval from its planned velocity change. True coast
  intervals command zero CYCLE. In this native generated-course family, the
  nonzero adjacent CYCLE supplies the powered interval's limiter; do not blend a
  shutdown marker backward across a whole interval.
- Select FLOW from native heat balance, using current temperature, effective
  capacity and the reactor's actual thermal step duration. Do not change core
  temperature directly. Native Run still performs the thermal update and all
  native temperature/fuel checks remain in place.
- Integrate the corresponding normalized reactant cost over intervals, including
  the native idle floor. Store cumulative endpoint budgets and evaluate partial
  intervals explicitly when reconciling tanks. Additional independent quadrature
  and arbitrary subdivision checks validate the integration formula, including
  linear ramps crossing the idle floor.
- Restrict this candidate to an already ideal-temperature reactor and stable
  hardware. The 90%-temperature cases explicitly retain native controls/budgets;
  they test conservative fallback, not a validated cold-start discount.

For one pair at CYCLE 0.20, this course uses **265.720 kg He-3** under native
accounting versus **133.657 kg** under the candidate. At CYCLE 0.38, native again
charges 265.720 kg versus 253.231 kg under the candidate. Those comparisons hold
the test's powered durations fixed, rather than pretending the two CYCLE values
are the same journey geometry. Candidate fuel totals agree across all four
schedules, reactor temperature stays at ideal, and native trajectory endpoints
are unchanged. Native fuel totals also agree across partitions because playback
reconciles to its budgets, but native reactor temperatures do not.

Results: ignored `obj/torch-playback-study.csv` and
`obj/torch-playback-events.csv`; source and reproduction instructions remain in
the oracle. These are pre-patch fixtures only and are excluded from the plugin
build. The production correction and its additional regression coverage are
documented in [TORCH-COURSE-FIX.md](TORCH-COURSE-FIX.md).

### Implementation constraints settled by the model

Apply to newly plotted player torch courses first; preserve native skill discount
and safety policy. Keep physical-budget units tied to the reactor configuration
that produced them. Tank removal, capacity changes or damage outside the profile's
supported envelope must invalidate the profile before reconciliation can create
an inappropriate fuel refund. A cold core retains vanilla behavior until a new
supported course is plotted. NPC shallow navigation and manual burns stay outside
this patch's scope.

The offline reference uses fine stepping as an oracle. Runtime profile construction
and evaluation must use bounded waypoint work and closed-form interval costs,
with cached lookups and explicit cleanup when the course is replaced or cleared.
Existing in-flight/save-restored courses need positive profile identification;
do not reinterpret their remaining vanilla budgets as newly discounted fuel.
