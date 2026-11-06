# Offline excursion analysis

Run from the workspace, without the game:

```powershell
dotnet run --project Tools/ExcursionAnalysis -c Release -- EXCURSION-E-H6GR.jsonl EXCURSION-ANALYSIS
powershell -NoProfile -ExecutionPolicy Bypass -File Tools/ExcursionAnalysis/Plot.ps1
dotnet run --project Tools/ExcursionAnalysis -c Release -- --self-test
```

Streams JSONL with Newtonsoft, extracts compact events/snapshots, deduplicates sequences by generation/session, and writes summary JSON, CSV, event windows and a standalone PNG. The output directory defaults to `EXCURSION-ANALYSIS`; pass `-Directory` to the plotter for another output directory. Charts use Windows' standard .NET charting library, with no visible window.

The analyzer references the installed game DLL and invokes `BodyOrbit.UpdateTime` only on newly constructed offline objects using the captured orbital elements and hierarchy. It does not connect to the inspection API or change the game. Ephemeris checks evaluate position at `positionCalculationEpoch` and backward one-second velocity at `velocityCalculationEpoch`, not blindly at the later API capture epoch. Agreement verifies the captured state's consistency with the game's prescribed path; it is not independent validation of the game's physical constants or Kepler solver.

`docked-cache-checks.json` compares captured player/target velocities with their references evaluated at a common epoch while they are locked to VORB/VORB auxiliary. `orbit-fit-checks.json` compares each stored velocity handoff against the backward difference and a centred 0.1-second numerical derivative of its newly fitted orbit. The derivative is an estimate of instantaneous velocity, not a measured trajectory derivative. UI-event target velocity is unavailable at every display sample; avoid attributing every in-flight jump solely from the nearest polling snapshot.

Integrity reporting distinguishes malformed analysis records from transport failures already recorded by the writer. Sequence gaps before the first retained event are marked bootstrap gaps. Display gaps are not equivalent to sequence loss; a closed/inactive MFD can stop readout production. The provided flight ends on arrival at the derelict, not on return to VORB.
