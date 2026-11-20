# Wall-mounted Bottle Refiller

Standalone version 0.1.7, tested with Ostranauts 1.1.0.4. Requires BepInEx 5;
Ostrabus is optional. Game updates are not blocked by version or assembly hash.
The plugin needs the native APIs and asset definitions it uses to remain
compatible; Harmony reports an error if its command-loader hook cannot attach.

Buy a **Wall-mounted Bottle Refiller (Loose)** from a Supply kiosk after its next
normal restock. Install it on a wall using a power connection, open its inventory
and insert up to four undamaged handheld O2 bottles. Open **Control Panel** and
turn it **ON**. New units start OFF.

The refiller checks once per game second. With sufficient source gas, an empty
bottle reaches 90% rated pressure in five game seconds, then tapers to full by
thirty seconds. Each bottle follows this curve, including four bottles together;
partly full bottles start at the corresponding point on the curve. Low supply
can slow or stop filling. The native pump motor and gas hiss play during filling
and stop when idle, switched OFF, unpowered, paused or unloaded.

It draws from a gas canister positioned at its intake, or from the
compartment at that point if no canister is present. An empty canister blocks
room fallback; remove it to use cabin gas. With limited gas, bottles share the
available supply in proportion to their current filling requests. No gas is created.

**It is not an oxygen filter.** Nitrogen, CO2 and other gases transfer along
with oxygen. Existing bottle contents stay mixed in. A full-pressure bottle
is not necessarily a usable oxygen supply. Filling four empty bottles from
ordinary cabin air consumes roughly 2.45 cubic metres of room gas and produces
roughly 21% of a normal pure-oxygen charge. Native gas composition tooltips and
cabin alarms remain authoritative.

OFF, lack of power, damage or uninstallation stops filling. The device cannot
connect to sensors. It uses the stock two-position ON/OFF panel and stock
inventory window. It does not simulate compressor mechanics, compression heat or
off-screen filling aboard unloaded ships. The donor pump's price, mass, power
and work requirements are retained as initial balance values.

## Install and remove

Close Ostranauts, extract the release ZIP, and run `Install.cmd`. A non-default
installation can be selected with `Install.cmd -GameDirectory "D:\Games\Ostranauts"`.
The installer registers the native asset mod and installs only
`Ostrabus.BottleRefiller.dll`. Reinstall after changing other Supply-stock mods
so their effective stock is included in the composed rows. Existing kiosk stock
changes on native restocking; installation does not mutate a running save.

Run `Uninstall.cmd` while the game is closed to remove the filling plugin and
future refiller stock. Native definitions remain enabled so saved equipment,
its native work actions and contained bottles remain readable. Remove saved
refillers before disabling or deleting their asset definitions. The native
panel and inventory still work without the DLL, but refilling does not.

Ordinary `Deploy-Mod.cmd` deployment continues to manage the full Ostrabus
build and neither installs nor removes this standalone feature.

## Updating a 0.1.0 test instance

Run the new installer with the game closed, then reload. Existing installed
world units regain the native protection that keeps mounted equipment out of
movable ground inventory. If a unit was already incorrectly carried, use
**Repack carried refiller** on that item to convert it to its genuine loose form
through native mode switching, retaining its bottles. This recovery action is
available only while the broken installed item is carried; ordinary mounted
equipment still requires Uninstall work.

Native uninstallation can place the loose item inside nearby storage when it
cannot fit on the floor. If it appears to disappear, check nearby containers,
including underfloor racks.

## Implementation and checks

Four equipment forms (installed, loose, damaged and damaged loose) retain the
same native 2x2 bottle inventory. Native work conversions move the existing
bottles; conversion loot suppresses fresh contents. Native power processing
sets `IsPowered` directly without extra powered/unpowered mode conversions.

One Harmony hook recognizes this mod's command in `CondOwner.AddCommand`.
The custom component services only fully loaded, registered world objects;
barter, rating and ship previews cannot supply or receive gas. It stages
balanced species deltas and a single mixed temperature per bottle, then calls
the native `GasContainer.Run` for pressure, partial-pressure and gas-mass updates.
Nothing is added to the save format: native mode flags, gas conditions and
container relationships store the persistent state.

Native definitions retain drag-slot metadata so 0.1.0 saves can reconstruct
already carried instances. After reconstruction, the component removes runtime
drag eligibility from mounted world units. This avoids stranding an old saved
item before the player can use its recovery action. The bottle Inventory action
opens separately from the ground-item grid.

The equipment uses corrected 16x48 sprites with a neutral normal map: the output
face and lamp recess project into the exposed bottle-side tile. Loose units
show the custom refiller partially covered by the native pump's flat tan sleeve,
with its stepped diagonal openings fitted to the shorter refiller body.
The bottle-side indicator is green while gas transfers and amber while ON and
powered but idle, including empty/full racks or a missing/empty gas source.
OFF, unpowered, loose and damaged units stay dark.
Pausing silences the pump while retaining the last simulated indicator state.
Damaged installed and loose forms have separate cracked, scorched artwork,
including their inventory portraits. The loose damaged form uses the same tan
sleeve over the damaged equipment. Damaged units do not fill or light their
indicator; native repair work restores the healthy form and artwork.

See `VALIDATION.md` for executed checks and the remaining live acceptance pass.
The [art sources and regeneration steps](Art/README.md) retain only the current
masters and required layers; temporary comparisons are written under `obj/`.
From the repository, `Build.cmd` builds the DLL and `Package.cmd` runs the native
regressions and produces an independent ZIP. `Generate-Assets.ps1` regenerates
the new IDs from the reviewed native donor definitions; it does not edit vanilla
assets. Tests and installers never require the main Ostrabus plugin.

For developer testing, `spawn ItmOstrabusBottleRefillerStock` creates the loose unit.
