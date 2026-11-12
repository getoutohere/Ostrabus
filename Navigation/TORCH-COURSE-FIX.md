# Player torch-course fuel and control correction

**Status: included in 0.4.0.** Live ordinary and braced interplanetary burns cover
powered SFF, coast and final braking. Automatic endpoint cleanup and the newest
moored-departure preview correction remain offline-validated cases. The record
below distinguishes implementation, native-assembly tests and live evidence.

## First live normal-simulation burn, 2026-11-11

O-GXD, 26,798 kg, one active feeder/laser pair, approximately 30-minute course
at a 3G limit, no SFF and no tow. Retained private trace:
obj/torch-first-burn-complete.jsonl; streaming analysis, summary, segments and
scalar CSV in obj/Analyze-TorchBurn.ps1 and obj/torch-first-burn-*.
All 3,834 records were written, zero dropped, with no recorded call exceptions
or truncated fuel scans. Sampled state was analyzed offline, not pasted into chat.

- Planned powered/coast time: approximately 23.8 / 6.1 game minutes.
- Measured He-3 use 127.46256 kg and D2O use 85.01753 kg. Native tank capacity
  is volume times density; these reactant condition quantities are mass.
- Equivalent full-flow fuel use: 0.3203466 h versus approximately 0.3202931 h
  in the observed plan segments, a 0.0167% difference. Boundary/idle timing and
  small capacity wear are included in the measured baseline-to-final interval.
- Velocity differences within powered segments independently give 2.999957 to
  3.000050 G; sampled coast segments give zero acceleration. Core temperature
  stays at 0.725 MeV within float noise. No added StatICABLWall damage.
- FLOW applied at Fusion entry averages about 80.10%, with constant 32.206%
  CYCLE. Native NavData.TimeAdvance nevertheless restores an older FLOW value
  between Run calls, as low as zero and initially about 8.20%. Its FLOW read
  precedes CatchUp, and its final slider write follows the corrected reactor
  control. This is a confirmed competing-controller defect in the integration;
  the healthy heat/fuel result does not make the stored/UI setting correct.
- Final FLOW, CYCLE and thrust are zero and the course is detached.

The two follow-ups are now repaired in the working tree and native offline
regressions; they have not yet been rechecked live. This observation does not
validate SFF or towing playbacks.

The initial live preview also retained the native estimate because the reactor
was in power mode (baseline knobRatio 0). Engage replanned after selecting torch
mode, and only then applied the correction. Eligible warm previews now use the
same future-torch budget in either mode. Plotting does not change the reactor's
mode, controls or inventory; native engagement checks and runtime validation
still require torch mode before execution.

Always-on miscellaneous bugfix for Ostranauts 1.0.1.10. This fuel/control component
does not change trajectories, manual burns or shallow NPC cargo policy. The
separate player-only [momentum-aware planner](TORCH-INITIAL-VELOCITY.md) now changes
newly plotted torch trajectories in the full development build. No PDA setting or
custom saved object is added.

## Faults addressed

- Native planning charges maximum reactant use for every powered second, regardless
  of the reactor settings required by that segment.
- Native FLOW feedback can settle above the required feed rate while cooling hides
  the surplus. Catch-up also applies a different thermal step than normal ticking.
- Interpolating CYCLE across waypoint boundaries can keep throttling throughout
  the beginning of an already-coasting trajectory, or taper braking prematurely.
- Native tank reconciliation does not refresh the fuel cache used by autopilot.

See the [investigation/model](TORCH-FUEL-MODEL.md) and
[cache reproduction](TORCH-FUEL-INVESTIGATION.md).

The separate [incoming-momentum investigation](TORCH-INITIAL-VELOCITY.md)
confirms native cancellation-first trajectory planning. Its replacement solver
is implemented separately and feeds ordinary waypoints into these fuel/control
fixes; both still require their remaining live validation.

## Implementation

`MiscBugFixes/TorchCourseFuel.cs` constructs a bounded interval profile after the
native course-plot UI finishes planning. It applies only to the player's fully
materialized ship, with a running reactor in power or thrust mode, working
cooling, an already near-ideal core and thermally feasible settings. Execution
requires thrust mode. Unsupported/cold previews
retain their original native budgets and behavior.

