# Building and testing Ostrabus

Run these commands from the repository root.

The [0.6.0 snapshot](RELEASE-0.6.0.md) targets Ostranauts 1.1.0.0 with partial
in-game acceptance and separately documented offline-only additions. Read the
[compatibility audit](COMPATIBILITY-1.1.0.0.md) and
[feature ledger](FEATURE-COMPATIBILITY-1.1.0.0.md) for validation evidence and boundaries.
The separate [content-availability audit](CONTENT-AVAILABILITY-1.1.0.0.md) covers
new equipment, ship-design sources, stations, residents and narrative access in
the player's continuing save; it does not infer availability from hook compatibility.
Use the preserved `Ostrabus-5.1.0` source tag and a 1.0.1.10 installation to build
the previous game-version baseline; changing the reference path does not make the
current native integration compatible with older assemblies.

## Build and install

Requires the .NET 8 SDK and a local Ostranauts installation with BepInEx.
Game assemblies are referenced from that installation.

```powershell
dotnet build Ostrabus.csproj -c Debug
.\Deploy-Mod.cmd
```

The standard Windows Steam installation is discovered from system folder settings.
For another installation, set `OSTRANAUTS_DIR` to the game directory before building
or running tests. Deployment also accepts `-GameDirectory`. Paths stay in your
environment or ignored `*.local.*` files, rather than source control.

