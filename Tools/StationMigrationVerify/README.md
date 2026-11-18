# Read-only migrated-save verification

Compare a pre-migration archive with an actual post-migration save, independently
of executing the migration planner again:

```powershell
dotnet run --project Tools/StationMigrationVerify -c Release -- <baseline.zip> <migrated.zip> StandaloneMods/StationMigration/Assets/migration.json <private-report.json>
```

Uses physical section IDs from the manifest, retaining station layouts and only
their relevant saved COs while streaming past the remainder. Reports retained,
removed, added and changed item identities; nested parent/slot links; saved
container/stack/ownership/persistent references and inventory coordinates;
GUI settings and condition overrides; native completion flags; kiosk
definitions/CO admission data; and docking/tow links. Station-local CO records
take precedence over root records, matching native restoration.

Both ZIP hashes must match before/after. No save writes, extraction, game code,
scene loads or live API calls. The report lists differences for assessment; it
does not automatically classify every difference as a migration bug. Epochs
are included so elapsed gameplay is not mistaken for a controlled same-time
comparison. It does not prove trade UI or full scene reconstruction.
