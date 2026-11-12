# Multiple-ship remote barter audit

Investigation and implementation against the shipped 1.0.1.10 assembly, November
2026. The repairs below are implemented in the local build. No running game or
saved inventory was modified by the offline validation. Reproductions live in
[`Tests/MultiShipBarterAudit`](../Tests/MultiShipBarterAudit/README.md).

## How the native mechanism works

`Ship.GetOwnedDockedShips` finds eligible ships connected to the trading location,
including owned ships reached through other station areas. This is remote
delivery within the native docking/access rules, not delivery to arbitrary ships
elsewhere in the solar system.

`Ostranauts.Ships.AsyncShipLoader.LoadDockedBarterZoneShips` creates offscreen
`BarterZoneShip` instances for shallow ships with zones. Each has a cloned layout
and materialized items; the original system ship retains the authoritative saved
layout. The loader caches copies by ship registration ID and exposes them after
`FullyLoaded` becomes true.

`Ostranauts.ShipGUIs.Trade.GUITradeBase.GetZones` gathers eligible zone/ship pairs.
The map uses `JsonZone` objects as keys, so equal names on different hulls do not
collide here. Purchase delivery filters zones by the selected ship's registration
ID. Sell-list gathering deduplicates overlapping-zone contents by item ID.
Transactions finish by calling `SaveAsyncShips<BarterZoneShip>`, which writes
each ready copy back to its corresponding original ship.

Assets inspected: `conditions_simple/conditions_simple.json` (`IsZoneBarter`),
`condtrigs/condtrigs.json` (`TIsBarter`, `TIsLootSpawnOK`, `TIsDataItem`,
`TIsInfiniteContainer`), and barter zones in `ships/HeavyTug02.json` and
`ships/OKLG Mescaform.json`. Zones carry their own `strRegID`, tile coordinates
and person specifications. The assets define eligibility; they do not require
zone names to be globally unique or define the async loader/writeback lifecycle.
The defects below belong to C# mechanisms, not trading policy in these assets.

## 1. Equal zone names hide one ship's sale inventory

**Reproduced by executing native `GUITrade.BuildSellList`.** Two valid zone rows,
`SHIP-A / Barter` and `SHIP-B / Barter`, create only one displayed row. The second
produces `Skipping duplicate key: Barter`. Changing only the second zone's name
shows both rows.

The row registry `_dictTradeRows` keys ordinary items by item ID but zone rows
only by `JsonZone.strName`. Ship identity is discarded after correct zone
discovery. The second zone's contents are not merged into the first row.
`GUIFactionTrade` inherits this row-building mechanism too.

Proposed repair: give each zone row a key that includes its ship and zone
identity, with no collision with item keys. Preserve displayed names, saved zone
names, purchase routing and native access checks. Renaming player zones is a
workaround, not a cause-level fix.

Requested presentation: sort zone groups by ship callsign, then zone name, and
display headers such as `O-GXD / Cargo`. Keep personal inventory in its existing
section. Display prefixes must not mutate saved `JsonZone.strName`; internal row
identity must be corrected independently of the visible label.

## 2. Unloading one async ship cancels every ship's loading job

**Reproduced by executing native `AsyncShipLoader.Unload(string)`.** With A and B
loading, `Unload(A)` stops both coroutines, clears the entire coroutine list, but
removes only A from `_loadStateDict`. B remains registered and unfinished.
`IsShipLoading(B)` stays true, `GetShip(B)` fails, and `LoadBarterZoneShip` refuses
to restart B because its registration ID is already cached.

This needs an overlapping load and targeted unload. Targeted unloads occur in
ship previews, broker sales and ship-rating operations; ordinary full unloads
intentionally cancel everything. The result can be persistently unavailable
barter contents until the loader is globally reset. It is a session lifecycle
failure, not proof that the underlying saved cargo has been deleted.

Ordinary buy/sell handlers and trade-window closing do not call `Unload`. This
does not require concurrent broker and barter windows: copies start loading at
save-load completion or after station transit, before any trade window opens.
Closing a broker preview, selling a ship or running/closing ship rating can
cancel those still-pending jobs; a later trade window then sees the stranded
copy. The cancellation defect is reproduced, but this entire player UI sequence
has not been run end to end.

Proposed repair: associate loading handles with their owning async records.
Targeted unload cancels only that record's job; global unload still cancels all.
Handle normal completion and cancellation without retaining discarded ships.
Our cooperative barter initialization optimization does not repair cancellation
ownership and can make the overlap window longer.

## 3. Reimporting an already marked item breaks writeback

**Reproduced through native purchase, sale, repurchase and unload methods.**
`BarterZoneShip.SaveChangedCOs` adds a new item's `AsyncItem` GUI property map with
`Dictionary.Add`. That marker remains on the runtime item after a successful
commit. Native full-ship materialization removes it in `Ship.GetItems` and its
contained-item restoration path; ordinary item transfers do not.

The initial small fixture modeled physical transfer. Follow-up validation now
executes `GUITrade.OnBuy -> OnSell -> OnBuy` on the same item, including actual
`BuyItems`, `TileUtils.DropCOsNearby`, `TryFitItem`, `PopHeadFromStack`,
`RemoveFromCurrentHome`, ordinary/specialized ship addition/removal,
`RecordTransaction`, and `SaveAsyncShips`/`SaveChangedCOs`. No fixture directly
moves the item or edits its owning ship during these transactions.