Powered intervals follow the native planned velocity change and nonzero limiter;
true coast intervals use zero CYCLE. The cost follows native heat balance and the
idle feed floor, preserving the native pilot-skill modifier. Cumulative budgets
are ordinary NavData waypoint values. Runtime evaluation is a bounded interval
lookup, not fine-stepped reactor simulation.

The reactor Run prefix chooses FLOW from the actual current core temperature,
effective capacity, desired CYCLE and native thermal timestep. It does not set
temperature directly, bypass shutdown or suppress temperature checks. Native
reactor math, tank writes and trajectory playback still execute.

The existing playback patch also replaces exactly two computed control writes
inside NavData.TimeAdvance: the ordinary interpolated CYCLE and legacy FLOW
feedback assignments. For the active corrected course they yield to the reactor
controller, so the stored settings and sliders retain the controls actually
applied by Run. Uncorrected courses delegate to the original setter. Native
literal-zero writes for aborts and arrival remain direct and unchanged, including
when the course has not yet detached. This extends an existing patch class and
adds no new hooked method or per-frame allocation. Unknown call-site shapes fail
binding instead of suppressing other controls.

`Tools/TorchNativeOracle/ControlHandoffChecks.cs` adds 56 native regressions for
power-mode prediction, unchanged physical settings/tanks during preview,
preview/engagement equality for both pilot modifiers, native burn/coast/braking
control retention, ordinary/SFF completion resets, legacy/manual behavior,
cold/unknown-mode exclusions and saved corrected courses restored in power mode.
The latter remain identifiable but fail runtime validation and idle safely.
The original failures were reproduced before the respective repairs. Run the
focused suite with the oracle argument `control-handoff`; the normal oracle run
also includes it with lifecycle, save/reload and towing integration checks.

Tank reconciliation uses the profile's cumulative budget at the current epoch,
clamped at arrival. Thus a jump crossing acceleration, coast and braking accounts
for all crossed intervals rather than charging the endpoint settings for the
whole jump. The original kg-per-normalized-second conversion is retained across
small changes from wear/repair; substantial configuration changes cancel instead.
The separate cache postfix synchronizes the actual remaining reactants afterward.

## Lifecycle and safeguards

- Profiles use a ConditionalWeakTable keyed by NavData. Replaced/discarded courses
  are collectible, including their profile-to-ship references. Playback context
  is restored by a Harmony finalizer on both success and exception.
- Replacement courses and manual operation do not inherit the old profile.
- Native GetJSON/LitJson/NavData reconstruction preserves the corrected budgets.
  A restored course must match both those interval costs and its stored plotting
  FLOW before it can regain corrected control. The existing `fFlowMultPlot` field
  stores the actual balanced plotting FLOW, preserving its native meaning; it is
  not a new save type or an arbitrary marker. Legacy native courses are left alone.
- Tank identity/volume, capacity, cooling, exhaust configuration, reactor mode,
  pilot modifier and externally changed reactant totals are checked. Lost fuel
  must not be silently recreated, nor added fuel silently discarded. Such changes
  cancel the course, idle controls, clear the target and unregister the native
  autopilot controller; the player must plot again. A warning/log entry explains it.
- Reactant observations update only after legitimate native reactor consumption
  and course reconciliation. A change discovered inside CatchUp also prevents the
  following stale reconciliation from writing fuel, and re-idles controls after
  the original playback method finishes.
- Inventory checks read the native raw typed lists without allocating filtered
  copies. Profile arrays/tank snapshots are allocated when profiling a course,
  not on every frame. There are no polling coroutines or added world sweeps.

Without Ostrabus, saved NavData remains normal game data and loads natively;
Ostrabus's reactor-control corrections are naturally absent. The patch does not
introduce a save dependency on a mod-defined type or sidecar file.

## Validation and verdict

`Tools/TorchNativeOracle` links the actual production source and runs it against
the shipped assembly under bundled Mono. The matrix compares native behavior,
the reference model and production hooks for one/two/four pairs, different
throttle/temperature states and normal/hourly/whole-course/irregular jumps.

Lifecycle coverage uses the actual native waypoint generator and playback, the
game's LitJson serializer and NavData constructor, native autopilot requirements,
configuration changes discovered before and inside CatchUp, reactant loss/refuel,
course replacement, manual-operation isolation, exception unwinding and weak-key
collection. The fixture replaces Unity/environment services, not the native fuel,
thermal, waypoint interpolation or requirements logic.

