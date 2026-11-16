# Custom plugin builder

Build an Ostrabus variant containing your chosen **active features**. Requires
Windows PowerShell 5.1, the .NET 8 SDK, and an installed copy of Ostranauts with
BepInEx 5. Game assemblies are read locally and never redistributed.

Double-click **Build-Custom.cmd**. Choose a preset, toggle individual numbered
features, then press **b** to build. Dependencies are added automatically and
shown with `+` and a reason. Remove their dependents first to exclude them.
Profiles can be saved and loaded from the menu.

`course-controls` adds mouse-wheel adjustment alongside the game's native
Shift-drag precision controls. Existing profiles retain that feature ID.
The separate `nav-module-placement` bugfix corrects native dragging under
non-centered parent pivots; it remains independently selectable.

The result is a folder and ZIP under `dist/`, including an installer, DLL/PDB,
selection manifest and the selected profile. Nothing is installed automatically.
Close the game, extract the ZIP, then run its **Install-Custom.cmd**. The installer
can request Windows elevation when installing into Program Files.

**Deploy-Mod.cmd remains the maintainer's full-build deployment path.** Custom
generation uses fresh directories under `obj/CustomBuilder/`; it never overwrites
the normal `bin/Debug` plugin or changes the maintained gameplay source.

The optional standalone Apartment Refuel Terminal is outside the feature menu
and remains independently installed. Switching profiles preserves its DLL and
saved-item definitions; installers refresh its supply stock to match the newly
enabled native shop mods. See [the companion guide](../StandaloneMods/ApartmentRefuel/README.md).

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
`civilian-visits` includes ordinary visit completion and home-dispatch repairs.
The separate simulation feature `civilian-asteroid-visits` sets attached asteroid
visits to 5-120 game minutes; bugfix-only selections retain native visit timing.
`bounty-preservation` separately protects active bounty targets and their original
hulls from administrative retirement and cleanup, keeping native reboarding and
physical death. It adds no polling and is included in Recommended and Full.
Hidden support features are included through dependencies. These groupings are
packaging choices, not a promise of compatibility with future game patches.
Recommended includes `massfix` through `legacy-commands` and `navfix` through
`navigation-targets` so existing saves can be repaired. Other developer diagnostics
remain excluded. Ceres guardrails are still individually selectable and included
in Full; Recommended excludes the stable `ceres-navigation` ID, regardless of its
current menu number.
The Ceres feature requires the shared native-priority return integration.
General RCS budget, Navy candidate affordability and native retreat progression
repairs are included with `navigation-targets` in bugfix presets without enabling
Ceres reserve policy.
`ai-flight-controls` independently includes controller-withdrawal cancellation,
normal/SFF stale-dispatch checks, player course handoff and terminal fuel-retreat
repair. Selecting it alone preserves the same protections as the full build;
`navigation-targets` and Ceres remain optional and compose with those patches.
Torch features are individually selectable: `torch-fuel-cache` repairs fuel checks;
`torch-fuel` adds course consumption/reactor control and requires that cache fix;
`torch-momentum` adds momentum-aware player trajectories and requires `torch-fuel`
for its shared nav-console planning scope/integration. `torch-towing` remains
independent of those fuel/trajectory changes, but requires
`passive-docking-attachment` for the body-lock release handoff and
`route-completion` for correct end-of-course coasting outside GUI SFF. The attachment
feature preserves existing wreck locks during passive visits and is separately
classified as simulation; selecting towing also selects this dependency.
Bugfix presets include the fuel and towing repairs; Recommended and
Full also include the momentum-aware simulation improvement.

`cargo-loading` includes bundle loading, shared multi-actor market accounting,
freighter sizing and normal/emergency cargo arrival handoffs. Valid destination
overflow is still dumped; missing receivers retain the carrier. `economy-sff`
requires this feature so skipped-time and ordinary transfers use the same contract.

`rotation-module` adds the physical 1x1 RCS Rotation Module as QoL and requires
`ai-flight-controls` for native controller-lifetime safety. Its native item assets
are independent of the PDA app. Omitting it from a later profile retains already
installed item definitions, removes future stock additions and registers no rotation
hooks. Existing fitted modules have no custom UI in that build; remove them from
their console if the native UI warns about a missing prefab. Reselecting the feature
or full deployment restores stock. See the [module guide](../NavConsole/Rotation/README.md).

Run commands from the repository root:

SFF-only and SFF/economy compositions retain tagged PASS entry and sub-minute
decision-step physics, and require native completion/destination-integrity
repairs. Hull collisions share the jump tag without enabling orbital simulation.
The offline custom harness executes those generated paths; successful Harmony
binding alone does not establish their behavior.

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

## Native asset features

The catalog's `nativeMods` entries associate selected features with independent
native payloads. `quick-pda-hack` packages `OstrabusPdaHack` under `NativeMods/`,
with file checksums. It installs no Harmony hooks and needs no Settings app.
Custom installation explicitly enables selected native payloads and disables
omitted payloads in the active load order. Full `Deploy-Mod.cmd` also installs
the native feature, preserving an existing disabled setting; reenable it in the
game's Mods screen when wanted.

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
