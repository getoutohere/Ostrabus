# Non-destructive station kiosk migration: feasibility

Investigated 2026-11-17 against native assembly SHA-256
`967927e609d93e5233fe16260d61de3cb4aa5d753b738ed8a6ba1dba4cf9fde0`.

**Implemented as a separate [Station Kiosk Migration plugin](../StandaloneMods/StationMigration/README.md).**
It is not compiled into Ostrabus or installed by `Deploy-Mod.cmd`.

The generalized implementation plans all affected station item/CO changes before
native system initialization. One loader transpiler replaces the system-init
call and guards only the kiosk-specific respawn call. No station visit, forced
simulation, custom saved journal or pending per-station work is needed.

The user's final conflict policy supersedes the initial proposal: uninstall
blocking construction to its native loose form, preserve IDs/state/contents,
move loose roots aside, and allow overlap with loose items when nearby floor is
occupied. Native global and per-station completion flags make the mod removable
after saving. The initial 0.1.0 candidate misidentified two existing station
sections as variants and inherited vanilla's targeting mistake. Version 0.1.1
corrects this: JATL_SUB/Lemuria Terminal and MSUZ_RB/Renbao Pavilion Seven receive
their authored kiosks; JATL/Atlantis Landing Zone and MSUZ/Panmen's main section
are protected without unauthored layout changes. Manifest bindings are now
checked against the native world spawn assets, not copied from the migration list.

The implementation's maintained deterministic and native suites cover template
variants, rotation/translation, construction, nested inventory/settings,
crowded-floor overlap, local/global CO precedence, map parts, repeat loading,
JSON persistence, Harmony composition, loader admission, completion flags and
removal. Full Unity scene reconstruction and trade-UI acceptance remain pending.
The original offline work modified no player save or installed game DLL. The
subsequent user-run live load/save validation is recorded below.

## Live continuation load/save verification (2026-11-17)

The installed standalone DLL matches the tested 0.1.1 package exactly. BepInEx
reports successful completion of all eleven physical section decisions; no
migration exception or station-save failure appears in the session logs.

The pre-migration `next` archive and user-created `1.1.0.1` archive have identical
simulation epoch `65634699370.0248`. Independent read-only comparison using
`Tools/StationMigrationVerify` finds:

- 97,258 existing item IDs retained. The only removed IDs are BCER's two authored
  original rock-wall tiles, at saved grid positions (-42, -96) and (-41, -96).
- Seven new kiosk items; HQCH and MLAB's two kiosk conversions retain their IDs.
  All nine corresponding saved CO definitions match their kiosk layout entries.
- Only two other existing item layout changes: BCRS's obstructing dollies move
  from x approximately 95 to 97, retaining their y positions, IDs and settings.
- All 5,639 JATL_SUB and 10,350 MSUZ_RB original item IDs are retained. The two
  parent sections JATL and MSUZ have no item additions/removals/layout changes.
- 31,820 nested item parent/slot links checked; 58,024 existing saved CO records
  checked for container, stack, ownership, persistent and inventory-coordinate
  fields, with no differences. The only GUI-setting difference is the intended
  removal of MLAB's obsolete kiosk Trader map; no condition overrides differ.
- All station docking and secured-brace links unchanged. Native global marker
  and all eleven per-section completion flags persist in the new archive.
- Source/archive hashes match before and after inspection; the verifier does
  not write saves or alter the live game.

Reports: `obj/Compatibility-1.1.0.1/live-migration-save-verification.json` and
`.log`. This verifies native continuation loading and save persistence, not
materialized station interior/trade-UI acceptance. Subsequent reload checks are
recorded below.

The user subsequently reloaded `1.1.0.1` with the companion retained. Live API
generation advanced from 7 to 15 and completed loading. All eleven section COs
read back `IsDebug1-1-0-1Fixed=1`. The same-session logs still contain only the
original eleven migration decision lines and one completion message; there is
no second migration batch, native kiosk migration invocation or guard failure.
The second native saved-CO index contains 447,653 records versus the initial
447,588, matching the persisted 65-record net change (seven new kiosks plus
sixty required support records minus two retired rocks). This validates the
completed-save skip path in game with the companion installed. Report:
`obj/Compatibility-1.1.0.1/live-migration-reload-verification.json`.

