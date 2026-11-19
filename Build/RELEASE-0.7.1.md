# Ostrabus 0.7.1

Prepared for **Ostranauts 1.1.0.3 (`public_test`)**, Windows and BepInEx 5.
This is the source checkpoint before the next game update. It does not declare
compatibility with an unreviewed game assembly. Status remains experimental and
partially playtested; no deployment, tag, push or hosted publication is implied.

## Changes since 0.7.0

- **Planetary route safety:** check the actual native route between waypoints
  against moving celestial bodies, retry native detours, reject unsafe saved
  courses before playback, and retain native manual steering when plotting
  fails; see [route safety](../Navigation/FREIGHT-CELESTIAL-SAFETY.md).
- **Freight arrivals and cargo lifecycle:** retain native final RCS approaches
  during normal flight, SFF and PASS, carry fuel budgets between stages, and
  remove a destroyed cargo hull's market without crediting a fictitious delivery;
  see [freight accounting](../Orbital/Economy/README.md).
- **Retrograde references:** accept finite negative orbital periods supported
  by native assets, preserving their direction in ephemeris and gravity queries.
- **Crew recruitment:** copy the regional station list before temporarily
  filtering shortages, so one unsuccessful search cannot permanently remove
  recruitment sources; see [the native reproduction](../Tests/NpcIdentityCheck/README.md).
- **Retiring pilots:** permit routine stale-ship crews to return to compatible
  offscreen stations that are full, retaining native transfer and subsequent
  population management; this is an intentional
  [simulation policy](../Navigation/CREW-RETIREMENT.md).
- **Docking precision:** extend the existing bounded coordinate correction to
  cover accumulated docking translations that erase ports from the temporary
  fit grid, with [saved RES1 reproduction and controls](../Tests/DockingDiagnosis/README.md).
- **Single-port docking:** optionally skip the target-port menu when native comms
  offers exactly one eligible option, using a saved Mod Settings toggle and
  preserving native clearance; see [docking QoL](../QoL/Docking/README.md).
- **Wreck damage severity:** provide a configurable initial-damage curve and
  optional console diagnostics for severity, wear and actual incoming ray
  budgets; see [wreck controls](../QoL/WreckSeverity/README.md).
- **Nav tutorial note:** close the hidden paper through its native state setter
  after console initialization so it cannot silently block map contact clicks,
  including on a newly installed console; see [nav controls](../NavConsole/README.md).
- **Automatic death evidence:** record NPC deaths, living removals and crewed
  hull destruction attempts to bounded external files, with optional API access
  and no save fields or manual trace cycle; see
  [obituaries](../Diagnostics/Obituaries/README.md). This is diagnostic evidence,
  not a repair of every underlying cause of death. Release review also excludes
  crewless hull/projectile teardown before snapshot allocation and AI enumeration.
- **Custom builds and evidence:** include the new feature selections, declare
  navigation/cargo dependencies, filter their PDA controls, and retain updated
  behavior-graph, population, freight and save-inspection tools.
- **Game compatibility:** retain the reviewed
  [1.1.0.3 native integration](COMPATIBILITY-1.1.0.3.md); align the main DLL and
  four bundled native mods at 0.7.1 with game asset metadata for 1.1.0.3.

## Independently installed plugins

- [Station Kiosk Migration 0.1.3](../StandaloneMods/StationMigration/README.md)
  admits the reviewed 1.1.0.3 loader without changing its migration payload;
  previously migrated saves do not need another migration.
- [Boneyard Turnover 0.1.4](../StandaloneMods/BoneyardTurnover/README.md) adds
  optional regional wreck replacement after absences, with native template pools,
  protections, save bookkeeping, spawn-marker handling and offset-field centres
  derived from their authored facilities. It excludes Earth and hauler-serviced
  yards. This is new simulation policy and uses its own optional save ZIP entry.
- Apartment Refuel Terminal retains version 0.1.1 and its separate installer.
  Full `Deploy-Mod.cmd` still deploys the complete main plugin; none of these
  independent plugins is silently activated. Custom builds remain isolated.

## Acceptance and limitations

The player confirmed the single-port shortcut and hidden-note correction in game.
Traffic/save comparisons exercised route-safety changes during play, but do not
establish that every restored or combat-damaged ship can recover. The final RES1
precision revision and turnover field-centre revision have offline acceptance;
their final binaries are not represented as fully playtested.

