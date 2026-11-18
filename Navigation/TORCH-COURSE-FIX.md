# Player torch-course fuel and control correction

**Status: included in 0.4.0.** Live ordinary and braced interplanetary burns cover
powered SFF, coast and final braking. Automatic endpoint cleanup and the newest
moored-departure preview correction remain offline-validated cases. The record
below distinguishes implementation, native-assembly tests and live evidence.

## Sensor selection cancelling a player course, 2026-11-17

**Status: implemented and offline validated against 1.1.0.2; pending live test.**

Native `Ostranauts.Ships.Commands.FlyToAutoPilot.RunCommand` replaces
`shipSituTarget` with the selected sensor contact's situation while an existing
course continues. `FlyToPath.RunCommand` can then finish early near that contact.
Our `ShipTargetLifetime.Removed` also mistook that shared situation for proof
that the plotted destination had been removed, clearing the unrelated route.
This is a C# association defect. Reviewed `condowners_navmods.json` and
`cooverlays_navmods.json` register the course module; they do not define this
target-resolution or cleanup mechanism.

`Navigation/PlayerCourseTargetPatch.cs` keeps those native command reads tied to
the existing plotted destination rather than the sensor selection, without
changing the selection shown in the UI. Registered destinations retain native
motion, independently offset targets remain owned, and coordinate fallback
copies its endpoint rather than allowing native updates to mutate a waypoint.
Target removal uses the explicit destination identity for an active player
course and repairs legacy shared-contact associations before cleanup. Removing
the actual destination still cancels the course. Native plotting also omitted
clearing an earlier hull ID for an empty-space plot; the successful-plan postfix
records the current hull, stellar or body identity, or clears it for coordinates.

The feature is packaged with `navigation-targets`, applies to player/non-NPC
course playback, and adds no saved fields, per-frame callback, tracking table or
replacement flight controller. Native requirements, pause aborts, route playback,
fuel, movement and NPC targeting remain authoritative.

`Tests/AiPlannerDiagnosis -- autopilot-target` executes the shipped native
commands and destruction/restore hooks. Its A/B controls reproduce native
scan retargeting and premature arrival; repaired cases include nearby, distant,
hidden and saved/reloaded contacts, coordinate/offset goals, genuine destination
loss, NPC exclusion and native pause failure. Successful-plan identity checks
exercise the production postfix with a detached GUI fixture; they do not execute
Unity drag/input or the full GUI planner. Engine presentation and asset
materialization are fixture boundaries. The original live O-WG8P selection and
subsequent disposal fit the reproduced failure, but no original cancellation
call stack was captured. Diagnostic evidence is under
`obj/AutopilotStop-20261117/`.

## Coast acceleration / crew knockdown integration repair, 2026-11-16

**Status: implemented, offline validated and live coast replay confirmed.**

At epoch 65634576597.314873, M-TZ3T and its braced partner O-GXD reported
1.087722 G while the corrected reactor's FLOW/CYCLE and modeled thrust were
zero. Course points 28/29 have identical velocities: X =
6.75063184760173e-6 AU/s and Y = 2.6773542711665261e-6 AU/s. The interval is a
coast, but its first waypoint retains the previous 3 G burn's `vAccIn`; the
second has zero. Native `NavData.TimeAdvance` interpolates those cached force
vectors independently of velocity. At progress 0.63742590346815142, this yields
1.087722 G, matching the captured ship/group input. The trajectory itself still
interpolates equal velocities and therefore appears correct.

Our reactor control correctly clears thrust for coast; native course playback
can restore the obsolete vector between the reactor's .27-second updates.
This is a missing integration boundary in our corrected playback. Native
`Ship.Gravity` uses reported onboard acceleration for crew encumbrance, maneuver
wear and a >1 G change threshold for `KnockDownCrew`. The reviewed assets
`ACTShipKnockDown` / `TIsShipKnockDownTarget` correctly protect seated/prone crew;
neither those rules nor the threshold should be weakened.

The existing playback postfix now reports the derivative of the velocity that
native playback actually prescribes for the current interval, after reactor
catch-up and before group gravity consumes it. It changes neither waypoint
positions/velocities nor the interpolation of those values. Equal endpoint
velocities report zero internal acceleration; powered intervals report their
prescribed velocity slope. The corrected input is synchronized through the
existing native docking-group input method. Fuel, controls, attachment release
and completion retain their existing mechanisms.

