# Boneyard Turnover implementation investigation

Status: the investigation below informed the implemented **standalone 0.1.0**.
It is built and offline-tested, with no runtime plugin installed during this work.
See [current validation](VALIDATION.md) for production evidence and live limits.
Reviewed Ostranauts 1.1.0.2 assembly SHA-256:
`133a9cc94ad4f4d5db951b65249b6cdf78f53063419d836b8871b92960ec3c37`.

The [proposal](PROPOSAL.md) remains the policy reference, including Earth and
active-yard exclusions. This document records concrete integration choices,
executed evidence and the remaining work before runtime acceptance.

## Findings and recommended implementation

1. **Use native lightweight wrecks.** `SolarSystem.StellarDerelict` and its native
   save DTO preserve template, identity, ownership, factions and attachment data.
   Nothing here requires a replacement fleet, custom ship type or full interior
   construction before the player enters the field.
2. **Use an optional entry inside the save ZIP, rather than an external sidecar.**
   The native writer can include separate metadata without altering native JSON.
   The native parser ignores it and native resaving without the metadata hook
   drops it. This removes the proposed external-file signature/commit problem
   and keeps copied/cloud-synced saves self-contained.
3. **Ship normal BepInEx CFG values.** `HalfLifeDays=7` and `CurveShape=1`
   reproduce the agreed default. The actual BepInEx ConfigFile roundtrip works
   in the offline study. The [example CFG](BoneyardTurnover.example.cfg) documents
   the proposed consumer settings; it does not install a plugin.
4. **Preserve cumulative absent age for shaped curves.** A shape parameter must
   not turn repeated short trips into an unintended rate multiplier. One age
   value per survivor solves that without a visit journal.
5. **Do not blindly reuse the native placement wrapper.** It can log a failed
   safe-coordinate search and still return a radius and attach the wreck. A
   replacement must have checked valid coordinates before its predecessor is
   removed.
6. **Restrict instantiated-ship deletion to the verified shallow shape.** The
   [risk extension](RISK-REVIEW.md) executes native removal of a visited saved-only
   hull and base CO, plus its registry cleanup. It also reproduces orphaned live
   items, unresolved occupancy, incoming references and partial cleanup failure.
   Temporary exclusions and postconditions are required before enabling turnover;
   arbitrary loaded-inventory destruction is outside the supported shape.

## Curve and configuration

The survival curve for an initially eligible wreck is:

```text
S(age) = 2 ^ (-(age / HalfLifeDays) ^ CurveShape)
replacement probability by age = 1 - S(age)
```

Age means accumulated days outside the region while tracked, not calendar age
of the hull. Staying in the region does not advance it. Both settings must be
finite and positive; invalid values must not reach replacement code. The runtime
implementation should skip turnover with a useful diagnostic when settings are
invalid, rather than silently select an unrelated rate or affect native loading.

With `HalfLifeDays=7`, the initial curve behaves as follows:

| CurveShape | 1 day | 7 days | 14 days |
| ---: | ---: | ---: | ---: |
| 0.5 | 23.0% | 50% | 62.5% |
| 1 | 9.4% | 50% | 75% |
| 2 | 1.4% | 50% | 93.75% |

Capture the validated settings in the departure record. Editing the CFG affects
new departures, not a journey already recorded in the save. Surviving ages
remain available when a later departure uses new settings; the new settings
govern that subsequent interval, with no retroactive replacement pass.

### Several short trips

For a survivor with accumulated absent age `a` returning after another `dt`
days away, use its conditional probability:

```text
p = 1 - S(a + dt) / S(a)
  = 1 - exp(-ln(2) * (((a + dt) / H)^shape - (a / H)^shape))
```

For shape `1`, age cancels and this is exactly the original exponential proposal.
For other shapes, resetting age on every departure would change the curve:
shape `0.5` would reward short-trip splitting, while shape `2` would suppress
turnover. The prototype tests the conditional calculation across fourteen
one-day absences against one fourteen-day absence for all three example shapes.

Fresh replacements start with age zero. Survivors gain the elapsed absent time;
the next departure retains their accumulated age. Protected or no-longer-member
hulls leave the eligible roster. No history of past visits is retained.

### Replacement count with different ages

For equal probabilities, the proposal's randomized rounding of `N*p` is sufficient.
Shaped curves can give survivors different probabilities. An offline selection
prototype accepts a supplied candidate permutation and uses systematic sampling
with one uniform offset. It retains each candidate's probability and limits total count
to the floor or ceiling of `sum(p_i)`, with no duplicate selections. The study
checks mixed probabilities, zero/one boundaries and deterministic replay. Runtime
code must supply the randomized order and offset from its reproducible stream.