Historical cargo redistribution and explicitly prepared save repairs are separate
maintenance operations. Installing 0.7.1 does not repeat them, recreate past
casualties, relocate ships or deliver historical lost cargo automatically.

Long PASS loading stalls and some historical casualties remain incompletely
explained. Obituary hooks retain future evidence; their bounded storage and queue
do not constitute a live performance benchmark of every population burst.
The [reload-memory investigation](../Notes/RELOAD-MEMORY-INVESTIGATION.md) found
heap reservation and overlapping loading data, but did not prove or repair a
complete explanation for the reported memory growth.

Native integration tests substitute documented Unity presentation/scene boundaries.
Repeated numerical assertions and behavior-graph scenarios are not independent
campaign playtests. Unknown future native assemblies still require review.

## Release verification

Final receipts are retained under ignored `obj/Release-0.7.1/`. Debug and Release
build cleanly with zero warnings/errors against the reviewed native hash
`e9e7b8796423701c157ea5718cb407e6517ac0435ef947062c9a27464f4bc82f`.

| Check | Result |
| --- | --- |
| Main Harmony binding | 324 classes bound, one optional damage-log observer disabled by configuration; zero failures/warnings. |
| Freight/navigation runner | 97 passing cases, including 78 normal-flight/SFF/PASS voyage replays. |
| Crew identities/recruitment | Eight native baseline checks and 133 repair checks. |
| Bounty preservation/retirement | 174 bounty checks plus 26 native retirement/population controls. |
| Obituaries | 169 native death/removal, storage, API and crewless-teardown checks. |
| Wreck severity | 80,295 assertions, mostly repeated numerical curve checks; native logging/RNG controls included. |
| Single-port docking / PDA | 39 native shortcut checks and 97 asset/registry/toggle checks. |
| Nav console | 701 scrollback, 18 tutorial-note and 837 range/save/native-IL checks. |
| Docking precision | Vanilla/corrected RES1 replays with the connected group, plus 36,021 numerical/loaded-grid assertions. |
| Boneyard turnover | 10,457 assertions including native lifecycle, field-centre and persistence cases. |
| Station migration / apartment refuel | 59 native loader checks and 273 native refuel checks. |
| Custom builder | 660 self-test checks and 11 compiled, verified, native-bound feature combinations, including full and independent new modules. |
| Behavior graphs | All seven slices validate; the bounty test fingerprint was reviewed/refreshed without changing modeled policy. This does not prove whole-game coverage. |
| Installers and save tools | 16 PDA, 11 station, 13 turnover and 18 offline-repair apply checks; deployment backup controls, apartment installation and eight exact-JSON repair controls pass. |
| Distribution archive | 59 content/metadata/hash checks pass; 62 entries contain the main DLL, four native mods and separately installed Apartment Refuel, with no game DLLs, saves or independent turnover/migration plugin. |

The custom profiles were `ai-rendezvous`, `cargo-loading`, `ai-crew-recruitment`,
`ai-crew-retirement`, `obituaries`, `obituaries-inspection`, `wreck-severity`,
`single-port-docking`, `single-port-docking-settings`, `pda` and `full`.
Disabled diagnostic hooks are also exercised enabled by their focused native tests.

| Artifact | SHA-256 |
| --- | --- |
| Main Debug DLL | `628aabc6200777386b57ad516137a432df21953e131e9cd03810c9f1b54baa31` |
| Main Release DLL | `0044ecece2f45c99714735705085cdf45e7dbef53fac5cadfd3b360ce429ac1e` |
| Prepared Debug archive | `3cca715893c3612a0bb65a1d7c5552f687e605a1a247d124f8b57b343925fdbc` |

The archive is `dist/Ostrabus-20261119-143103-980.zip`; station migration 0.1.3
and turnover 0.1.4 were packaged independently. Apartment Refuel retains 0.1.1
with its native compatibility label refreshed to 1.1.0.3 after the native checks.
Source control excludes generated binaries, packages, full saves, installed game
assemblies and raw scratch captures. Installer tests changed only workspace
fixtures; release preparation did not deploy to the running game or edit saves.
