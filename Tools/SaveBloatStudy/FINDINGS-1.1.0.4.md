# Initial save retention findings

November 20, 2026. Native serialization/lifecycle source audited against
Ostranauts 1.1.0.4. This is a diagnostic study, not a cleanup implementation.

## Evidence

Two existing archives from the same character were read without modification:
the older `next` backup and the recent `autosave_3_fine`. Their epochs are about
7.15 game days apart. Reports retain SHA-256 identities and verify the source
hash again after each read. Private detailed reports, actor IDs and source paths
remain under ignored `obj/SaveBloatStudy-20261120/`.

The native root `strVersion` is this playthrough's original save version; it is
not evidence that the most recent save was written by that old executable.

| Measurement | Older backup | Recent save |
|---|---:|---:|
| Archive size, MiB | 108.69 | 123.50 |
| Total uncompressed entry bytes, MiB | 1,395.13 | 1,540.35 |
| Physical hull entries | 275 | 337 |
| Deferred world objects | 237 | 212 |
| Saved object-state records | 519,098 | 568,645 |
| Character-like state records | 842 | 880 |
| Dead character-like records | 90 | 92 |
| Saved interaction-history entries | 2,656,917 | 2,783,831 |
| `OKLG_UNK` hull JSON, uncompressed MiB | 189.77 | 177.79 |

Character-like records have social state or a saved body type; this includes
robots. Physical hull entries include stations and regional references, not just
mobile ships. These are snapshots of an actively changing world, not a
controlled estimate of a particular patch's growth rate.

## Item state: broad retention is connected, but layout links can be stale

The recent archive has 568,645 unique object-state IDs and no duplicate IDs
across the scanned global/per-hull tables. Every state record has a saved layout,
crew/spawner, ship-CO, or `aStack`/`aLot` link. The first apparent detached group
was 1,001 loot-spawner records: these are anchored by `aShallowPSpecs`, which
native loading handles separately. They are not a deletion target.

This is a structural result, not proof that every retained object is usable.
The stricter parent check finds **21 items on `O-UBJ7` / NASS Patrol 40** whose
four parent IDs appear in neither any saved layout nor any object-state table:

- Two `SOCReminisce` and two `SOCShareAnotherStory` inventory objects.
- One `LiquidWater` object.
- Sixteen `ItmAmmo9mm` objects.

Their saved CO states remain in the global table and total **27,803 raw JSON
characters**, excluding their small layout records. The existing independent
`InventoryLoadAudit` reproduces all 21 as missing-parent dependency failures.
This is a concrete orphan candidate with negligible size impact. Its original
creation/removal event and any non-inventory references are not yet established;
deletion or forced relocation is not authorized by this result.

Relevant native mechanisms:

- `Ostranauts.Core.LoadManager.SaveGameData()` first trims cached state for
  missing hulls, then removes records not retained by saved hull item/state
  collections. This is not a complete transitive ownership/reachability test.
- `Ship.SaveCOs()` seeds its item dictionary from existing `JsonShip.aItems`
  before merging current objects. An old row can therefore keep state alive
  when its parent chain is no longer valid.
- `Ship.SpawnItems()` defers subitems until their parent resolves. Missing
  parents can leave saved data inaccessible without removing it.
- `Ship.InitShip()` and `Ship.SaveCOs()` consume/produce `aShallowPSpecs`; these
  spawner rows must be included in retention analysis.

The prior [saved-parent repair](../../MiscBugFixes/SAVED-INVENTORY-PARENTS.md)
addresses a different case: a damaged parent still exists and must be restored
before native destruction/spill handling. It does not establish what happened
to these four absent parents.

## Most decision history repeats authored baseline training

The recent save contains **2,783,831** condition/action history entries. Of
these, **2,711,053 (97.39%)** match current native training entries by condition,
interaction name, iteration count, single-precision total and absence of
target-specific score data. Those matching entries occupy about **125.5 million
compact JSON characters**. That number is not compressed ZIP space.

The cause is explicit in both source and assets:

1. `PersonSpec.MakeCondOwner()` clones the `Abner` or `Robot` personality's
   history into the new character.
