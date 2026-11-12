# Station collisions duplicate AI controllers and strand freight

Confirmed and repaired against game 1.0.1.10, November 12, 2026.

## Saved evidence and root cause

Both `vorb` (epoch 65632483120.3488) and `prep` (65632521679.8399) contain
two native AI records for O-LVKJ:

| Registry | Home | Type |
| --- | --- | --- |
| INTERREGIONAL | MHNG | HaulerCargo (64) |
| VNCA | VNCA | Scav (2) |

Its log records impacts with MHNG. Its original consignment was 633 electronics
for MTRS. Later saves and the live registry lack the hull, while its market still
contains all 633. The pilot Hong Roy survives at VNCA_SD. The records establish
conflicting controllers and inaccessible cargo, not every intervening AI tick
or the cause of every other orphaned market.

`CollisionManager.ProcessCollision(Ship, Ship, double)` has two symmetric
station-impact branches. For a manned AI ship it may refill RCS, then calls
`AIShipManager.AddAIToShip(ship, Police-or-Scav)` irrespective of its current job.
That unsaved three-argument overload is a reuse-or-create mechanism, but
`TryGetAIShip(ship, atcLast)` searches only the requested/current regional bucket.
A freighter controlled in INTERREGIONAL can acquire a local scavenger controller.
Both command the same hull, crew and route through separate behavior trees,
homes and blackboards. Native save persists both records.

On ATC departure, `RegionCleanup` snapshots the local bucket. `AIShipCleanup`
receives the duplicate and destroys the shared hull despite its interregional
cargo registration. That path moves eligible crew but neither delivers nor
unregisters the virtual cargo market. Stock remains without a carrier.

The former traffic projection assumed one registry classification per hull.
It now represents dual registration, native regional destruction and orphaned
cargo, with a contrasting repaired scenario. This is a shared-engine boundary,
not new freight destination policy.

## Repair

`AIControllerReusePatch` repairs the public unsaved three-argument `AddAIToShip`:
reuse an existing controller for the exact live hull across all regions. Preserve
its role, region, home, tree, blackboard, mission and queued work. Do not construct
or re-enqueue a fallback. With no controller, native creation remains authoritative.
The explicit five-argument saved/player-autopilot setup is untouched.

`CargoControllerSavePatch` runs before native `ApplySaveData`. It removes only
the demonstrated recoverable duplicate pattern: one INTERREGIONAL cargo record,
only additional local Scav/Police records whose home equals their regional bucket,
and a surviving AI hull with native matching cargo ownership (`home + CargoHauler`)
and origin. Retain the original cargo record verbatim, before duplicate construction
can have side effects. Unknown ownership, changed ownership/origin, missing hulls,
multiple cargo records, Auto/other conflicting roles and incomplete metadata
are left alone. Routes, fuel, manifests, payloads and save format are unchanged.

Reviewed the native constructors alongside authored market actor configs,
CargoSpecs/CoCollections and `TIsValidStationDropOff`. Those assets describe
cargo/crew eligibility; they do not request role changes after collision.
C# owns the defect. Native cargo loading, threat response, delivery and legitimate
destruction remain authoritative.

Included in the existing custom-build **ai-crew-integrity** bugfix. No new frame
callback, shipment registry or save field. The lookup allocates no collection
and runs only on unsaved registration attempts. Grouping/filtering is confined
to load; it retains no game-object references afterward.

## Offline validation

```powershell
dotnet build Tests/AIShipLifecycleCheck -c Release
dotnet run --project Tests/SaveJsonCompatibility/Host -c Release --no-build -- Tests/AIShipLifecycleCheck/bin/Release/netstandard2.1/AIShipLifecycleCheck.dll controller-identity
```

187 bundled-Mono assertions. Actual native station collisions create duplicate
controllers; native save retains both; native regional cleanup removes the hull
while its 633-unit market survives. Both collision directions retain one controller
with the repair. A nine-role matrix covers fallback calls, normal native creation,
same-ID replacement objects and explicit Auto setup. Native restore, exact payload
preservation, idempotence and ambiguous-case exclusions are checked. Native
DockCargoAndDespawn/UnregisterCargoShip then delivers 633 units exactly once
with zero RCS, preserving stock and fuel.

An optional detached root-save capture containing `pilots` after the mode argument
replays actual saved metadata. The private `prep` capture adds four assertions
(191 total): precisely the competing VNCA scavenger is removed, while the original
cargo payload/tree/blackboard and unrelated freight remain unchanged.

Fixtures replace pilot materialization, queue side effects, station population
setup, Unity destruction/VFX, damage definitions and localization. Native collision
branches, regional lookup, registration, save/restore, cleanup control flow and
market transfer execute. This does not emulate a complete Unity voyage or crew
relocation scene. Original saves and the live game are not modified.

Related checks also pass: 52 existing lifecycle assertions, 1,443 economy
assertions, 410 traffic-graph expectations/safety checks, 413 custom-builder
self-tests and 227 full-plugin Harmony bindings without warnings or failures.
The selective `ai-crew-integrity` package builds and binds all 10 selected patch
classes with zero frame callbacks. The traffic dependency audit includes the
reviewed collision/registration/cleanup boundary and repository patch fingerprint.

## Limits and historical recovery

O-LVKJ's current orphaned 633 electronics are not automatically moved. Prevention
and saved duplicate repair cannot recover an already removed hull. A missing
hull alone cannot justify returning/deleting stock: it can also follow completed
delivery, legitimate destruction, deferred loading or another fault. Historical
recovery needs specific provenance and an explicit conserved transfer.

Fuel feasibility, emergency diversion cargo policy, full destination markets and
other retirement paths remain separate mechanisms. No recurring orphan sweep,
automatic refund, forced arrival or replacement behavior controller is introduced.
The existing departure-frame and SFF continuation repairs remain independent.
