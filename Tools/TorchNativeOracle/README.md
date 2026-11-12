# Native torch-course oracle

Offline development harness using the installed game assembly and bundled Mono.
It links the production torch fuel/control, towing and momentum-aware planning
sources. Unity/environment boundaries are fixtures; native reactor algorithms,
waypoint generation, orbital ephemerides, playback, tank accounting and JSON
reconstruction execute where the corresponding test exercises them.

Run from the repository root:

```powershell
dotnet build Tools/TorchNativeOracle -c Release
dotnet run --project Tests/SaveJsonCompatibility/Host -c Release --no-build -- Tools/TorchNativeOracle/bin/Release/netstandard2.1/TorchNativeOracle.dll transfer-planner
```

| Argument after DLL | Coverage |
| --- | --- |
| none | Reactor/thrust/fuel model, native reproductions, production corrections, lifecycle/control handoff, towing, and improved-trajectory playback |
| `initial-velocity` | Unmodified native cancellation-first kernel versus collinear analytic controls |
| `transfer-solver` | Vector transfer equations, independently stepped integration, random/edge cases and allocation measurement |
| `transfer-planner` | Production kernel integration, native waypoints and full moving-orbit wrapper; scope isolation and nonconvergence rejection |
| `transfer-playback` | New trajectories through corrected native playback, normal/SFF steps, solo/towed groups, save/reload, preview and physical fuel |
| `control-handoff` | Power-mode preview, engagement consistency, applied/stored controls and safe fallback |
| `sff-clock` | Independent reactor/global clocks, partial catch-up, mixed burn/coast/braking, arrival overrun, saved courses and manual-operation isolation |
| `barter-reactor` | Native reactor reconstruction investigation |

The original playback suites advanced the fixture CO clock and global ephemeris
together. They did not cover SFF's independently advanced clocks. `sff-clock`
deliberately separates those clocks and runs native TimeAdvance/SetReactants and
FusionIC.CatchUp with the production patches. The unfixed powered cases charged
twice when reactor time lagged. They now charge once in both orders for one/two/
four feeder pairs and both pilot modifiers. Partial catch-ups, repeated skips
across all course phases, native JSON reconstruction, post-arrival idle fuel and
subsequent manual use also pass. A wear-call observer verifies the native core
still receives the full elapsed wear interval (the fixture substitutes the wear
operation itself); native heat calculations execute unchanged. This mode passes
256 checks and is included in the default suite, now 402,667 assertions across
the full matrix. These counts include repeated invariants, not distinct scenarios.
It does not execute the entire Unity GUIFFWD coroutine.

The station-departure update adds native Fusion checks for all three feeder-pair
configurations: excluding the station from an intended departure must preserve
the real engine force and full physically attached mass. The native maximum-
acceleration preview uses the braced departure subset while moored. The separate
towing harness covers station visitors, brace chains and native unbraced damage.

Run native suites sequentially; the host shares runtime staging files. Private
CSV/log outputs remain under ignored `obj/`. Expected missing Unity internal-call
warnings in the host are distinct from assertion failures; check the process
exit code and completion message. These offline results do not replace live
playtesting. See [planner design](../../Navigation/TORCH-INITIAL-VELOCITY.md) and
[fuel/control validation](../../Navigation/TORCH-COURSE-FIX.md).