The runtime version still needs a private, reproducible random stream and
persisted departure/selection state. `MathUtils.Rand(..., strID)` is **not** a
seeded RNG: native code uses Unity randomness and keeps anti-repeat dictionaries
under those IDs. Creating a new string ID for each draw would grow native state.
Avoid that shortcut. Native loot weights/evaluation should remain authoritative;
a scoped native random-state adapter and its restoration require testing before
production use. No random adapter is installed by this study.

## Field catalog and authored assets

The reviewed asset defines eleven applicable environment variants with seven
distinct template pools:

| Field | Environment IDs | Native template pool | Placement |
| --- | --- | --- | --- |
| Mercury/HQCH | 100 | `RandomDerelictHQCH` | 400-700 km beyond the resolved anchor's radius |
| Luna/EJDR | 1000 | `RandomDerelictEJDR_GEO` | 100-150 km around the authored offset center, 3,500 km at 250 degrees from `EJDR_GEO` |
| Phobos/MTRS | 1002, 10020 | `RandomDerelictMTRS_Phobos` | 60-150 km beyond Phobos's radius |
| Ganymede/JFTS | 1005, 1008 | `RandomDerelictJFTS` | 20-60 km around the authored offset center, 750 km at -5 degrees |
| SVIR | 1006, 1007 | `RandomDerelictSVIR` | 200-300 km beyond the resolved anchor's radius |
| MSUZ | 102, 1020 | `RandomDerelictMSUZ` | 250-600 km beyond the resolved anchor's radius |
| Europa/JPTN | 103 | `RandomDerelictJPTN` | 700-800 km beyond the resolved anchor's radius |

These are asset rules, not seven new hardcoded coordinate systems. Offset
placement depends on the station and reference geometry at the requested epoch.
For ordinary rings the native helper adds the reference body's radius; using
the listed numeric bounds as absolute radii would be wrong around a moon.

Exclude Earth map 800 and OKLG/VORB maps 801/802. Exclusion is by field identity,
not merely region name: an excluded field must not cause an otherwise eligible
neighboring field to be misclassified. Preserve current story variants and
invalidate a departure record if its field definition changes.

The native `TIsDerelictCollector` and `TIsDerelictSpawner` triggers require
`StationIsDerelictCollector` and `StationIsDerelictSpawner` respectively.
`AIShipManager.GetHaulerSpawnStations` evaluates those authored capabilities;
runtime fleet counts are not a valid eligibility criterion. The actual station
and field matching must still be exercised through native region transitions.

## Proposed hook boundaries

These are implementation targets identified by source review, not installed
hooks or a promise of a final hook count.

| Boundary | Purpose | Constraint |
| --- | --- | --- |
| `AIShipManager.RegionCleanup` | Snapshot the departing field and crew absence before cleanup changes representations. | Commit only after a successful actual region change; discard on native failure. |
| `SolarSystem.EnvironmentEntities.LoadEnvironmentForRegion` | Revalidate and apply due turnover before old IDs enter the native spawn queue. | Loading a save is not a new visit. Do not mutate during incomplete initialization. |
| `Ostranauts.Core.LoadManager.SaveGameData` | Capture immutable turnover metadata alongside the native SaveDto. | Use the saved epoch; never read the later live world from a saving worker. |
| `Ostranauts.Core.Models.SavingJob.WriteSaveImagesToZip` | Add frozen metadata before the native ZIP commit. | Omit the optional entry on metadata error; leave the native save mechanism authoritative. |
| `DataHandler.LoadSaveFile` plus successful-load activation | Stage optional archive metadata for the incoming native save. | Clear prior-session state; accept missing/corrupt data without interfering with native loading. |

Departure capture must precede native `UnloadEnvironmentForRegion`, which can
convert never-visited ships into deferred records. Already visited ships remain
instantiated, so a deferred-only census would omit the most relevant candidates.

Do not trust `ship.GetPeople(false).Count == 0` alone. It includes only resolved
person condition owners; `PersonSpec.GetCO` can return null for an unresolved
saved occupant. A nonempty native person list, saved crew payload or unresolved
occupancy is a reason to retain the hull. Existing Ostrabus manual-pruning code
already checks the discrepancy; this standalone module needs its own conservative
guard without depending on that DLL.

## Save metadata refinement

