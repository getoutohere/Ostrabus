# Ostrabus 0.4.0

Prepare the accumulated work since 0.3.1 as a source/public snapshot. Target:
Ostranauts 1.0.1.10 (`public_test`), BepInEx 5. This preparation stages changes;
it does not create a commit, tag, push or hosted release.

## Included

- Torch autopilot: repair stale fuel checks that can abort affordable braking;
  correct plotted consumption, preview estimates, FLOW/coast control and duplicate
  SFF charging; preserve incoming velocity during player course planning.
- Braced towing: propagate a member's plotted course through its connected group,
  use its combined mass and retain the brace-release autopilot abort. Moored
  departure previews follow clamped braces without counting the station or other
  station visitors. Runtime mass and native unbraced damage remain physical.
- Signal boxes: repair gate/output propagation, reconstruction and connection
  lifetime while preserving intentional wiring through temporary mode changes.
- Trading and loading: identify/order barter zones across multiple ships; fix
  loader cancellation ownership, writeback safety and saved inventory beneath
  damaged parents; retain the paced/indexed offscreen loading improvements.
- AI lifecycles: repair expired/converted target identity, civilian visit selection
  and return continuation, FFWD coordinate order, attached-hull cleanup and stale
  timers; prevent regional collision recovery from duplicating freight controllers.
- Navigation display/collisions: preserve correct exterior outline scale, align
  docking/placement and avoidance bounds, and refine compatibility around ports.
- Autotask: discover restoration work after additional idle/needs transitions.
- Performance/inspection: conservative trigger classification; request-only
  controller/economy consistency audits; explicit freight approach and orphaned
  cargo recovery APIs; opt-in deep torch tracing and improved work timing.
- Tooling/docs: expand the evidence-backed AI behavior graph and dependency
  review, include the standalone native MVOL apartment-transit mod source, and
  update feature selection and regression documentation.

The standalone MVOL asset mod remains separately installed as documented in its
directory. No installer silently edits vanilla assets or enables that separate
native mod. Root README/module notes distinguish completed repairs, intentional
simulation policies and investigations without a production patch.

## Deliberately excluded

The latest bounty-escort permission repair, its native harness and module note,
and its entries in the shared README/catalog remain in the working tree outside
this commit. Its live test was not completed. Generated captures, archives,
assemblies and complete saves remain ignored.

## Selection and compatibility

Project, plugin, PDA and package metadata are 0.4.0. Plugin/config/native-app
identifiers stay unchanged for existing installations. No save conversion or new
mod-defined save type is introduced by these changes.

The custom builder now exposes `torch-fuel-cache`, `torch-fuel`, `torch-momentum`
and the existing independent `torch-towing`. Fuel/control requires the cache
repair; momentum planning requires the fuel/control module's shared UI scope.
Recommended continues to include simulation features except `ceres-navigation`,
plus legacy massfix/navfix maintenance commands. Full remains its superset.

## Validation record

Validation is performed against a clean candidate containing exactly the intended
source, with the bounty implementation and shared-file entries removed. Native
harnesses use the installed game's assembly and bundled Mono, sequentially.
Individual module notes describe scene fixtures and limits; assertion totals
include repeated invariants and are not counts of distinct gameplay scenarios.

The live OKLG-to-VORB two-ship flight covered normal acceleration, repeated SFF,
coast and final braking, with fuel matching the cumulative schedule to within a
gram at the recorded checkpoints. The player disengaged at about 1 km/s for
docking. Automatic course completion and the last moored-preview correction are
offline-validated cases, not additional completed live tests.

Preparation results:

- Clean-candidate Debug and Release builds: passed, with no compiler warnings.
- Native Harmony binding audit: 228 patch classes bound, zero failures/warnings.
- Native regressions: signal boxes, multi-ship barter, saved inventory parents,
  paced barter initialization, trigger classification, hull rendering/collision
  consumers, crew rest, controller identity, freight recovery, target lifecycle,
  civilian visits/SFF/region cleanup, retirement, economy, PDA and diagnostics passed.
- Torch regressions: 402,667 reactor/fuel assertions, 343,871 towing assertions,
  4,728 solver scenarios and 31,342 native planner integration assertions passed.
  The reactor total includes the independent-clock duplicate-charge regression.
- Custom builder: all 26 profiles compiled, finalized idempotently, retained
  registration order and bound against the native game assembly. This includes
  cache-only, fuel/control-only, momentum and independent towing selections.
- Installer fixtures: 16 PDA checks, predecessor rename/rollback checks and 39
  custom-installer checks passed without touching the installed game.
- Five graph models passed consistency checks; 26 synthetic assembly-review checks
  passed. Reviewed source fingerprints and missing traffic predicate dependency
  declarations were repaired. Native dependency audits report no changed source
  groups or graph elements awaiting revalidation after that review. These checks
  cover declared dependencies, not the complete game behavior model.
- The reviewed Release ZIP contains 16 files, one Ostrabus DLL, symbols, PDA
  assets and installation support. DLL/PDA versions match 0.4.0; the bounty type,
  game dependencies, captures and author-specific paths are absent. The ignored
  archive is `dist/Ostrabus-0.4.0.zip`.

The index is compared with this bounty-free candidate before handoff. The bounty
files and their three shared-file entries remain unstaged in the working tree.

Proposed commit title:

`Ostrabus 0.4.0: repair torch autopilot, traffic lifecycles and trading`
