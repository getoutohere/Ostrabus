# Optimizations

Harmony patches for save/load costs and bounded profiling telemetry. Each optimization is isolated
here so it can be reviewed or disabled independently.

## Saved CO ship lookup

`SavedCoShipLookupPatch` targets the generated `MoveNext` method for
`CrewSim.DoLoadGame`. During load, the game checks each saved CO's
`strRegIDLast` against the complete saved-ship array using `Enumerable.Any`.
The patch replaces only the `JsonShip.Any` call in that iterator with a lookup
against a `HashSet<string>` built once for that ship array. Ship IDs use
ordinal comparison, matching C# string equality in the original predicate.

The closure containing the current `JsonCondOwnerSave` is read from the
compiler-generated predicate delegate. If the expected closure or array shape
is not present, the helper falls back to the original `Any` scan and logs a
warning. At the end of the load iterator it logs indexed and fallback counts.
The ship array is the weak-table key so the index can be collected after load.

This changes lookup complexity from O(saved COs × saved ships) to
O(saved ships + saved COs). It does not parallelize JSON parsing or ship
creation; all game object work remains on Unity's main thread.

The latest observed load recorded 329,251 indexed lookups and zero fallbacks. This verifies usage, not elapsed time saved; no timing baseline for that optimization exists yet.

## Saved-system coroutine batching

`SystemInitBatchPatch` wraps only `StarSystem.Init(JsonStarSystemSave, JsonShip[])` when loading an existing saved system. It drives the original iterator in its original order on Unity's main thread. Null suspensions during faction restoration, body restoration, asteroid-field restoration and body hierarchy restoration can be coalesced into one frame, up to a cooperative 4 ms budget and 128 original steps per call. A single original step cannot be interrupted, so the budget is not a hard frame-time guarantee.

The wrapper observes progress callbacks only while synchronously executing its own inner iterator. Every progress callback forces the next original yield through so the loading screen can redraw. Only the exact labels `Parse System Bodies`, `Parse Asteroid Fields` and `Parse System Body Hierarchy` enable later batching; unknown labels disable it. The initial faction restoration precedes the first inner progress label and starts eligible. The factory checks that the phase-observer Harmony prefix is installed before wrapping; if absent it keeps the original iterator and warns once.

All non-null yields, explicit waits and nested coroutine objects pass through unchanged. Ship/station spawning, company restoration, final docking initialization, JSON parsing and the new-game fallback retain their original scheduling. Disposal forwards to the original iterator exactly once; exceptions propagate and the thread-local observer context is restored in a finally block. There is no global reference retaining an iterator or save object after it is released by Unity.

Settings in `[Optimizations]`:

- `BatchSystemInit = true` enables this patch; set false and restart for a comparison using original scheduling.
- `SystemInitBudgetMs = 4` sets its per-call work budget, clamped to 0.5-8 ms; invalid/non-finite values use 4 ms. The entry cap remains 128.

Profiling keeps counting original iterator suspensions, even when they are coalesced. Use the new counters to distinguish them from yields returned to Unity: `systemInitBatchingEnabled`, `systemInitBatchBudgetMicroseconds`, `systemInitBatchCalls`, `systemInitInnerSteps`, `systemInitSuppressedNullYields`, `systemInitReturnedNullYields` and `systemInitReturnedOtherYields`. The `StarSystem.Init.batch` method metric measures wrapper work including its nested original calls and must not be added to their times.

Validation: production scheduler/collector tests passed 24 assertions covering preserved entry order, time and entry budgets, progress boundaries, unbatched ship phases, original wait/child identity, disposal/cancellation, exception identity, observer cleanup and suspension counters. In the scheduler test, 2,490 cheap entries required 19 outer yields. The library builds with zero warnings/errors. The next same-save Unity load will measure the actual effect against the 140.009 s baseline; no live speedup is claimed yet.

## Live load profiling

`[LoadProfiling] Enabled = true` in the plugin config enables collection by default. Install the current DLL and restart the game before profiling; the running old assembly cannot provide these hooks. Collection itself is observational. System-init yield batching is a separate optimization controlled by the settings above; work remains on Unity's main thread and the existing lookup optimization is retained.

Query `GET http://127.0.0.1:18764/profiling/load`. It is served directly on the HTTP thread, including before the world is ready and during main-thread loading stalls. It copies detached telemetry under a short lock and never reads Unity objects from the HTTP thread. It uses the inspection API's existing loopback, method, Host and Origin checks. There is no reset/mutation endpoint.

