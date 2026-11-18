# Incremental compatibility: Ostranauts 1.1.0.2

Reviewed 2026-11-17 against the preserved 1.1.0.1 installation and the
1.1.0.2 assembly decompilation; exact assembly hashes are recorded below.

**The main Ostrabus plugin requires no runtime adaptation or retirement.**
It builds without warnings/errors and has identical DLL bytes when built against
the two installations. The optional Station Kiosk Migration companion does
require adaptation; version **0.1.2** supports both reviewed game assemblies.
These are source, assembly, asset and offline execution results, not new Unity
scene acceptance. No game installation or player save was modified by this audit.

## Native changes

| Assembly | SHA-256 | MVID |
| --- | --- | --- |
| 1.1.0.1 | `967927e609d93e5233fe16260d61de3cb4aa5d753b738ed8a6ba1dba4cf9fde0` | `802a99ef-a2fd-470c-9281-4f299c953c7d` |
| 1.1.0.2 | `133a9cc94ad4f4d5db951b65249b6cdf78f53063419d836b8871b92960ec3c37` | `158470fc-8494-4bfe-b750-ad9d18dbf8d0` |

Of 165 managed DLLs, only Assembly-CSharp changed. The executable, UnityPlayer
and bundled Mono runtime match the backup. The whole-assembly normalizer finds
four changed methods, one added compiler-generated static-data field, and no
removed members or changed referenced-assembly bytes. Exactly three decompiled
C# files changed:

| Method | Native change | Ostrabus assessment |
| --- | --- | --- |
| `CrewSim.DoLoadGame` iterator | Adds `1.1.0.2_Scrap_Kiosk_Fix`, respawning JATL_SUB and MSUZ_RB for older saves unless their existing `IsDebug1-1-0-1Fixed` conditions are set. | Existing main-plugin loading hooks still bind and their compiler-closure/restore tests pass. Adapt the separate migration companion as described below. |
| `Ship.Maneuver` | Removes `fRCSExhaustV == 0` from the early RCS rejection condition. Thruster strength and distribution count remain required. | The existing default exhaust-speed fallback inside the method becomes reachable with a zero exhaust cache. This does not populate missing old-save thruster/distribution caches or replace our RCS-cache repair. Rotation and shallow torch-release prefixes remain applicable. |
| `Ship.CalculateRating` | Tests zero RCS strength instead of checking zero exhaust speed twice. | Corrects the rating calculation's guard. No Ostrabus replacement to retire. |
| `CondOwner.AddReplyThread` | Adds a zero-period, priority `ImportantReply` ticker when either participant is the selected character. | Native reply scheduling changes; no Ostrabus replacement conflicts with it. Existing ship-surrender condition checks pass. |

Native `CrewSim._TeleportCO`, `SaveToShallow`, `Ship.Destroy`, `NavData` playback,
`ShipSitu` integration and celestial collision handling are unchanged. This
update does not establish a fix for the observed PASS stall or the O-C7FU loss.
The independently prepared transit-profiler coverage repair remains in the
current build; it is diagnostic work, not a stall correction.

## Hooks and behavior graph

The actual bundled-Mono binding audits pass **306 classes, zero failures and
zero warnings** on each game version. All **391 hook/target rows** retain their
target identities/signatures:

- **386** target bodies/contracts are unchanged.
- **Three** changed rows target `CrewSim.<DoLoadGame>d__276.MoveNext`: loading
  profiling, saved-CO ship lookup, and concrete ship-file readers.
- **Two** changed rows target `Ship.Maneuver`: rotation manual-input handling
  and shallow RCS/torch control handoff. Both are prefixes, not replacements of
  the native exhaust-speed guard. Relevant execution checks pass.

A targeted native contrast also exercises the exact changed Maneuver branch:
1.1.0.1 rejects a zero exhaust-speed cache, whereas 1.1.0.2 produces its fallback
RCS thrust. On 1.1.0.2, 23 additional assertions compare unhooked native actuation
with both production prefixes and the deferred rotation controller. They check
exact acceleration components, angular state and fuel for zero/normal exhaust
caches, AI/non-AI hulls, and native rejection of missing thrusters, distribution
or physical fuel. AI cases also verify stale torch input is released. Acceleration
components are compared directly because Unity's approximate Vector2 equality
can classify these AU-scale accelerations as zero. The old-assembly negative
control fails at the intended zero-exhaust rejection, establishing that these
checks distinguish the changed behavior rather than merely rebinding hooks.

