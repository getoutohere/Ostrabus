# Detached station migration regression checks

Links the standalone plugin's actual `Catalog`, `Grid` and `Migration` sources.
Uses 1.1.0.0/1.1.0.1 vanilla JSON; an optional saved ZIP provides real layouts
and selectively streamed CO records. The ZIP is opened read-only. Synthetic
blueprints are materialized as saved fixtures with explicit CO records, without
mistaking blueprint-only actor payloads for a saved world's identities.

```powershell
dotnet run --project Tests/StationMigrationPlanCheck -c Release -- <old-data> <new-data> StandaloneMods/StationMigration/Assets/migration.json [save.zip]
```

Covers thirteen template/variant cases, independent grid rotation/translation,
uninstallation of blocking walls and containers, retained damage/settings/nested
contents, crowded-floor overlap, map parts without redundant CO records,
station-local precedence, repeat loading and JSON persistence. The optional
save exercise also verifies item-ID preservation and input immutability.

This does not execute Unity physics, scenes, dynamic damage rejection or the
trade UI. The native suite separately checks actual loader integration and
admission. Reports are diagnostic artifacts under `obj/Compatibility-1.1.0.1`.
