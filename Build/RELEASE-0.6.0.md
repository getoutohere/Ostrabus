# Ostrabus 0.6.0

Prepared for **Ostranauts 1.1.0.0 (`public_test`)**, Windows and BepInEx 5.
Status: **experimental and partially playtested**. The compatibility port has
in-game acceptance for the interactions below; the later additions have native
offline evidence and remain pending live acceptance. This is not a claim of
complete campaign, UI or simulation coverage.

The preceding source checkpoint is `be8e282`, tagged `Ostrabus-5.1.0`, targeting
Ostranauts 1.0.1.10 with 0.5.0 package metadata. That historical tag is retained
unchanged. This release uses the requested **0.6.0** version throughout the main
plugin, project, bundled native-mod metadata and package manifest.

## Included changes

- **1.1 compatibility:** retain the reviewed existing features, adapt twelve
  feature integrations and add legacy propulsion-cache restoration, nav-module
  placement correction and an assets-only CO2 scrubber audio repair. The
  [per-feature ledger](FEATURE-COMPATIBILITY-1.1.0.0.md) and
  [compatibility audit](COMPATIBILITY-1.1.0.0.md) distinguish native comparisons,
  executed tests and source-reviewed boundaries.
- **Old-save steering:** reconstruct missing 1.1 RCS propulsion metadata from
  installed parts and native eligibility rules, retaining fuel and combat damage.
  This is an ordinary forward-compatibility feature, not world-content migration.
- **Nav UI:** correct parent-pivot placement of the course panel; retain wheel
  fine adjustment alongside native Shift-drag; adapt hull, proximity and station
  collision integration to native double distances. Docking-clearance replies
  now use the same selected-hull geometry as docking controls and target data.
- **SFF and PASS:** share bounded, decision-aware world stepping between GUI
  SFF and passenger time skips, drain native pending end-turn work, refresh
  instant-moor destinations, preserve moving body-bound endpoints and retain
  existing course-completion, freight and collision-history ownership.
- **Bounty persistence:** retain active targets, existing ownership claims and
  their original hulls through administrative retirement and cleanup. Native
  departure, custody, expiry, hand-ins and physical death remain authoritative.
- **Passive docking attachments:** preserve an existing authored body attachment
  through visits and tether reconstruction; release it for actual movement or
  an authorized course. Preserve paused-haul saved state and native station,
  ground, mooring and brace constraints. The independent review tightened
  permanent-station identity guards and custom towing's completion dependency.
- **Inspection and maintenance tooling:** retain the nav-layout inspector,
  propulsion/build inspection, bounded lifecycle diagnostics, updated behavior
  graphs with assembly invalidation, content audits and reproducible regression
  tools. These do not install a replacement AI executive.
- **Build and native assets:** update full/custom installation and packaging for
  1.1, including scrubber audio and optional Apartment Refuel stock composition.
  Full deployment continues installing the complete omnibus. Custom builds
  retain dependency resolution and filter PDA controls to selected features.

## In-game evidence and remaining acceptance

| Area | Evidence for this snapshot |
| --- | --- |
| Continuing a legacy save | Loaded in 1.1 with Ostrabus; the legacy RCS defect was reproduced, repaired and followed by a repeat two-hour SFF and ordinary AI resumption. |
| Docking and console controls | Player confirmed docking, rotation, adjustable proximity range and both nav/comms scrollbacks. |
| Course-module placement | Player reproduced the disappearing course panel and subsequently confirmed the placement repair. |
| Continued play and world content | The player continued the legacy campaign in 1.1. Save/code/asset comparisons verify native initialization of the audited new content; visiting a new field is not proof of every random template or part being present. |
| Restored wheel plus Shift-drag | Native precision/input tests pass; no separate live acceptance is claimed for the final restored combination. |
| Later SFF/PASS refinements | Native time-step, AI handoff, freight and completion tests pass. The exact final implementation has not completed a new live acceptance pass. |
| Bounty persistence | Native station, retirement, expiry, custody, hand-in and original-hull reboarding tests pass; live acceptance pending. Previously deleted actors are not recreated. |
| Passive attachment and its review fixes | Original and expanded production-DLL studies pass, including native hauler, wear, towing and GUI/PASS continuation. Live acceptance pending. |
| Standalone Apartment Refuel | Separate optional plugin with its own version, tests and acceptance limits. Packaging it does not activate it. |

## Validation

The prepared 0.6.0 DLL builds with **zero warnings and errors** and passes the
full bundled-Mono Harmony binding audit: **297 decorated patch classes, zero
failures and warnings**. Binding verifies target compatibility, not complete
behavior.

Release-preparation checks also passed: legacy RCS/save compatibility (281),
nav-module placement (727), PDA settings (93), Apartment Refuel (273), scrubber
audio/native assets (39), custom-builder self-tests (593), all seven behavior
graph slices with source and native assembly fingerprints, custom installation
(88), and companion installation/coexistence (51). Installers were exercised in
disposable workspace fixtures; the running game installation was not changed.
The full package passed 53 metadata/payload checks, containing only the omnibus
and optional companion DLLs, with no game assemblies or private captures.

Final omnibus identity (Debug distribution build):

- Assembly version: `0.6.0.0`.
- MVID: `b2c177b2-8747-425e-9445-5c72666baeea`.
- SHA-256: `8617d45098cb0805675e0f45f60693f1f503b47dbd5bb5b48dbfd97fed6dcaf4`.
- Native assembly SHA-256: `a79a5081d9c94307a45fe1d0b55ff1aa1d0af6ea8dfce1f9acc5961cf260ef07`.

These are this release-preparation checkpoint's identities. Earlier receipts in
the module audits describe their own historical builds.

Recent implementation-level results, preserved in module docs and reproducible
from the repository:

| Check | Result |
| --- | ---: |
| Passive attachment, native controls/wear and completion dependency A/B | 449 assertions |
| Braced towing and departure mass | 343,871 assertions |
| Orbital/SFF/PASS | 5,121 assertions |
| Production/freight progression | 1,494 assertions |
| Route completion and recovery | 689 assertions |
| Docked gravity | 47 assertions |
| Bounty preservation | 174 assertions |
| Bounty departure servicing / stale lifecycle | 166 / 42 assertions |
| Hull geometry/contact and geometry ownership/concurrency | 12,465 / 60,687 assertions |
| Hull snapshot/asset/native-coordinate/render-copy checks | 1,237 assertions |
| AI/hull avoidance composition | Both patch-installation orders pass |
| Traffic / bounty behavior graph | 529 / 126 expectations and safety checks |

The new tests use explicit Unity, rendering, destructive teardown, scene or
calendar fixtures where documented. Large assertion counts are not a claim that
every possible ship or campaign outcome was exercised. Final packaging checks
also inspect metadata, included native assets, disposable-install behavior and
exclusion of game binaries and private captures.

## Scope and packaging boundaries

- No automatic world-content migration is installed. The withdrawn metadata-only
  prototype remains source-only investigation history, explicitly not recommended.
  See [content availability](CONTENT-AVAILABILITY-1.1.0.0.md).
- The executive prototype and its 1.1 requalification are offline research. They
  remain excluded from the plugin and distributed runtime payload.
- Complete saves, game assemblies/decompilation, local settings, captures, build
  outputs and packages remain outside the source commit.
- The old-version baseline and historical evidence remain intact. No support for
  other native assembly builds is implied by this 1.1 snapshot.
- Commit preparation does not deploy, tag, push or publish a release.

Suggested commit title:

`Ostrabus 0.6.0: support Ostranauts 1.1 and preserve simulation continuity`
