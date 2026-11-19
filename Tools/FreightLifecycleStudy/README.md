# Freight lifecycle study

Read-only, offline investigation of freight hulls, pilots, cargo markets and saved
routes. Nothing is installed in the game; no live state or save archive is changed.
Outputs belong under ignored `obj/`, not in the release package.

Build against the configured game installation:

```powershell
dotnet build Tools/FreightLifecycleStudy/FreightLifecycleStudy.csproj
$study = 'Tools/FreightLifecycleStudy/bin/Debug/net8.0/FreightLifecycleStudy.dll'
$saves = Join-Path $env:USERPROFILE 'AppData/LocalLow/Blue Bottle Games/Ostranauts/Saves'
dotnet $study index obj/FreightStudy/index.json $saves O-2P3 O-KKWJ
dotnet $study summarize obj/FreightStudy/index.json O-2P3
dotnet $study root obj/FreightStudy (Join-Path $saves 'pathcheck/pathcheck.zip') O-2P3
dotnet $study geometry obj/FreightStudy/pathcheck-root.json O-2P3
dotnet $study report obj/FreightStudy/pathcheck-root.json
dotnet $study root obj/FreightStudy (Join-Path $saves 'chaos/chaos.zip') O-2P3 '--person=Redeem Guzman' --market=MLAB --market=MHNG --people-everywhere
dotnet $study compare obj/FreightStudy/pathcheck-root.json obj/FreightStudy/chaos-root.json O-2P3
```

`index` inspects archive entries across available saves and reads selected hull
records up to 4 MiB each. `summarize` reports one ID's saved motion, route dates,
crew IDs and recent ship log. File modification order is only a search aid:
compare root epochs and allow for reverts before claiming a continuous history.

`root` extracts selected hulls, their crew records, AI controllers, origin and
destination markets, body definitions and orbit hierarchy. `--person=ID` and
`--market=ID` select records whose hull has already vanished. The main root and
candidate station files are streamed. `--people-everywhere` additionally searches
every saved ship entry; without it, a missing pilot is only absent from the
selected/expected locations. Native CO records must begin with `strID`; the
extractor fails explicitly if that schema changes instead of silently reporting
absence. Separate output directories preserve multiple selections of one save.

`compare` reports hull presence, crew search results, cargo quantities and SHA-256
fingerprints of actor IDs plus sorted exact saved stock entries. Matching totals
alone are weaker evidence than matching item records. `report` aggregates stock
without spawning its concrete game items.

`geometry` reconstructs the saved parent hierarchy using the installed game's
actual `BodyOrbit` class and calls its ephemeris at each future saved route point.
It tests non-placeholder solid bodies, including small bodies such as Ganymed;
manufactured station/region references remain part of the hierarchy. It refuses
attached future route points rather than treating their cached coordinates as
free flight. Distances use the native AU conversion. Each report records the
assembly MVID used for the calculation.

This is a saved-point test, not a replay of AI decisions or collisions. It neither
subtracts hull radius nor tests interpolation between points or random asteroid
field encounters. A clear result does not establish a safe entire route; a point
inside a body establishes invalid geometry but does not recover a historical
destruction call stack. Account for legitimate ground-port handoff before
classifying a shallow endpoint overlap as a proven crash.

The independent native destruction reproduction uses the existing scene-boundary
fixture and the game's bundled Mono runtime:

```powershell
dotnet build Tests/AiPlannerDiagnosis/AiPlannerDiagnosis.csproj
dotnet Tests/SaveJsonCompatibility/Host/bin/Debug/net8.0/MonoHost.dll Tests/AiPlannerDiagnosis/bin/Debug/netstandard2.1/AiPlannerDiagnosis.dll freight-destruction
```

Its native A/B assertions reproduce the retained-market defect and verify the
production repair, despawn, actual hull replacement and same-ID preview controls.
Unity internal-call diagnostics can occur in this isolated fixture; its explicit
completion receipt and exit status determine success.

`AiPlannerDiagnosis` also accepts `freight-rendezvous ROOT.json SHIP_ID` to run
the production rendezvous validator on the extracted route and station. It
prints the final-rendezvous predicate alone; this diagnostic deliberately does not
claim planetary clearance. Use the [full route regressions](../../Navigation/FREIGHT-CELESTIAL-SAFETY.md)
for production validation and native voyage playback. `fixture OUTPUT ROOT IDs...`
strips retained physics inputs for those tests; `root --all-stations` includes all
saved station headers needed by the real native obstacle and departure planner.
`root --station-lifecycle` additionally streams station faction IDs, ShipCO save
conditions and saved crew counts, including hidden stations without ports.
It does not infer runtime population or expand DEFAULT conditions; compare those
raw inputs with native `GetStations(true)` and `TryRecycleCrew` before concluding
why a station was ineligible. Existing geometry-only selection is unchanged.