The user then restarted and loaded the same save with the migration companion
removed. BepInEx lists only Ostrabus and Apartment Refuel Terminal; the live API
is ready on the expected native/plugin MVIDs. All eleven section completion flags
remain 1. No native kiosk migration invocation or migration failure appears in
the new session, and the native saved-CO index retains 447,653 records. The source
archive hash still matches the independently verified migrated archive. All
eleven sections remain shallow; kiosk IDs are absent from the live materialized
CO map, and no forced loading was performed. This passes the companion-removal
load check, while materialized interior/trade-UI acceptance remains pending.
Report: `obj/Compatibility-1.1.0.1/live-migration-removal-verification.json`.

The following records the original feasibility investigation, including the
earlier deferral proposal. The linked standalone README governs current behavior.

All eleven changed templates, nine new kiosk definitions and nine physical
stations in the player's `next` save were inspected. Five native loader-contract
checks and twenty initial offline projection assertions passed before implementation.

## Actual template changes

| Station/template | Authored change |
| --- | --- |
| BCER: Port Mojave / Port Mojave Xin | Add kiosk at (-8, 22), rotation 180; remove walls at (-10, 22) and (-9, 22). |
| BCRS: regular / Titan variant | Add kiosk at (1, 16), rotation 90. |
| EJDR | Upgrade generic scrap kiosk at (58, -22), rotation 0. |
| HQCH | Replace refuel kiosk at (-10.5, -3.5) with scrap kiosk at (-11, -4), rotation 90. |
| JATL_SUB: Station_JATL (Lemuria Terminal) | Add kiosk at (52, -50), rotation 180. |
| JFTS | Add kiosk at (-15, -6), rotation 180. |
| MLAB | Upgrade generic scrap kiosk at (-17, -35), rotation 90. |
| MSUZ_RB: Station_MSUZ_RB (Renbao Pavilion Seven) | Add kiosk at (9, -28), rotation 180. |
| SVIR | Add kiosk at (-24, 29), rotation 0. |

These are root-item spatial changes, not installation-fit results. Floors,
wires and lamps can share coordinates. Native socket and placement rules must
decide compatibility; deleting every object at the new position would be wrong.

## Initial proposed boundary (superseded by the standalone implementation)

The assets already define the desired fixtures and trade rules. The C# migration
destroys saved interiors to acquire them, so the core repair belongs in C# and
must continue using those assets. Editing installed vanilla files or replacing
whole station JSON definitions does not repair saved-state preservation.

1. Intercept only the kiosk-regeneration call in `CrewSim.DoLoadGame`. Leave
   unrelated migrations, encounter reset and the debug respawn menu intact.
   A declined retrofit must defer without invoking full respawn or receiving
   a success marker.
2. Retrofit when the original station naturally loads to an appropriate state.
   This supplies native tiles/placement rules without forcing distant stations
   into full simulation. Retain the existing hull, geometry, crew, inventories,
   rooms, gases, zones, markets, ownership, docking group and motion references.
3. Resolve the actual station variant and use its authored kiosk definitions and
   placements. Add new items through native creation. Existing-item conversion
   must retain relevant identity/state/contents and apply the new trader bindings.
4. Preflight the footprint, front access space and affected objects before
   mutation. Apply the user's conflict policy: preserve structural edits; move
   intersecting loose items aside. Find clear nearby destinations on the same
   station, retain container/stack identity and contents, and reserve all
   destinations before applying anything. Native placement and room checks stay
   authoritative. Remove only the explicitly retired original fixtures/walls
   where the saved layout still matches. Defer/report structural conflicts or
   lack of safe relocation space; never regenerate the station as a fallback.
5. Reuse native per-station completion markers only after verifying the fixture
   and its persistence. Repeated loads must not duplicate kiosks or stock.
   No frame poll, retained tracking collection or replacement AI is required.

This suggests one narrow load-iterator integration and one station-load hook.
It does not require replacing ships, changing orbital locks or rebuilding
docking relationships. Natural bodies, stations and regional variants retain
their separate identities.

## Proven persistence constraints and conversion caveats

The [native checks](../Tests/StationMigrationCheck/README.md) demonstrate that:

- `DataHandler.GetCondOwner` prefers an existing live CO, then `dictCOSaves` and
  its `strCODef`, before the requested ship-layout definition. Changing the
  `JsonItem` name alone does not upgrade the saved or live object.
- `Ship.SpawnItems(bTemplateOnly: false)` skips an added item without a saved CO
  record. Appending a bare JSON item is insufficient.

Source review additionally establishes:

- Template spawning remaps item GUIDs. A blueprint GUID is not reliable saved
  object provenance.
