# Cargo loading and disappearing freight investigation

Reviewed against 1.0.1.10 on 2026-11-14. The loading repair is implemented and
offline validated. Historical cargo recovery is an explicitly authorized,
separate operation; its receipts are recorded with the live evidence.

The subsequent [lifecycle repairs](FREIGHT-LIFECYCLE-AUDIT.md) add native
reproductions and production fixes for stock arithmetic, sizing, missing-market
loss and normal/SFF emergency retirement. Valid receiver overflow remains
deliberately discarded. The exact historical removal caller remains unproved.

## Confirmed interaction with our loading correction

`Ostranauts.Trading.MarketManager.GetTradeRoute` seeds its selected bundle with
one DTO, then appends all routes for that origin/destination, including that same
DTO instance. The multi-candidate branch therefore repeats the first consignment.
The single-global-candidate branch does not. Native selection itself reproduces
the repeated reference in the shipped-assembly fixture.

`MarketManager.RegisterAICargoHauler` asks `DataHandler.GetMarketConfig` for each
physical pod on each consignment. That method constructs a fresh configuration
from JSON. It does not return the already loaded configuration in the carrier's
ShipMarket. The loader later calls `ShipMarket.AddMarketConfig` with the same pod
ID, overwriting the previously loaded configuration. This also affects genuinely
different commodities in a bundle, even if unused pods remain aboard.

Our existing `CargoLoadingPatch` correctly stops the native accepted consignment
from being added back to the source. That native duplication previously masked
some overwrites by making the goods available for the second removal. Fixing the
duplication alone exposes real loss when the repeated or bundled load overwrites
the first configuration. This is a regression in the completeness of our repair.

The existing SFF dispatcher removes repeated DTO references before loading.
Ordinary `AIShipManager.CheckTradeRoutes` previously did not. Before this repair,
SFF was protected from the repeated-seed case but not from overwrites between
distinct commodities. Native bundle construction is now corrected for both.

## Native and repaired results

All quantities below come from shipped methods. Pod configuration and cargo
requirements are loaded from the installed JSON. Source categories, prices/masses,
Unity logging, singleton access and trader UI synchronization are fixture inputs.

| Case | Source metal after | Carrier metal after | Carrier product after |
| --- | ---: | ---: | ---: |
| Vanilla: 50 metal, one consignment | 50 | 50 | 0 |
| Previous patch: same single consignment | 0 | 50 | 0 |
| Previous patch: same DTO repeated | 0 | 0 | 0 |
| Previous patch: 100 metal, repeated 50-unit DTO | 0 | 50 | 0 |
| Previous patch: 50 metal + 20 product, two available pods | 0 | 0 | 20 |
| Repaired bundle construction and pod reuse: repeated DTO | 0 | 50 | 0 |
| Repaired bundle construction and pod reuse: repeated DTO + distinct product consignment | 0 | 50 | 20 |

The repair excludes the selected seed from its companion append and resolves a
pod's existing carrier-market configuration before creating one from its template. Native pod
eligibility, capacities, item cloning, overflow and return-to-source logic still
execute. Short source loads, remaining source stock, a load split across two pods
and a later category with no free pod are also checked.

`Tests/OrbitalCheck` mode `cargo-study` passes **16 reproduction/repair assertions**.
It deliberately proves vanilla and previous-patch failures before applying the
production patches inside the offline process. Native route selection itself is
checked before and after repair. The economy suite separately passes **1,477
checks**, including ordinary loading and SFF dispatch/arrival. The earlier
passing suite did not cover the complete native multi-consignment path.

Production repair: correct repeated bundle construction at GetTradeRoute and
retain actual pod state inside RegisterAICargoHauler, while keeping the accepted
remainder correction. These are validated instruction substitutions, preserving
the native selection, pod loop and transfer mechanisms. No frame hook, runtime
journal, new saved type or asset override is added. Multiple-source trader batch
accounting and cargo hull sizing are repaired by the subsequent lifecycle work;
the loading substitutions alone do not cover those boundaries. Missing cargo is not replaced by periodic refills or
post-hoc cancellation of empty ships.

The authored CargoPodDefault/Climate/Rad/Spec templates start empty and expose
their cargo requirements. These assets are doing their intended job; an asset
override would not repair the C# configuration lifetime or duplicate bundle.

## Live survey and separate disposal question

During the survey, O-HRVF3 (OKLG_FLOT Hauler 77, bound for SVIR) was newly
registered with an empty AnyHull pod. O-BEGL, O-EH8 and O-QMJ5 also had empty
manifests. This fits the reproduced loading mechanism, but there is no captured
live input DTO list establishing exactly which quantities each ship lost.

November 14 follow-up at epoch 65634070065.5222: those same four empty hulls
remain registered, crewed and following FlyTo routes (O-EH8 to MTRS, O-BEGL to
JATL, O-QMJ5 to VORB, O-HRVF3 to SVIR). This is continued execution of their
original empty delivery trips, not observed new empty dispatches. Current arrival
handling accepts an empty manifest, unregisters the market and then allows native
station retirement. The loading repair does not recreate lost historical loads.

