# Player torch-course fuel and control correction

**Status: pending in-game playtesting.** This describes the local working-tree
and Debug implementation. Patch sources and the native test harness may be absent
from a commit that includes this document; documentation does not imply the fix
is included in that commit. Offline validation below is not live-game validation.

Always-on miscellaneous bugfix for Ostranauts 1.0.1.10. Native trajectory planning,
waypoint positions/velocities, manual burns and shallow NPC cargo policy remain
unchanged. No PDA setting or custom saved object is added.

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

## Implementation

`MiscBugFixes/TorchCourseFuel.cs` constructs a bounded interval profile after the
native course-plot UI finishes planning. It applies only to the player's fully
materialized ship, with a running reactor in thrust mode, working cooling, an
already near-ideal core and thermally feasible settings. Unsupported/cold previews
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

Final result: **4,096 native-assembly checks passed**, including 144 full playback
comparisons and 86 lifecycle checks. **154 Harmony patch classes bind with zero
failures or warnings.** Debug DLL/PDB build cleanly.

Verdict: the scoped correction is ready for an ordinary in-game validation trip.
This is an offline correctness verdict for the supported envelope, not a claim
that every Unity UI, station transition or combination of other mods has been
exercised live. Deploy/restart and plot a new supported course; a legacy active
course is intentionally not retroactively discounted.