Scope is an eligible corrected player torch profile and its actual course-owner
situation. Intervals with analytical/body-attached points or RCS force/fuel use
retain native handling; their velocity changes must not be double-counted as
torch force. Uncorrected/AI/manual/unsupported courses are untouched. No new
Harmony class/target, polling, saved field or replacement controller is added.
Per-interval slopes are cached in the existing weak profile, not serialized.

`Tools/TorchNativeOracle/CoastGravityChecks.cs` reproduces the observed numbers
with a numerical coast segment and synthetic surrounding burns. The old
playback postfix is removed for the A/B control; all other corrected-control
logic remains installed. Actual native `Ship.SetThrust` and `Ship.Gravity`
produce 10 knockdown requests across five prescribed conflicting update cycles.
The repair produces zero coast input and zero requests. This establishes the
mechanism and repairs the observed stale vector; the paused live capture did
not record the exact historical Unity call order.

The focused suite passes 707 checks: solo/towed groups, native-format/fresh/saved
courses, independently advanced reactor clocks, normal/SFF mode, repeated coast
updates, powered acceleration and legitimate knockdowns, RCS/analytical
exclusions and uncorrected fallback. The native gravity setter executes; actor
queue effects, audiovisual effects, damage application and crew gravity/item
recalculation are explicit fixture boundaries. The earlier fuel/towing harness
substituted SetThrust or suppressed Gravity and therefore did not prove this
interaction safe. Native SFF also suppresses knockdowns during the skip itself.
These limits explain how earlier validation could miss it, without claiming a
proven date for the first live occurrence. Code history at `ed83d46` (0.4.0)
already has the zero-coast reactor control and a playback postfix that only
handles aborts; native 1.0.1.10 and 1.1.0.0 both interpolate the cached input
vector. The reproduction uses a fresh attached profile and an ordinary debit,
not native-course adoption or a refund. The mismatch therefore does not require
either recent fuel repair. Private live snapshots and native
outputs are in `obj/Knockdown-20261116/`.

Final validation: the full torch oracle passes 408,488 assertions, including
the focused 707. Docking/orbit 834 and route-completion 689 checks pass, all
304 current full-plugin patch classes bind cleanly, and independent corrected
fuel/towing custom packages compile and bind with their original class counts
(7 and 8). This change extends an existing postfix and adds no runtime hook.

### Live replay

Loaded module `706bae10-4c4c-471f-882c-a8f3d93c411b` matches the rebuilt DLL.
At epoch 65634576657.051994 the same 67-point VORB course remained attached in
segment 28. FLOW/CYCLE, modeled reactor thrust, member input, group-anchor input
and both hulls' interior gravity were zero. Subsequent ordinary-time snapshots
retained those values. Ayden's complete active-condition snapshot at
65634576724.621567 contains neither Prone nor Sitting/SittingSecure, so that
observation does not depend on the native seated exemption.

A later snapshot at 65634598345.664253 still has zero coast input and the same
course in segment 31. Over 21,688.612259 game seconds since the initial snapshot,
He-3 decreased by 1.200644349829 kg and D2O by 0.800829776135 kg; core wall damage
did not change. The next captured GUI SFF receipt covers exactly 21,600 seconds,
65634598357.178986 through 65634619957.178986. At 65634620001.9031, after normal
time resumed, the course remains active in segment 34 with zero reactor/member/
anchor input and zero interior gravity on M-TZ3T and O-GXD. Core wall damage is
still 1.83360720971577.

These are polled live states and a native SFF receipt, not a continuous record
of every crew interaction. They confirm removal of the captured coast mismatch
through normal updates and skips; the repaired build's later braking/arrival
are not yet claimed as live observations. Evidence: private
`obj/Knockdown-20261116/post-reload-*`, `live-*` and the orbital receipt.

## Reactant refund and native-course recovery repairs, 2026-11-16

**Status: implemented; offline validated against 1.1.0.0, pending live playtest.**

The `depart`, `coast` and `arrive` snapshots retain the same D2O tank identity.
Its stored amount changes from 16,822.15198861 kg to 10,413.5440748712 kg,
then exactly its 44,722.8 kg capacity. The player reports no D2O purchase or
transfer. At arrival, five He-3 canisters are empty and the last holds its
5,216.0440246582 kg capacity. Later live tank purchases are outside this saved
increase. No live fuel repair or retroactive fuel subtraction has been applied.

