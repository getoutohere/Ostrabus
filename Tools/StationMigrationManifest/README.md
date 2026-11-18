# Station migration delta manifest

Regenerates the standalone plugin's compact embedded manifest from preserved
1.1.0.0 and installed 1.1.0.1 assets. No whole station templates, player IDs or
player-save coordinates are packaged. Kiosk placements, required local supports,
old-kiosk replacement sites, the two retired rock walls and unique alignment
fixtures come from the native templates. Physical station IDs are verified
against `NewGame.aSpawnStations`, independently of the native migration list.
Two protected parent sections have no authored kiosk; the kiosks are placed in
the existing JATL_SUB and MSUZ_RB sections instead.

```powershell
dotnet run --project Tools/StationMigrationManifest -c Release -- <old-data> <new-data> StandaloneMods/StationMigration/Assets/migration.json
```

The generator refuses unexpected non-rock authored deletions or kiosks appearing
in previously unchanged parent sections. Such content changes require review rather
than silently broadening the migration. The plugin additionally pins the reviewed
native assembly and validates saved-layout registration before changing data.
