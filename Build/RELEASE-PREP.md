# Public snapshot preparation

Current target: Ostranauts 1.0.1.10 (`public_test`), BepInEx 5, Windows.
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
- Native PDA metadata targets 1.0.1.10. Plugin version remains 1.0.0 pending a
  release-version decision; no Git tag or hosted release is created by packaging.
- Legacy plugin/config/app identifiers are intentionally retained where needed
  for installed users and saved settings. Renaming them is not cosmetic cleanup.
- Economic SFF and API live results are recorded in `Orbital/Economy/README.md`.
  Native binding and offline regression checks are documented with each module.

## Decisions and follow-up

- Decide whether to distribute an installable experimental ZIP or keep the public
  repository source-only. A local candidate archive does not publish a release.
- Choose the public snapshot version/tag and synchronize plugin/project/PDA
  versions before tagging. Do not advertise compatibility beyond the tested game.
- Keep known investigations distinguishable from completed fixes. In particular,
  orphaned economic records remain an investigation, not an automatic cleanup.
  See `Orbital/Economy/ORPHANED-MARKETS.md`.
- Diagnostic hooks that support live captures remain opt-in and removable. Review
  launch defaults before broadly distributing the package; do not silently change
  the author's gameplay defaults as part of documentation cleanup.
- Existing license text and attribution are preserved. Review its authorship and
  intended terms before a tagged release rather than inventing replacement terms.
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
