# Incremental compatibility: Ostranauts 1.1.0.4

Reviewed 2026-11-19 using both decompiled projects, authentic native assemblies,
shipped JSON, actual Harmony bindings and offline native execution.

The main plugin is compatible after **retiring the PDC conversion-ammunition
hook**, now superseded by native assets. The remaining patches retain their
existing scope. The current source follows the committed 0.7.1 / 1.1.0.3
checkpoint; no new release number or commit is implied by this audit.

## Baseline recovery and scope

There is no complete preserved 1.1.0.3 installation. Its exact
`Assembly-CSharp.dll` survived in a previous test output and matches the SHA-256
recorded in the [1.1.0.3 audit](COMPATIBILITY-1.1.0.3.md). It was copied into
the ignored evidence directory before any test rebuild could replace it.

| Game | Native assembly SHA-256 | MVID |
| --- | --- | --- |
| 1.1.0.3 | `e9e7b8796423701c157ea5718cb407e6517ac0435ef947062c9a27464f4bc82f` | `5b1ee244-539d-4902-8d62-97490deee9d7` |
| 1.1.0.4 | `904c72870f55124bbe3f9ee924a4a650279c362541b5795174d0cf940b97c474` | `edbf4229-73b5-4bd1-92f1-a2add0704e7a` |

The preceding audit established that the other 164 managed DLLs and all 552
shipped data JSON files were identical between the preserved 1.1.0.2 install
and 1.1.0.3. Those unchanged files provide the comparison baseline here.
Only `Assembly-CSharp.dll` changes among the 165 managed DLLs. The executable,
UnityPlayer and embedded Mono DLL also match the preserved baseline.
This does not establish byte identity for every texture, sound or other
non-JSON asset in the missing full 1.1.0.3 installation.

## Complete code and data delta

Three decompiled files change: `CondOwner.cs`, `GasContainer.cs` and `Ship.cs`.
The normalized assembly comparison covers 34,051 member identities in each
version: three changed bodies, one added signature and one removed signature,
with no changed directly referenced assembly bytes.

| Native method | Actual change | Ostrabus consequence |
| --- | --- | --- |
| `global::CondOwner.Interact()` and `AddReplyThread(...)` | Removes the caller-supplied epoch argument; each recipient now timestamps its reply with its own `Clock.Epoch`. `CleanupReplies(bool)` already measures age with that recipient clock. | Native fix for premature reply expiry when actor clocks differ. Our radio-surrender/work repairs address conversation flags, not this timestamp defect; they remain necessary. No production hook calls the removed signature. |
| `global::Ship.BreakIn()` | Caps the hole-loop bound at `Mathf.Min(fBreakInMultiplier * 10f, aTiles.Count / 2f / 75f)`. Other wear and damage-ray paths remain unchanged. | Native small-wreck protection is retained. Our optional wreck-severity QoL still adjusts the severity input and can log the native damage rays; it does not replace the new cap. No default or curve was retuned. |
| `global::GasContainer.get_AtmoSafe()` | Changes the safe atmosphere preset's oxygen from 20 to 22; nitrogen remains 80 and temperature 297. | Native atmosphere initialization change. This does not replace our separate scrubber sound repair, or alter our helmet/vent safety thresholds. |

The floating-point hole-loop bound is not rounded down: a positive fractional
bound still permits an iteration. This is a cap on hole attempts relative to
the native tile count, not a promise that tiny wrecks suffer no damage.

Four of the 552 JSON files change, affecting 72 entries:

| Asset | Change |
| --- | --- |
| `condowners/condowners.json` | `ItmFloorGrate02` (Polished Regolith Floor): native `StatMass` changes from 6.5 to 36. |
| `condowners/condowners_ship_combat.json` | `ItmShipWeaponMassThrower01Off` is displayed as `Coilgun (Off)`. |
| `installables/installables.json` | 50 weapon install/uninstall recipes replace inline `aLootCOs` with named `strLootOut` tables. |
| `installables/installables_repair.json` | 20 weapon repair recipes make the same change. |

The floor mass and coilgun name were not mentioned in the supplied patch notes.
No new station templates, narrative plots, ship templates or environmental
payloads appear in this JSON delta. Bundled Ostrabus native mods do not override
the 72 changed entries.

## PDC hook retirement

