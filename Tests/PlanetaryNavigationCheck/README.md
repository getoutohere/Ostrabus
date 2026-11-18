# Planetary navigation checks

Run the departure-avoidance regressions with:

```powershell
dotnet build Tests/PlanetaryNavigationCheck -c Release
dotnet Tests/SaveJsonCompatibility/Host/bin/Release/net8.0/MonoHost.dll Tests/PlanetaryNavigationCheck/bin/Release/netstandard2.1/PlanetaryNavigationCheck.dll avoidance-repair
```

`AvoidanceRepair.cs` establishes native failure controls, replays the numeric
O-YKWV approach in `AcceleratingDeparture.json`, and executes native `FlyTo`
before/after patching to verify route interruption and ordinary resumption.
It covers slow closing, acceleration from rest, acceleration turning a pass
clear, separating/overlapping/stationary cases, long coasting horizons,
unchanged shared collision tests, unchanged actor state and zero warmed query
allocation. Unity equality, a fixed regional station/acceleration, ephemeris
updates and escape-point presentation are test boundaries. The native `FlyTo`
dispatcher executes with counting manual/path leaves; this does not claim a
complete Unity collision-free flight replay. Run the default suite below for
the actual native planetary steering/arrival replays.

See [the defect, repair and evidence boundaries](../../Navigation/PLANETARY-AVOIDANCE-AUDIT.md).
The requested [station-docked traffic policy](../../Navigation/STATION-DOCKED-COLLISIONS.md)
is covered by `StationPolicy.cs`, including actual native proximity-scan execution.

The test project links production avoidance/geometry and existing detached
ephemeris code. It executes the shipped assembly under the game's bundled Mono
runtime. It does not load or modify a running game.

```powershell
dotnet build Tests/PlanetaryNavigationCheck -c Release
dotnet run --project Tests/SaveJsonCompatibility/Host -c Release --no-build -- Tests/PlanetaryNavigationCheck/bin/Release/netstandard2.1/PlanetaryNavigationCheck.dll
```

The compact fixture retains seven anonymous ship geometries, direct docking
links, body orbital elements and the patrol's recent path. It contains no save
archive, inventory, character data, machine path or Unity object graph. The native
and future-only controls must reproduce a body impact. The production patch must
complete native FlyTo with the original target and positive body/wreck clearance
for each of three possible docking-group anchors. It does not execute the next
physical Dock action.

`StationArrivalEvasion.json` additionally retains O-BFZ2's complete saved 86-point
return route, with its registry ID normalized to the harness alias. That replay
starts at the saved epoch/position/velocity and uses the hauler's native speed and
role. Native/future-only controls must impact; the production policy must reach
the original station's native docking handoff for every tested group anchor.

`Geometry.cs` independently samples 800 random quadratic trajectories; exercises
selection constraints and native exclusions; and measures warmed helper allocation
and body-cache preservation. `Avoidance.cs` checks the native evasion direction,
historical/future prediction controls and the actual production transpiler.
`Forecast.cs` lists fixture boundaries: native body ephemerides, planning,
interception, waypoint advancement and command bodies execute; Unity translation,
pilot eligibility, presentation and scheduler notifications are explicit doubles.
Patrol initial velocity is estimated from saved neighboring path samples; the
hauler retains its saved velocity and existing plan. This is a
controlled replay, not a complete Unity simulation or an assertion that every
possible obstacle configuration is solvable by reversing a native dodge.

`departure-replay` first reproduces the deployed guard's sustained oscillation
(701 plans, 1,398 thrust reversals, no arrival). It then requires completed trips,
positive hull clearance, bounded replanning and retained destinations for head-on,
mirrored and moving encounters, two AI cadences, a clear pass and full-DLL hull
toggle/installation-order combinations. Build the Debug omnibus before running
this mode: it tests that DLL as well as linked helpers. Native planned fuel debits
are recorded; the translation fixture does not charge manual/rotational fuel.

```powershell
dotnet build Ostrabus.csproj -c Debug
dotnet build Tests/PlanetaryNavigationCheck -c Release
dotnet Tests/SaveJsonCompatibility/Host/bin/Release/net8.0/MonoHost.dll Tests/PlanetaryNavigationCheck/bin/Release/netstandard2.1/PlanetaryNavigationCheck.dll departure-replay
```

`avoidance-repair` also checks local/admission horizons, distant speculative
encounters, group-anchor offsets, exact manufactured reference identity,
exceptional flight/query cleanup and zero warmed allocation. Its earlier
counting-leaf dispatch check is retained as a branch check; it cannot prove that
continuous navigation works.