- `CondOwner.ModeSwitch` transfers identity/contents but copies old GUI maps and
  adds conversion wear. It cannot be used blindly for an administrative upgrade.
- `Trader.UpdateGPMs` honors cached `strCTBuyLast`/`strCTSellLast`. Changing trade
  table names while retaining those caches can keep obsolete filters active.
- Stock, definition, trade bindings and persistence must remain coherent. A full
  inventory reroll is not necessary to update regional trade rules.

## Actual continuation and variants

The inspected `next` ZIP has neither 1.1.0.1 migration-completion marker nor
per-station kiosk-fix markers. It is a pre-migration baseline. Its origin version
is `Release Build: 1.0.1.7`. `LoadManager` preserves `CrewSim.strSaveVersion`
on save, so a resave does not itself advance this native migration gate.

Two actual resolutions expose a native migration-target mismatch:

- JATL uses `Station_JATL_LZ`; the changed `Station_JATL` belongs to the separate
  JATL_SUB/Lemuria Terminal section.
- MSUZ resolves through native NewGame lookup to `Station_MSUZ`; the changed
  `Station_MSUZ_RB` belongs to MSUZ_RB/Renbao Pavilion Seven.

The parent templates contain none of the corresponding new regional kiosks.
Native regeneration still rebuilds those parents and marks success while
omitting the two actual kiosk sections. These are not new maps: both physical
sections and their spawn entries already exist in the 1.0.1.10 backup. The
corrected standalone manifest upgrades the substations at their authored sites
and protects the parents. No new placement is invented.

## BCER and BCRS saved-grid projection

The read-only projection now examines the actual saved interiors and their
saved CO installation states, not just template item centers. All inspected
root items have saved state records. It expands deterministic socket flags,
rotates item footprints, and checks the kiosk's physical cells plus its front
access row. Floors, wiring and other compatible layers remain in place.

Saved interior coordinates differ from template coordinates after native
`Ship.MoveShip` / `Ship.RotateCW`. Alignment is confirmed using unique installed
fixtures, rather than a hull bounding box that player construction can alter:

| Station | Interior alignment | Result |
| --- | --- | --- |
| BCER | Translation approximately (-32, -118), no rotation; 22/22 unique fixtures agree. | Kiosk fits at saved center (-40, -96), rotation 180. The two authored rock-wall removals are still present and match; no additional structural conflict or loose-item overlap is projected. |
| BCRS | Translation approximately (+93, -85), no rotation; 24/24 unique fixtures agree. | Kiosk fits at saved center (94, -69), rotation 90. Two loose dollies intersect its required clearance. |

The BCRS dollies have separate clear relocation candidates:

- `49bddd2e-5aeb-4b04-b924-a47c2e8db42f`: approximately (95, -71) to (97, -71).
- `7886b220-06ce-4807-b6a2-956cb6e65a04`: approximately (95, -68) to (97, -68).

These are **plans, not completed moves**. Candidates are on connected clear
floor belonging to the station, outside kiosk/access cells, other loose-item
footprints and reserved destinations. Synthetic overlap cases also find a
relocation; injecting an extra structural wall makes each station defer instead.
The input layouts and source ZIP remain unchanged.

Projection limits: it reconstructs deterministic sockets from saved installation
conditions and asset definitions. It does not instantiate damaged live tiles,
run native room/door/docking-boundary checks, or execute item conversion and
inventory persistence. Those remain implementation validation requirements.
The source contains an uneven socket row in `ItmToolBox02`; the projection follows
the native integer-division footprint rather than assuming a rectangular source
array. This study does not modify that unrelated asset.

## Initial evidence limits and implementation gate

The [read-only tool](../Tools/StationMigrationStudy/README.md) and five native
contracts establish feasibility and persistence constraints, not a working
migration. Before production integration, exercise native item creation/conversion,
placement, trade UI/filter initialization, save/reload, markers/idempotence and
failure rollback. Include loose loot, nested containers, relocated/destroyed
kiosks, player construction, loaded/unloaded stations and docked player ships.
Verify unrelated item state and station/docking/kinematic state are preserved.

Reports: `obj/Compatibility-1.1.0.1/station-retrofit-study.json`,
`station-grid-study.json` and `station-contracts.log`. ZIP hashes match
before/after. No save or installed DLL
was modified. Recovery of previously discarded items is a separate backup-based
repair, not an effect of preventing regeneration.
