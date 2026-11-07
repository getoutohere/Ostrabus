# Temporary memory and cleanup diagnostics

Purpose: distinguish retained live simulation objects, dead Unity wrappers,
uncollected managed data, native assets and allocator reservations. These are
observations, not automatic repair. No GC, asset unloading, deletion, registry
cleanup, timer changes or altered native return values are performed.

## Controls

| Request | Behavior |
|---|---|
| `POST /memory/start` | Install the temporary observers and take a baseline. Repeated start preserves the current session. |
| `POST /memory/snapshot` | Capture current memory, registry ownership, async hulls and weak samples. Works with tracing off too. |
| `POST /memory/assets` | Explicitly scan loaded Unity objects and estimate texture, mesh and material memory. Includes inactive objects/assets. |
| `GET /memory?since=0&limit=25` | Read detached records; `since=0` returns recent records. Other cursors return records after that sequence. |
| `POST /memory/stop` | Take a final snapshot, remove every memory observer and scene listener, and release weak samples. Retain detached results. |

All controls execute on Unity's main thread. Start/snapshot/assets require a
loaded game. Stop can run without a ready simulation. GET serves cached results
off the Unity thread, including while loading. No request selects arbitrary
reflection targets or invokes arbitrary code. Controls require POST; no request
body or extra query parameters are needed.

The API has a two-second deadline. An expensive asset scan may finish after its
HTTP request times out; its record is still available on GET. Retrieve that
record before repeating the scan. Asset scans allocate a temporary object array
and can hitch. They are never called automatically.

## Zero dormant game hooks

`MemoryDiagnosticHooks` deliberately has **no `HarmonyPatch` attribute**. It is
not included by startup `PatchAll`. Start explicitly patches these 11 methods:

- `Ship.Destroy` and the separate `Destroy` methods on `BarterZoneShip`,
  `AsyncPreviewShip` and `AsyncRatingShip`.
- `CrewSim.EmptyScene` and `DataHandler.Init`.
- Both `AsyncShipLoader.Unload` overloads.
- `CondOwner.Destroy`, `CondOwner.OnDestroy` and `Tile.Destroy`.

Start also subscribes to scene-loaded/unloaded events. The observers survive
transits and save reloads so the teardown boundary is visible. Stop unpatches
the independent Harmony owner and unsubscribes both scene listeners. Plugin/API
disposal stops them too. There is no memory Update callback, timer or background
sampler, including no disabled hook that runs and immediately returns. The
only dormant integration is explicit API route dispatch, when API requests are
handled. Starting the game again leaves tracing off.

## What is measured

- Managed used-memory estimate without forcing collection; Mono heap usage and
  reservations; Unity allocated/reserved/unused-reserved memory; GC counts.
- Process private committed memory and working set at explicit snapshots and
  before/after teardown boundaries. These counters overlap other categories:
  do not add them together as independent allocations.
- Global registered CO count, native debug CO count, simulation/scene hull
  counts, saved CO count and ship-image-cache entries.
- Registered COs grouped by definition, owner ID and ownership category:
  current world hull, async copy, destroyed hull, outside-world hull or unowned.
- Actual managed nulls versus Unity-destroyed wrappers, and COs marked destroyed
  that are still in the global registry. Suspect examples include IDs, owner
  runtime type/identity, parent ID and whether the owner's own item map still
  contains the same object. A tile helper being absent from an item map alone
  is not evidence of a leak.
- Raw/dead animator entries and the existing async loader's hull collection,
  including incomplete loads. Reading it never calls `MonoSingleton.Instance`
  and therefore never creates a loader. A destroyed singleton wrapper's managed
  dictionary is still examined if a managed instance remains.
- Counts of normal managed CO destruction, Unity component destruction, Unity
  destruction without the managed destroyed flag, and those still registered
  at that moment. These counters observe different stages; unequal counts alone
  do not prove a leak.
- Bounded weak-reference samples of ship items/helpers before destruction and
  registered COs whose Unity destruction bypasses managed cleanup. Later
  snapshots show whether wrappers were collected, still have native objects,
  remain registered or retain condition/interaction collections. Samples do not
  keep COs alive. Survival before a natural GC does not prove retention.
- Explicit asset scans count Unity objects by type and list the largest 16
  textures/meshes/materials. Their estimated memory does not cover every native
  allocation and is not a complete accounting of process memory.

While active, per-CO hooks update counters and bounded samples; they do not scan
registries or log per CO. Registry censuses occur at explicit requests and
EmptyScene/Init/async-Unload boundaries. Ship destruction records are cheap
summaries. Diagnostic failures are isolated and native exceptions propagate.

## Bounds, logging and removal

History is a 256-record ring. There are at most 512 weak samples; histograms
retain 128 keys plus an overflow bucket and return the top 32. At most 1,024
records per session are mirrored as `[MemoryDiagnostics]` JSON lines to the
BepInEx log. Snapshots contain detached scalar data, not game-object references.
The diagnostic buffers and formatting do allocate while enabled; the bounds
keep that contribution controlled. GET does not trigger a census or collection.