`Ship.AIRefuel()` assigns only `fShallowRCSRemass`; the RCS refill patches do not
write fusion reactants. This path does not explain D2O appearing in a tank.

Native `FusionIC.SetReactants(double)` can reconcile an overstated remaining
budget by adding reactants through `RemoveFromTanks`. Its temporary
`CTReactantA`/`CTReactantB` selectors require the corresponding positive fuel
condition, so `CODicts.GetTriggeredCOListByType` excludes empty canisters.
If the included He-3 tank is already full, the paired refund cannot reach its
target. Repeated reconciliation can keep adding D2O until that tank fills too.
There is also a remainder error in `FusionIC.RemoveFromTanks`: after filling a
tank, native code sets the outstanding negative withdrawal to minus that tank's
free space, discarding the actual remainder. A full first tank therefore stops
a refund even if empty tanks follow it.

The optional `saved-fuel` oracle mode loads only extracted numerical tank/core
and route records. A controlled native refund case starts with 10,000 kg D2O,
one full He-3 tank and empty spare He-3 tanks. Eighty reconciliation calls fill
D2O to 44,722.8 kg while He-3 stays at 5,216.0440246582 kg. Removing the
production fuel-cache postfix gives the same result. Including empty tanks
before the full tank permits one paired refund; ordering the full tank first
also requires the remainder correction. Fuel
debit/reconciliation executes native C#; typed-list eligibility is an explicit
fixture matching the reviewed native selectors. This establishes a reachable
native mechanism matching the observed asymmetry, not an exact replay of the
historical calls or proof of the original excessive-budget source.

The saved trip also retains native one-budget-second-per-powered-second and
zero-coast-rate points. Its route fails recognition by the corrected saved
profile, so installed patch classes alone do not establish that this historical
trip used corrected consumption. The old recovery path recognizes only an
already-corrected saved profile, and permanently ignores a course after even a
temporary eligibility failure. The exact initial plotting call that missed the
correction has not been established. Private evidence is retained under
`obj/SffStall-20261116/fuel-*.json` and `saved-fuel-*.txt`; full saves are not
copied into the repository.

The existing fuel-cache feature now supplies all non-destroyed typed tanks to
native `SetReactants`, retaining its own ship-ownership filter. A prefix limits
refunds to the smaller paired free capacity. A narrow additional transpiler
changes only `RemoveFromTanks`' refund remainder to
`outstanding += capacity - contents`. Positive withdrawals retain native logic.
This fixes materialized fuel reconciliation generally; it does not refuel AI
ships, alter manual reactor consumption or change tank capacity/stoichiometry.
The `conditions.json`/`condowners.json` assets define kg quantities and tank
hardware correctly; the selectors and arithmetic are native C# defects.

Player course recovery now explicitly recognizes native powered/coast budgets.
After native reactor catch-up, it anchors a corrected remaining-trip budget to
the physical inventory at `FusionIC.TimeLastRun`. The pending interval through
global time is charged once by the existing settlement mechanism. Past fuel
expenditure is retained; adoption does not credit or deduct historical burns.
Cold or temporarily unavailable player/control state is retried. Unknown budget
formats retain native handling; a recognized but unaffordable remainder aborts.
This uses existing waypoint/flow serialization and weak runtime profiles, with
no new saved fields, polling or long-running telemetry.

`ReconciliationRepairChecks.cs` runs 5,065 assertions covering 80 repeated refunds
per tank arrangement, one/two/four feeder pairs, missing/full/empty/overfilled
tanks, paired capacity, ordinary withdrawals and exclusion of foreign/destroyed
tanks. The stripped numerical
`Tests/Fixtures/Torch/NativeBudgetCoast.json` retains the observed 67-point native
course shape. Both clock orders consume the remaining analytic burn/coast
integral within 1 mg He-3, preserve the D2O ratio, never gain fuel, and recognize
the adopted course through native JSON reconstruction. The private full
numerical extract also advances coast through braking with both fuels declining.
These are native algorithm tests with fixture Unity boundaries, not a replay of
every historical Unity call or proof that all past flights used the correction.

Final validation: 407,781 torch-oracle assertions, 449 docking/orbit checks,
689 route-completion checks and 5,368 orbital checks pass. All 301 full-build
patch classes bind with zero failures or warnings. Custom fuel-cache, corrected
fuel and towing packages compile, preserve ordering and bind successfully; the
custom builder passes 595 self-checks. Debug DLL/PDB build without warnings.

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
