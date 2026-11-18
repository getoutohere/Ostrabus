# Incremental compatibility: Ostranauts 1.1.0.1

Reviewed 2026-11-17 against the preserved 1.1.0.0 installation and the
1.1.0.1 assembly decompilation; exact assembly hashes are recorded below.

**Verdict: no Ostrabus runtime patch adaptation or retirement was required for
this update.** The current full plugin, including the new orbital-velocity
bugfix, builds without warnings against 1.1.0.1 and passes the checks below.
This is an incremental source/assembly/native-test review, not a full Unity
save-load replay or live acceptance of the new station migration.

**The native kiosk migration is destructive to existing station interiors.**
It replaces station item/layout records with template contents. Player-dropped
items, storage-container contents, installed equipment and construction changes
on the affected station hull can therefore be lost. Retaining NPCs and docked
ships does not preserve that property. Plugin compatibility does not establish
that continuing an old save through this native migration preserves all items.

The subsequent [non-destructive kiosk migration investigation](STATION-KIOSK-MIGRATION-FEASIBILITY-1.1.0.1.md)
records the placement/persistence constraints. The subsequent
[standalone Station Kiosk Migration plugin](../StandaloneMods/StationMigration/README.md)
implements in-place saved-item upgrades and native completion flags. It is
packaged separately, has offline coverage, and is pending Unity scene/UI
acceptance. The main Ostrabus DLL alone does not provide this protection.

Targeting correction: the native kiosk block names JATL and MSUZ, but the authored
kiosks are in the existing JATL_SUB and MSUZ_RB physical sections. Standalone
version 0.1.1 corrects those targets while separately protecting both parents.
The initial 0.1.0 candidate inherited this native targeting error and is superseded.

## Identities and scope

| Assembly-CSharp | SHA-256 | MVID |
| --- | --- | --- |
| 1.1.0.0 backup | `a79a5081d9c94307a45fe1d0b55ff1aa1d0af6ea8dfce1f9acc5961cf260ef07` | `8130eff7-382e-4fff-97c5-67c9ade3a42a` |
| 1.1.0.1 | `967927e609d93e5233fe16260d61de3cb4aa5d753b738ed8a6ba1dba4cf9fde0` | `802a99ef-a2fd-470c-9281-4f299c953c7d` |

The game executable, UnityPlayer and bundled Mono runtime binaries match the
backup. All directly referenced managed assembly bytes match too.

The rebuilt plugin is byte-identical to the preceding 1.1.0.0 candidate:
SHA-256 `2d7e1d5ae3c2a08735276089e7e80f27c45753ed365daec41220a05befc1bf2b`.
The installed DLL matched this hash at the end of the review. That verifies disk
contents, not which native assembly an already-running game process has loaded.

Five decompiled C# files changed:

| Native class | Change | Ostrabus disposition |
| --- | --- | --- |
| `CrewSim` | Save-load fixes reset the Luna/Jade Rabbit first-docking encounter and retrofit scrap kiosks through station respawn. | Three hooks target the changed loading iterator; reviewed and native-tested below. |
| `Powered` | Recharge limits initial distribution to available energy, terminates partial allocation and reuses a key list. | No Ostrabus Recharge replacement; signal-box repairs remain distinct and pass. |
| `Pathfinder` | Walking fatigue now checks consecutive frames as well as elapsed simulation time. | Our adjacent-work range correction targets a different, unchanged method and passes its native checks. |
| `Ostranauts.ShipGUIs.Market.GUICargoPod.GUICargoPod` | `SetData` returns early when the player is null. | This does not replace cargo-pod market isolation, freight accounting or barter repairs. |
| `GUIGameCredits` | Adds a writing credit. | No integration change. |

The full assembly normalizer records 18 changed, 66 added and 63 removed
member identities. These are not 147 behavioral changes: compiler-generated
Pathfinder state machines and Powered lambda names shifted, producing paired
add/remove identities and changed references to them. The five-file source
comparison explains those changes. The identical-input control reports no
changed members or references.

## Actual hook comparison

Fresh bundled-Mono binding receipts were collected separately against both
assemblies, preserving the old plugin/harness before rebuilding. Both bind
306 classes with zero failures and zero warnings. The normalized comparison
covers 391 hook/target rows, not 391 distinct native methods:

- 388 target bodies/contracts are unchanged.
- Three changed rows all target `CrewSim.<DoLoadGame>d__276.MoveNext`: loading
  iterator profiling, saved-CO ship lookup, and concrete ship-file readers.
