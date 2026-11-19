# Incremental compatibility: Ostranauts 1.1.0.3

Reviewed 2026-11-18 against the preserved 1.1.0.2 installation, both decompiled
projects, shipped JSON, actual Harmony bindings and offline native execution.

**The main Ostrabus plugin needs no runtime adaptation or feature retirement.**
The Debug DLL built against either version has identical SHA-256:
`6507eeb26934ff1e9bd5a231f83df6408c92c6099ea5a95dc25f45b814a2775e`.
The four bundled native mods' compatibility labels now target 1.1.0.3. Main
deployment still installs the complete plugin; standalone plugins remain separate.

## Native delta

| Version | Native assembly SHA-256 | MVID |
| --- | --- | --- |
| 1.1.0.2 | `133a9cc94ad4f4d5db951b65249b6cdf78f53063419d836b8871b92960ec3c37` | `158470fc-8494-4bfe-b750-ad9d18dbf8d0` |
| 1.1.0.3 | `e9e7b8796423701c157ea5718cb407e6517ac0435ef947062c9a27464f4bc82f` | `5b1ee244-539d-4902-8d62-97490deee9d7` |

Only Assembly-CSharp changes among 165 managed DLLs. All 552 shipped JSON files
are byte-identical. The executable, UnityPlayer and bundled Mono runtime match.
Exactly five decompiled C# files change:

| Native method/type | Change | Ostrabus assessment |
| --- | --- | --- |
| `GUIOrbitDraw.AddOrbital`, `DrawBody`; `Ostranauts.ShipGUIs.Utilities.BODraw` | Add blue atmosphere bands and yellow micrometeoroid-risk bands using existing layer density/risk data, with corresponding visibility and destruction handling; make `aProjs` readonly. | Our hull rendering handles ship silhouettes. These native body overlays retain their native path. Referenced fields/method contracts remain valid. |
| `CondOwner.Interact` | Marks an immediate reply as urgently scheduled when its recipient is the selected character. | Native reply scheduling remains authoritative; surrender/work condition repairs do not override this branch. |
| `Powered.Recharge` | For a power generator after more than 1,800 seconds, adds elapsed generation to the available `StatPower` before charging connected storage. | No Ostrabus replacement of this method. Market-production catch-up does not perform physical electrical charging. |
| `Ship.CalculateRating` | Uses cached raw RCS count instead of the materialized thruster-list count; includes the fallback when exhaust/raw count is zero. | This repairs rating, not missing old-save propulsion/distribution caches. Our save-forward compatibility repair remains necessary. |

The whole-assembly normalizer reports 26 changed, 64 added and 61 removed member
identities. Most added/removed identities are compiler-generated GUI callbacks
and iterators renumbered after the new fields. These are not dozens of new
gameplay mechanisms. Navigation physics, `FlyToPath`, AI controllers, cargo
handoffs, `Ship.Destroy`, loaders and environmental spawning are unchanged.
No new station/content payload or save migration is introduced by this update.

## Hook and graph review

All **313** main-plugin patch classes bind without failures or warnings.
The direct comparison covers **399 hook/target rows**: **395 unchanged bodies**
and **four changed bodies**, with no missing targets. The changed rows target
`GUIOrbitDraw.OnDestroy`, `LoadModules`, `Start` and `Awake`; their decompiled
control flow is unchanged, while generated callback identities move. Our
cleanup, module-loading, scrollback and tutorial-note hooks remain applicable.
Actual map/module/sensor/rotation regressions pass on the new assembly.

All seven behavior-graph slices were audited against the new assembly/assets.
Accepted snapshots were preserved. Their conservative flags include older,
unaccepted native changes and broad virtual/iterator dependencies. Those flags
are not an incremental 1.1.0.3 change count; the isolated assembly/source delta
above establishes that scope. No graph scenario is counted as a native flight test.

## Standalone compatibility guards

The native loader and turnover mechanisms are unchanged, but their exact-hash
guards correctly refuse an unreviewed assembly:

- **Station Kiosk Migration 0.1.3** admits the reviewed 1.1.0.3 hash and retains
  1.1.0.1/1.1.0.2 admission. Its two-site loader contract and migration payload
  are unchanged. Previously migrated saves need no repeat migration.
- **Boneyard Turnover 0.1.2** admits reviewed 1.1.0.2 and 1.1.0.3 hashes in both
  runtime and installer. Turnover policy, safeguards and persistence are unchanged.
- Unknown hashes remain rejected. The retired 1.1.0.0 save-migration prototype
  has not been promoted into a supported 1.1.0.3 plugin.

The optional plugins were tested on both actual 1.1.0.2 and 1.1.0.3 assemblies;
installers operate only on disposable workspace fixtures. Normal `Deploy-Mod.cmd`
does not activate either optional plugin.

## Evidence

The complete [freight/navigation regression runner](../Tests/AiPlannerDiagnosis/Run-Freight.ps1)
passes **97 cases**, including **78 voyages** across normal flight, SFF and PASS.
This includes the newly repaired planetary clearance, final-approach consistency
and destruction/market lifecycle checks. Independent native route geometry and
full-plugin navigation/gravity composition retain their documented boundaries.

Focused new-assembly execution covers nav-console behavior and module placement,
sensor/EM runtime, hull outlines, rotation, surrender/work, save-forward loading,
native station migration and native boneyard turnover. Separate strict-guard,
older-assembly and installer controls cover the optional plugins.

Representative receipts include 727 module-placement checks, 649 sensor/EM
checks, 337,543 rotation checks, 281 save-forward checks and 118 native loader
checks. The station companion passes 59 native checks on each actual assembly;
turnover passes 10,408 on each (most are a sampling sweep, not independent play
scenarios). Five selected custom profiles compile, verify and bind: module
placement, course controls, rotation, economy and the full preset. Counts within
and across suites overlap and are not an additive measure of game coverage.

Packaged installers pass 11 station-migration and 13 turnover checks, including
unknown-assembly rejection and preservation of unrelated plugins/configuration.
Prepared archives are `dist/StationMigration-0.1.3-1.1.0.3.zip` and
`dist/BoneyardTurnover-0.1.2-1.1.0.3.zip`. Already migrated stations require no
additional conversion; an installed turnover plugin needs the compatible build
because its prior exact-hash guard refuses 1.1.0.3.

Detailed source/member/asset deltas, actual binding identities, graph audits and
native test receipts are retained under ignored `obj/Compatibility-1.1.0.3`.
These are offline compatibility results, not a claim of new live-scene acceptance.
No installed assembly, native asset, live ship, market or player save was changed.