2. The definitions come from `data/ai_training/ai_training.json` and `robot.json`.
3. `CondOwner.GetJSONSave()` serializes the history; `Ostranauts.Condowner.CondHistory.GetJson()`
   writes all named entries, including unchanged training entries.
4. `CondOwner.GetMove2()` and `GetNetInteractionResult()` use these
   records to choose and score actions. `JsonCondHistory.GetData()` reconstructs
   them on load.

Consequently this is repeated baseline state, **not unused memory that can be
blindly pruned**. Native loading expects complete histories. A future storage
optimization would have to preserve baseline version/values, learned changes,
zero-versus-missing semantics, and behavior across asset updates. This study does
not implement such a format or recommend silently substituting newer training
for an older character's history.

Dead characters alone retain about 12.5 million compact characters of decision
history. Whether those histories can be omitted without affecting any legitimate
revival, inspection or narrative path remains a separate audit question; death
does not authorize deleting the character, inventory or relationships.

## The hidden NPC holding station is not growing in this comparison

`OKLG_UNK` is a native authored hidden station on Ganymed, not an orphan registry.
The recent archive contains 360 character-like records there, 66 dead. Its raw
hull JSON shrank by about 12 MiB between these archives.

`AIShipManager.CrewCleanup()` routes certain displaced crews there;
`AIShipManager.HandleDeads()` transfers eligible dead station occupants there.
`StarSystem.GetPersons()` still searches eligible hulls according to authored
person/ship specifications. The world definition explicitly supplies this
station in `data/star_systems/star_system.json`.

This confirms intentional retention, not the long-term necessity of every byte
inside it. It does not justify clearing the station or killing/deleting hidden
NPCs. In this comparison there is no evidence of a rapidly growing corpse pile.

## Retired markets exist, but are a small footprint

The recent archive has **65 market buckets** whose IDs resolve to neither a
physical hull nor a deferred world object. Together these occupy approximately
146,401 compact JSON characters:

- **57 still contain stock entries.** They are unresolved economic state and
  possible recovery obligations, not disposable history.
- **Eight have no stock entries**, totaling just **2,571 compact characters**.
  They remain cleanup candidates requiring lifecycle/ID-reuse review, with very
  little size benefit.

Native `Ostranauts.Trading.MarketManager.LoadMarketData()` reloads each saved
bucket and `GetJSONSave()` serializes the registry again; this path does not
validate hull existence. `StarSystem.RemoveShip()` is not itself a market cleanup
operation. Existing Ostrabus `CargoMarketLifetime`/`CargoDelivery` handle audited
current destruction and delivery boundaries; old buckets alone do not prove
those repairs have regressed.

## Lower-impact collections

In the recent root JSON, active and historical plot records together occupy about
34,447 raw characters. The ledger is about 830,035 characters, and the entire
market save is about 1,072,440 characters. Ship images and other ship-associated
non-JSON entries occupy about 3.39 MiB uncompressed. These do not account for the
bulk of the save.

Native `CondOwner.LogMessage()` bounds ordinary character messages to 50;
`Ship.LogAdd()` bounds ordinary ship logs to 1,000. This study does not conflate those
collections with the separately patched comms-history lifecycle.

## Next investigations, in order

1. Trace the four absent inventory parents on O-UBJ7 through nearer saves and
   native removal paths; determine whether the proper repair restores/transfers
   the contents or removes genuinely obsolete records. Do not erase evidence
   before establishing that cause.
2. Audit dead characters' AI-only decision state separately from their identity,
   possessions, social memory and story relevance. Potential benefit exceeds the
   empty-market cleanup, but safety is not established yet.
3. Evaluate lossless baseline/delta storage as an independent optimization if
   save/load costs warrant a format change; this is not an orphan-cleanup fix.
4. Validate lifecycle cleanup for empty absent-hull market buckets and ensure
   future ID reuse cannot resurrect stale market configurations.

No save, game state, installed asset, or runtime plugin was modified. The new
study tool's fixture checks cover ownership roots, structural orphans, cycles,
source identity, JSON spans and training comparisons.
