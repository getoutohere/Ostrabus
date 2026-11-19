# Station Kiosk Migration

Standalone BepInEx 5 plugin for **Ostranauts 1.1.0.1, 1.1.0.2 and 1.1.0.3**. Ostrabus is optional.

Version **0.1.3** also admits the reviewed 1.1.0.3 assembly. Its native loader and
station assets match 1.1.0.2; the migration behavior and payload are unchanged.
Prevents the native scrap-kiosk migration from replacing entire stations and
discarding their saved interiors. Applies the kiosk changes to existing item
data before station construction instead. No installed vanilla assets are edited.

Use **0.1.1 or later**. The initial 0.1.0 candidate copied vanilla's two wrong
physical station targets. Version 0.1.1 applies the authored kiosks to the actual
substations and protects the mistakenly targeted parent sections separately.

Use **0.1.2 for game 1.1.0.2**. That game update adds a second native respawn
block for JATL_SUB and MSUZ_RB, including saves originating in 1.1.0.1.
Version 0.1.2 admits both reviewed assembly hashes, intercepts the appropriate
one or two kiosk respawn sites, and applies missing substation upgrades before
the new version gate. The kiosk payload and preservation rules are unchanged.

## Install, migrate, remove

1. Keep a backup from before the native migration. Close the game and run
   `Install.cmd` from this package. It detects the Steam installation; an explicit
   path can be supplied with `Install.cmd -GameDirectory "<your-game-directory>"`.
2. Load the continuation save. The BepInEx log reports the per-station results and
   `Station migration complete`. Check the stations when convenient, then save
   normally, preferably under a new name.
3. Close the game and run `Uninstall.cmd`. Continue from the newly saved game.

The mod sets the native global `1.1.0.1_Scrap_Kiosk_Fix` history entry and the
native per-station `IsDebug1-1-0-1Fixed` condition. These persist through ordinary
saving and prevent the vanilla migration from regenerating stations after the
plugin is removed. There are no plugin-specific saved item types or pending
station visits. Loading an earlier, unmigrated backup still needs the plugin.
If vanilla or 0.1.0 already set the global marker, 0.1.1 can still upgrade the two
unprocessed substations. It skips completed physical sections and does not
recreate kiosks deliberately removed after their migration.
The same two per-station conditions also suppress the new 1.1.0.2 respawn
block after removal; the game records its own `1.1.0.2_Scrap_Kiosk_Fix` history
entry when it processes that block. A save already completed with 0.1.1 retains
this protection and does not need another migration solely for 1.1.0.2.

The installer refuses unreviewed native assemblies. It changes only this
plugin's DLL, retains backups on replacement, and does not deploy or remove
Ostrabus. It cannot recover property already discarded by vanilla regeneration;
use a pre-migration backup for that.

## Preservation rules

- Upgrades BCER, BCRS, EJDR, HQCH, JATL_SUB, JFTS, MLAB, MSUZ_RB and SVIR.
  Also protects JATL and MSUZ from vanilla's mistaken regeneration. Compact,
  template-derived deltas include faction/layout variants; no player save IDs or
  player-specific coordinates are embedded.
- Aligns the delta to the saved interior's grid rotation and surviving unique
  fixtures. The station hull, identity, docking group, orbit/motion, crew,
  markets and unrelated inventory/layout are retained.
- Keeps compatible floors, wiring and other overlapping layers. Uninstalls
  blocking construction using its native installed/loose asset pairing, keeping
  item identity, damage, contents and connection settings. Only the two original
  rock-wall pieces explicitly removed by the BCER template change are retired.
- Moves obstructing loose roots as units, with their contents still attached.
  Searches within eight grid cells for floor outside kiosk/access cells; if
  clear floor is occupied, permits overlap with other loose items. A crowded
  floor does not defer the upgrade.
- Adds only missing authored floor/wall support needed by the kiosk. Adds or
  upgrades the regional kiosk using native item definitions and trader maps;
  preserves an existing kiosk's ID and inventory, and removes old trader caches.
  Does not apply the extra wear of an ordinary work-driven mode switch.
- `JATL_SUB` is Lemuria Terminal (`Station_JATL`); `MSUZ_RB` is Renbao Pavilion
  Seven (`Station_MSUZ_RB`). These are existing separate station sections, not
  alternate layouts of the parent hull. Their templates contain the new kiosks.
  JATL's landing zone (`Station_JATL_LZ`) and Panmen's main section
  (`Station_MSUZ`) receive no unauthored kiosk or other layout changes.
- Repeated loading is idempotent. An existing regional kiosk is retained, even
  if deliberately relocated. Both station-local and global saved CO records are
  handled with native precedence, including ordinary `MP|` map parts.

Planning/deserialization completes for all affected stations before changes are
committed. An unsupported layout, ambiguous registration, corrupt object record
or construction without a native loose form stops loading with a diagnostic;
the mod never falls back to whole-station regeneration. The overlap fallback
allows loose-item overlap while requiring floor beneath the entire footprint
and excluding structural obstacles and the kiosk/access cells. It does not
claim a particular pressure or atmosphere for the destination floor.

## Build and verification

```powershell
dotnet build StandaloneMods/StationMigration -c Release
powershell -NoProfile -File StandaloneMods/StationMigration/Package.ps1
```

The package contains its own installer, uninstaller, DLL, README and license.
`Deploy-Mod.cmd` continues to deploy the normal full Ostrabus build and does not
install this one-time companion.

The manifest is reproducible from the 1.1.0.0 and 1.1.0.1 assets using
`Tools/StationMigrationManifest`. Its payload describes the eleven authored
template deltas and two protected parent sections; it contains no complete station
templates or player save data.

Offline suites live in `Tests/StationMigrationPlanCheck` and
`Tests/StationMigrationNativeCheck`. Their reports distinguish deterministic
JSON/grid tests, native loader admission and Harmony binding from Unity scene
reconstruction and trade-UI playtesting. No test writes a player's save.

Validation on the reviewed assembly: 262 deterministic assertions (including
all thirteen template/section cases and eleven saved sections), 55 native checks,
11 installer checks, and a composed binding audit of 307 patch classes with
zero failures/warnings. A live continuation load and ordinary save have now been
verified against the same-time pre-migration snapshot: all expected item IDs and
container/stack links were preserved, all nine kiosk CO records persisted, both
protected parent layouts were unchanged, and all eleven section flags plus the
global marker persisted. The expected two rock-wall removals, two dolly moves
and two kiosk conversions were the only changes to existing item identities or
positions. Reloading that saved continuation with the companion still installed
also passed: all eleven native section flags read back as 1, and no second
migration batch or native kiosk regeneration ran. A subsequent restart/load with
the companion removed also passed: BepInEx loaded only Ostrabus and Apartment
Refuel Terminal, all eleven live completion flags remained 1, and native kiosk
regeneration did not run. The loaded save archive matches the independently
verified migrated archive. Stations remained shallow, so materialized
interior/trade-UI acceptance remains pending.