Fuel/control result: **4,202 native-assembly checks passed**, including 144 full
playback comparisons, 86 lifecycle checks, 56 preview/control handoff checks and
49 joint reactor-force, grouped playback, fuel and brace-abort checks from the
separate [towing repair](Towing/README.md). The full plugin binding audit passes
**220 classes** with zero failures or warnings, including the separate player
trajectory kernel. Debug DLL/PDB build cleanly.
The towing feature can also be built independently of the fuel/control changes.

The momentum-aware trajectories add 32 solo/towed normal/SFF playback scenarios
and 398,200 assertions, bringing the complete default oracle to **402,402 checks**.
Its separate solver and planner modes cover 4,728 numerical scenarios and 45
moving-orbit wrapper cases; see the [planner validation](TORCH-INITIAL-VELOCITY.md).

Verdict: the ordinary live burn validates accounting/thermal behavior for its
tested configuration. Its preview/control follow-ups are repaired and pass the
native regressions; additional live SFF/towing validation remains pending.
This is an offline correctness verdict for the supported envelope, not a claim
that every Unity UI, station transition or combination of other mods has been
exercised live. Deploy/restart and plot a new supported course; a legacy active
course is intentionally not retroactively discounted.

### Live SFF accounting failure, 2026-11-12

The subsequent OKLG-to-VORB braced-tow flight exposed an accounting gap. After
about 6.133 game hours including a six-hour SFF, both vessels retained identical
velocities and the velocity change measured 3.000 G. He-3 consumption, however,
was 5,499.690 kg against the course's cumulative 2,775.937 kg: 1.9812 times the
prediction. Actual tank contents and reactor caches agreed. This is physical
overconsumption, not a display-only failure. Movement/tow success does not
validate SFF fuel accounting. This live failure was reproduced and repaired
offline as described below, then confirmed in the repeated powered SFF flight.

The native timing boundary explains a reproducible double charge:

1. `GUIFFWD.FFWD` advances `CrewSim.system.Update` before catching up scene COs.
2. `NavData.TimeAdvance` uses the new `StarSystem.fEpoch`, calls reactor CatchUp,
   then reconciles tanks to the course fuel budget.
3. `FusionIC.CatchUp` calls Update, whose Run decision and elapsed time use
   `COSelf.Clock.Epoch` and `fTimeLastRun`. A lagging CO clock does not consume the
   skipped interval yet, so the course reconciliation charges it first.
4. When that CO clock subsequently catches up, native Run/Fusion consumes the
   interval again. The current reactor postfix observes those lower tank totals
   without recognizing that the course already accounted for the same time.

The `sff-clock` oracle reproduces this ordering using native reactor/playback
methods and current production patches. Powered ten-minute and six-hour jumps
consume exactly twice their scheduled He-3 with lagging CO time, versus once
with synchronized clocks. The original suites synchronized these clocks and
therefore missed the defect. An initial one-hour probe landed in the fixture's
coast phase and did not reproduce a doubled powered burn; the dedicated cases
now stay within a continuous powered segment. The reproduction establishes the
mechanism; the precise call ordering in the live skip was inferred from source
and before/after measurements, rather than captured by an enabled trace.

Evidence remains in ignored `obj/oklg-vorb-engaged-reactor.json`,
`obj/oklg-vorb-sff1-reactor.json` and the pre-repair reproduction
`obj/torch-sff-clock-study.json`. No live fuel intervention or additional trace
was applied while diagnosing this finding.

The repair records the pending interval already paid by a successful corrected
course tank reconciliation. A narrowly matched transpiler adjusts only the
elapsed-time multiplier used to calculate fuel in `FusionIC.Fusion`: subtract
the overlap between this reactor update and the paid interval. It does not
change either clock, suppress reactor Run/Fusion, refund fuel after the fact or
change authored JSON rules. Native heat, power, damage and wear retain their
original elapsed time. C# owns both mechanisms; the reactor/tank assets define
fuel conditions and hardware, not the ordering of these clocks.

The paid interval ends at course arrival, so later idle/manual fuel remains
chargeable. Once the native reactor has processed through it, that interval has
no effect. The weak table stores two numeric timestamps per affected reactor,
with no ship/course/CO references in its values and no saved or serialized state.
The scalar slot is reused, avoiding allocations for repeated reconciliations;
discarded reactor keys and their slots are collectible. Legacy courses, other
reactors and manual operation without a paid interval retain native fuel logic.
No new polling or trace is required.

