# Freight lifecycle audit, 2026-11-14

The [November 18 implementation and native saved-voyage regressions](../../Navigation/FREIGHT-CELESTIAL-SAFETY.md)
add whole-course planetary clearance, normal/SFF/PASS final-leg consistency and
market cleanup for actual registered cargo-hull destruction. The historical
evidence below remains qualified; those repairs do not prove the cause of every
old shipless manifest and do not automatically credit lost cargo.

Status: confirmed accounting and handoff repairs implemented and offline tested.
Reviewed native 1.0.1.10 C#, the installed market/loot/interaction JSON, and our
loading and SFF economy paths. The preceding [loading repair](CARGO-LOADING-AUDIT.md)
is implemented alongside the delivery/retirement repairs described below.

The objective is accounted cargo and plausible native ship/crew disposition.
Do not replace that with periodic replenishment, forced delivery, blanket fuel
exemptions, deleting historical markets, or a second AI controller. Native fuel,
damage, combat, capacity and personnel rules remain separate concerns.

## Implemented repairs and preserved disposal policy

The user clarified that a hauler at a valid receiver **unloads what fits and dumps
the remainder**. Capacity overflow is intended disposal, not a defect to repair.
Full receivers do not make carriers wait, reroute or seek another buyer. The
earlier proposed overflow-retention change was abandoned before release.

- `MarketWithdrawalPatch` subtracts this actor's result, preserving preferred
  owner selection and native withdrawal/cache/price handling.
- `MarketBatchDepositPatch` forwards only the unaccepted tail. The caller's list
  stays intact. The integer/item overload is unchanged.
- `CargoHullSizingPatch` accumulates each consignment's native rounded pod count.
  Native regional/interregional pools, randomness and pod eligibility still apply.
- `CargoDelivery` validates the receiver and stages its stock before removing the
  carrier market. Native actor stocking retains concrete item definitions and
  capacity rules; accepted stock commits once, overflow is discarded, then the
  carrier market is removed. Receiver market/actor/list/price-map identities and
  carrier-discount pricing are preserved. Missing receivers, self-transfer and
  staging failures retain the source unchanged. A trader refresh failure after
  commit cannot trigger a duplicate transfer.
- `CargoArrivalPatch` gates both native cargo arrival methods on successful
  disposition. `CargoEscapeArrivalPatch` adds the missing handoff only to ordinary
  Dock's NPC station-retirement call for HaulerCargo pilots. Native clearance,
  player docking, nonstation docking, PASS and other pilot types retain their
  branches. Normal emergency arrival now matches native cargo FFWD's existing
  refuge-station unload behavior; it does not invent a different buyer or route.
- `SffEconomy` checks successful transfer before counting delivery or retiring the
  carrier. It excludes Flee/priority instructions from economic catch-up by actual
  command instance and saved escape checkpoint. Native escape handling remains
  authoritative. A rejected departure requests an all-or-nothing refund; if that
  fails, it preserves/registers the original new hull and load at the real spawn
  state with its origin as the native docking target.

The last fallback handles failed departure accounting, not delivery overflow.
There are no new saved types, shipment journal, periodic scans or telemetry hooks.
Arrival staging allocates temporary stock copies at handoff; stock/sizing IL
substitutions preserve native methods. No ordinary-frame scan is added. The stable
`cargo-loading` custom-build selection covers these shared accounting/handoff
repairs; economy-sff already requires it.

The destination representation limitation, loaded-mass preflight question and
exact historical destructive caller remain investigations. They are not declared
repaired simply because the confirmed handoffs now pass.

## Native baseline reproductions

`Tests/OrbitalCheck`, mode `delivery-study`, executes shipped methods under the
game's bundled Mono. Native failures are recorded before applying production
patches: **521 assertions pass** across reproduction, controls and repaired behavior.
Only singleton/Unity logging, trader synchronization,
station lookup and physical/crew retirement boundaries are fixtures. Stock
withdrawal/deposit, hull selection, the cargo constructor's tree, ordinary Dock's
station branch and cargo role FFWD/market handoff execute native code.

