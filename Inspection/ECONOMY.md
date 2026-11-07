# Background economy inspection

All routes are GET-only, run on the existing Unity-thread inspection pump, and
return the usual `generation`, simulation `epoch`, capture time and `inspectionMs`
envelope. The detached result contains no game objects. No new Harmony hooks,
polling, history, save fields or ordinary-frame scans are installed.

## Routes

| Route | Purpose |
| --- | --- |
| `/economy` | Schema version, market count, initialization, production/dispatch clocks, ATC/test/pause state, SFF economic settings and last economic catch-up report. |
| `/economy/stations` | Registered markets whose hull has the analytical station-body flag. Includes damaged/hidden entries for diagnosis. |
| `/economy/markets` | Every registered market, including freight, player cargo and unresolved/orphaned entries. |
| `/economy/hubs` | Stored hub-to-regional-market mappings used by native trade redirection. |
| `/economy/markets/{id}` | Market identity, hull/lifecycle state, raw blockade-condition presence, actor/recipe/category counts and related links. `/stations/{id}` and `/freight/{id}` are detail aliases. |
| `/economy/markets/{id}/stock` | Category quantities/capacities, stored prices, native supply/demand eligibility and rejection reasons. |
| `/economy/markets/{id}/production` | Recipe inputs/outputs, shortages, output storage constraints, cycle rate/clocks and inferred current readiness. |
| `/economy/markets/{id}/actors` | Backing trader/pod configurations, owner/hub IDs and actual per-actor category counts. |
| `/economy/markets/{id}/manifest` | Actual virtual `MarketItem` entries, with owner actor, category, slot and concrete item type. |
| `/economy/markets/{id}/trade/{category}` | Per-counterpart import/export supply/demand pairing before hub redirection and stochastic/flight checks. |
| `/economy/freight` | Markets with cargo pods, paired with available raw AI registration, home/origin/destination, course endpoint and stored fuel/range fields. |
| `/economy/stations/{id}/freight` | Related pod-equipped markets: currently targeting this station, or matching its pilot home/hull origin. Fields identify which match applied. |

The stock, production, actors, manifest, trade and related-freight views also accept
`/economy/stations/{id}/...` and `/economy/freight/{id}/...`. Station-prefixed paths
validate the station-body flag; market paths remain available for nonstation cargo.

Lists accept `offset` and `limit` (default 25, maximum 50). Economy offsets support
up to 1,048,576 so large freight manifests remain accessible. Follow `nextOffset`
until null; a filtered collection may return an empty page with an advancing cursor.
Offsets count enumerated entries in live order, not a durable ID sequence. Separate
requests are not an atomic census; compare envelope epochs/generations.

## Example: why is VORB not producing industrial products?

```powershell
Invoke-RestMethod 'http://127.0.0.1:18764/economy'
Invoke-RestMethod 'http://127.0.0.1:18764/economy/stations/VORB/production'
Invoke-RestMethod 'http://127.0.0.1:18764/economy/stations/VORB/stock'
Invoke-RestMethod 'http://127.0.0.1:18764/economy/stations/VORB/trade/AnyMetal'
Invoke-RestMethod 'http://127.0.0.1:18764/economy/stations/VORB/freight'
```

Use the exact category IDs from stock/production rows, not translated item names.
For an inbound freighter, `/economy/freight/{id}/stock` gives cached category totals;
`/manifest` gives its individual virtual cargo items. `/actors` and `/manifest` can
reveal disagreement with cached aggregate stock without repairing or hiding it.
These items are economic inventory, not necessarily physical COs in a cargo room.

## Interpretation

- Production attempts consume the input quantities and emit `outputPerCycle`.
  `configuredRatePerHour` is the signed native configuration; the interval follows
  its absolute value. `missingInputs`, `outputFull`, clocks and `status` are separate
  so a recipe may be both waiting for a time slot and lacking supplies.
- An uninitialized `LastUpdateTime == 0` stays uninitialized. Inspection does not
  call `Producer.IsReady`, which would write a timestamp.
- Prices and aggregate quantities are cached native values. Missing cached prices
  use the native fallback multiplier of 1 and report `priceCached: false`. Unit
  price/mass fields are null if their lazy caches have not been calculated; queries
  deliberately do not calculate them.
- Supply requires a production output, the native stock-fraction gate and a price
  multiplier below 1. Demand requires a production input, its stock-fraction gate
  and a multiplier above 1. Float arithmetic matches the game, including rounding
  at the apparent 20% boundary. Eligibility may still have zero transferable amount
  when storage is unconfigured; quantities are reported without normalization.
- Trade rows are *candidate pairings*, not selected or promised routes. They do not
  execute `GetTradeRoute` or its blockade/random rolls, apply hub redirection, choose
  a cargo model, or calculate flight feasibility. Use `/hubs` to inspect the stored
  redirection topology. Native selection can legitimately dispatch nothing.
- A pod-equipped market is not automatically an active NPC freighter. Player cargo,
  parked hulls, stale registrations and other non-NPC cases remain visible and
  distinguishable through lifecycle and `flight` fields. PASS passenger transports
  without cargo markets are not miscounted as freight.
- `planArrivalEpoch` is a course endpoint, **not a delivery ETA**. Torch courses can
  end before final RCS approach/docking. Negative remaining time is preserved. Range
  and fuel use stored state; full/edit fuel caches may be stale. Follow `aiDetails`
  for existing detailed navigation inspection.
- A home/origin match in a station's freight view does not imply a ship is currently
  outbound. `incoming` specifically means its current scan destination is the station.

## Bounds and side effects

Pages share the existing cooperative 1 ms main-thread budget; one bounded row is
allowed for forward progress. Recipe inputs are limited to 32 per row, commodity
role inference to 128 recipes/32 inputs, actor stock summaries and hub members to
64, pod discovery to 512 actors and AI indexing to 4,096 entries. Truncation and
incomplete inference are explicit. Incomplete role inference yields null native
eligibility; an incomplete AI index yields unknown missing registrations. Budget
exhaustion while skipping to an offset returns 503 rather than a stuck cursor.
An individual row/JIT/GC can exceed the cooperative budget.

No call creates the economy singleton, initializes producer clocks, consumes RNG,
rebuilds caches, advances positions, evaluates triggers, loads ships or transfers
stock. Snapshots allocate only when requested; they are not retained by this API.
Schema version 1, native IDs, explicit units and the separation of raw state from
inferred eligibility provide a basis for future economic simulation work without
coupling inspection to mutations.

Offline checks use the game's assembly and bundled Mono. They compare supply-gate
rounding with native behavior, inspect shortage and freight scenarios, check backing
versus cached inventories, pagination/bounds, detached item data and missing-singleton
handling. Guards reject calls to production, route selection, lazy price/mass helpers,
position advancement, trader synchronization and the repairing AI ship getter.
The combined economic/inspection suite passes 1,412 checks; the existing 4,850 orbital
checks also remain available. No live game reload is needed to run these checks.
