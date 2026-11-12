# Inventory bookkeeping optimizations

Always enabled in Optimizations. These changes remove unnecessary work during
remote trading, contained-item loading and saving. They introduce no saved
fields, background workers, polling or global collection hooks.

## Remote barter write-back

`BarterWritebackPatch` replaces `BarterZoneShip.SaveChangedCOs` with the native
operation sequence and an ordinal ID lookup. Membership uses the map **key**,
which need not equal the CO's current ID. New items are not inserted into that
lookup: native membership remains the original hull's array until final commit.
If a nested callback replaces that array, the lookup is rebuilt for it.

Existing and duplicate/null IDs, new-item serialization, GetJSONSave calls and
global save registration follow the native operation sequence. The final array
still combines the barter copy's current layout with newly serialized records
and is assigned to both ships. Null source records/arrays and null copy layouts
retain the native error path.

The separately categorized [writeback safety repair](../MiscBugFixes/MULTI-SHIP-BARTER-AUDIT.md)
is now shared by native and optimized paths: an existing ASYNCIDENTIFIER marker
is preserved rather than added twice, and a `finally` restores exact temporary
coordinates on success or failure. The custom `barter-index` feature requires
`barter-writeback`, including safety when malformed inputs fall back to native
execution. Serialization exceptions still propagate; this is not a general
transaction rollback mechanism. Equivalence tests compare the optimization to
the repaired native method.

The ID set and additions list are borrowed from `BarterWritebackScratch`.
Each thread retains at most one idle buffer; active and nested calls cannot
share it. Finally clears IDs, serialized records and the source-array reference.
Buffers whose peak unique-ID count or additions capacity exceeds 32,768 are
not retained. The normal path reuses their backing storage after warm-up.
The output array and native per-new-item save/tag objects still allocate.
The empty output uses Array.Empty rather than creating another empty array.

## Contained-item removal

`ContainedItemRemovalPatch` changes only the removal calls inside native
`Ship.SpawnItems` and the AsyncPreviewShip initialization iterator. It captures
the index already supplied to List<JsonItem>.get_Item and uses that index when
removing the processed record. The existing paced barter coroutine uses the
same helper directly. Normal JSON deserialization creates distinct record
objects, including when IDs are duplicated.

The helper validates the record still occupies that index, otherwise falling
back to native Remove-by-value. No sets, caches or per-item allocations are
introduced. Parent dependency retries, destruction/failed-creation branches,
slot retries, record ordering and native yields remain in place. Interior
removals can still shift references; this removes the redundant search, not
every possible quadratic cost in dependency processing.

The transpiler requires the expected two full-loader and one preview removal
sites. Only those method bodies change; List.Remove is not globally patched.

## Save cleanup

`SaveRemainingIdsPatch` leaves SaveGameData's serializer, save DTOs, archive
writing and trimming decisions native. It replaces four List<string>.Remove
calls on the method's private remaining-ID lists. Every result must immediately
be discarded in IL; a changed native shape fails patch binding.

A scoped dictionary counts requested removals for the current private list.
Switching lists first compacts the preceding list in order. Before native
enumeration of the remaining-ID dictionary, the last list is compacted too.
One ordered pass removes the requested number of occurrences, preserving
duplicates, missing-ID behavior and leftover order. In native SaveGameData
these lists are fully populated before removals begin; nothing reads their
intermediate contents before final trimming. No global CO dictionary cleanup
is deferred or replaced by this patch.

Only one list's removal counts are held at a time, allowing storage reuse across
ships within a save. Prefix/finalizer scope management also supports nested
saves and exceptions. Finally releases all list/ID references. Each thread
retains at most one idle scope; a peak count above 32,768 causes it to be dropped.
Retained capacities follow the runtime's collection growth policy, so the
threshold describes entries, not an exact byte capacity. There is no persistent
ship/item index to invalidate.

## Offline validation

`Tests/InventoryPerformanceCheck` runs the actual native methods before and
after patching under the game's bundled Mono runtime. Fixtures stub Unity and
serialization leaf operations identically for both executions.

- Write-back compares JSON, coordinates, tagging, serialization order, global
  save registration and exceptions. Cases include case-sensitive/duplicate/null
  IDs, differing map keys, missing save results, nested calls replacing the
  original array, malformed layouts and existing tags.
- Full and preview loaders compare creation, parent/slot assignment order,
  final membership, stacking restoration and readiness. Cases include nested
  chains, missing parents, failed creation, destruction and rejected slots.
- SaveGameData runs through its actual remaining-ID construction, ship loop,
  exception boundaries, DTO population and final cleanup. The fixture stops
  after assigning main-save aCOs, before unrelated objectives/system/market
  serialization. Comparisons include full/shallow hulls, construction lots,
  orphan/moved/layout-only IDs, skipped waypoints and a per-hull save failure.
  A top-level failure verifies finalizer cleanup.
- Randomized list tests cover duplicate IDs, missing IDs, null removal requests
  and interleaved lists. Additional checks exercise pool reuse, reentrancy,
  clearing and oversized-buffer rejection.

Representative 8,000-item fixture runs under Mono:

| Path | Native | Patched |
| --- | ---: | ---: |
| Matched-item barter write-back | about 1,500 ms | under 1 ms |
| SaveGameData through final trimming, reverse-ordered fixture IDs | about 1,310 ms | about 6 ms |

These are offline method-path timings, not whole-save or in-game FPS claims.
Serialization leaves are stubbed in the save fixture. Existing barter-init
equivalence tests also cover the changed paced loader; startup binding is
audited against the real game assembly.

The bundled Mono runtime returned zero from GetAllocatedBytesForCurrentThread
even after a deliberate allocation. Those zeros are not allocation evidence.
The same production scratch/removal helpers are therefore additionally checked
under .NET 8 with a verified working allocation counter, without Unity calls or
Harmony hooks. After warm-up, 1,000 cycles of indexing 8,000 barter IDs and
1,000 cycles of recording/compacting 8,000 save IDs each allocate **zero bytes**.
Known-index removals also allocate zero bytes. This establishes the helpers'
allocation behavior under .NET; it does not claim total Unity operations are
allocation-free.

Build/run the netstandard2.1 fixture using the existing bundled-Mono host.
Run the allocation-only target with:

```powershell
dotnet run --project Tests/InventoryPerformanceCheck -c Release -f net8.0
```
