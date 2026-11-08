# Economic activity during Super Fast Forward

Part of `Ostrabus.Orbital`. Requires Orbital and its SFF correction to be enabled.
`[Orbital] SffEconomy = true` is the launch default for this subfeature. Disabling
it retains native economic scheduling while leaving the other orbital fixes intact.

## Defects

`GUIFFWD.FFWD` calls `StarSystem.Update` once for the whole skip. The world advances
its epoch and ships, calls `AIShipManager.FFWD`, then `AIShipManager.Update`, then
`MarketManager.Run`. The trade check schedules `current epoch + 7200`, losing
intermediate dispatch opportunities. Production catches up multiple cycles, but
only after delivery and dispatch: newly delivered inputs can feed earlier cycles,
and dispatch can miss stock that should already have been produced.

Conservation testing also reproduced a native loading error in
`MarketManager.RegisterAICargoHauler`: when a pod accepts the entire list, the
method breaks out of the loading loop but retains that list as its remainder.
It then adds the consignment back to the origin. `CargoLoadingPatch` clears the
accepted input list after `TryAddStock` has cloned its entries. Overflow handling
retains the native list and count. This small correction also applies outside SFF.

## Timeline

The world-update prefix prepares a finite event queue from native producer clocks,
the saved next-trade-check clock and eligible freight. Catch-up executes just before
native AI FFWD, after the world's physical update reaches the final epoch.

- Production executes native `Producer.Run` at recipe intervals, retaining fractional
  time and advancing failed cycles without banking work during input starvation.
- Each due two-hour dispatch opportunity uses native market pairing, profitability,
  randomness, cargo model selection, ship creation and actual inventory loading.
- The duplicated first DTO returned in a bundled trade route is considered once.
- Native `FlyToPath.PlanTrip4` receives explicit departure-time points and loaded
  cargo mass. Rejected plans return the manifest before retiring the new hull.
- Eligible existing and new freight follows its native course. Completed departure
  or obstacle legs can continue through native historical-time planning inside the
  same skip, rather than treating every endpoint as final approach. Once nearby,
  bounded local SFF navigation handles the final approach. Arrival requires
  distance/speed inside the native docking threshold; expiration alone is not delivery.
- Arrival transfers inventory through `UnregisterCargoShip`. At equal timestamps,
  arrivals precede production, which precedes dispatch. Physical crew/hull retirement
  uses native `DockAndDespawn` at the end of the skip.
- Unfinished shipments retain ordinary market manifests and native NavData (when
  the plotted portion remains), or resume native final-approach AI. No custom save
  records or persistent shipment registry are introduced.

Native overlapping production, trade checks and managed freight FFWD are suppressed
only within that world update. Loaded trader synchronization is coalesced until the
timeline finishes. The finalizer clears all ship/market references. Inspection keeps
bounded scalar reports, not live game objects.

Eligibility requires an actual cargo market, a shallow visible AI freighter, an
isolated hull, a station destination, and no combat target or active reaction. Only
idle, FlyTo and DockCargoAndDespawn commands qualify. PASS passenger transports share
the cargo pilot class but lack cargo markets and are excluded. Player ships, loaded
ships, combatants, docked groups and unrelated AI retain their native paths. Flights
which cannot be safely prepared remain native; their eventual end-of-skip deliveries
do not feed the timeline's earlier production.

## Historical planning without rewinding the world

`FreightPlanningScope` supplies a departure clock to four native planner/locking
methods. Outside that synchronous scope the clock getter returns the real epoch.
The global `StarSystem.fEpoch` is never assigned by this feature. Station coordinates
are temporarily projected for native nearest-station queries, then restored exactly;
no-wake queries use copies. Body caches are restored to the real epoch in cleanup.
Only newly spawned freighter hulls receive historical departure coordinates. Existing
player course objects and waypoints are never passed to this adapter.

## Bounds and observability

