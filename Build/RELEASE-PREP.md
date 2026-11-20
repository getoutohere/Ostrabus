# Public release preparation

Current scope: [Ostrabus 0.7.2](RELEASE-0.7.2.md), supporting **Ostranauts
1.1.0.4 (`public_test`)**, Windows and BepInEx 5. The project, plugin, four
bundled native mods and main package metadata are aligned at 0.7.2; bundled
native assets target game 1.1.0.4. Independent companions retain their own versions.

The top-level README groups the inventory by feature category and subject,
retaining links to implementation and evidence. Release notes distinguish live
observations, deterministic offline tests and unresolved PASS loading/death
questions. No release preparation is a claim of complete campaign coverage.

## Review and validation

1. Review tracked and untracked source since the 0.7.1 checkpoint, including
   behavior-graph evidence, selected fixtures, companion code and documentation.
2. Build Debug and Release against the installed 1.1.0.4 assembly, check actual
   Harmony bindings, and run the changed modules' native regression suites.
3. Check orbital composition with AI, docking, course playback and torch modules;
   verify selected custom profiles and their filtered PDA settings.
4. Run `Package-Mod.ps1 -Configuration Release`, inspect metadata and archive
   contents, and test installers against disposable workspace layouts.
5. Keep packages, binaries, raw captures, complete saves and game assemblies out
   of source control; review the staged diff before any requested commit.

Current receipts are under ignored `obj/Release-0.7.2/`; final check results,
known limitations and artifact identities belong in [the release notes](RELEASE-0.7.2.md).
Preparation does not deploy to the running game, commit, tag, push or publish.

## Packaging and independent companions

- `Deploy-Mod.cmd` always installs the full normal build; `CustomBuilder` creates
  isolated variants with explicit dependencies and selected native assets.
- The main archive includes the optional Apartment Refuel Terminal's separate
  installer without activating it or changing its 0.1.1 version.
- Station Kiosk Migration 0.1.4 is packaged independently for the reviewed
  1.1.0.1--1.1.0.4 loaders; normal deployment never activates it.
- Boneyard Turnover 0.1.5 is an independently packaged simulation feature for
  1.1.0.2--1.1.0.4; full and custom deployments never activate it.
- Bottle Refiller 0.1.7 is independently packaged with native equipment assets,
  an installer/uninstaller and its one-hook gas-transfer plugin; it does not
  require Ostrabus or become enabled by full/custom deployment.
- The metadata-only SaveMigration110 prototype is not recommended and remains
  outside normal packages; native content availability is documented separately.
- Existing plugin/config/app identifiers remain stable for installed users and
  saved settings; no identifiers are renamed for cosmetic consistency.
- Existing license text and attribution remain unchanged.
- The inherited Thunderstore workflow is still template infrastructure with an
  unconfigured publisher/URL and reference-assembly input; no hosted publishing
  is invoked or certified by local packaging.

## Historical checkpoints

- [0.7.1](RELEASE-0.7.1.md), commit `3559c92`, preserves the 1.1.0.3 checkpoint
  before the compatibility review, hull-avoidance and identity repairs, bonus
  diagnostics and the independent bottle refiller.
- [0.7.0](RELEASE-0.7.0.md), commit `a148ec2`, preserves the preceding
  1.1.0.2 release before this incremental compatibility and feature checkpoint.
- [0.6.0](RELEASE-0.6.0.md), commit `fbc3e8b`, is the preserved 1.1.0.0 compatibility
  checkpoint; its build identities and original acceptance notes stay historical.
- [1.1.0.1](COMPATIBILITY-1.1.0.1.md) and [1.1.0.2](COMPATIBILITY-1.1.0.2.md)
  reviews document subsequent native changes and companion adaptations.
- [1.0.1.10 baseline](BASELINE-1.0.1.10.md), commit `be8e282`, is tagged
  `Ostrabus-5.1.0` and retains its original 0.5.0 metadata; build it against the
  preserved old installation rather than redirecting current integration code.
- Earlier release scopes remain in [0.5.0](RELEASE-0.5.0.md),
  [0.4.0](RELEASE-0.4.0.md), [0.3.1](RELEASE-0.3.1.md),
  [0.3.0](RELEASE-0.3.0.md) and [post-0.2.2 preparation](PREPARED-CHANGES.md).


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