`global::Installables.Create(JsonInstallable)` is unchanged. For a named output
it uses the existing loot table, including its authored `bSuppress=true` flag.
`global::Interaction.ApplyEffects()` passes the inverse of that flag as
`bLoot` to `global::DataHandler.GetCondOwner(...)`. Thus the update's 21 PDC
conversion recipes stop generating another starting magazine before native
inventory transfer. The existing 15 PDC variant tables and native power/damage
mode outputs also suppress replacement starting loot.

The direct factory-call inventory was reviewed for other same-ID replacements:
body/skin and ship-editor replacement explicitly disable starting loot;
lock GUI replacement is lock-specific; the CrewSim mouse path requires
Open/Closed names absent from all PDC variants. Ordinary new spawns and saved
restoration remain native. There is no remaining vanilla conversion requiring
the production PDC prefix.

`PdcConversionAmmoPatch` was removed from production. Its original prefix is
preserved only in `Tests/PdcAmmoCheck/HistoricalPdcConversionAmmoPatch.cs` for
reproduction. Existing custom profiles may retain the hidden `pdc-ammo` ID;
it adds no hooks, assets or callbacks. New menus and presets omit it.
See the [PDC evidence](../MiscBugFixes/PDC-CONVERSION-AMMO.md).

## Hook, graph and standalone review

The full main DLL binds **323 patch classes**, with one optional damage-log
observer disabled by `Prepare`, **zero failures and zero warnings**. Compared
with the actual 0.7.1 / 1.1.0.3 binding receipt, there are 412 unchanged direct
hook/target bodies, one changed body (`Ship.BreakIn`), the intentionally removed
PDC hook, and one observer with no reported original target. The changed wreck
initialization target passes its actual IL/patch-contract regressions, including
the enabled damage-log path. Bindings are not a substitute for semantic review.

All seven graph slices were audited against the new assembly and assets:
retriever, scavenger, deployer, target lifecycle, traffic, bounty and encounters.
Accepted snapshots were preserved. Conservative graph flags include older
unaccepted changes and broad dependencies; they are not the incremental change
count. The complete assembly and JSON delta above supplies that distinction.
Native AI controllers, navigation, SFF/PASS movement, freight delivery, market
production and docking implementations are unchanged in this update.

- **Station Kiosk Migration 0.1.4:** admits the reviewed new hash in its runtime
  and installer. Its native loader still contains the same two kiosk respawn
  sites. Its construction-preservation adapter now reads named uninstall tables
  as well as inline arrays, so an obstructing installed weapon can be preserved
  loose without losing damage or ammunition. Random/ambiguous conversion tables
  are not guessed. Kiosk locations, payload, completion markers and lifecycle
  policy are unchanged. Already migrated saves require no repeat migration.
- **Boneyard Turnover 0.1.5:** admits the reviewed new hash in runtime and
  installer. Turnover rates, saved state, field-centre resolution and protection
  rules are unchanged. Replacement wrecks still use native generation, including
  the new small-wreck cap.
- **Apartment Refuel Terminal 0.1.1:** passes its native regression suite;
  its asset compatibility label now includes the current game.

Unknown assembly hashes remain rejected by the two strict standalone plugins.
Normal `Deploy-Mod.cmd` still installs the complete main plugin and its bundled
native mods; it does not activate either standalone migration or turnover plugin.

## Executed evidence and boundaries

- Main Debug and Release builds: zero errors or warnings; actual full-plugin
  Harmony binding and normalized hook-body comparison completed.
- Freight runner: **97 cases, including 78 complete voyage replays**, covering
  normal flight, SFF and PASS.
- Native reply/atmosphere A/B: **16 legacy checks and 18 current checks**;
  mismatched actor clocks reproduce immediate reply expiry in the authentic
  1.1.0.3 assembly and retain replies correctly in 1.1.0.4. Duplicate replacement
  and the exact native expiry boundary remain intact. No Ostrabus patch applied.
- PDC native recipe/factory A/B: **368 checks**, including all 21 conversion
  recipes. Old assets produce 75 fresh rounds at the deterministic midpoint;
  new assets produce zero without the historical hook. Native parsing, recipe
  generation and recursive factory run; Unity construction and the engine-heavy
  `SetData` leaf are fixtures, not full scene-level inventory transfer.
