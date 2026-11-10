# Temporary Socials profiling checks

```powershell
dotnet build Tests/SocialsProfilingCheck -c Release
dotnet run --project Tests/SaveJsonCompatibility/Host -c Release --no-build -- Tests/SocialsProfilingCheck/bin/Release/netstandard2.1/SocialsProfilingCheck.dll
```

18 checks run under bundled Mono, binding the production observer to its 19
actual native/UI/helper targets. Controlled native/UI bodies produce nested
row/portrait/fallback calls and a deferred layout callback; managed stopwatch,
allocation counters, snapshot generation, Harmony installation and removal run
normally. Unity shader/profiler initialization, UI work and the frame clock are
boundaries. Timings here describe fixtures and are not live UI measurements.

Checks cover zero startup/GET hooks, idempotent arming, rejection of unrelated
pre-opening work, nested inclusive/exclusive timings, detached first-input data,
deferred layout, automatic completion, retained results, original exception
propagation, scope cleanup, cancellation and no-opening timeout. All owned hooks
are removed on every completed test path. No game saves or UI assets are changed.