After diagnosis, stop the session first. The three `MemoryDiagnostic*.cs` /
`MemoryInspection.cs` implementation files can then be removed along with their
route handling and disposal call in `InspectionApi.cs`, and the API documentation.
There is no configuration, save schema, persistent game data or plugin startup
registration to migrate. The AnimationModule reference is only a build reference;
it loads no extra diagnostic behavior by itself.

## Reproduction plan

1. Start from a fresh process/save load. Enable tracing and take one explicit
   asset snapshot to establish the scene and allocator baseline.
2. Perform the user's normal OKLG transit loop and return to the same starting
   scene. Take another snapshot; compare like-for-like loaded scenes, not just
   raw process memory at different stations.
3. If testing save reload, leave tracing enabled. It captures old-scene cleanup,
   DataHandler's leftovers, async ownership and the newly loaded scene. Take a
   snapshot after loading settles; use natural GC counters and weak samples to
   distinguish pending collection from retained references.
4. Export records through the API, stop and verify `active=false`. Fix the first
   proven ownership/cleanup failure rather than suppressing the warning or
   periodically sweeping registries.

## Existing evidence, November 6, 2026

One save-reload batch contained 3,726 `Destroying leftover CO` entries, including
1,904 `TIL` helpers, floors, conduits and other equipment. No native orphan-loop
warning was present in that run. The warning is emitted by `DataHandler.Init`,
not ordinary station transit. A subsequent process observation measured 17.28 GiB
working set and 20.01 GiB private memory; the user's nominal ~10 GB is not a
controlled baseline. The log was preserved under ignored `obj` artifacts.

`CrewSim.EmptyScene` does not explicitly unload the separate async loader before
destroying the main world. `Tile.Destroy` clears its CO reference without normal
CO destruction, and `CondOwner.OnDestroy` does not perform full managed cleanup.
Those are paths to investigate, **not proof they explain this batch or the entire
memory increase**. Scene-unload timing and deferred Unity destruction also matter.
The prior stale-count orphan-loop defect is separately documented in
[ORPHAN-CO-CLEANUP.md](../MiscBugFixes/ORPHAN-CO-CLEANUP.md).

## Live transit reproduction, November 6, 2026

Tracing was enabled after a fresh load, then the user performed an OKLG transit
loop and returned to the same apartment scene. Baseline and after-state both
had 83,131 native CO components, four materialized ships and 679 animator entries
with zero dead animator entries. The trace reported zero diagnostic failures.

| Measurement | Before | After |
|---|---:|---:|
| Process resident memory (external PowerShell) | 9.91 GiB | 13.92 GiB |
| Process private memory (external PowerShell) | 12.08 GiB | 16.13 GiB |
| Managed used estimate | 6.68 GiB | 8.70 GiB |
| Global CO registry entries | 83,131 | 161,212 |
| Registry entries with destroyed Unity objects | 0 | 78,081 |
| Natural GC count | 258 | 262 |

The 78,081 retained dead entries exactly match the tile counts on the destroyed
hulls: 64,753 ordinary ship helpers plus 13,328 helpers from seven barter copies
(1,904 per copy). Sample helpers still contain conditions and references to old
owning ships despite Unity destruction. The global registry is a strong root,
so this is confirmed retention across natural collections, not just pending GC.

Async Unload **did run** throughout this transit reproduction; the loader's
dictionary was empty afterward. Thus missing async-Unload calls are not the
cause of this transit leak. The common defect is helper-CO lifecycle cleanup:
ordinary tile destruction clears the tile reference and destroys the Unity
object without the helper's managed `CondOwner.Destroy`; async copy teardown
removes mapped item IDs but leaves helper IDs before destroying the hierarchy.
`CondOwner.OnDestroy` disposes the clock but does not unregister/clear the CO.
The surviving helper-to-ship reference can also retain the old async copy's
uncleared item map. Fix these ownership boundaries rather than periodically
sweeping the global registry.

Textures grew by only 17.30 MiB. The observed object retention establishes a
real leak, but without a heap graph we do not attribute every byte of process
growth to it. The in-game Mono Process API returned zero process counters, so
the process values above come from external `Get-Process`; other metrics came
from the diagnostic API. Capture artifacts are ignored `obj/MEMORY-LIVE-*` JSON.
All hooks/listeners were stopped afterward; `GET /memory` confirmed inactive.

## Offline checks

`Tests/MemoryDiagnosticsCheck` validates explicit installation/removal, absence
of startup attributes, scene-listener removal, ownership classification, dead
wrappers, counters around actual native `OnDestroy`/`Tile.Destroy`, passthrough
of native exceptions, detached/bounded records, asset summaries and weak samples
that do not prevent collection. GC is forced **only in the offline test**, never
by the diagnostics. Engine-heavy lifecycle bodies and profiler/resource calls
are fixtures, not a hosted Unity scene or live memory-leak measurement.