There is no ordinary-frame economic scan. A binary-heap queue node is reused for each recurring
producer; there is no allocation per scheduled cycle, although native production and
inventory methods retain their own allocations. Skips over 24 hours or estimates
over 250,000 production cycles fall back before changes. Ordinary game SFF is at
most six hours. Errors retain already applied economic events rather than replaying
stock transfers. A failed departure returns its cargo and subsequent events continue.

`GET /orbital` exposes `sffEconomyEnabled` and `lastEconomy`: dispatch opportunities,
departures, deliveries, production attempts/successes, rejected flights, errors,
work time and up to 128 departure/arrival records including cargo category counts.
These reports are also included in flight-journal orbital snapshots.

## Offline verification

```powershell
dotnet build Tests/OrbitalCheck -c Release
dotnet run --project Tests/SaveJsonCompatibility/Host -c Release --no-build -- Tests/OrbitalCheck/bin/Release/netstandard2.1/OrbitalCheck.dll economy
```

Tests use the installed game assembly and its bundled Mono runtime. They exercise
actual market pairing, loading, delivery, production and native market save/reload;
Unity ship construction/retirement and route generation use deterministic fixtures.
The native planner clock/LockToBO adapter and restoration run against real methods.
Coverage includes the original loading duplication, full/partial/zero pod acceptance,
six hours versus two three-hour skips, chronological input availability, partial
flight, rejected/throwing plans, duplicate suppression, disabled scope, queue bounds
and cleanup. These checks do not claim a full Unity ship-factory or live route test.

Flight-stage regression cases cover departure-to-torch continuation, partial
travel, several intermediate legs, carried fuel budgets, expired and absent
courses, and null/throwing/non-advancing continuations. Continuation uses detached
origin motion, never temporary live-hull relocation or a global-clock rewind.
It permits at most eight new plans per freight preparation and uses the native
5,000 km torch-planning cutoff to distinguish remaining long travel from local
approach. See [departure waypoint diagnosis](../../Navigation/DEPARTURE-WAYPOINTS.md)
for the separate native reference-frame correction and its controls.

Validation: 1,443 economic/inspection checks and 4,850 existing orbital checks pass under the
bundled Mono runtime. A separate .NET 8 run processes one million queue events with
zero scheduler allocations after setup; this excludes native economic work and uses
a real allocation counter rather than the bundled Mono counter. All 132 startup
Harmony patch classes bind with no failures or warnings.

The read-only [economic inspection API](../../Inspection/ECONOMY.md) exposes live
market stocks, recipe bottlenecks, cargo manifests, raw trade eligibility and hubs.

## Live validation (November 7, 2026)

The deployed DLL hash matched the Debug build. A three-hour skip in HQCH processed
two due dispatch opportunities, preserving the existing two-hour schedule exactly.
It completed 3,431 production attempts (467 successful), two departures and one
delivery with zero errors/rejected flights. Preparation took 5.259 ms and catch-up
90.8623 ms in this run; these are observations, not general performance guarantees.

VENC freighter O-YE2 delivered 177 HVAC and 26 luxury goods to VORB. HVAC stock rose
by exactly 177; luxury stock rose by 23, consistent with three normal consumer cycles.
Its cargo market was removed and AI registration cleared. BCER freighter O-H161
remained registered on its native MLAB course with all 110 media goods aboard. The
pre-existing MVOL-to-MHNG torch freighter also retained its route. The orbital endpoint
serialized the real cargo report successfully; logs contained no new errors.

All economic endpoint families were exercised, including pagination, raw manifests,
station-related freight and commodity pairing. Warm overview capture took 0.0401 ms.
VORB remained Metal-starved: this test restored dispatch activity without altering
native trade preferences or guaranteeing that a particular shortage is serviced.

Separate follow-up: before this skip, 97 registered market entries referred to no
current hull; 95 had cached stock and none had recipes. These pre-existing records
were exposed by the new API, not created by this skip. Their cleanup and economic
consequences need a separate lifecycle investigation; validation did not prune them.
See [orphaned market follow-up](ORPHANED-MARKETS.md) for evidence and investigation scope.