| Boundary | Actual native result | Consequence and certainty |
| --- | --- | --- |
| `ShipMarket.RemoveStockForCategory` | Request 8 from actors holding 2, 2 and 6; returns 6 and leaves 4. | Subtracts cumulative output instead of this actor's removal. Underfills an otherwise available dispatch/transaction. Conserves the actual six moved; this case is not disappearance. |
| `ShipMarket.AddStockForCategory(string, List<MarketItem>, ...)` | Input 5 into two actors with space: stores 10. Preferred actor with room for 3 plus a second actor: stores 8. | Passes the entire original batch to each actor without consuming the accepted prefix. Affects native load refunds and ship-to-ship transfers, not just AI freight. |
| `MarketManager.UnregisterCargoShip` at a full destination | Carrier 3, receiver capacity 2: receiver gets 2 and carrier market is removed. | Native overflow disposal, explicitly preserved per user clarification. A rejected departure's refund is a separate all-or-nothing contract. |
| Same method with an absent destination market | Carrier 3, no destination market: carrier market is removed. | All three units become unregistered. Loss at this boundary is proved; ordinary reachability of a missing station market is not. |
| `AIShipManager.GetCargoShipForRoute` | Same nine-pod-plus-one-item bundle selects 4-pod pool or 36-pod pool solely when reordered. | Loop overwrites required pod count with the last consignment. Actual JSON selects IbexCargo versus MesaCargo for these interregional pools. Poor sizing combines with loading overflow/refund; sizing alone does not prove destruction. |
| Cargo threat-response `Flee/Dock` | Native ordinary station Dock retires/hides carrier with all 3 cargo units still in its market. | Normal-time emergency arrival does not execute the freight handoff. This proves stranded cargo at retirement, not permanent deletion or the history of our three vanished freighters. |
| Same escape `FlyTo` under native FFWD | Native cargo FFWD instead executes DockCargoAndDespawn and unloads those same 3 units. | Baseline normal-time and skipped-time disposition differ. Previous economic SFF admission also accepted this leaf by name. Production unifies retirement handoff and excludes escape/priority work from economic delivery catch-up. |
| Cargo target loss | Actual `HaulerCargoPilot.GetTarget` returns origin when scan target is null. | Destination exists only as current navigation target; a separate durable shipment destination is absent. This is a representation limitation. Whether a particular target-clearing event is faulty requires its caller. |

The integer/item deposit overload is a negative control: it threads a remainder
and conserves five items across two actors. The batch defect must not be
misattributed to the per-item loop used by ordinary freight arrival. Earlier
loading tests also retain their own vanilla/previous-patch/production distinctions.

## Source path coverage

| Stage | Native mechanism reviewed | Our integration and remaining limits |
| --- | --- | --- |
| Dispatch | `AIShipManager.CheckTradeRoutes`, `GetCargoShipForRoute`, `CanReachTarget`; `MarketManager.GetTradeRoute`, `RegisterAICargoHauler` | Loading/bundle corrections have native reproductions. Ordinary reachability precedes loading. SFF plans after loading and refunds on rejection, through the unsafe native arrival transfer. Loaded-mass preflight consequences have not been quantified. |
| Normal flight | `HaulerCargoPilot` tree; `TargetNewIfNone`, `FlyTo`, `FlyToPath` | Station-directed path execution differs from target admission: GenerateNavData can reject predicted negative fuel even where an existing shallow path continues. Failed planning can clear the sole buyer target. Do not infer that zero RCS by itself stops every shallow freighter. |
| SFF | `StarSystem.Update`, `AIShipManager.FFWD`, `HaulerCargoPilot.FFWD` | Bounded continuation/arrival gates remain. Corrected handoff completion and actual Main/Flee ownership are tested, including restored escapes and missing receivers. |
| Diversion | Cargo ThreatResponse/Flee tree, `TargetNearestStation`, `SurrenderOffer`, ordinary `Dock` | Diversion replaces current target. A threat branch is conditional on its native checks; mere presence of another ship does not demonstrate it ran. Test invokes the actual terminal leaf; full hostile sensor/negotiation simulation is outside the fixture. |
| Arrival | `DockCargoAndDespawn.RunCommand`, `.ResolveInstantly`, `.TransferMarketCargo`; `UnregisterCargoShip` | Production gates normal/instant/escape retirement on committed transfer. Valid receiver overflow is discarded; missing receiver or staging error preserves the carrier. |
| Retirement | `CrewSim.DockAndDespawn`, `AIShipManager.UnregisterShip`, `BaseCommand.TryInstantCleanup`, `StarSystem.CleanupStaleShips`/`Update`, `Ship.Destroy`, region cleanup | Ordinary retirement, stale cleanup, direct destruction and region cleanup are different contracts. Main freight controllers are INTERREGIONAL and not normally in a departing local bucket. Existing retention/ownership patches do not make freight indestructible. |
| Save/load | `LoadManager.SaveGameData`, `AIShipManager.GetJSONSave`, `Ship.GetJSON`, `MarketManager.GetJSONSave` and native market stock serialization | Hull/controller/market are saved separately. SaveGameData catches per-hull serialization exceptions and can omit a hull while continuing. No such exception is established for the historical three. No new saved state was added by this audit. |

