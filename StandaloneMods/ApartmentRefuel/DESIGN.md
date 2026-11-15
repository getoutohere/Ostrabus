# Standalone apartment refuel kiosk implementation plan

Accepted design and native-code evidence. Implemented for Ostranauts 1.0.1.10;
see README.md and the offline test README for current validation and live checks.

Add an expensive private refuel terminal that the player buys from a station's
Sundries/supply kiosk and installs in a purchased station apartment. It uses the
existing paid refueling screen and services only vessels physically docked
directly to that apartment. This is an optional QoL feature, not a vanilla bugfix.

Delivery: a separately installable native asset mod and BepInEx 5 plugin, bundled
alongside the Ostrabus release. Neither part depends on `Ostrabus.dll`. The feature
also works without installing the omnibus mod.

## Independent plugin and distribution

Keep the implementation under `StandaloneMods/ApartmentRefuel/`, with its own
`ApartmentRefuel.csproj`, plugin entry point, version and Harmony owner. Proposed
assembly: `Ostrabus.ApartmentRefuel.dll`; plugin ID: `com.ostrabus.apartmentrefuel`.
Reference the game, Unity, BepInEx and Harmony assemblies directly. Do not reference
the Ostrabus project, configuration registry, settings app or Inspection API.
Offline development tests may use the existing test host without creating a
consumer dependency on it.

The root Ostrabus project must exclude `StandaloneMods/**/*.cs` from compilation
before this source is added. Otherwise the SDK's recursive source glob would
compile a second copy into the omnibus DLL. Custom builds must exclude these
sources as well. Test that the omni assembly contains no private-terminal patch
classes and that installing both plugins registers each new hook only once.

Produce an independent ZIP containing the plugin, native assets, its own
`Install.cmd`/PowerShell installer, README and license. Include the same optional
package under `StandaloneMods/ApartmentRefuel/` in the main release archive.
Installation copies the plugin to its own BepInEx subdirectory and registers
`OstrabusApartmentRefuel` with the native mod loader. Bundle the needed installer
helpers so the package works without the source checkout or main release.

Main `Deploy-Mod.cmd` and custom installers continue managing their existing
Ostrabus components. They must not delete, disable or replace this independently
installed plugin or its native mod. Building/packaging the release can build and
bundle the standalone project without automatically installing it. Offer separate
development build/deploy commands for testing the terminal.

This remains a compiled-plugin mod: the asset-only MVOL transit mod's lack of a
BepInEx requirement does not apply. Apartment and transaction safeguards still
need the integration described below. The native asset definitions must remain
available for saves containing the new item even after its plugin is removed.

## Player experience and proposed balance

The **Residential Dockside Refuel Terminal** is a connection to the station's
utility service. It does not manufacture fuel or contain a free starting supply.
The player buys convenience and equipment; every subsequent purchase still costs
the normal native rate. No subscription, new fuel market, discount or private
station economy is introduced.

Proposed loose-item base price: **$250,000**. The generic supply kiosk's native
`DiscountSell` range is 1.2-1.3, giving approximately **$300,000-$325,000** before
other native condition/vendor/market adjustments. For scale, the HyperHandy's
native base value is $33,480 and a nav station's is $16,880. Price is authored in
the assets rather than enforced by another purchase hook.

Reuse the fuel kiosk's existing artwork, 2x2 fixture footprint and 31 kg mass.
The loose form is cumbersome equipment that can be transported on a ship or
dolly, but cannot function there. Use the existing install/uninstall work system,
Mortorq tool checks and normal damage/repair mechanics. Do not inherit the public
kiosk's indestructibility or NPC use interaction. One loose unit must be consumed
to produce one installed unit, and uninstalling must return exactly one unit.

One unit is offered per native Sundries restock. In the inspected assets the
general-supply family is `ItmKioskSupplies01` with `TraderChargenSupplyKiosk*`
property maps and 17 `ItmSupplyKiosk*Inv` stock tables. Add it to those retail
inventories, not scrap merchants, licensed salvage desks or freight manifests.
The local UI label must be confirmed against these identifiers during playtest;
the shipped item definition calls this family Supply Kiosk.

