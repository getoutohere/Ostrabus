# Explicit freight recovery APIs

Developer tools, not automatic simulation policy. Both operations use the existing
Unity-thread request pump. GET previews; POST revalidates and applies to one exact
call sign. An external caller can process selected ships sequentially. No background
scan, trace toggle, ledger, new Harmony hook or custom saved type is installed.

## Bring a live freighter to its destination

```
GET  /economy/freight/{id}/approach
POST /economy/freight/{id}/approach?confirm=APPROACH
```

Optional `/{stationId}` after `approach` must equal the ship's current station
target. The endpoint does not select routes or redirect an existing shipment.

Requires one unambiguous native HaulerCargoPilot, a visible independent shallow
AI hull in free flight, existing living non-player crew with a conscious pilot
candidate, no combat/reaction/overriding queue, a normal FlyTo/DockCargoAndDespawn
step, an available destination station/market and room for the complete manifest.
Player/plot ships, materialized hulls and docking/towing groups are excluded.
A passenger transport using the same pilot class is excluded by its missing cargo
market/pods. Unrecognized native behavior-tree layouts are refused.

The POST stops old maneuvers, clears NavData and TripFuelPrediction, rebinds the
canonical current station target and clears the old AI pause. Native
FlyTo.ResolveInstantly supplies the travel completion and matching station velocity.
The ship is staged 10 km from the station along its previous approach direction:
native Size-based instant placement can hug an inaccurate station radius or a
station-attached hull, so it is not used as the final position. This remains inside
the native cargo command's 15 km/100 m/s arrival gate.

The actual HaulerCargoPilot constructor tree is inspected for the sticky sequence
whose FlyTo is followed by DockCargoAndDespawn. Its existing SaveData checkpoint
advances to that actual docking child, and the controller's active-command reference
matches it. No private fixed child index or parallel behavior tree is invented.
Native threat/priority evaluation remains authoritative on subsequent AI ticks.

No docking, unloading, crew transfer or despawn occurs in the API call. The normal
AI runs its own DockCargoAndDespawn afterward. Hull, controller, crew, faction,
manifest and fuel remain in place until native arrival. Repeating an already-ready
arrival is refused. The destination's capacity is checked at the request, not reserved
against other deliveries while time advances.

### Save boundary

A payloadless DockCargoAndDespawn queued through AddCommand is omitted by native
BaseCommand.GetSaveData. Do not use such a queue as the durable pending arrival.
The authored sticky-sequence checkpoint already serializes, restores and executes
in vanilla, including with the mod removed. This API introduces no custom save
fields or load-time recovery requirement.

## Credit cargo whose hull is already gone

```
GET  /economy/freight/{id}/recover/{stationId}
POST /economy/freight/{id}/recover/{stationId}?confirm=RECOVER
```

The destination is mandatory. RegisterAICargoHauler does not persist the route's
DestinationStation in its cargo market/pod records; home/origin/hub metadata is
not proof of the intended receiver. The caller must establish the historical
destination or explicitly choose a recovery destination. The preview labels this
as an explicit caller choice. Nothing automatically distributes the 123 unresolved
market records found in the November 12 survey.

Requires an absent hull, a matching isolated cargo-pod market without production
or loaded traders, valid item definitions/category labels, consistent stock/capacity
caches and a real destination station/market that can accept every unit. A surviving
hull is refused even if hidden, uncrewed or damaged: use normal simulation or the
appropriate ship recovery instead. Player ownership and active plot protection
remain in force. Shared pod/config/affected-list aliases and malformed data are
refused. Normal request bounds include 4,096 source units, 1,024 actors per market,
bounded metadata/alias scans and at most 32 stale controllers for cleanup.

### Conserved transfer and cleanup

Blind UnregisterCargoShip removes the source first and does not report rejected
stock. Its public ShipMarket batch-adder can also re-present an untrimmed input
list to another actor after partial acceptance. Recovery uses native per-actor
TryAddStock instead, trimming the remainder between stores.

Changes are first staged in detached market/config copies. Only affected stock
lists and mutable market caches are copied; producer definitions/clocks remain
untouched. Native TryAddStock preserves concrete item types through native clones;
BuildConfig calculates the resulting stock/capacity/prices. Conservation is checked
before committing to the existing destination objects, preserving market, config
and existing list identity for station GUIs. Staging failures retain both markets.
Insufficient capacity refuses the entire operation; it neither drops overflow nor
expands station storage.

After credit, native MarketManager.UnregisterShip removes the old market. Stale
controllers for that absent hull are removed from native regional lists and both
scheduler queues through AIShipQueue.UnregisterShip. Native AIShipManager's ordinary
unregister path cannot safely handle an absent/repaired-to-null Ship getter, so
this narrow absent-hull operation removes the stale regional entries directly.
Residual hub memberships and ownership are also removed. It does not delete/revive
NPCs, erase old saves, invent a replacement hull or clear unrelated AI targets.

Native trader synchronization runs after commitment. If it throws, the response
still reports cargo credit committed, with traderSynchronizationError; the removed
source cannot be credited again. Inspect station inventory before trading in that
case. A repeated recovery of the same source returns 404 without adding goods.
Empty valid orphan cargo markets can be cleaned without creating stock.

## Offline validation

```powershell
dotnet build Tests/AIShipLifecycleCheck -c Release
dotnet run --project Tests/SaveJsonCompatibility/Host -c Release --no-build -- Tests/AIShipLifecycleCheck/bin/Release/netstandard2.1/AIShipLifecycleCheck.dll freight-recovery
```

46 bundled-Mono checks execute production endpoints against the shipped assembly.
They cover read-only previews, confirmation/destination handling, unsafe approaches,
native 10 km travel completion, checkpoint save/restore without mod-specific types,
subsequent native AI-tree delivery, unchanged fuel/crew, exact 633-item conservation
across two destination stores, concrete item identity/type distributions, cache and
GUI object identity, scheduler/hub/ownership cleanup, native cargo save/reload,
repeat protection, full destinations, invalid/shared manifests, isolated staging
failure, synchronization failure after commitment and empty orphan cleanup.

Fixtures replace reactor/Unity maneuver work, ship position advancement, pilot
materialization, threat/collision searches, trader synchronization and the final
CrewSim population handoff. Native FlyTo positioning, behavior-tree transitions,
DockCargoAndDespawn transfer, market actor stocking, cache construction, scheduler
cleanup and save/load mechanisms execute. This is not a full Unity docking voyage
or physical trader inventory test. No live ship movement or credit was performed
while implementing these endpoints.
