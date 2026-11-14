# Cargo-pod registry diagnosis

Offline diagnosis and regression harness; no installed assets, live game state or
save files are changed. `native` reproduces the unpatched game; `patched` installs
the production prevention patch in the test process alongside Unity fixtures.
See [the implemented repair](../../MiscBugFixes/PREVIEW-MARKET-ISOLATION.md).
The `repair` mode also tests the explicit
[cargopodfix reconciliation command](../../MiscBugFixes/CARGO-POD-FIX.md).

## Native finding (before the prevention patch)

The ship-rating preview can register its temporary cargo pods in the real ship's
persistent market. Its destruction does not remove those market configurations.

Native sequence in game 1.0.1.10:

1. `Ostranauts.Ships.AsyncShipLoader.LoadRatingShip` clones the ship JSON but assigns
   the preview the original ship's `strRegID`.
2. `Ostranauts.Ships.AsyncRatingShip.Init` gives the copied items new GUIDs and adds
   the items to its active offscreen scene. Installed cargo pods have the asset
   condition `IsMarketActor` and the `Trader` GUI property map `CargoPodSpec`.
3. `Ostranauts.Trading.MarketActor.Update` registers each copied pod using the
   shared ship call sign. It has no exclusion for presentation-only ships.
4. `ShipMarket.RegisterLoadedTrader` creates a persistent config for each new ID.
5. `AsyncRatingShip.Destroy` removes the copied IDs from `DataHandler.mapCOs` and
   destroys the preview objects. `MarketActor.OnDestroy` unregisters the loaded
   component through `ShipMarket.UnRegisterLoadedTrader`, which intentionally
   retains persistent configurations. That retention is necessary for real ships
   unloading from the scene, but a preview should never have registered them.
6. `MarketManager.GetJSONSave` / `ShipMarket.LoadFromSave` preserve the ghosts.
   `MarketManager.GetCargoPods` and the cargo transfer GUI enumerate configs without
   requiring a corresponding physical item.

This is a C# isolation failure. The installed/loose cargo-pod asset distinction is
appropriate; changing the assets would also affect real cargo pods. The repair
boundary is preventing presentation-only clones from joining the live economy,
not deleting persistent config whenever a real component unloads. Existing ghost
records require separate reconciliation, with cargo-preservation considerations.

## Evidence

The live inspection found four installed items on M-TZ3T and four on O-GXD.
Their markets contained four and eight configs respectively. O-GXD's extra panel
IDs were `fda7`, `2697`, `2516`, and `2cf6`; all had empty stock and no live CO.
The `option` save independently contains only four O-GXD ship market mappings and
physical pods, but eight global O-GXD market configs, including these same ghosts.
The older `plot` and `update` saves have only four global configs; `depart` already
has eight. These snapshots establish when the discrepancy appears, not the exact
historical UI click that created the four particular GUIDs.

The native fixture reproduces the mechanism with the actual `MarketActor.Update`,
registration, preview destruction, loaded-trader removal, pod enumeration and
market save/reload methods from the shipped assembly. It supplies prepared real
and `AsyncRatingShip` pod objects instead of rendering the full rating UI. Unity
object equality, singleton access and the irrelevant blockade lookup are fixtures;
the destruction stand-in delivers the component `OnDestroy` callbacks Unity would
deliver. Eleven baseline assertions pass, including a permanent-removal control.
The corrected mode passes 30 assertions including offscreen barter cargo,
same-ID visual copies, sync/transaction isolation and retained historical orphaned
stock. Existing ghost cleanup is outside the prevention patch.

Assembly SHA-256:
`8058f7a07f93d5d44678aaf8d7f749050c0d9b1c9ed1c7419eb521ddcd2097e7`

## Run

From the repository root:

```powershell
dotnet build Tests/CargoPodDiagnosis -c Release
dotnet run --project Tests/SaveJsonCompatibility/Host -c Release --no-build -- Tests/CargoPodDiagnosis/bin/Release/netstandard2.1/CargoPodDiagnosis.dll native
dotnet run --project Tests/SaveJsonCompatibility/Host -c Release --no-build -- Tests/CargoPodDiagnosis/bin/Release/netstandard2.1/CargoPodDiagnosis.dll patched
dotnet run --project Tests/SaveJsonCompatibility/Host -c Release --no-build -- Tests/CargoPodDiagnosis/bin/Release/netstandard2.1/CargoPodDiagnosis.dll repair
dotnet run --project Tests/CargoPodDiagnosis -c Release --framework net8.0 -- market '<save.zip>'
dotnet run --project Tests/CargoPodDiagnosis -c Release --framework net8.0 -- history '<saves-directory>' option depart update plot
```

Use .NET 8 for ZIP inspection because the standalone Unity Mono host does not
provide the native compression helper used by its ZIP library. Save inspection
streams past unrelated player JSON rather than retaining that large document.
The native fixture runs with the game's bundled Mono runtime. The host may print
its usual unresolved Unity-native-call warning; the assertion result and exit
status determine success.