The service screen retains native He-3, D2O, RCS N2, installed life-support
O2/N2, reactor ablator and battery services where supported by the selected ship.
It preserves the native empty-universe restrictions. Nothing is delivered to
loose tanks, backpacks or arbitrary remote owned ships.

## Confirmed native mechanisms

| Source | Behavior relevant to this feature |
| --- | --- |
| `GUIStationRefuel.Init` | Builds the vessel list from the kiosk's own `Ship.GetDockedShips()` and opens the normal service rows. |
| `Ship.GetDockedShips` | Returns direct docking links, not the transitive dock group; its collection can also contain mooring links. |
| `GUIStationRefuel.SetupFields` | Reads installed containers and capacities. Chooses the last ATC ship as the ledger provider when available. |
| `GUIStationRefuel.OnSubmit` | Uses the kiosk's own ship ID as ledger provider instead; checks the interacting character's `StatUSD`, creates/pays native ledger entries, then fills resources. It does not recheck docking. |
| `Ship.GetRCSCans`, `GetRCSRemain`, `GetRCSMax` | Follow distributor gas inputs with docked-hull searches. Life-support N2 discovery also permits docked-hull queries. |
| `Ship.AddDockingFees` | Issues native docking bills to the nearest regional port authority, rather than necessarily the physical docking station. |
| `GUIShipBroker.SetupApartments`, `OnPurchaseConfirm` | Purchases use `<host>|RES_<number>` IDs; the residence is a separately owned, hidden ship instantiated with `isStation: true`. |
| `JsonShipSpec.Matches` | Expresses owner/station/hidden tests, but has no residential-template or apartment-host relationship predicate. Its player-ship test means the selected crew member's current ship, not legal ownership. |
| `Installables.Create` | Generates native work actions, input requirements and conversions from JSON; `JsonInstallable` does not expose a destination apartment predicate. |
| `Item.CheckFit`, `CrewSim.InstallFinish` | Placement resolves actual floor tiles across docked hulls. The selected crew member's location is insufficient to identify the destination. |
| `CondOwner.ModeSwitchInternal` | Transfers identity, persistent conditions, contents and GUI maps during item conversion. It requires tests that an uninstalled terminal cannot retain working service access. |
| `Room.CalculateRoomValue`, `Ship.GetShipValue` | Installed item values contribute to property valuation, which is multiplied for ships with life support. A large new fixture value can therefore amplify resale proceeds. |

The native fuel inventory numbers are fixed service availability values, not a
withdrawal from station freight stock. This feature retains that abstraction.
It does not consume industrial cargo or fix wider refueling/economy behavior.

## Apartment and vessel eligibility

Resolve eligibility from current native state, not a flag copied onto the kiosk
when it was installed. A valid installation requires all of the following:

1. The actual destination hull is owned by `CrewSim.coPlayer.strID` in the system
   owner registry. Merely standing there, claiming it in a character list, or
   belonging to its faction does not grant permission.
2. The hull has the broker's residential ID structure, a currently valid station
   body state, and an authenticated residential template. Its host ID resolves
   to an existing station. A renamed ship, player-built ship, captured pirate
   vessel, purchased freighter or ship docked to a station is not an apartment.
3. The template belongs to the shipped residential offerings. The inspected
   `Res*.json` family contains 11 residences, including OKLG, BCER/BCRS, MSUZ,
   MLAB, EJDR and aerostat variants. Validate this set against broker offer loot
   during implementation; a newly added or unknown template requires review.
4. All required floor/support cells beneath the fixture belong to that same
   apartment. Reject placement on the visiting ship or across a docking seam.

Use actual item definitions and registry identity, never the editable public
name. An apartment with no visiting ship can still contain an installed kiosk;
its screen simply has no eligible vessel and cannot submit a purchase.

An eligible service target is a live, non-station vessel joined **directly** to
the apartment through a reciprocal physical docking-port connection. Exclude
tethers/mooring links, the apartment itself, the host station, another apartment,
other station visitors and ships connected only through a tug. For
`apartment -> tug -> wreck`, only the tug is eligible; the wreck must dock to the
apartment in its own right. This is an intentional interpretation of the
requested service boundary, not a claim about vanilla limitations.