The original proposal used external sidecars. Source review and native tests
support a simpler optional entry named `boneyard-turnover.state.json` inside the
save ZIP, outside `ships/`. The native main save parser reads the requested
player JSON file; `CrewSim.DoLoadGame` selects ship files using `ships/`.
Consequently this entry is not a native ship or game-record input.

The native writer creates a fresh archive from SaveDto contents. Without the
metadata hook, a later native resave omits that entry while retaining the native
wreck records. This avoids external-file matching, full-archive rehashing and
separate Steam Cloud handling. No new native save field or plugin-specific
serialized CLR type is introduced.

Important findings behind this choice:

- `SavingJob.ThreadFunction` supports a worker thread and reports exceptions in
  `job.Exception`. `IsDone` does not itself mean success.
- `LoadManager.NotifyListenersWhenDone` invokes `OnSaveFinished` after both its
  success and failure branches. That event alone is unsafe for committing an
  external sidecar.
- `JsonGameSave.aCustomInfos` is not a general persistent extension dictionary.
  `CrewSim.CustomInfosString` reconstructs seven known entries and
  `SetCustomInfos` handles known keys. Unknown mod bookkeeping would not survive
  ordinary native resaving through that mechanism.

The recommended archive approach shares native commit/failure handling. Still
required: enforce metadata bounds/schema, snapshot identity validation, safe
restore after load success and sensible behavior when the plugin is disabled.
Malformed optional metadata must disable catch-up for that data, not prevent
the game from loading.

## Executed evidence

[The offline study](../../Tools/BoneyardTurnoverStudy/README.md) passes **184
checks** under the game's bundled Mono runtime in the initial pass. The extended
[risk review](RISK-REVIEW.md) now brings the suite to **252 checks**, covering
actual managed native hull/base-CO destruction, retained references and orphan
items, the native spawn queue, saved-CO trimming and metadata failure/rollback
controls. Its project has no reference to
`Ostrabus.dll` and no `BepInPlugin` registration.

- Actual BepInEx CFG save/load preserves both values; curve tests cover defaults,
  monotonicity, invalid parameters, half-life meaning, trip partitioning and
  mixed-age selection probabilities.
- Installed assets supply the eleven reviewed variants and seven pool names;
  bounds/attachment flags and excluded map IDs are checked.
- Actual native `SavingJob` writes a ZIP whose wreck DTO is read by native
  LitJson and reconstructed as native `StellarDerelict`, retaining identity,
  ownership, factions and a manufactured reference string/circular attachment.
- Native saving succeeds both synchronously and on its actual worker thread.
  A controlled invalid destination completes with an exception, demonstrating
  the success distinction without touching player saves.
- An offline hook adds the optional archive entry. Actual
  `DataHandler.LoadSaveFile` still parses native data with valid or corrupt
  optional bytes. Removing that hook and invoking native saving again drops the
  entry without losing the native player JSON.
- Supplying a failed safe-coordinate search to native
  `SetupDerelictPosition` reproduces its unsafe continuation: it returns a radius
  and locks an out-of-bounds position despite the failed search.

Tests write only small private temporary fixtures. Their supplied boundaries
include Unity logging and the failed-coordinate result. They do not open or
modify a live save, call the inspection API, load a complete Unity scene, spawn
the actual ship interior or prove safe destruction of a real visited wreck.

## Implementation gates recorded during investigation

These were the investigation's proposed gates. Production field/eligibility,
replacement, transition and persistence integration now exists and is exercised
by the production harness. Packaging is independent; live scene acceptance and
transition-cost measurement remain pending. The original checklist follows.

1. Build read-only field/eligibility previews for deferred and instantiated hulls,
   with ownership, occupancy, attachments, AI, plots and bounty controls. Prove
   actual anchor resolution and story-variant changes before any deletion.
2. Implement frozen state/config capture and optional archive persistence; test
   failed/cancelled saves, load failure, branching, rollback, metadata corruption,
   repeated callbacks and plugin-free load/resave.
3. Exercise template evaluation and checked placement with a scoped reproducible
   random adapter. Prepare replacements detached from world registries, then
   compose the verified shallow cleanup with protection/reference checks and
   one-for-one publication before enabling batches. On pre-removal failure retain
   the predecessor; on partial native cleanup failure stop the operation.
4. Integrate transition-only turnover and measure the visited-hull cleanup cost.
   Package independently with native-load/uninstall and full/custom Ostrabus
   coexistence tests. No live mutation or deployment until those gates pass.
