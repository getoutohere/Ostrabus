# Boneyard Turnover 0.1.4 validation

Reviewed against Ostranauts **1.1.0.2**, native assembly SHA-256
`133a9cc94ad4f4d5db951b65249b6cdf78f53063419d836b8871b92960ec3c37`.
Version 0.1.2 also supports the reviewed 1.1.0.3 assembly
`e9e7b8796423701c157ea5718cb407e6517ac0435ef947062c9a27464f4bc82f`.
Native turnover entrypoints and all shipped JSON are unchanged in that update;
runtime and installer retain exact guards for these two reviewed assemblies.

The later [field-model review](FIELD-MODEL-REVIEW.md) tests the real saved
populations against the complete body hierarchy. It distinguishes definition
catalog checks from actual spatial admission: the displaced legacy Mercury
cohort needs a separate historical repair, while 0.1.4 corrects the Ganymede
saved-centre reader. No speculative change selecting a yard anchor from the
wrecks' current parents is included.

## Executed evidence

The production harness runs under the game's bundled Mono runtime, references
the compiled standalone plugin, and reuses the native fixtures from the earlier
252-check investigation. It currently passes **10,457 assertions**; 10,000 of
these are a systematic-sampling sweep, not 10,000 independent gameplay scenarios.
This result was repeated with the reviewed 1.1.0.2 and 1.1.0.3 assemblies.
Additional saved-world model checks pass 5,775 assertions across seven fields;
the docking suite passes 1,412, including 578 checks with the twelve historical
Mercury placements. See the field-model review for fixture boundaries.

The 0.1.4 centre change is confined to standalone field resolution. It reads a
unique map-matched native facility, checks its physical/deferred identity,
classification, original owner, stationary parent attachment and authored
radius, and keeps native field bounds and template pools. Physical native saves
that substitute the registry ID for the template name are accepted. Passive
visitors do not redefine the centre. Missing/duplicate/wrong-map/wrong-template/
claimed/free/orbiting/reattached/radially displaced controls are rejected.
No shared movement, docking, gravity, AI, SFF or towing hook was changed and
the standalone's eight existing native bindings remain unchanged.

The substantive controls cover:

- Default and shaped survival curves, accumulated absent age, invalid inputs,
  distinct selections, randomized count rounding and individual probabilities.
- All eleven supported native map variants read from installed JSON, their
  actual default/explicit references, ring bounds, Earth/OKLG/VORB exclusion and
  authored hauler-capability exclusion. Inherited map IDs on substations do not
  create duplicate fields or additional turnover rolls.
- Production eligibility for visited saved-only shallow wrecks and deferred
  wrecks; temporary loading, occupants, dock groups, AI, live/saved incoming
  targets, plot tokens, changed ownership and moved-field controls.
- Actual managed native `Ship.Destroy(false)` and condition-owner cleanup,
  followed by production one-for-one publication and native world serialization.
  A supplied native cleanup failure stops subsequent deletions and does not
  publish a replacement or pretend to roll back the partially destroyed hull.
- Native loot evaluation and safe-coordinate search through production staging;
  detached preparation, preserved factions/ownership/map, rejection of IDs still
  retained by saved native data, rejected failed
  placement and additional deferred-wreck clearance.
- Departure snapshot, successful-transition requirement, crew absence (including
  unresolved roster entries), invalidated story/mod field definitions,
  departure-captured CFG, survivor age, consumed returns, and repeated-callback
  refusal. Native environment loading queues the committed replacement ID.
- All eight native hook targets bind successfully. The standalone assembly has
  no reference to `Ostrabus.dll`.
- Production metadata hooks compose with actual native `SavingJob` ZIP writing;
  metadata roundtrips, frozen worker bytes, memory load, reload refusal,
  cross-world/epoch rejection, required schema, invalid ages, oversize and corrupt
  data. Removing the metadata hook and resaving natively drops the entry.
- Earlier native fixture controls for actual worker saving, cancellation,
  pre-commit failure, archive rollback and contained optional-write failure.