Do not additionally require ownership of the serviced ship: the requested rule
is physical docking, and buying fuel for a captured or visiting ship is plausible.
The user must be the player or controllable player crew. The native interacting
character remains the payer, as at public kiosks; apartment ownership does not
create a refund, income stream or separate account.

Recheck apartment ownership, installed/working state, direct docking and target
identity immediately before submitting. If ownership changes, the terminal is
removed/damaged, a ship undocks, or the saved target becomes stale, reject the
purchase before ledger or tank mutations. Refresh the normal screen and explain
that the service connection changed. Do not silently select another vessel.

## Assets own the item and work behavior

Create a native mod named `OstrabusApartmentRefuel`, installed after core using
the existing PdaSettings/native-mod installer approach. Reuse image paths from
the game; do not redistribute native assemblies or build a replacement UI.

Proposed asset files and scope:

| File under `Assets/` | Contents |
| --- | --- |
| `mod_info.json` | Feature metadata and matching-game/plugin requirement. |
| `data/condowners/apartment_refuel.json` | Unique loose/installed working and damaged item definitions; four states, with no public kiosk definition overwritten. |
| `data/items/apartment_refuel.json` | Loose and installed geometry using native kiosk sprites, fixture sockets for installed states and ordinary loose-item sockets for transport. |
| `data/conditions_simple/apartment_refuel.json` | Feature-specific identity marker. |
| `data/condtrigs/apartment_refuel.json` | Working/loose/installed/input triggers and a runtime integration gate that defaults to `TNever`. |
| `data/interactions/apartment_refuel.json` | Private Use/Allow actions reusing the native refuel panel; no NPC idle attraction. |
| `data/guipropmaps/apartment_refuel.json` | Native `GUIStationRefuel` panel mapping with a feature identifier. |
| `data/installables/apartment_refuel.json` | Native installation, uninstallation, repair/restoration and dismantling definitions for the new item states. |
| `data/loot/apartment_refuel.json` | New-unit stock loot and required conversion/destruction outputs. No filled fuel containers or inventory payload. |

Do not inherit `IsTraderNPC`, `IsMarketActor`, infinite storage or the public
kiosk's `IsCategoryIndustrialProducts` bulk-goods classification. Selling the
terminal as a retail item must not register another station market actor or make
the new expensive equipment a substitute for ordinary factory/freighter cargo.
Use native ordinary-item barter eligibility for the loose forms.

Use a unique private kiosk marker rather than `IsRefuelKiosk`. The generic public
kiosk detector and its unconstrained `GUIRefuelStation` interaction must not
provide a second route around the new private action. The installed private
interaction still opens the existing refuel prefab and uses its native methods.

Keep both install entry paths: the loose item's quick action and the PDA build
catalog. Each must require the actual loose input. Gate normal service with
installed, working and integration-ready triggers; leave tool/skill/progress,
repair recipes and native conversions authoritative in JSON.

### Prevent property resale inflation

Proposed installed-state appraisal value: **$2,500**, while the loose recoverable
unit retains its $250,000 base value. A mounted utility concession should not
inflate an apartment broker's valuation by hundreds of thousands, multiplied by
room/life-support modifiers. The owner can uninstall it before selling property
and retain or resell the expensive equipment normally.

This is an intentional asset pricing choice. Prove that native conversions use
the destination state's base price while preserving wear and identity; do not
introduce a general ship-value hook. Test the maximum authored room multipliers,
pristine/damaged states, broker discounts and cached quotes for a profitable
buy/install/sell/recover loop. The native broker destroys sold apartment hulls:
items left installed are sold with the property, not returned automatically.

## C sharp integration stays at the boundaries

A pure asset clone can open the native service screen, but cannot enforce the
actual destination apartment, revalidate a live docking link at submission, or
make the native quote/payment provider consistent. Use a small feature-specific
adapter; do not replace the transaction body, resource algorithms or AI logic.

