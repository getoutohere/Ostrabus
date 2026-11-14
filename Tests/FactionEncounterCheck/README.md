# Faction encounter native checks

Runs vanilla and the production encounter patch against the shipped 1.0.1.10
assembly under the game's Mono runtime. First reproduces the four-ship convoy
0.64 AU from BCER, including copied 1,530 km/s cruise velocity. Then repeats with
the patch and requires zero actor constructions.

Checks cover authored BCER/BCRS/COHO geometry, all Ceres belt clusters, the
inter-station corridor, six-hour/thirty-day ephemerides, relative speed, connected
torch use, missing/stale infrastructure, ordinary local severity, post-finale
six-ship battles, protected/busy existing actors, preserved physical state,
fleet caps and changed-IL rejection. It reads shipped JSON, rather than copying
the production eligibility calculation into the assertions.

Native execution includes BeatManager's weighted checks, severity loop,
depot/candidate counting, placement writes, patrol assignment and registration.
Fixtures replace Unity scene services, conditions, actor/crew construction,
safe-coordinate sampling, command construction, low-fuel result and POI lookup.
Orbit setup uses native BodyOrbit construction and CalculatePAFromET; policy
positions use the production pure ephemeris. This harness proves staging and
eligibility boundaries, not actual flight, NPC construction or combat outcomes.
It applies the production prefix/transpiler, not a test rewrite of BeatManager.

Build and run:

```powershell
dotnet build Ostrabus.csproj -c Debug
dotnet build Tests/FactionEncounterCheck -c Release
dotnet run --project Tests/SaveJsonCompatibility/Host -c Release --no-build -- Tests/FactionEncounterCheck/bin/Release/netstandard2.1/FactionEncounterCheck.dll
```

Full native patch binding and a standalone custom-plugin binding check complement
these scenarios. The custom selection must not silently enable Orbital hooks.
Validation on 1.0.1.10: 690 checks passed; the isolated custom feature binds one
patch class (two native methods), with no frame calls or Orbital hooks. The full
build binds 246 classes with zero failures/warnings; the existing MM suite still
passes its 316 checks.