- Wreck-severity suite: **80,295 assertions**, including curve sweeps, actual
  native IL contracts, observer binding, logging, exception cleanup and RNG
  boundaries. This does not execute full Unity wreck construction.
- Towing: **343,871 assertions** across playback, mass, braces, station departure,
  chain membership, consumption, reconstruction and cleanup. Counts include
  systematic sweeps, not that many independent gameplay scenarios.
- Station migration: **233 plan assertions** including named weapon outputs,
  an obstructing PDC with existing ammunition/damage, random-output rejection
  and existing preservation controls; **61 native integration checks**. The
  adapted reader also passes **203 plan assertions and 61 native checks** with
  the preserved old assets and authentic 1.1.0.3 assembly respectively.
- Boneyard turnover: **10,457 production assertions**; Apartment Refuel: **273**.
- Focused native suites also pass surrender/work, obituaries, alternative work,
  dolly mass, loader/forward compatibility, hulls/collision consumers, docking
  permissions, nav scroll/range, single-port docking, PDA settings and the
  compressed RES1 docking replay. Their detailed scope remains in each module.
- Custom builder: **661 self-checks** and five compiled, verified, native-bound
  profiles: retired PDC ID, course controls, wreck severity, scrubber audio and
  the full preset. The retired PDC profile binds zero hooks.
- Packaged standalone installers: **11 station and 13 turnover checks**, including
  unknown-hash rejection and preservation of unrelated files. Scrubber audio:
  **39 native/asset checks** plus install/reinstall/omission controls.
- The full archive has **62 entries**: the main and optional Apartment Refuel
  plugins are its only DLLs; game assemblies, runtime dependencies and saves
  are excluded. Bundled native-mod compatibility labels read 1.1.0.4.

Prepared standalone archives are `dist/StationMigration-0.1.4-1.1.0.4.zip` and
`dist/BoneyardTurnover-0.1.5-1.1.0.4.zip`. An existing turnover installation
needs its new standalone build: normal main-plugin deployment does not replace
that DLL, and its old exact-hash guard correctly rejects 1.1.0.4.

The initial compatibility-only Release main-plugin SHA-256 was
`451be27ad7d292b3a305ad5a3f70d2337852a50b8820034a686c7192d1595dd4`.

Receipts and recovered comparison inputs are under ignored
`obj/Compatibility-1.1.0.4`; they include exact binary identities and graph audit
reports. These results establish offline compatibility, not new live-scene
playtesting. No installed game files, live markets, ships or player saves were
changed during this audit.

## First live startup confirmation

After the player deployed and loaded the candidate, read-only inspection confirmed
the tested native MVID `edbf4229-73b5-4bd1-92f1-a2add0704e7a` and main-plugin
MVID `0d954eca-5d42-4cae-b825-91963ccf7fb3`. Installed main Debug and standalone
turnover DLL hashes match the built candidates. BepInEx reports successful main
patch installation, Apartment Refuel 0.1.1 and Boneyard Turnover 0.1.5 ready.
No BepInEx startup failure or exception in the retained 512-entry Unity/plugin
log window was found. That window does not cover the entire Unity startup log.

The paused generation-7 snapshot at epoch `65635282920.8981` contains 334 hulls
and 43 AI registrations: 32 freighters, one PASS transport sharing the cargo
controller role, and ten OKLG actors (seven scavs, one civilian, one retriever
and one police ship). All registered captains are living aboard their matching
hulls; no duplicate or stale controller entry was observed. All 32 freight
markets retain positive cargo and future route endpoints. A route endpoint is
not a guaranteed delivery time.

All 127 market aggregate/backing-stock comparisons completed and matched,
including a direct retry of a budget-limited HQCH comparison. The 66 shipless
market IDs all existed in the earlier OKLG-arrival baseline; 57 retain 29,798
units, unchanged from that baseline. No redistribution was performed.
The paged freight route hit its inspection budget; controller-confirmed hull IDs,
the complete market list and direct cargo-stock queries supply the fleet count.
The PASS transport has no freight market and is excluded from that count.

One non-controller hull, B-ZDE, has recorded RCS acceleration matching the
retriever O-ALTU to which it is docked. This is shared towing motion, not evidence
of an uncontrolled accelerating wreck. No active combat or shipboard encounter
was reported. The game remained paused throughout; resumed steering, actual
deliveries and fresh damage generation are not established by this snapshot.
Raw captures are under ignored `obj/Survey-1.1.0.4-live-20261119`.