Installer checks pass in both source and packaged form: copy verification,
upgrade backup, reversible/repeated removal, preserved user CFG, untouched
native/main/custom files, no asset overrides and unsupported-assembly refusal.
Fixtures use private temporary directories or a verified workspace path. They
do not edit installed game files or player saves.

## Boundaries and remaining live acceptance

The offline host supplies Unity RNG storage, logging, object disposal, transform
enumeration and editor validation. The native managed destructor executes, but
the engine's final object lookup is replaced only in the fixture. Unity RNG
restoration is tested around production `try/finally`; this is not a statistical
test of Unity's generator. Some native calls print missing-engine/internal-call
diagnostics in the offline log. These are fixture limitations, not live-game
exceptions reported by this mod.

The harness supplies native condition storage and synthetic world objects for
production guards. It tests supported field resolution against actual JSON, but
does not load a complete Unity scene or materialize a replacement's full interior.
Full native `RegionCleanup` is not replayed as a scene; production before/after
observers and native environment queuing are exercised separately.

Normal and SFF arrivals share `AIShipManager.RegionCleanup` followed by
`SolarSystem.EnvironmentEntities.LoadEnvironmentForRegion`, as verified in the
native call chain. No per-frame loop, asynchronous controller, new timeskip
engine or live trace is added. The work occurs once at actual departure/return;
metadata is bounded to current cohorts rather than a historical journal.

Live acceptance should cover an ordinary departure/return with boarded and
unvisited wrecks, saving while away, removal/reload without the plugin, and an
SFF/PASS region crossing. Actual transition frame cost, rendered-cache disposal
and arbitrary third-party callbacks remain unmeasured. Unexpected native cleanup
exceptions remain an irreversible-boundary risk; the implementation stops and
logs the affected wreck rather than continuing the batch.

No live deployment, live turnover or commit was performed for this implementation.

## 0.1.1 timestamp regression repair

Read-only inspection of the real `ai` save found that native JSON stored
`65635026132.7693`, while the optional metadata retained
`65635026132.769279`. The 0.1.0 exact-double comparison rejected matching state.
The earlier production persistence fixture used an integer clock and missed
this serialization difference.

`FractionalTimestampChecks.cs` reproduces that clock through actual native
`SavingJob` ZIP writing and LitJson reading. It failed before the correction
with "Native timestamp rounding discarded valid departure metadata" and passes
afterward. Additional controls cover:

- Pending departures and absent ages survive production metadata loading.
- Existing high-precision 0.1.0 metadata remains readable, with schema 1 unchanged.
- World-start clock rounding and a non-English system culture remain valid.
- Different epochs, world starts, players, regions and world seeds are rejected.
- A downward-rounded clock survives immediate native resaving; the departure
  is not incorrectly rejected as future or given negative elapsed time.
- Genuinely future departures and non-finite native clocks remain invalid.

The reader compares values after formatting through the native JSON writer;
it does not introduce an arbitrary tolerance or alter simulation time. No new
Harmony targets, configuration settings or native save fields were added.
The actual player save was inspected without modification. This 0.1.1 repair
has not been deployed to or playtested in the live game during this work.

## 0.1.3 saved Luna departure repair

The read-only `coast` save records TRANSIT-ZONE with an empty optional `Fields`
array despite retaining all 19 Luna wrecks. Two guards incorrectly rejected
native NPC spawn markers:

- `Ship.SaveCOs()` stores Pspec loot-spawner items in `aShallowPSpecs`; it stores
  actual crew in `aCrew`. Every Luna wreck has two or three `SysLootSpawner`
  entries. Both the saved-array length guard and the blanket loaded-item guard
  incorrectly rejected these abandoned shallow hulls.

The initially suspected moving-centre defect was not corroborated. A first
fixture incorrectly supplied Luna as the nearest reference. Actual native
StarSystem.GetNearestBO selects EJDR_GEO itself; this manufactured placeholder
is not a hyphenated ship orbit. Native CalculateEntityPosition then uses its
zero-vector fallback, producing the same saved centre. The corrected fixture
executes native reference selection and agrees with the saved centre within a
metre. No field-geometry change is included in this repair.

