# Torch autopilot fuel-cache investigation

**Pending in-game playtesting.** Implementation and native test-harness files
may be absent from a commit retaining this investigation. The implementation
described below exists in the local working tree/Debug build; offline findings
are not a claim that the patch has been validated in-game.

Investigated November 8, 2026 against the shipped 1.0.1.10 assembly and decompiled
source. Implemented as the always-on miscellaneous bugfix
`MiscBugFixes/TorchFuelCachePatch.cs`.

## Live observation

The player's final braking approach ended with:

`Low Torch Reactant: -6374.05913401463. Disengaging autopilot.`

The subsequent ship snapshot reported a positive cached fusion budget of
6059.3285158693434. This field is normalized fuel seconds, not kilograms.
The warning prints cached fuel minus predicted waypoint remainder. It does not
print fuel required to finish the maneuver. The failed command clears NavData,
so the exact failed waypoint and simultaneous tank state were not captured.

## Confirmed synchronization defect

`NavData.TimeAdvance` invokes `FusionIC.CatchUp()` and then
`FusionIC.SetReactants(interpolatedWaypointFuel)` for a materialized reactor.

`CatchUp()` can run the normal reactor simulation over the elapsed interval.
`Fusion()` consumes reactants and recalculates `Ship.fShallowFusionRemain` from
the physical tanks. `SetReactants()` subsequently reconciles the physical tanks
to the plotted course, including refunding consumption when necessary, but
does **not** refresh that ship cache. Its own reactant total fields also describe
the pre-reconciliation inventory.

`FlyToAutoPilot.RequirementsMet()` reads that cache directly. It rejects a torch
segment if the cache is below half the next waypoint's predicted fuel remainder,
then clears the course. It does not first synchronize with the reconciled tanks.

Native SFF ordering exposes the gap: `GUIFFWD.FFWD` calls `StarSystem.Update`,
which advances ships (including NavData playback), then runs the AI manager.
Only after that call returns does GUIFFWD perform its explicit reactor catch-ups.
Those later refreshes cannot restore a course already rejected by the AI check.
The Orbital SFF patches preserve the player's native plotted-route playback;
this defect exists in the unmodified methods.

## Offline reproduction

`Tools/TorchNativeOracle` executes the shipped `FusionIC.Fusion`,
`SetReactants`, `RemoveFromTanks`, and private `FlyToAutoPilot.RequirementsMet`
under the game's Mono runtime. Unity ownership, condition storage, inventory
lookup, clock and unrelated side effects use explicit fixture boundaries.

With an operational one-pair reactor and ample tank capacity:

1. Start with 30,000 normalized seconds in both reactants.
2. Run a six-hour reactor consumption interval at full pellet rate: tanks and
   ship cache fall to approximately 8,400 seconds.
3. Reconcile the tanks to a plotted remainder of 24,000 seconds through native
   SetReactants. The physical tanks now contain 24,000; the cache remains 8,400.
4. Execute the native autopilot guard against a 25,000-to-24,000 waypoint pair:
   it rejects the course and clears NavData, despite the physical inventory.
5. Run a zero-duration reactor refresh (native minimum time applies), restore the
   same NavData, and execute the same guard: it passes.

A second case verifies that downward reconciliation also leaves the cache stale.
The complete oracle passes 1,414 checks. This proves the defect and false abort
mechanism offline; it does not recover the missing instantaneous state of the
player's particular abort. The six-hour case composes the actual native methods;
it does not execute GUIFFWD's complete Unity coroutine.

## Implemented correction

The SetReactants postfix synchronizes the reactor's and ship's cached reactant
availability after native tank writes. It derives availability from the actual
post-change inventory, accounting for both limiting reactants and the native
conversion. It does not blindly copy the requested value: unavailable capacity,
absent tanks or imbalanced reactants can prevent reaching it.

This repairs the producer/consumer boundary without replacing or relaxing the
autopilot guard. Its half-predicted-remainder policy is a separate design question;
the reproduction fails even that existing policy solely because its input is
stale. Native planning, fuel consumption, reactor shutdown, and temperature safety
behavior are preserved.

The oracle now passes 1,954 checks, including the production Harmony postfix
across 108 combinations of reactor capacity, available inventory, target fuel and
reactant imbalance. It executes the native autopilot guard immediately afterward:
adequately fueled courses remain active, while real capacity-limited shortages
still abort and clear their courses. These checks run offline; live confirmation
of a subsequent player torch course remains pending.