The compared main DLL SHA-256 is
`fb1e6e02ce01b2275dbe9657e8297759eeff0dd9712bafa75586a6badedd72ad`.
This identifies the current working-tree candidate, including the previously
prepared diagnostic changes; it is not a claim about a running game's loaded DLL.

All seven behavior-graph slices were audited with the new assembly/assets.
Accepted snapshots were not rewritten. Raw flags include previously unaccepted
1.1.0.1 changes and conservative virtual/iterator dispatch dependencies; their
counts must not be presented as changes newly introduced by 1.1.0.2. The isolated
1.1.0.1-to-1.1.0.2 assembly delta above establishes the incremental code scope.

## Assets and station preservation

Twelve shipped JSON files have different bytes. Full parsed-token comparison
shows all eleven station-template files have identical contents. The one
substantive asset change renames `TIsBarterBCERcrapKiosk` to
`TIsBarterBCERScrapKiosk`, matching its existing trader-loot reference. No Ostrabus
asset override is required, and there is no additional station layout payload
to migrate in this update.
The player reports having notified the developer of both this trigger typo and
the incorrect kiosk station targets before this update; the shipped changes
correspond to those two reports.

The new native kiosk block uses whole-station respawn, with the same property
loss mechanism documented in the [1.1.0.1 review](COMPATIBILITY-1.1.0.1.md).
It corrects the two physical section targets the standalone companion already
corrected in 0.1.1. The native log mistakenly still says it is applying the
1.1.0.1 fix; the eligibility/history marker is actually 1.1.0.2.

The previous companion is incompatible with the new assembly: its hash guard
refuses it, and direct offline binding reproduces the changed loader contract
(`init=1, kiosk respawn=2`, previously one respawn site). Companion **0.1.2**:

- Admits only the reviewed 1.1.0.1 and 1.1.0.2 hashes and checks their expected
  one/two kiosk respawn sites respectively.
- Intercepts both kiosk blocks on 1.1.0.2 while retaining other native respawns.
- Includes saves originating in 1.1.0.1 when applying missing substation upgrades
  before native world construction.
- Retains the existing kiosk payload, saved-object preservation rules and native
  per-section completion conditions. No new plugin-specific save fields exist.

The previously verified continuation already has both substation conditions set.
Those conditions also prevent the new native block from regenerating the two
sections after the companion is removed. That migrated save needs no additional
companion run solely for this update. Earlier unmigrated backups need the
updated companion. Main `Deploy-Mod.cmd` remains the full Ostrabus deployment
and does not install this separate one-time plugin.

## Regression evidence

| Check | Result |
| --- | --- |
| Full main-plugin build | Zero warnings/errors; identical plugin bytes across native versions |
| Actual hook binding on each version | 306 classes; zero failures/warnings; no target-signature changes |
| Orbital production-source study | 2,644 assertions; new native MVID confirmed |
| OrbitalCheck, AiRendezvousCheck, AiRouteCompletionCheck, PlanetaryNavigationCheck, DockingOrbitStudy, TorchNativeOracle | All six pass independently and composed with production orbital-velocity hooks: 12 successful runs |
| LoadCompatibilityCheck | 118 checks |
| ComponentInitializationCheck | 25 checks |
| ShipSurrenderWorkCheck | 69 checks |
| RotationModuleCheck | 337,543 checks, including 23 targeted exhaust-fallback contrasts |
| LocalNavigationFrameCheck | 61 checks |
| TileHelperCleanupCheck | 1,036 checks |
| TransitProfilingCheck | 26 checks |
| ShipDestructionEvidenceCheck | 14 checks |
| SaveForwardCompatibilityCheck | 281 checks |
| Companion native integration | 58 checks on each native version; actual old/new MVIDs confirmed |
| Companion JSON/grid plans | 194 assertions using the new installation's assets |
| Full main plugin plus companion binding | 307 classes; zero failures/warnings |
| Packaged companion installer | 11 checks; workspace fixture only |

The companion package is generated separately as
`dist/StationMigration-0.1.2-1.1.0.2.zip`. Detailed receipts and immutable old
binding controls are under `obj/Compatibility-1.1.0.2`. Engine-heavy fixture
boundaries remain explicit; this audit does not turn the incomplete live PASS
timing/death investigation into a resolved issue.
