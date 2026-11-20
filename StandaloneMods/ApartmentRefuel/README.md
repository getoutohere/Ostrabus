# Apartment Refuel Terminal 0.1.1

Optional standalone QoL mod for Ostranauts 1.1.0.2/1.1.0.3/1.1.0.4 / BepInEx 5. Includes its own
plugin and native item assets. It works independently of `Ostrabus.dll` and the
Ostrabus Settings app. No per-frame callbacks or new saved simulation state.

Buy a **Residential Dockside Refuel Terminal** from a station Sundries/Furnishings
kiosk after its next normal restock. The loose unit has a $250,000 base value
(typically $500,000-$750,000 at native Furnishings markups before other modifiers), weighs 31 kg
and uses a 2x2 fixture footprint. Bring it to a player-owned station apartment
and install it with the normal work/tools system. Uninstall to transport it.
It is oversized, drag/dolly-only equipment; it cannot be held in either hand or
worn on the back. Before updating from 0.1.0, drop any terminal already in hand,
save, close the game, run this installer again and reload.

The terminal opens the native paid refuel screen: He-3, D2O, RCS nitrogen,
life-support oxygen/nitrogen, ablator and batteries as supported by the vessel.
Only ships directly clamped to that apartment are eligible. Tethers, tow-chain
neighbors, stations and remote ships are excluded. Normal tank capacities,
prices, funds checks and station ledger obligations apply. There is no free
starting fuel or refueling discount. Uninstall before selling an apartment if
you want to keep the terminal; its installed property appraisal is deliberately
much lower than its portable retail value.

## Install or remove

Close the game and run `Install.cmd` from the standalone distribution. For a
nonstandard installation:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Install.ps1 -GameDirectory "<your-game-directory>"
```

The installer honors the native custom Mods location, verifies file copies and
keeps reversible backups. Windows may require write permission to the game
directory. It installs only `BepInEx/plugins/OstrabusApartmentRefuel/` and the
native `OstrabusApartmentRefuel` mod. Full/custom Ostrabus installers preserve
these components and refresh the composed shop stock when their own selection
changes. The main `Deploy-Mod.cmd` does not automatically install this companion.

Run `Uninstall.cmd` to remove the plugin and future shop stock while retaining
item definitions and native uninstall work for existing saves. Without the DLL,
new installation and services are disabled. Do not delete or disable the native
asset mod while a save still contains the item. Existing items in shop inventories
are not retroactively erased.

Shop entries are composed at installation time from the enabled native loot
definitions, preserving other Furnishings additions. General Supply and its
Rotation Module stock are left to their existing native mods.
If you later change other shop mods or load order in-game, rerun this installer
before playing. The mod must stay after other shop contributors in load order.

## Build and validation

From the repository root:

```powershell
dotnet build StandaloneMods/ApartmentRefuel/ApartmentRefuel.csproj -c Release
powershell -NoProfile -ExecutionPolicy Bypass -File StandaloneMods/ApartmentRefuel/Package.ps1
```

The package script produces an independent ZIP under `dist/`. The main package
also includes it as a separately installable directory under `StandaloneMods/`.
Main/custom compilation excludes the companion sources entirely.

Five native methods are patched: `Item.CheckFit`, `Interaction.Triggered`, and
`GUIStationRefuel.Init`, `SetupFields`, `OnSubmit`. Adapters are restricted to the
new item; ordinary station refueling retains its native behavior. Native JSON
defines the item, its four damage/install states, tools, work, repairs and stock.
Quoted resources and docking/ownership are rechecked before native payment and
delivery. Gas-input searches cannot spill purchases into another docked hull.
Shared pump inputs count each nitrogen tank once in both quote and delivery.

One native ledger defect needs a defensive guard: an identical same-time charge
can be discarded even when already paid, while refueling still proceeds. This
terminal rejects that repeated order before delivery. Choose a different amount
or briefly resume the game before repeating it. Public kiosks are unchanged;
the mod does not invent transaction names/timestamps or edit existing history.

[Offline checks](../../Tests/ApartmentRefuelCheck/README.md) exercise the shipped
assembly, generated work recipes, actual placement and paid service methods,
metadata conversion and packaging/installer coexistence. Scene rendering, full
work execution and complete save loading require live acceptance testing.

Remaining live checks: purchase/retail label, install and uninstall with tools,
all applicable services with wallet/tank inspection, reject placement on the
adjacent ship, exclude its tow partner, and save/reload the installed and loose
item. This build has not yet passed those live checks.

See [design and native evidence](DESIGN.md) for motivation, abuse cases and the
division between native assets and C# integration.
