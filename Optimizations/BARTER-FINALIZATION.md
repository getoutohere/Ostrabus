# Remote barter initialization: repeated saved-item scans

Follow-up: contained-item removal now uses its known index instead of searching
the pending list. Remote-trade write-back and save cleanup are also optimized;
see [INVENTORY-PERFORMANCE.md](INVENTORY-PERFORMANCE.md).

`BarterLocalIndexPatch` replaces `BarterZoneShip.AddToLocalInstance`.
`BarterInitPatch` replaces the remote-copy initialization coroutine with the
same native operations and additional cooperative yield points.
Always enabled; no polling, tracking, saved data or diagnostic hooks.

## Why a hitch can happen after transit

`GUITransit._Go` makes the destination playable and starts
`AsyncShipLoader.LoadDockedBarterZoneShips`. The offscreen barter copy continues
loading on Unity's main thread. Its initial item loop yields periodically,
but the contained-item reconstruction and finalization stages do not.

Inside that finalization, native `AddToLocalInstance` walks a snapshot of every
loaded CO. For each CO it linearly searches `json.aItems` for a matching ID.
Even when every loaded item is already represented in JSON, it repeats that
search for all items. With 9,892 items in the same order, this is 48,930,778 ID
comparisons. The source therefore contains a concrete avoidable CPU cost, not
just a poorly placed loading screen or a missing coroutine yield.

The replacement constructs a local ordinal ID HashSet once, then checks each
loaded item against it. Lookup work becomes linear in saved plus loaded items.
It keeps native snapshot order, existing JsonItem objects and the original
array when nothing is added. Newly generated loot still uses native
`Ship.GetJsonItem` and is appended immediately in the same order; the ID set is
updated for subsequent items. Array-copy/serialization costs of genuinely new
items remain. Null IDs and existing duplicate JSON IDs keep native matching
behavior. Malformed null arrays/records retain native handling.

The set is local to this one call and does not retain the hull or its inventory.

## Readiness and scope

After removing the quadratic scan, a live transit profile still caught a
2,271.62 ms frame gap aligned with a 2,245.73 ms final barter iterator step.
No GC collection occurred across that gap. The indexed AddToLocalInstance call
took 48.13 ms; reconstruction and other finalization work still occupied one
uninterrupted iterator step. A separate 7,239.58 ms initial transit/loading step
is outside this fix.

The replacement coroutine follows the 1.0.1.10 native algorithm. It checks a
shared 4 ms wall-clock budget before each outer item, contained-item attempt,
PostGameLoad call, index record and boundary tile. Exhaustion yields null;
Unity resumes the work on a later frame. All barter loaders share the deadline
for a frame rather than each receiving another 4 ms. The native outer 0.2 second
waits remain. Index construction and missing-item serialization also yield
between records; the synchronous index entry point uses the same algorithm.

Parent dependency passes, reverse contained-item traversal, slot failures,
temporary stacking suppression, PostGameLoad snapshot order, zone setup,
MoveShip and boundary obstruction assignment retain native ordering. No yield
occurs in the middle of temporarily disabling stacking. `_fullyLoaded` is set
only after the final boundary operation. Cancelling or failing the iterator
does not publish readiness; native AsyncShipLoader still owns unloading and
physical destruction. The scheduler retains only a frame number and deadline,
with no global reference to the copy, iterator or inventory.

`GUITradeBase.GetZones` asks `AsyncShipLoader.GetShip`, which returns false until
`FullyLoaded`. If the loader still knows that ship, GetZones sets `bZonesLoading`;
the trade UI shows the existing remote-zones-not-loaded message. Saving changed
barter COs is also gated by FullyLoaded. This patch does not bypass those gates.

This is a cooperative budget: an individual CreatePart, PostGameLoad,
SetZoneData, MoveShip, collection snapshot or other atomic call can exceed it.
No background threads touch Unity objects. Readiness may take more frames;
the existing trade-loading message remains visible until completion. It does
not promise a maximum frame time or change the initial transit/loading step.

## Offline validation

`Tests/BarterLocalIndexCheck` executes the native private method before patching,
then the patched method, and compares JSON contents/order, existing object
identity and native serialization calls. Fixtures cover empty inventories,
case-sensitive IDs, null IDs, duplicates, nested crate/backpack/stack references
and newly generated items. Only serialization of new fixture objects is stubbed;
the native repeated-scan method runs unchanged for the baseline.

At 9,892 existing items the offline native method took **2,003.96 ms**, versus
**1.87 ms** indexed. These are method timings under the game's bundled Mono,
not a measured live transit or whole-frame speedup. The 19 output-equivalence
checks passed. The Debug plugin and startup Harmony binding audit cover the
actual production patch.

`Tests/BarterInitCheck` compares execution of the actual native coroutine with
the replacement under the game's Mono runtime. Engine-heavy leaf operations
are fixture stubs shared by both runs. Cases cover a 300-level nested chain,
unresolvable parents, failed creation, successful/rejected slots and unchanged
saved records. Tests also exercise the real shared clock, readiness during
yields, cancellation both before and during reconstruction, and propagated
exceptions. These validate scheduling and operation equivalence, not live
rendering performance. The temporary transit profiler recognizes both native
and optimized iterators and remains completely unhooked unless started.