The dedicated `sff-clock` regression passes 256 checks, including one/two/four
pairs, both pilot modifiers, either clock order, partial catch-ups, repeated
burn/coast/braking transitions, arrival overrun, native course JSON restoration
and subsequent manual fuel usage. Native heat executes; an observer verifies
the full elapsed interval still reaches core wear (the fixture substitutes the
wear operation). Separate checks preserve pre-course fuel and verify discarded
reactors remain collectible. The full oracle passes 402,658 assertions, the
full plugin audit binds 228 patch classes with zero failures/warnings, and the
custom builder's 417 self-checks pass with this pending feature still excluded
from custom packages. Debug DLL/PDB build cleanly. These offline checks cover more
cases than the live flight below; they do not establish that every course phase
has been exercised live.

### Corrected powered SFF confirmed live, 2026-11-12

The player reverted the save, plotted/engaged a fresh M-TZ3T-to-VORB course with
O-GXD braced in tow, and ran normally until 3 G before pausing. The installed DLL
matched the rebuilt Debug SHA-256. Request-only snapshots captured the baseline;
no trace, fuel intervention or world mutation was needed.

The next SFF advanced exactly 21,600 game seconds. He-3 use was
**2,715.667119 kg**, against **2,715.667195 kg** scheduled: a difference of
0.000075 kg (about 0.08 g). D2O use was 1,811.349957 kg. Both reactant caches
exactly matched the physical tanks. The active course retained its identity and
VORB target; both vessels retained exactly matching velocities. Velocity change
measured 2.999998 G over the skip. Reactor temperature stayed at 0.725 MeV and
the wall-damage counter remained 1. Native SFF repairs brought pellet capacity
from 3.991027 to 4 and cooling to 0.6, accounting for the slightly higher endpoint
instantaneous model thrust (~3.005 G).

This confirms the duplicate-charge repair for this powered, two-pair, braced-tow
SFF case. The later midpoint reading also confirms the powered-to-coast
transition: at 30.1443 hours elapsed (28.3964 hours remaining), CYCLE, planned
acceleration, reactor thrust and group-anchor thrust were all zero. He-3 was
32,428.858697 kg, about 0.000901 kg below the cumulative planned inventory;
caches matched tanks, wall damage stayed 1, temperature stayed 0.725 MeV and
both hull velocities matched exactly. The original course/target remained active.
The final-approach reading at 58.2853 hours elapsed (919.461 seconds before
scheduled arrival) confirms the coast-to-braking transition. The original course
remained active at its planned 3 G, with both hull velocities still exactly
matching. He-3 was 23,399.080686 kg: cumulative use 18,329.158081 kg, within
0.000650 kg of the planned inventory. Both fuel caches matched tanks; temperature
remained 0.725 MeV and wall damage remained 1. The endpoint instantaneous model
acceleration was about 3.005 G after native repairs and minor mass changes.
Autopilot completion/arrival itself remains unobserved. Private
evidence: `obj/oklg-vorb-rerun-pre-sff-reactor.json`,
`obj/oklg-vorb-rerun-post-sff-reactor.json` and
`obj/oklg-vorb-rerun-post-sff-summary.json`.
The midpoint readings are under `obj/oklg-vorb-rerun-midpoint-*`.
The final-approach readings are under `obj/oklg-vorb-rerun-final-*`.

The player then intentionally disengaged at roughly 1 km/s relative to VORB
for manual docking. The last pre-disengagement sample had 40 seconds remaining,
343.766 km range and 1.159 km/s relative speed. The post-disengagement sample at
epoch 65633327765.871269 had no attached NavData, zero input acceleration,
23,288.400544 kg He-3 remaining (18,439.838223 kg used since engagement), matching
fuel caches, stable 0.725 MeV temperature, wall damage 1 and an intact two-ship
tow with identical velocities. Manual FLOW/CYCLE retained their last burn
settings; native NavModCoursePlot's disengagement branch clears the course and
controller without zeroing those controls. These are manual-takeover observations,
not proof of automatic endpoint shutdown/arrival cleanup. Evidence is under
`obj/oklg-vorb-rerun-disengaged-*`.
