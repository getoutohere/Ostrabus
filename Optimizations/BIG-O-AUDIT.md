# Algorithmic cost audit — 2026-11-07

Follow-up: the first three recommendations are now implemented and validated
offline. See [INVENTORY-PERFORMANCE.md](INVENTORY-PERFORMANCE.md). The findings
and prototype measurements below describe the original audit baseline.

First pass over the 1.0.1.10 decompilation and Ostrabus. This is an audit,
not a collection of implemented fixes. No production patches, settings or
live diagnostic hooks were changed. Source paths below are relative to the
decompilation root; line numbers describe that decompilation, not developer
source lines.

Priority combines reachability, scaling, measured cost and expected input size.
A nested loop alone is not evidence of a defect. Expected hash lookup costs
below assume ordinary hash distribution.

## 1. Remote barter write-back: measured, high priority

`Ostranauts.Ships/BarterZoneShip.cs:159`, `SaveChangedCOs(ref Ship)`.
For every mapped item, it calls `originalShip.json.aItems.FirstOrDefault`
matching ID. With R runtime items and J saved items this is O(R × J), even when
there is nothing new to serialize. With matching inventories it is quadratic.
Both buying and selling reach this through
`GUITrade.cs:672,763` → `AsyncShipLoader.SaveAsyncShips<BarterZoneShip>` →
`SaveChangedCO`. This is separate from the AddToLocalInstance fix.

The audit executes the actual native method under the game's Mono runtime,
with every item already saved. No serialization/Unity branch is needed for
that case. Median of three measured repetitions after warm-up:

| Items | Native write-back | Lookup prototype |
| ---: | ---: | ---: |
| 1,000 | 24.16 ms | 0.11 ms |
| 2,000 | 94.37 ms | 0.36 ms |
| 4,000 | 373.44 ms | 0.38 ms |
| 8,000 | 1,494.26 ms | 1.05 ms |

The prototype covers this matched-item case only, including final array
materialization. It is not a completed replacement. A production fix should
build a local ordinal ID set, preserving the native treatment of newly added
items, JSON array order, transforms and GetJSONSave side effects. It must
not silently change whether newly encountered IDs enter the membership set:
native membership tests use the original ship's array until the final commit.

## 2. Contained-item loader bookkeeping: confirmed, high priority

`Ship.cs:2547–2640` (`SpawnItems`),
`Ostranauts.Ships/BarterZoneShip.cs:53–115` (`Init`), and
`Ostranauts.Ships/AsyncPreviewShip.cs:~50–111` (`Init`). Our
`BarterInitPatch.Initialize` retains the same pattern.

These loaders traverse pending records by index but remove successfully
processed records with `List<JsonItem>.Remove(record)`. That searches from the
front, then shifts remaining records. Removing all N records can therefore
cost O(N²) even when each parent is immediately available. In a simple reverse
traversal, removing by the known index eliminates both the search and shifts.
With unresolved dependency passes, interior removals can still shift records.

An isolated Mono model of this exact list operation, with 8,000 immediately
resolvable records, took 59.20 ms versus 0.037 ms using the known index. This
is bookkeeping only, not a timed execution of SpawnItems or item creation.

The read-only save survey found 9,896 layout items on O-GXD, including 8,091
with parent references. An aItems-only parent-readiness model counted:

- 22,299 parent attempts across six passes;
- 26,095,728 reference comparisons from Remove-by-value;
- 6,631,413 shifted references.

This models successful creation/slotting and excludes crew records, loot,
remapping and actual runtime parent availability. Its 134 unresolved records
are not proof of corrupt objects. These are structural counts, not measured
game time. Across the save, 213 ship layouts were surveyed.

The general dependency algorithm can additionally take O(N × depth), reaching
O(N²) for an adversarially ordered chain. O-GXD's six passes suggest the
known-index removal is the smaller, more justified first change. A wholesale
topological reorder risks changing loot generation and slot/stack behavior.
Yesterday's frame budgeting improves responsiveness but retains these CPU costs.

## 3. Full-save CO cleanup: confirmed pattern, high priority to measure

`Ostranauts.Core/LoadManager.cs:~770–875`, `SaveGameData`.
It builds `Dictionary<string, List<string>>` grouping previously saved CO IDs
by ship. During serialization it removes each handled ID with List.Remove;
then it again removes each layout item ID from the same remaining-ID list.
At the end, remaining IDs identify obsolete save records to trim.

For K IDs on a ship, repeated search/compaction can be O(K²), including searches
for already removed IDs. This occurs during ordinary saving, not just migration.
No existing Ostrabus patch changes this local bookkeeping.

A Mono model of 8,000 reverse-order string removals took 1,281.01 ms;
HashSet removal took 0.585 ms. This is deliberately adverse ordering and
**not** a measured SaveGameData duration. Matching order reduces comparisons
but still moves array contents. Runtime grouping/order must be measured before
claiming a whole-save gain.

Candidate fix: use hash membership for remaining IDs, while preserving the
original order when enumerating leftovers for trimming. A companion set plus
one final ordered filtering pass can preserve native semantics without a
persistent cache or touching JSON serialization.

## 4. Power-network flood fill: confirmed pattern, medium priority

`TileUtils.cs:463–600`, `GetPoweredTiles`, called during ship initialization
and by `Ship.UpdatePower` (`Ship.cs:4101`). For each source it traverses the
connected network and clears tile visitation flags across the whole hull.
Queue membership uses a growing list's IndexOf. Duplicate edge detection
also scans the accumulated edge list. Worst-case traversal work can become
quadratic in network size, multiplied by the number of power sources.