- The lookup still replaces exactly one native `JsonShip.Any` call. The two
  ship-file reader sites still match their expected signatures. Compiler-closure
  and restore-coroutine tests pass against the new iterator.

The native orbital cache, coasting factory, ship navigation planners, route
playback and ship movement implementations are unchanged. No native correction
supersedes `orbital-velocity` or the reviewed existing navigation repairs.

## Asset and save-load changes

Twenty shipped JSON files changed: nine definition/interaction files and eleven
station-template files. The latter cover variants of nine stations. The
definition changes add station-specific scrap kiosks and their trade data,
expand Luna's first-docking encounter with a branching conversation, and
primarily revise Ceres dialogue text.
These do not alter the reviewed ship-AI policies. No Ostrabus asset overrides
were needed for this hotfix.

The kiosk changes are new in this hotfix. As a concrete example, HQCH's template
is byte-identical between the preserved 1.0.1.10 and 1.1.0.0 installations;
1.1.0.1 replaces an existing generic Refuel Kiosk instance with its regional
scrap kiosk. This is not evidence of a kiosk addition missed by the earlier
1.0-to-1.1.0.0 content audit.

The separate `1.1.0.1_FirstEnc_Fix` clears the player's `IsENCFirstDockEJDR`
condition once for saves older than 1.1.0.1. The expanded `ENCFirstDockEJDR`
encounter can therefore pass its existing first-visit gate again. This is new
narrative content in the hotfix, with an explicit old-save bridge; resetting
this encounter flag does not rebuild a station or require a fresh game.

`CrewSim.DoLoadGame` applies `1.1.0.1_Scrap_Kiosk_Fix` to older saves. It targets
BCER, BCRS, EJDR, HQCH, JATL, JFTS, MLAB, MSUZ and SVIR, skipping stations at
`Ship.Loaded.Edit` or above. Each reconstructed station receives the
`IsDebug1-1-0-1Fixed` condition; the overall marker is deferred when a station
was skipped. The native save writer preserves the save's origin version, so
resaving does not by itself advance this version gate and prevent another retry.

The unchanged `DebugRespawnShip.RespawnShip(..., bNPCs: false)` replaces the
station hull/interior from its template. It moves existing NPCs to the new
instance, copies ownership, conditions, kinematics and selected runtime metadata,
and undocks/redocks visiting ships. It does not copy arbitrary old interior item
records. This is a native save migration, not an Ostrabus migration or a new
custom content plugin. Its NPC retention and docking steps were source-reviewed;
their complete Unity execution was not reproduced by the offline harness.

## Regression evidence

| Check | Result |
| --- | --- |
| Full plugin build against 1.1.0.1 | Zero warnings/errors |
| Full hook binding | 306 classes; zero failures/warnings |
| Orbital production-source study | 2,644 focused assertions; new native MVID confirmed |
| OrbitalCheck, AiRendezvousCheck, AiRouteCompletionCheck, PlanetaryNavigationCheck, DockingOrbitStudy, TorchNativeOracle | All six pass both controls and production-hook composition |
| LoadCompatibilityCheck | 118 checks pass |
| WallWorkDiagnosis | 23 checks pass |
| SignalBoxAudit | 4,980 checks; production repair contrasts pass |
| TriggerClassificationCheck | 2,308 checks pass |
| BarterInitCheck | 16,894 checks pass |
| AiPlannerDiagnosis | 16 checks pass |
| Custom builder | Empty, orbital-velocity only, velocity with simulation, Recommended all compile, finalize and bind successfully |
| Graph invalidation fixture controls | 26 pass |

The orbital study retains identical positions in all 348 saved-reference samples
and the same captured VCRS/handoff results as the 1.1.0.0 prototype. The later
[live Venus check](../Orbital/Velocity/README.md#live-venus-check-1101-2026-11-17)
also confirms bounded ordinary-time player handoff/readout behavior and samples
AI progression; it does not extend acceptance to the native station migration.

All seven behavior-graph slices were audited with the new assembly/assets.
They correctly flag transitive changes, frequently through conservative global
`MoveNext` dispatch dependencies and renamed compiler-generated members. The
traffic slice also flags the previously documented fixed-ephemeris test-fixture
change. No accepted baselines were rewritten or automatically marked reviewed;
those raw flags are broader than the directly changed ship-AI mechanisms.

Detailed source, asset-entry, hook and graph reports are generated under
`obj/Compatibility-1.1.0.1`. Receipts retain assembly hashes, so later binaries
cannot silently stand in for this review. Tools and reports do not modify live
game state. Building does not deploy the plugin or modify player saves.