The fixture uses `ItmAlarmO2OffLoose` and its starting conditions from the shipped
CO definition. Native triggers loaded through `CondTrigger.CloneFromJSON` verify
that both the Fixer's high-value buy filter and its sell filter accept it. This
buy mode is a legitimate choice in `loot/loot.json: TraderCTsBuyFixer`.

Reproduction:

1. Buy the loose item into remote ship A. Payment, transfer and writeback succeed;
   the runtime item now has `AsyncItem` metadata.
2. Sell it back into the trader's barter zone. Native removal clears A's runtime
   and saved layout entry, the same CO moves to the trader, and payment succeeds.
   The marker survives.
3. Buy that item into remote B, or into A again. Native placement transfers it and
   payment charges the player. Writeback subtracts `SPAWNOFFSET`, throws
   `ArgumentException` on duplicate marker insertion, and never commits the
   destination layout or restores the coordinates.
4. Call native `AsyncShipLoader.Unload`, as station transit does. The item is
   unregistered and its temporary copy discarded, with no recovery writeback.
   The destination's original JSON still lacks it.

Controls remove only the marker before the sale, representing a cleared marker
state. All transfers, payments, destination commits and unloads then succeed.
Full `Ship.GetItems` materialization is source-traced, not executed by the control.

The existing `Optimizations/BarterWritebackPatch` reproduces the same failure:
its performance rewrite deliberately preserved native exception behavior.

This requires the same CO to survive trading without intervening full ship
materialization or a stacking replacement. It does not require two ships:
repurchasing into the original remote ship also fails. Payments demonstrably
remain applied after the exception. `Ostranauts.Core.LoadManager.SaveGameData`
gathers layouts/inventory from the original system ships rather than these
offscreen copies; there is no general save-time `SaveAsyncShips` recovery call.
This is an item-loss path, not just a cosmetic exception. It does not establish
that any particular item in the current player's save has already been lost.

Proposed repair: make marker insertion idempotent and restore temporary
coordinates in `finally`, including serialization failures. Update both the
native path and our optional optimized replacement so custom builds receive
consistent behavior. Do not strip unrelated GUI maps or bypass native transfers.

## Additional leads, not established normal-game failures

- `GUITrade.OnSell`'s fallback item search breaks after the first ready remote
  ship even when that ship does not contain the item. This is incorrect search
  logic, but normal items are also globally registered, so reachability needs
  proof before calling it a routine sale failure.
- Preview, rating and barter copies share a registration-ID cache without a
  requested-copy-type check. The consumer/saveback contracts differ. Audit UI
  sequencing before asserting a normal reachable wrong-copy transaction.
- Zone tile queries filter by owning ship after a fixed 25-hit physics query.
  Dense/overlapping colliders could exhaust that buffer before filtering. All
  copies use the same offset, but their saved layouts need not overlap; shared
  offsets alone do not prove cross-ship interference.

Existing item-removal dispatch, mass bookkeeping and async-destruction identity
repairs address different defects. They do not repair the three failures above.

## Validation

The offline harness executes native methods under the shipped Mono runtime.
274 assertions pass, including the original failure reproductions, corrected
zone row/header behavior, scoped cancellation and twelve native buy/sell/rebuy
sequences. These cover same/different destinations and retained/cleared markers
before repair, with the repaired native path, and with optimized writeback.
Each sequence also executes native unload. Serialization fault injection covers
both serializers, exact coordinate restoration, unchanged existing marker
payloads, failed layout commits and successful retries.

Cancellation checks include unknown IDs, surviving jobs reaching readiness,
global unload, cleanup, native barter/preview/rating iterators, and our optional
paced barter initialization iterator. Existing regressions pass: 41,093 inventory
performance/equivalence checks, 16,829 barter initialization checks, and 105
saved barter/container mass checks with the source fixture hash unchanged.
Full plugin binding: 205 patch classes,
zero failures or warnings. Unity engine leaves are substituted explicitly;
see the test README for boundaries.

## Implemented repairs and costs

- **QoL:** `QoL/BarterZones/BarterZonePatches.cs` implements callsign headers,
  ordering and distinct internal keys. It changes display construction only.
- **Bugfix:** `AsyncShipLoadingPatch.cs` records coroutine ownership at the three
  native start sites. The native unload body still decides which copy to
  destroy; its cancellation/clear calls now affect only that copy's jobs.
  A global unload preserves native cancel-all behavior. Ownership is weakly
  attached to the loader and stores handles/registration strings, not ship or CO
  references; entries are removed on targeted/global unload. Iterator-field
  metadata is cached by type. No per-frame work or saved data is added.
- **Bugfix:** `BarterWritebackSafetyPatch.cs` replaces only the native per-new-item
  serialization block with `BarterWritebackSafety.Append`, preserving the loop,
  membership checks and final JSON commit. The optimized writer uses the same
  helper. Marker insertion is idempotent, existing payloads are preserved, and
  exact coordinates are restored in `finally`. Exceptions continue to propagate.
  No ship/item references survive the operation; existing markers avoid another
  dictionary allocation. This prevents the reproduced loss rather than trying
  to reconstruct cargo after a failed trade.

Native IL substitutions are guarded against unexpected method shapes. The
optional optimized writeback has an explicit custom-builder dependency on the
writeback repair. The three features are otherwise independently selectable.
All repairs are applied by the normal plugin's existing Harmony lifecycle;
there is no new Update method or custom save schema.

Custom-builder validation: 391 self-test checks; generated and finalized QoL-only
(two patch classes), bugfix-only (three), and optimized combined (seven) plugins.
All three bind with zero failures/warnings and zero frame callbacks. The normal
Debug build remains the full plugin; no automatic deployment was performed.
