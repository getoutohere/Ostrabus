# Native torch-tow checks

Build and run from the repository root:

```powershell
dotnet build Tests/TorchTowCheck -c Release --nologo
dotnet run --project Tests/SaveJsonCompatibility/Host -c Release -- Tests/TorchTowCheck/bin/Release/netstandard2.1/TorchTowCheck.dll
```

The actual production sources are linked into the harness. It first reproduces
the native member-course/group-anchor overwrite and missing tow-mass defects,
then installs the repair and exercises native planner and playback methods.

The November 12 station-departure update passes **343,871 assertions**, including
100 moored-preview checks. These cover clamped-brace departure chains, either-side
braces, station visitors, release, orbital/ground/substation/mooring boundaries,
native manual force using the full physical mass, unbraced free-flight estimates,
scratch cleanup and zero allocation across 1,000 warmed previews. Native stress
is driven beyond 30 G-seconds and observed entering its catastrophic tow-damage
path; only the RNG and final damage operation are substituted there. The full
plugin binds 228 patch classes, and a generated towing-only build compiles and
binds its three classes independently. The joint reactor oracle passes 402,667
assertions, including 58 engine-force/towing integration checks that cover the
moored prediction/actual-force distinction. These are repeated invariants across
scenarios, not hundreds of thousands of distinct test cases. The following
November 11 totals record the earlier baseline.

The November 11 run passes **343,768 assertions**. Most are repeated pose, fuel
and rigid-offset invariants during a native burn/coast/brake course sampled at
one-second intervals; this is not a count of distinct gameplay scenarios.
Other cases cover full `PlanTrip4` plotting and ETA, different coast settings,
irregular/hourly/six-hour advances, both native SFF completion branches, native
course reconstruction, manual thrust, destroyed/detached mass, anchored and
conflicting-course exclusions, exception cleanup, brace assets/wear and three-ship
chain aborts including a released downstream brace and native autopilot
unregistration. The full binding audit passes 200 patch classes without failures
or warnings; a towing-only custom build binds its three classes independently.
A coarse coast test verifies native G-load/wear receives the current course
acceleration, and both execution scopes are checked after a native exception.

Native `ShipSitu`, `NavData`, `DockGroup`, `FlyToPath`, thrust conversions and brace
checks run from the installed assembly. Unity identity, ship mass/lookup, pilot
presence, planetary environment, logging, controller lookup and crew damage/
gravity effects use explicit fixtures. The brace/wear and controller subtests
temporarily remove their respective fixtures to execute native wear, controller
lookup and unregistration. No game process or save is modified.

The separate `Tools/TorchNativeOracle` suite passes **4,145 checks**, including
**49 towing integration checks**, covering native reactor/tank math and
joint operation with the pending torch fuel/control patches; its additional tow
checks verify force is not reduced twice, acceleration includes group mass, native
reactants are consumed, corrected courses still reach braking endpoints, and
brace release idles controls without debiting another course budget.

Implementation details, limitations and the user-requested brace safety policy
are in [Navigation/Towing/README.md](../../Navigation/Towing/README.md).