Planned native patch targets:

| Target | Narrow responsibility |
| --- | --- |
| `Item.CheckFit` | For installed private-terminal cursor definitions only, fold the apartment/floor-owner constraint into the native fit and tile-color decision. Invalid placement remains an ordinary red/non-fitting cursor, before placeholder creation or input consumption. Loose item placement/transport stays unrestricted. |
| `Interaction.Triggered(CondOwner, CondOwner, ...)` | For this feature's generated install/finish and private Use actions only, revalidate the relevant apartment/working state at execution. The loose-item action may start on a ship; its later destination is what must pass. Uninstall remains available even for an otherwise invalid installation. |
| `GUIStationRefuel.Init` | For the private marker only, filter the native direct-docked list to eligible reciprocal port links; preserve native vessel naming, selection and screen setup. |
| `GUIStationRefuel.SetupFields` | Resolve one canonical billing authority and isolate discovered tank recipients to the selected eligible hull. Keep quote capacities consistent with those recipients. |
| `GUIStationRefuel.OnSubmit` | Before native financial/resource mutation, revalidate the connection and current quote. Use the same billing authority and recipient restrictions as setup, then run the native payment/fill/event body. |

For billing, resolve the apartment's real host, then the native regional port
authority at that host's location, matching `Ship.AddDockingFees`. Use that
identity for both quoted obligations and new purchases. This can differ from
the physical host, such as VORB versus VNCA; do not use a stale global last-ATC
value or the apartment owner as the payee. Preserve native selected-vessel debt
and shared-fee rules without changing unrelated public kiosks.

Implementation validation exposed another native boundary defect:
`Ledger.AddLI` deduplicates against paid as well as unpaid entries using
`LedgerLI.Same`, including a 0.001-second timestamp tolerance. A second identical
paused `GUIStationRefuel.OnSubmit` can therefore deliver fuel without a new debit.
The private terminal preflights the actual payer's keyed ledger history and
rejects a charge the native ledger would discard. The player can change the
quantity or briefly resume time. This defensive adapter does not modify ledger
history, fabricate unique descriptions/timestamps or change public kiosks.
The fixture reproduces the native free refill as a public-kiosk control.

Native slider lower stops can also round slightly below zero because they
calculate `max * (onboard / max) - onboard`. The private validator normalizes
only that floating-point noise to zero; substantive negative orders are refused.

The recipient adapter must address both preview and delivery. Most native
resource queries already exclude docked hulls, but RCS and life-support N2 have
explicit cross-hull searches. Filter out containers whose `ship` is not the
selected vessel, deduplicate repeated references, and derive capacities from
the same eligible recipients. Only replace those query call sites inside this
feature's refuel GUI execution; do not patch world-wide tank, navigation or gas
lookup behavior. If tank state/capacity changes after the quote, clear the
pending order and request review rather than charging for unavailable capacity.

Use `CrewSim.OnGameFinishedLoading` to enable the asset integration gate only
after the feature's bindings and required definitions validate. Reset it on
module shutdown. This changes a global asset evaluator in memory, not an item
condition saved with the world. With the DLL absent or the feature omitted, the
asset-defined gate remains false and private Use/install actions are inert;
native uninstall/transport definitions can remain available. No per-frame
watcher, periodic world scan, telemetry or persistent service history is needed.

The planned footprint is five native method targets plus an existing load-event
subscription. Helpers operate on the current placement, apartment links and
selected vessel's tank lists, never the population or complete world inventory.
Reassess this count if native-assembly tests show an additional boundary is
necessary; do not silently expand the patch surface.

## Stock composition and packaging

The existing rotation module already overrides supply stock loot. Independent
full-record overrides for overlapping stock IDs would have last-mod-wins behavior
and can remove either product or leave references to an omitted feature.

Provide a shared Sundries stock composer with the standalone installer as well
as the main/custom installers. It produces one final native loot override per
affected stock table from the installed core data and enabled native overrides,
then adds the terminal's stock loot. Installation accounts for the other independently installed
product, regardless of install order. The standalone installer needs no main
DLL or source checkout; it bundles the composer and the terminal's item/stock definitions.
The final shared stock mod loads after the contributing item-definition mods.
Do not add a runtime trader/restock hook or edit installed vanilla files.
Preserve existing quantities and unrelated stock entries, with regression
comparisons against the current rotation-only output.

