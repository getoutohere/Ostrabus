# Cargo-pod registry reconciliation

`cargopodfix` repairs existing player-ship cargo registries. It complements
[preview market isolation](PREVIEW-MARKET-ISOLATION.md), which prevents new
phantom pod registrations. No repair runs automatically.

```
cargopodfix                         # Report all player-owned ships
cargopodfix dryrun O-GXD            # Report one owned ship
cargopodfix apply                   # Apply verified repairs
cargopodfix apply O-GXD             # Apply to one owned ship
```

The optional call sign and mode can appear in either order. Close trade/cargo
windows first; loading and ship-edit states are refused.

## Evidence and repair

The command compares installed physical pods with both per-ship market mappings
(`Ship.MarketConfigs` and `JsonShip.aMarketConfigs`) and the global native
`ShipMarket` actor configurations. Fully loaded ships use their live inventory;
shallow ships use saved items, current CO saves, condition overrides and native
item/GPM definitions. It never materializes a shallow ship. If an authoritative
offscreen barter copy exists, it must be fully loaded and agree on installed pod
identities; its runtime and saved mappings are synchronized as well.

The installed cargo-pod assets already define `IsCargoPod`, `IsMarketActor`,
`IsInstalled` and `Trader/CargoPodSpec`. These remain authoritative. This command
repairs registry mismatches; it adds no alternative cargo policy or item types.

- Remove proven absent, empty cargo-pod entries through native
  `MarketManager.RemoveMarketActorFromShip`, then remove stale ship mappings.
- Register a physically installed pod only if its global config is missing,
  using the physical pod's existing empty cargo template and native
  `MarketManager.AddMarketActorToShip`.
- Correct missing/stale mappings without re-registering existing configs. Native
  add would overwrite their stock, so it must not be used for this case.
- Preserve and report orphaned cargo, loose physical pods, loaded actors,
  duplicate identities, foreign ownership/reference conflicts, mismatched market
  templates and unknown/nonempty metadata. Cargo is never moved or credited by
  this command. Ambiguous/incomplete ship inventories are skipped.

A candidate-only cross-ship index also protects physical items and references on
other ships. Its data and all transaction snapshots are request-local.

## Transaction boundary

Ownership/canonical hull identity is checked before applying each ship's plan.
Physical pods, offscreen-copy identity, global market identity, individual config
references and orphan emptiness are rechecked before mutation. The command runs
synchronously on the native console/main-thread path without yielding.

Native removal/registration rebuild market aggregates and can unregister an
empty ship market. A per-ship rollback retains the original market, actor objects,
mapping dictionaries, aggregate dictionaries/lists and cached mass. A failure
restores those same objects and contents, including a market removed/recreated
during the transaction. Successful repairs on earlier ships remain committed.
No save file is written by the command; ordinary native saving persists repairs.

## Cost and validation

One console-dispatch hook; no frame hook, ongoing collection, history or new save
schema. Scans and allocations occur only when requested. Removing the mod does
not invalidate repaired saves.

The `repair` mode of [CargoPodDiagnosis](../Tests/CargoPodDiagnosis/README.md)
passes 100 assertions, executing production command code against the shipped
assembly in bundled Mono.
It covers dry runs, the four-real/eight-registered-pod case, missing registrations,
mapping-only drift, paid-for cargo preservation, shallow/remote inventories,
ownership scope, stale evidence, native serialization/reload, idempotency and
injected native cache failures (including market removal/recreation). Unity
equality, singleton/scene state and blockade lookup are fixture boundaries; the
native market operations and production planning/repair code execute unchanged.