## Subsequent hull-avoidance correction

Live pursuit exposed excessive dodges caused by Ostrabus applying native doubled
prediction circles to enlarged hull bounds. This is an existing geometry
integration defect, not a newly changed 1.1.0.4 method. The
[reproduction and repair](../Navigation/PLANETARY-AVOIDANCE-AUDIT.md) distinguish
the measured evasive motion from the deliberate saved-hull coasting control.
The current candidate confirms broad ship warnings against cached exteriors;
native clearance and forced docking remain authoritative. No Harmony hook was
added and no installed DLL or live ship was changed during implementation.

Offline validation covers both patch orders, native clamp eligibility, real
closing/crossing/rotation threats, acceleration, planned routes, group members,
missing-geometry fallback and the actual saved exteriors. Existing physical
collision tests and planetary navigation checks pass. The 97 freight cases
(78 full voyages), compressed RES1 docking replay, actual full-plugin binding
(323 classes, one disabled, zero failures/warnings), and four relevant custom
profiles also pass. Receipts are under ignored `obj/ForcedDockAvoidance-20261119`.
The first live confirmation above predates this correction; it does not certify
the new candidate's behavior in a running Unity scene.
The rebuilt Release candidate SHA-256 is
`05edc444f7bf1757df9607d327a5d5bd50a52a571747c23bb8020a29c2b4ae5b`.

Subsequent live inspection confirmed the corrected Debug candidate loaded as
MVID `4607460c-9dd8-42d9-ad0e-16a7511a9eea`. The repeated pursuit still produced
disruptive evasion/replanning during an accepted forced-docking approach. The
[live follow-up and replay](../Navigation/PLANETARY-AVOIDANCE-AUDIT.md) distinguish
the corrected false-circle case from a closing approach that predicts actual
contact under continued motion. The original pursuit issue is not resolved by
the geometry correction alone; the earlier offline clamp test did not cover
that moving interaction.
After playtesting, the player chose to retain the contested forced-docking
behavior and the deployed geometry correction. The proposed manual-clearance
avoidance exemption is not implemented; no further gameplay change is requested
for this encounter. This is a behavioral decision, not additional proof that the
earlier clamp fixture covered the moving pursuit.

## Optional native bonus-source diagnostics

The existing wreck-damage logging flag now also observes native bonus awards
and initialization consumption; the bonus rules and severity curve are unchanged.
The [module notes](../QoL/WreckSeverity/IMPLEMENTATION.md) describe the two optional
source hooks and native A/B boundaries. The wreck suite passes 80,317 assertions;
full-plugin binding passes with logging off (323 bound, three disabled) and on
(326 bound, none disabled), both with zero failures or warnings. These are
offline results, not a new live deployment. Receipts are under
`obj/ForcedDockAvoidance-20261119` with the `bonus-` / `wreck-bonus-` prefixes.
The rebuilt Debug SHA-256 is
`e46ac49971d116fbb3f29bc0d1b661b2617a71db0e52b552b442df38310b60b6`;
Release is `2fd579b760da6988c99b98866e0445b32bf289ad3f8bf5d0f7edbbcb47e0be86`.

## Deferred wreck names and registration allocation

The [identity audit and preventive repair](../MiscBugFixes/DERELICT-NAMES-AND-IDS-AUDIT.md)
preserve generated wreck names and reject newly generated IDs already used by
loaded or deferred objects. Both report interventions in the native player
console. Explicit-ID restoration/conversion and native asteroid/station naming
retain their existing semantics. The native converter's Harmony finally exit is
explicitly preserved and independently reproduced by a no-op-patch control.

Validation passes 349 native regression assertions, the 24-assertion control,
671 custom-builder checks and four compiled/verified/native-bound profiles.
Full-plugin binding passes with logging off (325 bound, three disabled) and on
(328 bound, none disabled), both with zero failures/warnings. Debug and Release
builds are clean. No live deployment or historical save repair is implied.
The new Debug SHA-256 is
`effdcc74805c61c45b1418b3ccf8d5dda0132d8d0e0951fd811d18554a1cf58e`;
Release is `fafc7ad90d381f150e5825ea6ea21a268ca05c70db59e1282669b9f916b3a966`.
