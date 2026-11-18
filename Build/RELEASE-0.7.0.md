# Ostrabus 0.7.0

Prepared for **Ostranauts 1.1.0.2 (`public_test`)**, Windows and BepInEx 5.
Status: **experimental and partially playtested**. This release includes all
maintained changes since the [0.6.0 checkpoint](RELEASE-0.6.0.md), with the
acceptance limits below; preparing it does not create a commit, tag or hosted release.

## Changes since 0.6.0

- **Follow-up gravity repairs:** clean up completed dodges after projectile loss
  and restrict orbital waiting to supported clear bound trajectories; see
  [the native study and repair scope](../Navigation/AI-GRAVITY-EXEMPTIONS.md#implemented-follow-up).
  These source changes were made after the initial release preparation;
  previously prepared archives must be rebuilt before distribution.

- **Orbital velocity:** update reference velocities at the requested time and
  fit the player's transition into orbital coast without changing its velocity,
  retaining the game's body positions and station hierarchy; see
  [orbital velocity](../Orbital/Velocity/README.md).
- **Collision avoidance:** admit slow and accelerating approaches to the existing
  avoidance logic, reject unsafe course resumption, and prevent repeated evasive
  reversals; see [avoidance evidence](../Navigation/PLANETARY-AVOIDANCE-AUDIT.md).
- **NPC approaches:** stop reactor safety limiting from weakening RCS planning,
  reject routes whose endpoint fails to meet the destination, and retain a
  freighter's delivery target after a numerical planning failure; see
  [freight RCS approaches](../Navigation/FREIGHT-RCS-APPROACH-INVESTIGATION.md).
- **Player course targets:** separate the plotted destination from the sensor
  contact, preventing early completion or cancellation when an inspected ship
  disappears while still cancelling when the actual destination is removed;
  see [the target repair](../Navigation/TORCH-COURSE-FIX.md#sensor-selection-cancelling-a-player-course-2026-11-17).
- **Torch fuel and coast reporting:** constrain reconciliation refunds to paired
  reactants and available tank space, recognize supported native-format saved
  courses without restoring past fuel, and report coast acceleration from the
  trajectory instead of a previous burn's cached thrust; see
  [fuel and playback](../Navigation/TORCH-COURSE-FIX.md).
- **Docking attachments:** retain a passively visited wreck's previous valid
  orbital reference during undocking instead of replacing a station reference
  such as HQCH with the nearest planet; see
  [docked orbit locks](../Navigation/DOCKED-ORBIT-LOCK.md).
- **Civilian destinations:** exclude derelicts from random civilian visits while
  preserving scavenger salvage, asteroid visits and already assigned journeys;
  this is an intentional [simulation policy](../Navigation/CIVILIAN-DERELICT-VISITS.md).
- **Station shelter:** exclude stations and every connected docking/tether chain
  from BeatManager micrometeoroid encounters, including fallback selection;
  see [micrometeoroid rules](../Micrometeoroids/README.md).
- **Loading and time skips:** resolve asset component types in the game assembly
  when Harmony relocates initialization code, bound trajectory previews at the
  next course completion, match fractional integration intervals to the native
  clock, and extend temporary loading/death diagnostics; see
  [component loading](../MiscBugFixes/MASS-BOOKKEEPING-AUDIT.md),
  [SFF/PASS](../Orbital/SFF-IMPLEMENTATION.md) and
  [profiling](../Optimizations/TRANSIT-PROFILING.md).
- **Compatibility and reference tools:** retain the reviewed 1.1.0.1 and 1.1.0.2
  native integrations, add a deterministic assembly-member comparison tool, and
  update behavior-graph evidence and content audits without claiming complete
  behavior-model coverage.
- **Documentation and packaging:** regroup and edit the README inventory, align
  the main plugin and four bundled native mods at 0.7.0 / game 1.1.0.2, and retain
  full deployment plus dependency-aware custom builds; remove machine-specific
  audit paths and keep custom DLL debug references portable after finalization.

## In-game evidence and limitations

The [1.1.0.2 compatibility review](COMPATIBILITY-1.1.0.2.md) compares the actual
native assemblies and relevant assets, including the new RCS fallback branch.
It does not establish that every game system is correct. The earlier
[1.1 port](COMPATIBILITY-1.1.0.0.md) and
[feature ledger](FEATURE-COMPATIBILITY-1.1.0.0.md) remain historical evidence.

- **Observed live:** the player confirmed smoother Venus velocity/readout behavior
  during normal controls, with bounded comparisons of nearby AI traffic; the
  [recording notes](../Orbital/Velocity/README.md#live-venus-check-1101-2026-11-17)
  describe the tested build and limits.
- **Offline acceptance:** the latest autopilot target, avoidance, NPC approach,
  passive-undocking and torch accounting refinements have deterministic native
  reproduction/regression evidence, but no claim of final-build live acceptance.
- **Long PASS loads remain unresolved:** loading stalls of tens of seconds were
  observed, and profiling separates simulation time from some loading/teardown
  costs without accounting for the entire stall.
- **Passenger-skip deaths are not fully explained:** a captured destruction stack
  establishes collision processing for one hull, but the historical starting
  state is insufficient to prove whether that collision was avoidable or caused
  by a remaining navigation defect; the diagnostics are not a fix for that question.
- **Existing damage:** these repairs do not recreate previously deleted NPCs,
  hulls or cargo, and a new destination filter does not rewrite existing visits.
- **Experimental options:** the optional save reader and policy features retain
  their existing defaults and evidence limits; release preparation does not
  silently enable them or certify every campaign outcome.

## Validation

Both Debug and Release builds pass with **zero warnings and errors** against
the installed 1.1.0.2 assembly. The actual full-plugin Harmony audit binds
**309 patch classes with zero failures or warnings**, including the follow-up
dodge cleanup.

| Release check | Result |
| --- | --- |
| Orbital production implementation | 5,368 assertions in the follow-up run; no failed gates |
| Orbital, AI approach, route completion, planetary avoidance, docking and reactor oracle | All six pass alone and composed with the production velocity patches: 12 runs |
| Player autopilot destinations | 66 native command/lifetime and plotting-identity checks |
| Avoidance admission and continuous departure | 53 checks and 93 sustained-replay assertions |
| Reactor/tank oracle | 408,488 checks, including 5,065 refund/pairing checks and 707 coast-gravity checks |
| Orbital timing / decisions / freight | 241 / 103 / 1,494 checks |
| Passive docking and undocking references | 834 checks, including 385 undocking-reference A/B cases |
| Dodge cleanup and waiting admission | 66 native/production study checks; 159 cumulative scavenger lifecycle checks |
| Micrometeoroid firing and selection | 439 checks |
| Component loading / legacy RCS restoration | 25 / 281 checks |
| Nav placement / rotation / hull drawing | 727 / 337,543 / 1,237 checks |
| PDA settings / quick hacking / scrubber audio | 93 / 176 / 39 checks |
| Civilian derelict filtering | 23 policy checks within the 87-check native traffic-role run |
| Custom builder | 614 self-tests; original four profiles pass; the follow-up dodge/wait profile compiles, verifies and binds all 53 selected patch classes |
| Native and companion installers | Disposable fixture installs pass, including 51 full/custom Apartment Refuel coexistence checks |
| Station Kiosk Migration | 58 native loader checks, 194 detached layout assertions and 11 packaged-installer checks |
| Behavior reference | All seven slices pass repository source-anchor/fingerprint and scenario/model consistency checks |
| Initial distribution archive | 59 metadata/payload checks; 62 entries; predates the follow-up gravity repairs and requires rebuilding |
| README inventory | 123 feature entries under 17 subject groups; all previous labels/links retained and every local target exists |

Counts describe their own fixtures and can overlap; they are not added into a
single coverage total. The full reactor oracle includes the refund and coast
cases named separately above.

Native-assembly tests use explicit engine/rendering/asset-materialization fixtures
where needed; binding checks prove patch installation, not every gameplay outcome.
Raw logs and package-verification receipts are under `obj/Release-0.7.0/` and are
not part of the source commit or consumer archive.

Behavior graphs retain their historical 1.1.0.0 native assembly pins; their
current repository source fingerprints and scenario consistency are checked
separately from the 1.1.0.2 compatibility audit. No new assembly baseline is
accepted merely to clear a revalidation flag. The player-autopilot interaction
has native regression coverage but remains an explicit gap in the target-lifecycle
graph's scenarios.

Final main-plugin identity (Debug distribution build):

- Assembly version: `0.7.0.0`.
- MVID: `35a700a5-3e2a-4167-9219-ad88d6ebcc5c`.
- SHA-256: `4dd2532165aebd48c5b1e2ffc4cbe5736423a7d7e30a658cb0109090e0e3499e`.
- Native assembly MVID: `158470fc-8494-4bfe-b750-ad9d18dbf8d0`.
- Native assembly SHA-256: `133a9cc94ad4f4d5db951b65249b6cdf78f53063419d836b8871b92960ec3c37`.

## Independent companions and packaging boundaries

- **Station Kiosk Migration 0.1.2** is a separate optional plugin, supporting the
  reviewed 1.1.0.1 and 1.1.0.2 loader blocks without rebuilding saved station
  interiors; it is packaged separately and never installed by normal Ostrabus
  deployment, and its latest 1.1.0.2 integration retains offline acceptance.
- **Apartment Refuel Terminal 0.1.1** retains its own version and separate installer
  inside the main archive; packaging it does not activate it or grant live acceptance.
- The metadata-only world-content migration prototype remains unnecessary and
  excluded; see the [content audit](CONTENT-AVAILABILITY-1.1.0.0.md).
- Full `Deploy-Mod.cmd` still installs the entire omnibus; custom profiles include
  selected features, dependencies and matching PDA controls.
- Game assemblies, decompiled source, complete saves, local settings, raw captures
  and the offline executive prototype are excluded from the distributed runtime.
- The inherited Thunderstore publishing workflow remains unconfigured; local
  full/custom installers and archives are the validated distribution paths.

Suggested commit title:

`Ostrabus 0.7.0: support Ostranauts 1.1.0.2 and refine navigation and simulation`