The correction recognizes only native root-owned spawners with no person or
crew component; real/saved crew, unresolved people, ordinary loaded objects,
unregistered markers and orphan markers remain excluded. No larger annulus,
new facility dependency, native save field or additional Harmony target is
introduced.

The repair dry run then exposed an incomplete whitelist in the first 0.1.3
candidate: H-145 contains a SysLootSpawnerNotBoarding overlay. The first saved
fixture retained marker counts but substituted the base name, missing this.
The final fixture retains all 48 actual saved marker definitions (47 base and
one NotBoarding). Production eligibility now uses native GetCondOwnerDef to
resolve authored base/Boarding/NotBoarding definitions instead of a literal
name whitelist. Both overlays are checked against the installed JSON and
resolved by the actual native method. This incomplete candidate was not
installed in the running game.

`SavedLunaChecks.cs` uses a small identity-free geometry fixture extracted from
the save. It exercises production eligibility for all 19 hulls with native
spawn-marker definitions, captures and commits their actual departure cohort, and
checks the unchanged centre using native orbital/reference/placement methods.
Additional controls test marker protection/cleanup. The cleanup check executes
native Ship.Destroy, RemoveInternal and CondOwner.Destroy; only RemoveCO's Unity tile/UI wrapper is
substituted because the offline host has no scene.

Both supported assemblies pass the production suite. Builds have no warnings
or errors. Installer tests operate only on a disposable workspace fixture;
they preserve native files, unrelated plugins and user configuration. This
repair is packaged but was not installed in the running game. The absence
already omitted by 0.1.2 remains omitted; no history or elapsed age is invented.
An independent [offline repair tool](../../Tools/BoneyardTurnoverRepair/README.md)
can restore the externally documented absence in a new archive, using the first
proven outside timestamp and validating every unchanged native payload.

## Reproduce

From the repository root:

```powershell
dotnet build Tests/SaveJsonCompatibility/Host -c Release
dotnet build Tests/BoneyardTurnoverCheck -c Release
dotnet Tests/SaveJsonCompatibility/Host/bin/Release/net8.0/MonoHost.dll Tests/BoneyardTurnoverCheck/bin/Release/netstandard2.1/BoneyardTurnoverCheck.dll
powershell -NoProfile -ExecutionPolicy Bypass -File StandaloneMods/BoneyardTurnover/Test-Install.ps1
```

The independent study remains runnable through `Tools/BoneyardTurnoverStudy`.
It does not register or reference the production plugin. The production harness
links its fixture sources under `IMPLEMENTATION_CHECKS`; no fixture code is
included in the distributed plugin.

## Native integration inventory

| Boundary | Purpose |
| --- | --- |
| `AIShipManager.RegionCleanup` prefix/postfix | Snapshot departing field before native cleanup; commit only after the region actually changes. |
| `SolarSystem.EnvironmentEntities.LoadEnvironmentForRegion` prefix | Consume and apply a recorded return before native spawn IDs are queued. |
| Both `CrewSim.LoadGame` overloads | Reset session tracking before real save loading. |
| `DataHandler.LoadSaveFile(string, Dictionary<string, byte[]>)` postfix | Read optional metadata alongside the native save, without changing native JSON. |
| `CrewSim.OnGameFinishedLoading` listener | Activate validated state after loading; initialization is not a return. |
| `Ostranauts.Core.Models.SavingJob.Start` / `StartWithoutThreading` prefixes | Freeze metadata on the main thread after the native save DTO is complete. |
| `Ostranauts.Core.Models.SavingJob.WriteSaveImagesToZip` postfix | Add frozen optional bytes before the native archive commit. |

The policy resides in this standalone mod because turnover is intentionally new
behavior. Native JSON remains authoritative for template weights, field geometry,
factions and damage rules. No installed vanilla asset is edited.
