# Public snapshot preparation

Current target: Ostranauts 1.0.1.10 (`public_test`), BepInEx 5, Windows.
Current staging scope: [Ostrabus 0.3.0](RELEASE-0.3.0.md): the custom plugin builder,
its installer and composition tests. Project, plugin and PDA metadata report
0.3.0. The two pending torch gameplay changes remain excluded.
The earlier [post-0.2.2 preparation](PREPARED-CHANGES.md) was committed as
`191d8d9`; it is historical context rather than this commit's new scope.
`Ostrabus-0.2` remains the latest local tag; this preparation creates no tag.
The local installation used for validation has BepInEx 5.4.23.6. The .NET 8 SDK
is needed to build, not to use the installed plugin. Game and loader dependencies
are supplied by the player's installation and are not redistributed.

## Prepared

- Root README states the source-first omnibus purpose, support expectations,
  tested game version and feature inventory.
- Source/build/test paths use installation discovery or environment overrides.
  Generated PDB paths map to `/_/Ostrabus`, not the author's workspace.
- Packaging includes the plugin, symbols, PDA assets, installer, existing license
  and standalone installation notes. Raw captures and game assemblies stay out.
- Native PDA metadata targets 1.0.1.10. Project/plugin/PDA versions are aligned;
  no Git tag or hosted release is created by packaging.
- Legacy plugin/config/app identifiers are intentionally retained where needed
  for installed users and saved settings. Renaming them is not cosmetic cleanup.
- Economic SFF and API live results are recorded in `Orbital/Economy/README.md`.
  Native binding and offline regression checks are documented with each module.

## Decisions and follow-up

- `Ostrabus-0.2` marks the earlier experimental snapshot. Custom builder validation
  produces ignored test packages under `dist/`; they are not a hosted release.
- Earlier snapshots are documented in [0.2.2](RELEASE-0.2.2.md) and the
  [post-0.2.2 changes](PREPARED-CHANGES.md). The current [0.3.0 scope](RELEASE-0.3.0.md)
  is recorded separately; commit/tag creation and pushing remain separate
  actions. Do not advertise compatibility beyond the tested game.
- Keep known investigations distinguishable from completed fixes. In particular,
  orphaned economic records remain an investigation, not an automatic cleanup.
  See `Orbital/Economy/ORPHANED-MARKETS.md`.
- Diagnostic hooks that support live captures remain opt-in and removable. Review
  launch defaults before broadly distributing the package; do not silently change
  the author's gameplay defaults as part of documentation cleanup.
- Existing license text and attribution are preserved. Review its authorship and
  intended terms before a tagged release rather than inventing replacement terms.
- The inherited Thunderstore workflow is still template infrastructure: its
  publisher namespace/URL and reference-assembly input are not configured for
  this snapshot. The validated paths are the Windows full/custom builders and
  their local package scripts; this preparation does not certify or invoke CI
  publishing. The package version field is aligned at 0.3.0.
- Selected native-state fixtures are useful regression inputs; exploratory tools
  may need ignored local captures. The repository does not redistribute decompiled
  game source, assemblies, or complete saves.

## Review workflow

Build the desired configuration, run the relevant offline suites and startup
binding audit, then run `Package-Mod.ps1 -Configuration Debug` (or `Release` after
building that configuration). Inspect ZIP contents and test the included installers
against a disposable installation layout before publishing.

Before tagging, review the staged diff and inspect the archive's file inventory.
Keep generated archives, binaries, local settings and live captures out of source control.

## Historical 0.2 source preparation, November 8, 2026

Changes since 0.1 include independent opt-in EM propagation/contact estimates and
their PDA controls/comparison tools; AI rendezvous/crew lifecycle corrections;
VORB retrieval depot selection; Venus dolly encumbrance; and station-relative AI
departure waypoints with intermediate freight-stage continuation during SFF.
The target orbital-extrema UI document is a proposal, not an implemented feature.

Module notes distinguish offline fixtures from live observations. Runtime sensor
tests include the outer-system paused-redraw regression. Navigation cases cover
station hierarchies and unchanged player plotting; economic tests cover virtual
stage clocks, carried budgets, partial travel, bounded retries and cleanup.
Selected orbital inputs live in `Tests/Fixtures/Navigation`; full saves and working
captures remain ignored. No custom save DTOs or new mod-required save records are
introduced by these changes. EM/contact settings default off in config.

That preparation pass did not itself create a commit/tag or publish a release.
The later `Ostrabus-0.2` tag records that snapshot; current preparation is described
in [prepared changes](PREPARED-CHANGES.md).

Preparation checks passed: 33 AI lifecycle, 69 departure, 649 sensor runtime,
55 dolly, 25 retrieval and 47 PDA checks. The related economic/orbital runs passed
1,443 and 4,850 checks, the model comparison passed 5,476 assertions, and the
startup binding audit bound 141 classes without failures or warnings. These are
offline results; the module notes describe live observations and test boundaries.
