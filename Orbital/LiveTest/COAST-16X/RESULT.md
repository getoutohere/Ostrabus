# Live coast result - November 4, 2026

Recording stopped after 138.90 game minutes at normal 16x simulation.
M-5N4 passed periapsis 116.11 game minutes after baseline; minimum sampled
altitude was 336.051 km. Final sampled altitude was 2,797.107 km, moving outward.
No sampled thrust, locks, or docking-history events involved this wreck.

The coast path was active: 185,261 total integrated ship steps, zero invalid
steps. These counters cover all ships, not just M-5N4. SFF was not used.

Energy was nearly constant approaching periapsis outside the atmosphere:
-13.345815701 initially, -13.345824445 km^2/s^2 at 358.09 km altitude.
The main energy gain occurred only while dipping below approximately 351 km.
The shipped Venus atmosphere ends at radius 6,402 km (350 km above its
6,052 km surface). CoastIntegration conservatively excludes this radius plus
one km and hands those steps back to the native integrator. The energy starts
increasing on entry and stabilizes again on exit. This residual is consistent
with the explicitly excluded atmospheric interval, not continued free-coast drift.

Final energy is -13.309411446 km^2/s^2: +0.272777% over the run, still bound.
Inferred periapsis changed 336.015 -> 336.065 km; apoapsis changed
11,527.265 -> 11,592.771 km. The atmosphere-free integrator works in the live
simulation; improving integration through atmospheric passages remains separate
work. No additional live run is needed to establish this result.

Analysis: ANALYSIS.json; raw data: NORMAL-16X.jsonl.
Reproduce: dotnet run --project Tools/OrbitDrift -c Release -- --coast-test .