To create a distribution archive:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Package-Mod.ps1
```

Close the game before deployment. The installer verifies copied files and retains
disabled backups of predecessor plugin assemblies. Existing configuration, saved
proximity ranges, return orders, and native app identifiers remain compatible.

For category presets or individual feature choices, run
`CustomBuilder/Build-Custom.cmd`; see the [custom builder guide](../CustomBuilder/README.md).
Custom builds and packages are isolated from normal build output.
`Deploy-Mod.cmd` continues deploying the full normal build.

The optional [Apartment Refuel Terminal](../StandaloneMods/ApartmentRefuel/README.md)
is compiled and installed separately. `Package-Mod.ps1` bundles its standalone
installer under `StandaloneMods/ApartmentRefuel/` without activating it. Full and
custom installers preserve an installed companion and refresh its composed supply
stock after their own native-mod selection changes. They do not replace its DLL.

The independent [1.1 migration prototype](../StandaloneMods/SaveMigration110/README.md)
is unnecessary for an already-upgraded save and is not recommended. Its two
body-map assignments do not enable content after native initialization. The
standard full/custom deployments do not install or activate it. Read its findings
for the corrected distinction between initial generation and saved world state.

## Project layout

Full deployment registers the assets-only `OstrabusPdaHack` native mod alongside
Settings, honoring custom Mods locations and preserving native disabled states.
Full deployment also installs the physical [RCS Rotation Module](../NavConsole/Rotation/README.md).
It also installs the assets-only [CO2 scrubber audio repair](../MiscBugFixes/ScrubberAudio/README.md)
as `OstrabusScrubberAudio`; no additional Harmony hook is needed.
Custom installers enable selected native features and disable omitted ones;
previously installed rotation item definitions are retained without new shop stock
to preserve items in existing saves.
PDA hacking installs no game hook and works independently of the Settings app.

- Feature directories contain their own implementation and notes: `AlternativeWork`,
  `CeresNavigation`, `Haulers`, `Navigation`, `Orbital`, `ShipboardAction`, `Venus`,
  `Sensors`, and `EMPropagation`.
- `MiscBugFixes`, `QoL`, `Hotkeys`, and `NavConsole` contain smaller patches/features.
- `Inspection` provides the localhost API; see its API documentation.
- `PdaSettings` contains the settings UI, native assets, and installation tooling.
- `Tests` contains offline native-code regression harnesses; `Tests/Fixtures` holds
  selected inputs, with local filesystem paths removed.
- `Tools`, `Guides`, and `Notes` contain analysis utilities and findings.
- `Build` and `Directory.Build.props` share installation discovery.

Build outputs, packages, logs, private overrides, and raw gameplay captures are
ignored. Only selected regression fixtures are versioned. Game assemblies and
complete saves are not required in the repository.

## Offline checks

Run from the repository root. The host uses the game's bundled Mono runtime so
the tests can exercise native methods and Harmony without launching Unity.

```powershell
dotnet build Tests/SaveJsonCompatibility/Host -c Release
dotnet build Tests/AiPlannerDiagnosis -c Release
dotnet run --project Tests/SaveJsonCompatibility/Host -c Release --no-build -- Tests/AiPlannerDiagnosis/bin/Release/netstandard2.1/AiPlannerDiagnosis.dll ceres
```

The same harness also supports `lifecycle` and `recovery`. Other test directories
document their own checks. Native assets are read from the configured installation;
exploratory diagnostics may additionally require locally captured input files.

The following additional suites need only the configured game installation and
checked-in fixtures. Build the named project in Release, then pass its
`bin/Release/netstandard2.1/<project>.dll` to the same Mono host:

| Project under `Tests/` | Argument after DLL | Coverage |
| --- | --- | --- |
| `AIShipLifecycleCheck` | `patched`; `arrival`; `rescue`; `instant-dock`; `retrievers-patched` | Native rendezvous/crew safety, station arrival, rescue, physical docking and SFF retrieval. No argument runs baseline reproduction only. |
| `AutonomousDisembarkCheck` | none | Native needs/leisure selection, early disembark filtering, local alternatives and permission overrides. |
| `DepartureWaypointCheck` | none | Native departure binding, station hierarchies and player-course controls. |
| `OrbitalCheck` | `economy` | Production, freight, loading, SFF stage continuation and economic inspection. |
| `OrbitalCheck` | `decisions` | Captured Ceres coast/impact, native patrol-phase handoff, decision-aware SFF scheduling, control exclusions and iterator binding. |
| `OrbitalCheck` | `pass-handoff-study` | Non-GUI coast-handoff negative controls, completion-boundary prototype and actual deployed PASS adapter comparison. |
| `SensorRuntimeCheck` | none | Sensor/EM hooks, offset consumers, paused outer-system contacts and fallback caching. |
| `VenusDollyCheck` | none | Dolly burden, unchanged physical mass and location transitions. |
| `DollyMassCheck` | none or `--graph <export.json>` | Native mass accounting and repair of nested containers, anatomy and characters; exclusions, rollback and idempotence. Optional detached save graph. |
| `VorbRetrievalCheck` | none | Native retrieval assignments, ownership, exclusions and saved controllers. |
| `ScavFuelDiagnosis` | none; `fuel`; `fuel-patched`; `refuel`; `sos` | Native scav Hold/sequence, mixed-mode fuel baseline/fix, capped refueling API and authored SOS entry/eligibility diagnosis. |
| `PdaSettingsCheck` | none | Settings registry, native assets and toggles. |
| `PortraitCompositionCheck` | none | Native portrait cold/warm paths, pixel equivalence, raw channel layouts, fallback handling, buffer allocation and guarded transpiler binding. |
| `SocialsProfilingCheck` | none | Temporary profiling registration, nested timing, deferred UI capture, native exceptions, timeout and removal of all diagnostic hooks. |
| `RotationModuleCheck` | none | RCS PID, native fuel/translation, dock-group anchor rotation, command save/load, SFF queue, hardware and cancellation boundaries. Native asset install fixtures: `Tests/RotationModuleCheck/InstallCheck.ps1`. |
| `NavModulePlacementCheck` | none | Native offscreen-placement reproduction, parent pivots, centered-layout preservation, repeated conversion, scales, bounds and IL drift. |
| `CoursePlotCheck` | none | Native thrust inversion, wheel increments/limits, native Shift-drag accumulator coexistence and hook binding. |
| `ApartmentRefuelCheck` | none | Standalone native refuel setup/payment/delivery, apartment fit, quote validation and conversion metadata. Installer/stock fixtures: `Tests/ApartmentRefuelCheck/Test-Installer.ps1`; full Unity work/save lifecycle remains a live check. |
| `MultiShipBarterAudit` | none | Native failure reproductions plus corrected multi-ship zone rows, loader ownership/cancellation and buy/sell/rebuy writeback, with optimization and exception controls. |
| `TorchTowCheck` | none | Native planner, grouped course playback, combined mass, brace-release aborts, save reconstruction and manual/anchored controls. |

Build `Ostrabus.csproj -c Debug` before suites that reference the plugin DLL.
Run native suites sequentially; the Mono host shares a runtime staging directory.
For the sensor model and vanilla comparison report, run
`powershell -NoProfile -ExecutionPolicy Bypass -File Tools/SensorComparison/Run.ps1`.
Generated reports stay under ignored `obj/`.

The [native torch-course oracle](../Tools/TorchNativeOracle/README.md) also covers
the fuel/control and momentum-aware player-planning changes, including
offline SFF, towing, save reconstruction and moving-target convergence tests.