The last three sessions are retained in memory across scene changes:

- `dataInit`: starts at `DataHandler.Init`, includes the background data-loader interval and main-thread post-load work, and ends when `LoadManager.AfterLoadThreadsFinish` completes.
- `sceneInit`: standalone `CrewSim.Awake` when there is no active load session. During a selected save load its cost is instead included in that save's session.
- `saveLoad`: begins when a save is selected, so extraction and the scene transition are included. Ends when `CrewSim.DoLoadGame` completes. Direct loads start at the first `MoveNext` instead. A new session supersedes any unfinished one. Exceptions mark a session faulted; externally stopped/cancelled iterators may remain active until the next session/plugin stop.

Each session reports wall time, measured main-thread work, outside-work time, stage labels, coroutine yield counts, inclusive method timings, the 32 slowest detailed calls, save-size/count counters, GC collection deltas, and managed heap size at its boundaries. Stages and method groups are capped at 64. It retains no game/save/iterator objects, records no per-CO samples, and emits one completion summary to the existing BepInEx log. No forced GC occurs. Completion/profile snapshots allocate bounded detached collections; ordinary gameplay does not accumulate telemetry.

Instrumentation covers both saved-load iterators, `CrewSim.Awake`, archive extraction, temporary-folder cleanup, main-save parsing, each ship-file parser call, shallow/full `Ship.InitShip`, `Ship.SpawnItems`, `_SpawnShip`, docking initialization, and disk image-folder loading. Initial data-loader worker and post-load methods are measured separately. Ship-file parsing is wrapped at the two known `DoLoadGame` call sites, avoiding patches on shared generic deserializer code; unexpected call-site shapes are left unmodified with a warning.

Interpretation matters:

- `measuredMainThreadWorkMs` is elapsed time inside instrumented main-thread calls, with nested spans counted once. It includes GC pauses inside those calls; it is not CPU time.
- `outsideInstrumentedWorkMs` includes frame waits, scene loading, rendering and any uninstrumented work. Do not call all of it idle/yield time. In-memory ship-image decoding in the scene callback, for example, is not individually timed.
- Method `inclusiveMs` values overlap. A parent ship initialization includes item spawning; do not sum all method groups. `mainThreadCalls`/`workerThreadCalls` identify the execution thread, not CPU utilization.
- `nullYields` are explicit iterator suspensions observed, not a measured number of rendering frames. Nested coroutine yields are separate.
- Heap-end minus heap-start is a net managed-memory change, not total allocation volume or peak memory. GC deltas are process-wide collection counts over the session.
- `errors` counts exceptions escaping measured methods. The game's JSON loader catches some failures internally; also inspect `/logs`.
- Per-ship/file progress names are grouped into stages, while slow-call records retain a bounded ID/basename. `savedItems` is the array/input count captured before ship/item initialization, not a count of instantiated Unity objects.
- Timings include this profiler's overhead. First loads also include JIT/cache effects; compare the same save/settings over more than one run before claiming a speedup.

Next run: start the game with the new build, select the same paused save, and leave it loading normally. The API can be polled during loading without the usual main-thread inspection timeout. After completion, export the JSON before restarting the process. We can then rank stage wall/work time, inspect the largest parse and ship-init calls, and decide whether coroutine scheduling or actual processing dominates.

Validation: the library builds with zero warnings/errors. A harness linking the production collector and production snapshot serializer passed 19 assertions covering live snapshots, nested timing, stage grouping/attribution, worker isolation, bounded storage, counters, supersession, error status and disabled collection. A second harness linking the production API, transport, collector and serializer passed 5 loopback checks: active/completed profile access with no main-thread pump or loaded world, Origin rejection, and POST rejection. Harmony installation and measurements on the actual Unity load remain the next integration check.

## First live profile — 2026-11-04

Hooks worked through a complete save load; `/health` reported ready afterward. Raw captures are `../LOAD-PROFILE-STARTUP.json` and `../LOAD-PROFILE-FIRST-COMPLETE.json` (session 2 is the save load). The startup `dataInit` session took 9.077 s wall with 1.469 s measured main-thread work. Save selection through completion took 140.009 s, of which 86.686 s was instrumented main-thread work and 53.323 s outside those spans. No stages/metrics were dropped, and all instrumented method error counts were zero. This is one run, not an A/B speedup measurement.

