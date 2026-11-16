# Optional 1.1 save migration

**Unnecessary prototype for an already-upgraded save; installation is not
recommended.** The two body-map assignments have no demonstrated gameplay
benefit after native 1.1 has populated the saved stellar arrays. They are inputs
to initial environment generation, not an ongoing content/cleanup controller.
This prototype was incorrectly promoted as useful migration work. The findings
below are retained as investigation history, not a reason to apply it.

Standalone BepInEx 5 plugin for the reviewed Ostranauts 1.1.0.0 public-test
assembly. It works without `Ostrabus.dll`, introduces no native asset definitions
and is excluded from the omnibus/custom builds and normal deployment.

**Most new 1.1 content already reaches old saves through native loading.** The
fresh-save comparison and code/asset investigation are recorded in
[FINDINGS.md](FINDINGS.md). This plugin finishes the omitted Ganymede/Venus body
map metadata while preserving the legacy populations. It does not need to create
the new regional boneyards, Earth platforms or shop stock in the audited save.

## Use

1. Build/package this standalone plugin and close the game.
2. Run `Install.cmd`, then load the existing save. Loading only logs a read-only
   plan; installing the plugin does not apply a migration.
3. In the game console, run `migrate110 plan`. For the audited legacy save this
   reports Earth 800 unchanged, Ganymede 0 to 801 and Venus 0 to 802.
4. Run `migrate110 apply` to apply the plan. Save normally afterward.
5. Close the game and run `Uninstall.cmd` if the one-time plugin is no longer
   wanted. The saved fields use the native format and require no plugin to load.

`migrate110 status` repeats the plan. Plans are rebuilt at command time. An
already-correct fresh/migrated save produces zero assignments. A custom nonzero
map, missing native definitions/body, incomplete Earth upgrade or unproven legacy
origin blocks the write. The installer and plugin refuse another assembly build.

The operation changes only missing `BodyOrbit.EnvironmentMapID` values and adds
one completion string to native `CrewSim.aPatchesApplied`. Native `GetJSONSave`
and `LoadManager` persist them. No custom save format is introduced. Existing
native upgrade markers are preserved. A commit failure restores changed maps
and the newly inserted marker; a stale plan cannot partially apply.

Wrecks, depletion, physical/deferred identities, positions, orbits, ownership,
crew, stations, apartments, cargo, markets and existing items are preserved.
There is no global environment regeneration, station respawn, forced restock,
loot reroll or conversion of visited hulls. Legacy wrecks retain their existing
representation; assigning a body's map does not retag them as new environment
objects. A fully depleted legacy field stays depleted.

New suits, RTGs, tanks, thrusters and ship designs use current definitions and
native acquisition/spawn paths. This tool does not convert legacy suit variants
or replace old ship blueprints. It also does not transplant a fresh save's random
populations or reset the player's progression to the new-game loot balance.

The only hook is console-command dispatch. One native load-event subscription
provides the read-only summary. There is no Update loop, background observer,
telemetry history or automatic save. Remove the plugin after saving if desired.

## Build/package

```powershell
dotnet build StandaloneMods/SaveMigration110 -c Release
powershell -NoProfile -ExecutionPolicy Bypass -File StandaloneMods/SaveMigration110/Package.ps1
```

The package is independent under `dist/`. Its installer copies one DLL into
`BepInEx/plugins/OstrabusSaveMigration110/`, verifies the copy and preserves
reversible backups. It writes no Mods loading order, native assets or save files.
For a nonstandard game path, pass `-GameDirectory` to `Install.ps1`.

The root `Deploy-Mod.cmd` continues deploying the full Ostrabus build; it does
not install or remove this separate optional plugin.

## Validation

The native regression harness is documented in
`Tests/SaveMigration110Check/README.md` in the source repository. It verifies
fresh-save no-op, the two-assignment legacy plan, partial/already-migrated cases,
depletion/identity preservation, blocked/stale inputs, commit rollback, actual
native body serialization/reloading and console hook binding. Private fresh and
legacy save slices are optional external fixtures, never redistributed.

No live migration has been applied to the player's save during development.