At the investigation baseline, O-6F7 (SVIR Hauler 75) is a separate stranded loaded carrier: 1,224 suits remain,
no crew/controller/route, zero commanded acceleration, roughly 6,413 km/s
heliocentric speed, and 936.205 kg RCS unchanged across reviewed saves. It was
already in this state at the earlier survey epoch 65634040923.677856. Save
comparison finds Tomiwa Trejo alive aboard through `wtf` (65633872851.5786),
then absent from the hull by `arrive` (65633959849.6322), a 24.17-game-hour gap.
The `arrive` hull archive instead stores that same actor ID aboard O-MYSS,
retaining the O-6F7 ownership claim and adding O-MYSS. Current live lookup finds
the ID alive at BCRS. StatAge changes from 28 in the O-6F7 save to 46 in O-MYSS,
but all 71 earlier body/inventory IDs and 10 personal-message GUIDs persist,
establishing continuity of the saved actor contents. The subsequent
[native NPC identity investigation](../../MiscBugFixes/NPC-IDENTITY-COLLISION-AUDIT.md)
reproduces a fresh-crew name collision bypassing the serving-pilot exclusion,
and finds the new-crew duty message in Tomiwa's retained history. This is a
confirmed native hazard strongly supported as this event's cause; the historical
runtime stack/RNG is unavailable. It is not a diagnosed death or legitimate
transfer. A preventive fresh-recruit identity repair is now implemented and
offline validated. Tomiwa remains unchanged. A later authorized recovery pruned
the empty hull and credited four items to VORB, retaining 1,220 suits without a
qualifying redistribution receiver. See the identity audit's live-recovery section
for the subsequent systemwide redistribution receipt. Evidence:
ignored `obj/economy-fleet-survey-folder.txt` points to the snapshot and timeline;
`obj/FreightPilotIdentity` reads actual aCOs records across both archives.

The newly dispatched ship's short initial non-torch course was a departure leg:
it subsequently entered a long torch course toward SVIR. Do not classify that
intermediate endpoint as an impossible route.

Three previously observed flights have a separate persistence/disposal failure:

| Ship | Original destination | Retained cargo | Pilot |
| --- | --- | --- | --- |
| O-1U1B | VORB | 1,224 suits | Dean Valencia |
| O-HV3B | JFTS | 449 textiles | Aubrey Sofia Khan |
| O-PG5 | MLAB | 22 media | Mariam Washington |

All three controllers remain in the `prep` save at epoch 65633982447.6731.
Their ship entries contain living pilot COs. By `burn`, epoch 65634025964.5318,
their hull entries and controllers are absent; a scan of every saved ship's COs
finds none of those pilots. Their cargo market records survive throughout the
seven reviewed saves. These observations bracket removal to about 12.1 game
hours; they do not identify the removal call or prove a death rather than some
unmaterialized personnel disposition.

Normal `DockCargoAndDespawn` calls `UnregisterCargoShip`, which removes the
carrier market before transfer. Ordinary station `Dock` instead calls
`CrewSim.DockAndDespawn` without cargo transfer. The cargo threat-response tree
can use that ordinary Dock. This previously recorded retirement gap remains a
candidate explanation, not an established history for these three ships.
The loading defect does not itself destroy hulls, and the civilian visit repair
postdates the saved disappearances.

Current survey: 123 absent-hull markets with positive cargo retain 82,280 virtual
units. Nine further unresolved markets are empty. All 173 market quantity audits
match cached and backing stock, so internal count agreement is not evidence that
a shipment still has a carrier or can deliver. No automatic attribution, recovery
or deletion of those historical records is justified by the census alone.

Evidence receipts are kept outside version control under
`obj/EconomySurvey-20261114-132016`, including native logs, raw API responses,
selected ship entries and streaming save comparisons. The investigation adds no
on-going recorder or runtime journal. The production repair substitutes native
bundle construction and loading instructions as described above.

## Authorized historical recovery

An index of 41 available save archives locates 21 historical hull IDs among the
123 cargo-bearing absent markets. Eight selected archives cover those IDs. Twenty
have an actual saved interregional cargo controller, matching ship/station target
IDs and a surviving manifest with the same category quantities. The remaining
ID's available snapshot is not a cargo flight and supplies no valid destination;
it is excluded. The other 102 orphan markets have no matching hull in the
available archives and are also excluded from destination assignment.

Fifteen verified shipments, **7,523 trade units**, were credited through the
existing explicit recovery API. Source markets were removed once, and all eight
receiving market audits matched backing stock afterward. Five further verified
shipments exceed current receiver capacity. The user chose to preserve caps,
credit only available space and retain the original overflow cargo.

`recover-available` implements that request without expanding capacity. It stages
native per-actor source withdrawal and destination stocking, verifies conserved
quantities, and preserves the exact remaining item types in the native source
market. Fifty-five freight recovery tests cover native arrival, full/partial
credits, concrete-item conservation, save restoration, retries and staging errors.
After save/deploy/reload, the deployed DLL matched the tested build and all 15
completed source removals persisted. Partial recovery then credited **390 more
units**: 294 ore to BCRS, 17 consumer goods to BCER and 79 suits to VORB. The
relevant destination stocks reached their native caps, without increasing them.
All partial source and destination quantity audits matched afterward.

Total credited: **7,913 units**. The five original cargo records retain **1,984
units**: 1,145 suits for VORB, 671 ore for BCRS and 168 consumer goods for BCER.
Of those five, three were partially credited and two had no space remaining when
their turn arrived. Recovery order used the earliest available saved observation.
The 103 historical cargo-bearing orphan records without a verified destination
were not reassigned or credited. Applied and remaining receipts stay beside the
external save evidence; there is no automatic future-transfer worker.
