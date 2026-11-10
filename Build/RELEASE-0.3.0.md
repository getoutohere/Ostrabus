# Ostrabus 0.3.0

Build custom Ostrabus plugins with category presets or individual feature choices,
automatic dependency resolution and reusable JSON profiles. Run
`CustomBuilder/Build-Custom.cmd` from a source checkout; the .NET 8 SDK, Windows
PowerShell and the installed game/BepInEx assemblies supply the build environment.

- Catalog 76 features across bug fixes, quality of life, performance, simulation,
  developer tools and shared support. Presets exclude experimental save JSON.
- Classify useful-zoom hull outlines and loose-meat cover changes as quality of
  life; bugfix-only presets omit them. Meat duplication remains a bug repair.
- Generate selected initialization, patch registration, cleanup and frame calls
  at build time. Remove unselected Harmony annotations offline; retain normal
  patch order and filter optional shared behavior. Unused helper code may remain
  in the DLL, with no runtime feature-selection loop.
- Filter the PDA settings to included features and package its native assets only
  when selected. Custom installation preserves other native mods, retains file
  backups and rolls back failed writes. DLL/PDB checksums accompany each ZIP.
- Keep custom build output under `obj/CustomBuilder` and packages under `dist`.
  The normal full build and `Deploy-Mod.cmd` remain independent.
- Align project, plugin and PDA metadata at 0.3.0. Keep the existing plugin GUID,
  configuration filename and native app identity.

## Validation

The 0.3.0 preparation passed:

| Check | Result |
| --- | --- |
| Builder logic/composition assertions | 353 passed |
| Compiled, verified and native-bound profiles | 22 passed |
| Disposable installer/rollback assertions | 39 passed |
| Clean source snapshot build | Zero warnings/errors; torch files absent |
| Clean source snapshot Harmony bindings | 179 bound; zero failures/warnings |
| Full local maintainer Harmony bindings | 184 bound, including uncommitted torch classes |
| Native PDA assets, registry and hooks | 88 passed |

Composition checks cover dependency closure, cycles, ownership, mixed/shared
patch classes, filtered settings, omitted callbacks and generated syntax. The
22-profile matrix compiles and verifies each selection, checks repeatable
finalization and normal hook ordering, and binds the actual generated startup
registrations with the game's bundled Mono runtime. Selected sensor/EM/worktrace
dynamic hooks are also installed and removed offline.

Installer checks use disposable game layouts to exercise PDA inclusion/exclusion,
custom mod paths, retained metadata, predecessor handling, checksums and rollback
after a deliberately blocked write. They also verify that custom builds leave
the normal Debug DLL untouched. PDB source paths are mapped and packages contain
no game or build-tool assemblies.

Live startup of custom profile `398ee09db837` was checked before the version bump:
its installed DLL matched the package, BepInEx loaded only one plugin, all 82
selected patch classes initialized without plugin/Harmony errors, and the
generated plugin had no frame callback. This confirms startup of that selection,
not live gameplay validation of every possible combination.

## Excluded work

The two torch gameplay changes still await in-game playtesting. Their three
implementation files and `Tools/TorchNativeOracle/` remain outside the commit.
Existing torch documentation remains marked pending. Local maintainer builds can
include those files; a clean source checkout and custom packages exclude them.

This preparation stages source and documentation. It does not create a commit,
tag, push or hosted release; generated validation packages remain ignored.
