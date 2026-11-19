# Native AI rendezvous checks

Runs the installed Ostranauts 1.1 `FlyToPath` planner under bundled Mono with
the production [repair](../../Navigation/AiRendezvousPatch.cs). The numerical
[fixture](../Fixtures/Navigation/RcsStationApproach.json) captures an anonymized
torch-to-RCS station approach, native body orbits and propulsion values.

```powershell
dotnet build Tests/AiRendezvousCheck -c Release --nologo -v quiet
dotnet Tests/SaveJsonCompatibility/Host/bin/Release/net8.0/MonoHost.dll Tests/AiRendezvousCheck/bin/Release/netstandard2.1/AiRendezvousCheck.dll
```

The checks first reproduce a native unconverged route, then install the production
acceleration, validation, rejection and playback patch classes. They cover a converged repair, six AI roles,
27 phase/mass/range combinations, weak RCS rejection, native safety caps,
player/torch/atmospheric-rotor boundaries, recursive waypoints, finite and
chronological points, moving-target sampling and exact restoration of historical
position/velocity caches throughout the target's body hierarchy. Free-hull
controls preserve a valid native visit and reject a divergent native plot;
the solver's fixed-position free-hull target model is retained.
Native freight `RunCommand` retries retain the correct destination only for
new numerical rejections; native remote delivery still fails.

Explicit fixtures supply Unity identity, conscious-pilot availability, empty
cargo mass, the dominant gravity body and the absence of obstruction detours
except in the dedicated recursive case. Propulsion, fuel prediction, orbit
sampling and route calculations execute native code. This is not a full scene,
an exact historical call replay or evidence that all weak-RCS transfers converge.
The separate OrbitalCheck suite composes these hooks with SFF/PASS and economy
execution; the custom-builder matrix verifies standalone/combined binding.

This suite's detour double isolates convergence; it does not establish safe
planetary navigation. The [full native saved-world voyage matrix](../../Navigation/FREIGHT-CELESTIAL-SAFETY.md)
executes the real obstruction planner and an independent native clearance oracle.
Manual-handoff controls separately preserve terminal status, player control,
pilot absence, missing destinations, propulsion loss and loaded fuel exhaustion.