There is an adjacent correctness concern: the powered edge loop increments
`k` but reads `aPwrTiles[i]` and `[i + 1]`; the unpowered loop increments `n`
but reads `aPwrTilesOff[m]` and `[m + 1]`. Thus these inner loops repeatedly
compare the same pair rather than iterating edge pairs. This is visible in
source, but downstream consequences and frequency have not been measured.

Do not simply replace the entire routine with one visited set: each tile's
connected-source collection must retain every relevant source, and edge lists
are externally consumed. An ordered queue plus separate membership/edge sets
is the candidate; first validate graphs with multiple sources, cycles and
unpowered sections, including output equivalence and the indexing issue.

## 5. General/meat flood fill: confirmed pattern, medium priority

`TileUtils.cs:602–687`, `GetFloodTiles` and `GetFloodTilesAround`, use IndexOf
on the growing discovery list. Broad traversals can cost O(V²) rather than
O(V + E). Both first reset visitation flags over the ship's entire tile array,
even for a small requested result.

`CrewSim.cs:7726` requests at most about 100 flood tiles, limiting the queue
search cost for that caller, though not the hull-wide reset.
`Meat.SpreadFast` (`Meat.cs:306–315`) instead requests the whole hull's tile
count, then truncates the resulting list to nTiles. It can therefore explore
a large accessible area just to use a small prefix of its traversal.

Candidate fix: ordered traversal with O(1) discovery membership. Separately
check whether SpreadFast may stop after obtaining its required prefix without
changing filter behavior or visitation side effects. No live timings yet.

## 6. Work selection: confirmed, lower priority at ordinary task counts

`WorkManager.CollectTasks` (`WorkManager.cs:1112`) inserts owned tasks before
unowned/allowed tasks as it scans. Allowed tasks followed by owned tasks cause
repeated shifting: worst O(T²). All-owned tasks append at the end and do not
have that shifting pattern. Actual native method measurements with a half
allowed, then half owned fixture:

| Tasks | Native collector | Stable two-list partition prototype |
| ---: | ---: | ---: |
| 1,000 | 0.106 ms | 0.021 ms |
| 4,000 | 1.108 ms | 0.081 ms |
| 16,000 | 19.245 ms | 0.439 ms |

Only the Unity time-scale getter is stubbed in this test. Reservations are
empty, all tasks are old enough, and real Task2.GetOwnership runs. The test
checks native output against the stable partition, but the prototype is not
a complete production replacement with reservation/time filtering.

`ClaimTaskDeferred` also checks task membership with IndexOf and calls
`DemoteTask` (`WorkManager.cs:1034`), which removes then appends each checked
task. One pass over all T tasks can cost O(T²). A native full demotion cycle
took 0.336 ms at 1,000 tasks and 78.92 ms at 16,000. This benchmark deliberately
executes an entire cycle synchronously; the actual claim code yields and limits
checks, so those numbers must not be presented as one normal game-frame hitch.
Any replacement must preserve mutable task order and reservation ownership.

Altwork wraps native CollectTasks after collection, so it inherits those costs.
Our own ranking computes costs once per task and sorts O(T log T). Its route
planner uses a heap/hash collections, and its supply preparation is capped at
30 candidates and 2,048 source tasks. Repeated supply-list checks are bounded
O(window × tasks), not an unbounded quadratic growth in work count.

## Secondary findings and exclusions

- `GUIZones.DeleteTilesFromZone` (`Ostranauts.ShipGUIs/GUIZones.cs:422–445`)
  searches all ship tiles by Index once for each zone tile: O(zone × hull).
  Reachable on zone deletion/editing; not a continuous simulation tick. Build
  an index once rather than assuming list position always equals Tile.Index.
- `CODicts.AddCO` uses List.Contains within each item-type bucket; bulk adding
  many floors/conduits of the same type is quadratic in that bucket's size.
  `Ship.AddCO` calls it. Worth measuring, but no native timing in this audit.
- `BarterZoneShip.RemoveFromLocalInstance` linearly finds and copies the saved
  array per removed item. Bulk remote sales of nested contents amplify this.
  Unlike the first finding, it mutates the inventory: batch at a real operation
  boundary, preserving item-removal ordering and notifications.
- Our `NavigationFuel.Controller` and `HaulerRecovery.Controller` scan all AI
  controllers. Callers include target changes, haul/anchor command stepping,
  recovery and pruning. Repeated command checks can cost O(haulers × all AI)
  per sweep; no timing establishes this as a material cost. The idle RunAI hook
  already receives its controller directly. Defer a persistent index until
  measurement justifies the lifetime/invalidation complexity.
- `Ship.CheckCOIDProblem(CondOwner)` does a linear value search but has no
  callers found in this assembly. Do not claim it causes runtime hitches.
- Main room flood-fill discovery already uses a HashSet; ordinary GetCOs
  traversal starts recursion only at root COs. Neither warrants labeling as
  a quadratic defect simply because inventories are nested.
- Projectile/ship collision loops are an expected broad-phase candidate cost,
  not proof of erroneous duplicate work. Spatial indexing is a separate,
  higher-risk engineering project.
- Startup transpiler ToList/ToArray calls and explicitly requested inspection
  snapshots are not continuous game-loop costs. Existing fixed save-ship lookup
  and barter initialization scans are excluded from new findings.

## Reproduction and next steps

`Tests/BigOAudit` runs native methods under the existing bundled-Mono host and
labels isolated algorithm models explicitly. All timings are median of three
measured runs after warm-up on synthetic fixtures; none predict whole-game FPS.
`Tools/BigOAudit` reads a supplied save ZIP and reports layout bookkeeping counts
without extracting or modifying the save. Machine-specific input/output paths
remain outside tracked source.

Recommended order: remote barter write-back; known-index loader removals;
full-save remaining-ID bookkeeping. Then investigate power graph behavior with
offline fixtures before changing it. These are opportunities to remove work,
not just distribute more unnecessary work across frames.
