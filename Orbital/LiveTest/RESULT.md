# Normal simulation orbit test â€” 2026-11-04

> Measurement correction: the original numbers below used a tighter Venus orbit
> solver than the game. The corrected energy gain is **1.11066%**, periapsis
> altitude 335.969 -> 375.654 km, apoapsis altitude 11,527.238 -> 11,756.737 km,
> and minimum sampled altitude 357.014 km. The wreck remains bound. See
> [TIMESTEP-FINDINGS.md](TIMESTEP-FINDINGS.md) for the reference audit and
> integration experiments. ANALYSIS-BEFORE-DOCK.json now contains corrected data.
Build: 264,192-byte Debug DLL, deployed timestamp 14:37:09. Orbital enabled;
SFF never used. Player O-GXD remained docked to VORB. Started at 8x, then 16x.

Primary free-coast subject: M-5N4 Some Assembly Required. Baseline epoch
65631764416.3972. Clean analysis stops before first recorded docking at
65631772606.059853 (`ANALYSIS-BEFORE-DOCK.json`). There are 329 clean samples over
136.224 game minutes, with no sampled thrust, body lock or analytical orbit lock.

- Actual periapsis passage: epoch 65631771381.462524, 116.084 game minutes after
  baseline, minimum sampled altitude 356.559 km.
- Specific energy: -13.343988 â†’ -13.268130 kmÂ²/sÂ², a 0.5685% increase.
- Inferred periapsis altitude: 338.370 â†’ 360.997 km.
- Inferred apoapsis altitude: 11,528.192 â†’ 11,642.614 km.
- The wreck remains bound. Invalid corrections = 0. Maximum observed normal world
  step reached 5.333333 seconds after the switch to 16x.

Conclusion: the corrected normal simulation carried this wreck through periapsis
without escape or the prior inward secular failure. Energy is not perfectly
conserved; measurable residual drift remains in the native numerical path. This
is one live case, not proof of correctness for all timesteps/ships/gravity domains.

Docking history identifies O-XE5 Deserted Wind (Scav, owner VORBScav, home VORB):

| Event | Epoch | Active command |
|---|---:|---|
| Dock M-5N4 | 65631772606.059853 | FlyTo |
| Undock | 65631772903.544769 | Hold |
| Dock again | 65631772923.612564 | FlyTo |
| Undock again | 65631773013.880806 | Hold |

Visits lasted approximately 297.5 and 90.3 game seconds. At inspection O-XE5 had
already departed, was in FlyTo, targeting VLA01, and was not in combat. Treat the
first dock as the free-coast experiment's cutoff rather than attributing later
changes to integration alone. No claim is made that actual salvage work completed.

Recorder stopped after periapsis and the encounters were captured. Raw readings,
baseline, docking events and endpoint diagnostic snapshots are retained here. The
`NORMAL-16X.jsonl` filename predates the temporary choice of 8x; analysis uses game
timestamps. The recorder STOP marker intentionally remains until the next run.

Reproduce the clean cutoff analysis from the workspace root:

```powershell
dotnet run --project Tools/OrbitDrift -c Release -- --normal-test . 65631772606.059853
```

