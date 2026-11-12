# Custom plugin builder

Build an Ostrabus variant containing your chosen **active features**. Requires
Windows PowerShell 5.1, the .NET 8 SDK, and an installed copy of Ostranauts with
BepInEx 5. Game assemblies are read locally and never redistributed.

Double-click **Build-Custom.cmd**. Choose a preset, toggle individual numbered
features, then press **b** to build. Dependencies are added automatically and
shown with `+` and a reason. Remove their dependents first to exclude them.
Profiles can be saved and loaded from the menu.

The result is a folder and ZIP under `dist/`, including an installer, DLL/PDB,
selection manifest and the selected profile. Nothing is installed automatically.
Close the game, extract the ZIP, then run its **Install-Custom.cmd**. The installer
can request Windows elevation when installing into Program Files.

**Deploy-Mod.cmd remains the maintainer's full-build deployment path.** Custom
generation uses fresh directories under `obj/CustomBuilder/`; it never overwrites
the normal `bin/Debug` plugin or changes the maintained gameplay source.

## Presets and automation

| Preset | Requested features |
| --- | --- |
| `bugfixes` | Bug repairs only |
| `bugfixes-qol` | Bug repairs and quality of life |
| `bugfixes-performance` | Bug repairs and optimizations |
| `recommended` | Bug repairs, quality of life, optimizations, all simulation changes except Ceres navigation guardrails, and legacy maintenance commands; menu default |
| `full` | All nonexperimental consumer features, including every simulation change and legacy maintenance commands |
| `developer` | Full consumer selection plus API/debugging/profiling tools |
| `empty` | No features; useful as a starting point or composition control |

All presets exclude experiments; `save-json` can be selected explicitly.
Hidden support features are included through dependencies. These groupings are
packaging choices, not a promise of compatibility with future game patches.
Recommended includes `massfix` through `legacy-commands` and `navfix` through
`navigation-targets` so existing saves can be repaired. Other developer diagnostics
remain excluded. Ceres guardrails are still individually selectable and included
in Full; Recommended excludes the stable `ceres-navigation` ID, regardless of its
current menu number.
Torch features are individually selectable: `torch-fuel-cache` repairs fuel checks;
`torch-fuel` adds course consumption/reactor control and requires that cache fix;
`torch-momentum` adds momentum-aware player trajectories and requires `torch-fuel`
for its shared nav-console planning scope/integration. `torch-towing` remains
independent. Bugfix presets include the fuel and towing repairs; Recommended and
Full also include the momentum-aware simulation improvement.

Run commands from the repository root:

```powershell
# Build without the menu:
.\CustomBuilder\Build-Custom.ps1 -Preset bugfixes -NoMenu

# Exact choices; required dependencies are resolved automatically:
.\CustomBuilder\Build-Custom.ps1 -Features door-autoclose,crew-hotkeys,pda-settings

# Reuse a saved selection:
.\CustomBuilder\Build-Custom.ps1 -ProfilePath .\CustomBuilder\profile.example.json

# Inspect a preset and save it without compiling a plugin:
.\CustomBuilder\Build-Custom.ps1 -Preset full -PlanOnly -SaveProfile .\my-build.local.json
```

Use `powershell -NoProfile -ExecutionPolicy Bypass -File` if script execution is
restricted. Set `OSTRANAUTS_DIR` or pass `-GameDirectory` for a nonstandard install.
Use `*.local.json` for private selections; those files are ignored by Git.

`features.json` is the authoritative catalog of stable IDs, categories, required
features, patch ownership, lifecycle calls and PDA controls. Profiles list only
requested IDs. The packaged `selection.json` also records resolved dependencies,
selected patch classes and the selection hash. `package.json` records binary
checksums. The selection hash identifies the recipe, not a source revision;
different source revisions can produce different binaries for the same recipe.

## What gets enabled

Selection determines which patches and lifecycle functions exist in the generated
bootstrap. An included optional feature still honors its normal configuration
default: inclusion does **not** turn on Orbital, Alternative Work, sensors or other
opt-in settings. Enable those through the normal console/config/PDA controls.

The PDA app is itself optional. When included, it shows only controls for included
features. When excluded, the package contains no PDA assets and installation
disables only Ostrabus's `TemplateSettings` native-mod entry. Other native mods
and their metadata are retained. Reinstalling a profile with the PDA explicitly
reenables that entry. Existing BepInEx configuration is retained across profiles.
The normal full installer preserves native-mod disabled states; if you return to
it after excluding the PDA, reenable Ostrabus Settings in the game's Mods screen.

Install one Ostrabus variant at a time. Full and custom DLLs use the same plugin
identity and filename. Switch profiles with the game closed, then restart it.
The installer keeps reversible file backups with `.bak` extensions, which BepInEx
does not load. Do not place an additional copy in another plugin directory.

## Runtime cost and boundaries

There is no runtime feature manifest, dependency resolver or per-feature selection
branch. Generated startup explicitly registers selected patch classes and runs
their initialization. Omitted features do not install their hooks, scene/load
listeners or frame callbacks. A build with no frame-driven features has **no
Unity Update method**. Included feature code retains its existing costs and live
toggles.

This is feature composition, not whole-program linking: inactive shared helper
code can remain in the DLL. It has no automatic patch registration. In particular,
selecting developer inspection/maintenance tools intentionally retains their
on-demand diagnostics and intervention utilities; their commands can call support
code when explicitly requested. Do not select developer tools for a minimal
consumer build.

Shared behavior is split during generation where necessary: proximity range and
parked-hull collision policy, render geometry and physical collision covers, core
gravity/coasting/SFF/economy, and mixed legacy command/social patches. The original
files are not rewritten. Native asset behavior and gameplay algorithms remain
owned by their existing modules.
Barter presentation (`barter-zones`) is QoL; scoped loader cancellation
(`async-ship-loading`) and item persistence (`barter-writeback`) are independent
bugfix features. The optional `barter-index` optimization requires writeback
safety so its native fallback and optimized path have the same guarantees.
Selected hooks retain the normal compiler's registration order, including equal
Harmony priorities; the builder does not alphabetize their execution order.

## Validation and maintenance

Every build validates catalog ownership/dependencies, generates fresh sources,
compiles, removes unselected Harmony annotations offline, verifies the resulting
assembly and binds its selected patches against the shipped game assembly using
the bundled Mono runtime. Binding logs stay under the build directory; Unity-only
internal-call notices from the offline host are not gameplay failures.

For the composition regression matrix:

```powershell
.\CustomBuilder\Test-CustomBuilder.ps1
.\CustomBuilder\Test-Installer.ps1
```

The matrix covers each preset, independent shared-feature selections, the empty
control and an all-features selection. Installer tests use disposable layouts
under `obj/`; they never deploy to the running game. Existing module regression
suites remain the evidence for gameplay behavior; binding alone is not a live
gameplay test of every possible feature combination.

When adding a patch, give it an owner in `features.json`. Unknown patch classes,
unknown method-scoped patches, ownership overlaps and dependency cycles stop the
build. When changing bootstrap initialization or adding cross-module calls, update
the catalog and, where necessary, `Tool/SourcePruner.cs`, then run the matrix.
Revalidate against the new shipped assembly after a game update.
If a normal Debug DLL exists, the matrix also checks its relative hook order;
rebuild that normal DLL first when testing a changed source checkout.