| Save-load stage | Wall seconds | Instrumented work seconds | Null yields |
| --- | ---: | ---: | ---: |
| Init system (principally faction restoration before the next stage label) | 41.538 | 0.034 | 2,492 |
| Read/parse ship files | 28.702 | 27.466 | 193 |
| Spawn saved ships | 25.598 | 23.902 | 192 |
| Read/parse main save | 19.051 | 18.477 | 1 |
| Fully materialize player ship and docked station | 8.629 | 8.623 | 1 |
| Extraction / scene transition | 7.784 | 7.168 | 0 |
| Parse body hierarchy | 2.863 | 0.002 | 172 |
| Parse bodies | 1.933 | 0.002 | 116 |
| Parse asteroid fields | 0.971 | 0.005 | 58 |

The archive expanded to 1,195,951,266 bytes: the main save was 495,782,934 bytes, 192 ship JSON files totaled 696,696,411 bytes, and PNGs totaled 3,438,249 bytes. There were 329,251 saved COs, all processed by the existing indexed lookup with zero fallback scans. The whole `Load star system` membership-filter stage took 0.252 s, supporting the conclusion that further optimizing that lookup is a low priority in this run.

Actual ship JSON deserialization consumed 27.459 s; archive extraction consumed 7.163 s. `OKLG_UNK.json` alone parsed in 5.731 s. Its shallow initialization took 10.053 s, including a 7.742 s `SpawnItems` call processing 23,579 input entries (the hull's saved item array had 25,381 entries). The player O-GXD full initialization took 8.536 s with 10,781 saved items; that includes nested initialization of docked VORB (4.310 s), so those full-init times must not be added together. The managed heap grew from about 453 MB to 6.90 GB net; peak allocation was not measured. The three GC counters each returned 59 and must not be summed as 177 separate collections.

The measured first optimization candidate is the per-entry suspension in faction/body/hierarchy reconstruction: the large Init system stage is almost entirely outside-work time and its 2,492 yields match roughly 60 resumptions/s. The expensive file parsing and shallow item construction are separate targets. Their observed costs do not justify parallel Unity construction; they justify focused investigation of pure parsing and forced-load item work after improving coroutine scheduling. No new load-speed change was applied during this baseline run.

## Second live profile — batching enabled

The next fresh-process load used the same archive (208 entries, byte-identical uncompressed totals, 192 ship files, 329,251 saved COs). It completed in **91.038 s**, compared with **140.009 s** in the baseline: **48.971 s shorter (35.0%)** in this pair of runs. Raw data is in `../LOAD-PROFILE-SECOND-COMPLETE.json`; the comparison is `../LOAD-PROFILE-COMPARISON.json`. The game reported ready, no stages/metric groups were dropped, and all instrumented method exception counts were zero.

| Stage | Baseline seconds | Batched seconds |
| --- | ---: | ---: |
| Init system | 41.538 | 0.307 |
| Parse bodies | 1.933 | 0.017 |
| Parse asteroid fields | 0.971 | 0.019 |
| Parse body hierarchy | 2.863 | 0.030 |
| Read/parse main save | 19.051 | 19.268 |
| Read/parse ship files | 28.702 | 28.289 |
| Spawn saved ships | 25.598 | 23.825 |
| Fully materialize player ship/docked station | 8.629 | 8.601 |

The original system iterator executed **3,048 steps in both runs**. The wrapper suppressed 2,814 original null suspensions and returned 232 null yields plus the original non-null completion yield, using 234 total wrapper calls including termination. All 192 shallow initializations and 3 full-initialization calls remained present. Original progress/yield telemetry still shows the same 2,492 Init-system yields and the same per-entry body/hierarchy counts; these are original suspensions, not actual frame waits after batching. Spawning still yielded once per saved ship (192).

Measured main-thread work changed from 86.686 to 84.728 s; outside-instrumented-work time fell from 53.323 to 6.309 s. Thus about 47.0 s of the improvement is explained by eliminated scheduling gaps; the roughly 2 s processing-time difference must not all be attributed to batching from one comparison. Main/ship parsing together still consume about 47.6 s of stage wall time. Parsing and heavy shallow item initialization remain the next investigative targets.

## Offline serializer benchmark

The temporary harness is `obj/JsonBenchmark/Run.ps1`, with its native Mono host in `obj/MonoHost`. Run it from PowerShell with `-SaveArchive <zip>` and optionally `-Entries <entry names>` / `-Rounds 3`. It copies the input archive, extracts selected entries using the host framework, and parses resident byte arrays in fresh standalone processes using the game's actual bundled Mono runtime, `Assembly-CSharp.dll` DTOs, and bundled Newtonsoft.Json 13. No game process or inspection API is required. The mod's production serializer is unchanged. These temporary files live under `obj` and can be removed by cleaning build outputs.

Unity's native random calls used by `PersonSpec` constructors are replaced by deterministic midpoint/minimum callbacks; the four name dictionaries and CO registry are initialized empty. Constructors and all parser/mapping code otherwise remain the game's actual implementations. This isolates parsing, not scene creation, ship spawning, game databases or main-thread requirements. It cannot establish that background deserialization is safe in Unity.

Each file/mode is parsed three times after a small DTO warm-up. Full GC occurs before each timed round. The recorded first round includes deeper contract/JIT initialization; reported warm times average rounds 2 and 3. Archive extraction, initial byte loading, full GC, fingerprinting and result output are outside the measured parse time. Baseline `lit-string` includes UTF-8 to UTF-16 conversion; streamed variants use the same resident bytes with a 16 KiB StreamReader. The strict LitJson variant disables comments and single-quoted strings only.

Compatibility validation fingerprints each first-round result recursively: public writable properties and public nonserialized-excluded fields, list order, sorted dictionary entries, nulls, exact strings, and exact floating-point bits. Computed read-only properties are excluded. Matching fingerprints validate those deserialized values for these inputs, not every possible save or game behavior. The runner rejects divergent fingerprints among the tested replacement modes.

Unmodified Newtonsoft already fails value comparison on O-QFNF: the game writes infinity using `9E99` / `9e99` sentinel notation and its custom LitJson converts them to infinities. The benchmark's `net-compat` reader converts numeric magnitude `9e99` to infinity. This is an experiment, not a complete replacement for LitJson semantics: probes also expose case-sensitive versus case-insensitive member lookup, duplicate dictionary-key handling, and a lower-case sentinel assigned to a double that LitJson rejects but the experimental reader accepts. A production replacement requires an explicit compatibility policy and broader fixtures.

The bundled Mono exposes `GC.GetAllocatedBytesForCurrentThread` but returns zero even across a known allocation; the harness detects this and records allocation bytes as unavailable. Heap-before/after observations are not allocation totals or peak memory. Streaming avoids materializing the entire UTF-16 input string but does not, by itself, establish a lower peak for the whole loading process.

Completed results are preserved in `../JSON-BENCHMARK-RESULTS.json`, including input/game assembly SHA-256 hashes, every measured round, and canonical fingerprints. Five selected entries totaling 715,414,751 UTF-8 bytes were tested. The main save alone contributed 24,988,556 inspected values. All five matched baseline fingerprints with streamed/strict LitJson and the experimental Newtonsoft compatibility reader; unmodified Newtonsoft diverged on the hauler control.

| Entry | LitJson string, warm seconds | Newtonsoft compatibility reader, warm seconds | Parsing time reduction |
| --- | ---: | ---: | ---: |
| Main save (496 MB) | 18.657 | 12.485 | 33.1% |
| OKLG_UNK (163 MB) | 5.141 | 3.943 | 23.3% |
| Player O-GXD (32 MB) | 1.034 | 0.643 | 37.8% |
| VORB (25 MB) | 0.840 | 0.613 | 27.0% |
| Hauler O-QFNF (0.8 MB) | 0.0327 | 0.0239 | 27.0% |

Streamed LitJson and disabling permissive syntax did not improve parsing speed in these runs. The main save's streamed/strict warm means were 19.072 / 19.810 s versus 18.657 s for the existing string path. Newtonsoft is a promising parser replacement candidate after compatibility work; these figures are not whole-game loading speedups. The offline baseline is consistent with the live main-save parse measurement (18.477 s), but the host lacks Unity's full database, heap and scene workload.

On systems with script execution disabled, invoke the temporary runner with `powershell -NoProfile -ExecutionPolicy Bypass -File obj/JsonBenchmark/Run.ps1`. This sets policy for that child process only. Generate the comparison report with the same invocation of `obj/JsonBenchmark/Summarize.ps1` after the runs complete.