The normal custom-builder feature selection must not compile this plugin's
sources or classify its native mod as an omitted omnibus feature. Bundling a
standalone package is independent of the custom DLL's selected patches. Any later
menu option for the terminal selects a separate package/install action, not a
second compiled copy. Test that full/custom reinstalls preserve a user's existing
standalone installation and include its enabled stock contribution.

As with any new physical item, saves containing it require its native item
definitions. The standalone uninstaller offers to remove/disable its DLL and
new shop stock while retaining definitions and native uninstall/transport
recipes. With integration absent, service/install remain gated off. Main/custom
installers leave those standalone assets alone. Do not advertise that deleting
all native asset definitions is safe for saves containing the item.

## Abuse cases and validation gates

| Scenario | Required result |
| --- | --- |
| Carry a loose unit aboard a ship | Transport works; no refueling access. |
| Place it on a ship docked to the owned apartment | Refused, even if the player stands inside the apartment. |
| Rename or claim a pirate ship as an apartment | Still refused; identity, template, station state and registry ownership are checked. |
| Place across a docking seam, in an NPC residence or on a public concourse | Refused before loose input is consumed. |
| Use PDA installation without owning a unit | Native input requirement fails; no free craft. |
| Tow several ships behind the directly docked ship | Only the direct physical neighbor is listed/serviced. |
| Align gas inputs across hull boundaries | Preview and purchase affect only the selected eligible hull's containers. |
| Uninstall, damage, sell the apartment or undock while a screen/quote is retained | Submit rejects before money, ledger or gas changes. |
| Have insufficient money or submit twice | Native rejection/reset behavior; no free fuel, duplicate debit or duplicate fill. |
| Install/uninstall, repair, rotate, save/load repeatedly | One unit, preserved wear/identity, correct state price, no fuel/contents generation. |
| Install multiple kiosks | Same prices and obligations, no accumulating discount, credit or fuel entitlement. |
| Sell an equipped apartment | Native property sale applies; no appraisal arbitrage or promised automatic equipment return. |
| Keep assets but remove the supporting feature/DLL | Existing items remain defined; service cannot fall back to unrestricted vanilla behavior. |
| Build with rotation only, terminal only, both or neither | Complete intended stock, no duplicates or missing-definition references. |

Implementation order:

1. Build an offline native-assembly fixture for apartment identity, placement,
   direct docking, billing identity, tank recipients and property pricing. Use
   real residential/template assets and native methods, stubbing Unity UI at
   the rendering/input boundary. Establish the public kiosk control behavior.
2. Add the assets and validate native loader/installable generation, all references,
   four item states, inventory conservation and generated work actions. Preserve
   resource/wear/price behavior through actual native mode switches and JSON saves.
3. Add the five boundary adapters. Execute actual native setup/submit/ledger/gas
   paths offline for N2, O2, He-3, D2O, ablator and batteries, with stale targets,
   rejected requests and multi-hull controls. Assert unchanged public kiosks and
   no mutation outside the eligible vessel.
4. Build the standalone project and package stock combinations. Verify Harmony
   bindings, absent-plugin gating, retained definitions and installer idempotence.
   Run with and without the omni DLL; full/custom reinstalls must leave the
   standalone plugin intact, with no duplicate hooks or lost shop additions.
   No automatic migration of existing station kiosks.
5. Playtest purchase at Sundries, install in an existing apartment, refuel with
   before/after wallet/ledger/tank inspection, uninstall, transport, reinstall and
   reload. Also reject installation on the ship next door and verify a second
   ship in a tow chain is excluded. Confirm retail labeling and final pricing.

These are the design gates, not a claim that offline fixtures execute Unity's
complete scene lifecycle. See the current README for the implemented boundaries,
passing checks and remaining live purchase/work/save-load acceptance tests.
