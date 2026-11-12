# Restore saved parents before resolving destruction

November 10, 2026. Implemented in `LoadedInventoryParentPatch.cs`.
Custom-builder feature: `saved-inventory-parents` (bugfix).

## Evidence

The session logged 144 unprocessed subitems on OKLG_UNK and four on H-3UTJ.
Read-only dependency audits of an earlier `newship` save and the latest
`autosave_6_undock` save reproduce exactly those counts. All 148 records descend
from 19 over-damaged clothing items. Seventy are pocket/system containers; 78 are
equipment, including batteries, oxygen canisters, filters, tools and weapons.

The same records and their saved CO states remain in both saves. This case is
inaccessible saved inventory, not demonstrated permanent deletion. Their saved
state should not be replaced with fresh starting loot.

## Cause and repair layer

`Ship.SpawnItems` skips an item when `IsItemDestroyed` judges its saved damage
terminal. Descendants subsequently cannot find that parent, so reconstruction
stops with the warning. The loader bypasses authored destruction handling before
the hierarchy exists.

Assets already provide the required mechanism: clothing damage recipes select
`MSDestroyEVASuit`, `MSDestroyEVASuitDmg` or `MSDestroyClothes` and their native
mode-switch destinations. `CondOwner.ModeSwitchInternal` transfers contained
items and handles locked pocket containers by extracting their contents before
destroying the obsolete pocket. The C# load-time shortcut prevents that mechanism
from running. No asset policy change is necessary.

## Scope and performance

During saved `Ship.SpawnItems` reconstruction, build an ordinal set of parent IDs
referenced by child records that also have saved CO state. A transpiler guards
the two existing damage-culling calls: referenced parents continue through native
creation; other items retain the original damage test. The parent keeps its saved
damage. Native reconstruction and subsequent destruction processing own outcomes.

No healing, item spawning API, forced spills, relocation, JSON rewriting, or new
destruction controller. Missing-parent failures remain failures. Template loot
generation and other loaders are unchanged. This does not claim to repair every
reason for an unprocessed-subitem warning.

The parent index is O(layout records); membership checks are O(1) expected. Pooled
thread-local scopes support nesting, restore the outer scope on exceptions, and
clear saved IDs on return. There is no per-item Harmony prefix or update loop.
The guarded native calls occur only during loading. No custom saved state or
dependency on the mod after saving.

## Validation

The extended `Tests/InventoryPerformanceCheck` passes 41,093 native Mono checks.
These include original behavior reproduction, container and slot restoration,
damaged nested parents, unchanged destroyed leaves and missing parents, unchanged
templates, input-record immutability, nesting and exception cleanup, plus existing
inventory optimization regressions. The real 148 dependency IDs restore through
the native loader in fixtures; engine creation/geometry and slotting are controlled
boundaries. This is not a live Unity destruction/spill test.

On .NET 8, 10,000 warmed parent-scope setup/teardown cycles allocate zero bytes.
Existing allocation tests for inventory indexes also pass. Native hook binding
passes with all current plugin patches together.

`Tools/InventoryLoadAudit` streams the main save one CO at a time, keeping selected
records only, and verifies the source ZIP hash is unchanged. Its dependency model
estimates loose-item health from definitions and overlays; installed-item
destruction, engine placement and arbitrary slot failures require native review.
It is diagnostic evidence, not a save repair utility.

Local ignored evidence: `obj/ConsoleIssues/inventory-newship.json`,
`inventory-current.json`, `inventory-tests.log`, and `patch-binding.log`.
