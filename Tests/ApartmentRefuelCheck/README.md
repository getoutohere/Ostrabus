# Standalone apartment refuel checks

Target: shipped Ostranauts 1.0.1.10 assembly, hosted in the game's bundled Mono.
Developer commands below run from the source checkout, not the release directory.

```powershell
dotnet build Tests/ApartmentRefuelCheck -c Release
dotnet run --project Tests/SaveJsonCompatibility/Host -c Release --no-build -- Tests/ApartmentRefuelCheck/bin/Release/netstandard2.1/ApartmentRefuelCheck.dll
powershell -NoProfile -ExecutionPolicy Bypass -File Tests/ApartmentRefuelCheck/Test-Installer.ps1
```

The native fixture loads real conditions, triggers, interactions, loot and all
11 residential templates, then generates the new work actions with native
`Installables.Create`. It binds all five production patch targets and executes
native `Item.CheckFit`, refuel setup/submit, ledger payment, gas additions and
RCS container discovery. Public-kiosk controls, stale quotes, insufficient money,
double submit, foreign/duplicate tanks, residence ownership, all templates,
cross-hull placement and plugin-absent behavior are covered.

The conversion fixture executes the native `ModeSwitchInternal` metadata phase
(persistent wear, identity, GUI properties and state valuation), then stops at
the scene inventory/room-movement boundary. Repeated installed/loose and damaged
conversions and the native save DTO's JSON round trip are checked. This does not
claim full scene spawning/destruction, physical inventory conservation or a
complete game save/load test.

Unity rendering/physics/component queries and gas thermodynamic stepping are
fixture boundaries; native refuel calculations, kg-to-moles conversions,
capacity limits and transactions execute. Native `GasContainer.Run` is replaced
with the deterministic scene-boundary pressure update. Tests do not independently
validate thermodynamics. No test code or game assemblies ship in the package.

The PowerShell tests install only into fresh workspace fixture directories, check
stock composition with Rotation/third-party shops and changed profiles, verify
idempotence, uninstall retention and the independent DLL's survival. Installer
tests never deploy into the actual game.

For transitions through actual packaged custom installers, first run
`CustomBuilder/Test-Installer.ps1`, then run this suite's `Test-Installer.ps1`
with `-WithCustomInstallers`. For simultaneous omnibus/companion hook binding,
build `Tests/PatchBindingCheck` and run it in the Mono host with
`--companion StandaloneMods/ApartmentRefuel/bin/Release/netstandard2.1/Ostrabus.ApartmentRefuel.dll`.

Live acceptance is still required for purchasing, full install/uninstall work,
UI selection, save/reload and operation beside the omnibus DLL.

Validated 2026-11-15: 239 native fixture checks; 56 standalone installer/stock
checks including actual empty/Rotation custom-profile transitions; existing custom
installer suite 88 checks; custom builder 559 checks; simultaneous binding of
286 omnibus and 5 companion patch classes, zero failures/warnings. Debug and
Release builds succeeded. These counts do not substitute for the live checks.
