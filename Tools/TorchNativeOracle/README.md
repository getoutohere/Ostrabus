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
| `coast-gravity` | Native thrust/gravity A/B for stale coast acceleration, crew knockdown requests, towing, saved courses, independent clocks and powered/RCS/analytical boundaries |
| `sff-clock` | Independent reactor/global clocks, partial catch-up, mixed burn/coast/braking, arrival overrun, saved courses and manual-operation isolation |
| `barter-reactor` | Native reactor reconstruction investigation |
| `saved-fuel <extract.json>` | Optional private numerical save extract: course recognition, independent clocks, multiple canisters and a controlled unbalanced-refund reproduction with/without the fuel-cache postfix |

`saved-fuel` is an investigation mode, not a regression assertion that the
historical save used corrected playback or that its entire Unity timeline was
reproduced. Its explicit empty-tank selector fixture follows native
`CTReactantA`/`CTReactantB`; the original broad matrix used supplied tank lists
and did not model positive-fuel filtering. The final refund contrast verifies
the resulting repeated D2O refill through native `SetReactants/RemoveFromTanks`.
Full-first tank ordering also exposes native `RemoveFromTanks` discarding the
refund remainder. Production corrections now include empty receivers, paired
free-capacity limits and that arithmetic repair. The default suite adds 5,065
refund/debit assertions, including 80 repeated calls per tank arrangement and
one/two/four feeder pairs.

`control-handoff` also exercises recovery of known native-format courses and
temporary cold/power-mode eligibility failures. A stripped, numerical 67-point
coast save fixture runs through braking under both reactor/global clock orders;
remaining He-3 use agrees with its analytic integral within 1 mg, D2O stays paired,
and the adopted course remains recognizable through native JSON serialization.
Past physical consumption is preserved. Unknown formats and unsupported states
retain native handling. See the fuel/control validation document below.

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
256 checks and is included in the default suite, alongside the recovery and
reconciliation cases above. The full matrix passes 408,488 assertions. These
counts include repeated invariants, not distinct scenarios.
It does not execute the entire Unity GUIFFWD coroutine.

The previous thrust boundary and suppressed gravity setter also missed a
coast-force mismatch: the reactor idles while native NavData interpolates the
last burn's cached acceleration into a constant-velocity coast. `coast-gravity`
runs actual native SetThrust and Gravity, observing the KnockDownCrew request
boundary. Its uncorrected control reproduces 1.087722 G and ten knockdown
requests across five prescribed contradictory update cycles; corrected playback
reports zero and requests none. Its 707 checks cover solo/towed, native/fresh/
saved, independent clocks and SFF mode, real powered knockdowns, unsupported
RCS/analytical intervals and native fallback. Actor queues, audiovisual effects,
damage application and crew item/gravity recalculation are fixture boundaries.
This is not a recording of the entire live Unity frame order.

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
