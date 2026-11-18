# Read-only station kiosk migration study

Compares the eleven changed 1.1.0.0/1.1.0.1 station templates, including faction
variants, and the nine new kiosk definitions. Optionally inspects a saved ZIP.
No game assembly execution, hooks, asset edits, save writes or extracted saves.

```powershell
dotnet build Tools/StationMigrationStudy -c Release
dotnet Tools/StationMigrationStudy/bin/Release/net8.0/StationMigrationStudy.dll <old-StreamingAssets/data> <new-StreamingAssets/data> <report.json> [save.zip]
```

The ZIP's SHA-256 must match before/after. Root metadata is streamed without
retaining the large global CO table; installation conditions are retained only
for items belonging to the BCER/BCRS layouts under study. Saved template
resolution follows the native respawn helper's NewGame lookup and preserves
variant names.

Root spatial deltas compare definition, position and rotation as multisets.
Floors, conduits and fixtures can share coordinates; this is not a native item
fit test. Template GUIDs are not used as generated-world identities. No candidate
match authorizes automatic removal, movement or conversion.

The optional saved-grid projection aligns BCER/BCRS interiors using their native
grid rotation and a majority of unique installed fixtures. It projects authored
socket requirements and front clearance, retires only matching authored removals,
and proposes nearby loose-item relocations on connected clear station floor.
Destinations exclude the new fixture/access space, other loose items and earlier
reservations. Synthetic overlap and structural obstruction cases exercise both
branches. Missing saved states prevent a successful projection.

This does not execute live Unity placement, dynamic damage, room/door/docking
checks, item conversion or save/reload. Item dimensions follow native defaults
and integer division, including incomplete trailing socket rows. No physical
objects or source saves are changed by a plan.

See the [feasibility findings](../../Build/STATION-KIOSK-MIGRATION-FEASIBILITY-1.1.0.1.md).