`CommandStatus` overloads equality by status code. `LocalizedResult` comparing
command-owned status objects is consequently **not** the suspected identity bug.
`TryInstantCleanup` backdates IsStale; it does not directly destroy the ship.
These source checks eliminate two tempting but incorrect explanations.

## Disappearance evidence is still narrower than the mechanism inventory

A later [saved-trajectory investigation](../../Notes/FREIGHT-STRANDED-CARGO-20261118.md)
identifies four other carriers with deep planetary penetrations in their saved
torch courses, unchanged surviving cargo, and missing pilots. It also reproduces
the native destruction/market leak and the current rendezvous validator's
acceptance of those unsafe courses. That new evidence does not establish the
historical removal caller for the three ships discussed in this section.

The three historical cargo hulls/controllers disappear between saves while their
markets survive. Neither successful native cargo arrival nor StarSystem's normal
stale-removal branch predicts that exact retained-market result: both explicitly
unregister the market. Ordinary emergency Dock explains retained market on a
hidden hull, but does not itself remove that hull from the world registry.

Direct `Ship.Destroy` removes the hull from StarSystem immediately and does not
unregister its market. It can therefore bypass the later destroyed-ship scan
which would otherwise unregister the market. Source callers include collision,
region/entity cleanup and explicit disposal. This matches the *shape* of the
historical residue, but does not identify its actual caller. Similarly, an omitted
ship entry during a failed save could leave separately saved market data.
Do not relabel either conditional path as the proven history of those ships.

The historical evidence and authorized one-time cargo recovery receipts remain
in the preceding audit. No additional live mutation or always-on recorder is
introduced here. Source review can establish and offline tests can reproduce
these defects without waiting real hours for another voyage; old snapshots
cannot reconstruct a discarded runtime call stack.

## Validation and layer ownership

The 521 checks include native player ship-to-ship transfers, preferred actors,
source shortages, capacity combinations, input-list immutability, concrete item
types, market identity, replay, self-transfer, failed staging/UI refresh, normal
and instant arrival, emergency/PASS/noncargo controls, native tree reload,
chronological SFF full/missing receivers and a loaded rejected departure whose
refund fails. Existing economy checks pass **1,480** assertions with actual cargo
constructor trees in the ship-creation fixture; loading regression remains **16**.
Full plugin binding passes **281** patch classes without failures/warnings; the
existing recovery and station-arrival suites pass 55 and 136 checks respectively.

## Live two-hour SFF validation

On 2026-11-14 the installed DLL matched the current Debug build. The baseline had
12 freighters: eight loaded and four already empty. All 12 complete stock audits
matched backing inventories and each had one controller on its actual hull.

The 7,200-second skip completed with one dispatch check, one departure, one arrival,
2,310 production attempts (712 successful), no rejected flights and zero reported
errors. Economic preparation took 27.8 ms and timeline work 48.6 ms in this run.

- O-L4G reached VORB. Its hull survives, its controller and cargo market are gone,
  and its pilot Sonia Ramsey is alive aboard VORB. No rescue API was used.
- O-10N departed MTRS for OKLG_FLOT with 100 actual AnyLifeSupport items; its
  manifest audit matches. Twelve active freighters remain: one arrived, one launched.
- Every other surviving freighter retains its pre-skip cargo quantity. All 12
  post-skip audits match, with no duplicate or stale-hull controller registrations.
- VORB's suit storage was 2,000/2,000 before and 1,999/2,000 afterward. The bounded
  arrival record captures O-L4G's 1,224-unit incoming manifest, not the precise
  accepted/discarded split. Do not claim that all 1,224 were credited or all dumped.

Read-only snapshots and comparisons are retained under the ignored
`obj/FreightValidation-20261114-154027` directory. This validates the observed
dispatch/arrival/crew-retirement path, not every historical disappearance or voyage.

The relevant JSON defines finite kiosk inventories, cargo restrictions, hull
pools and SOS/dropoff policy. It cannot fix remainder arithmetic, unregister
ordering or the hard-coded cargo constructor/FFWD mismatch. Those defects belong
in C#. Overflow disposal remains native. Asset-defined triggers remain authoritative;
there is no JSON override for these arithmetic and handoff repairs.

Run independently of the live game:

```powershell
dotnet build Tests/OrbitalCheck -c Release
dotnet run --project Tests/SaveJsonCompatibility/Host -c Release --no-build -- Tests/OrbitalCheck/bin/Release/netstandard2.1/OrbitalCheck.dll delivery-study
```
