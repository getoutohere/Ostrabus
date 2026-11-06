# Building and testing Ostrabus

Run these commands from the repository root.

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

## Project layout

- Feature directories contain their own implementation and notes: `AlternativeWork`,
  `CeresNavigation`, `Haulers`, `Navigation`, `Orbital`, `ShipboardAction`, and `Venus`.
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
