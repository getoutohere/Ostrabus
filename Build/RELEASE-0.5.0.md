# Ostrabus 0.5.0

Prepared source/public snapshot since `ed83d46` (0.4.0). Target remains
Ostranauts 1.0.1.10 (`public_test`), BepInEx 5, Windows. This preparation stages
the reviewed changes; it does not commit, tag, push or publish them.

The minor version reflects new simulation behavior and selectable features as
well as bug repairs. Existing plugin/config/native-app identifiers stay stable.

## Included

- AI control ownership: cancel abandoned courses at actual controller withdrawal,
  reject stale controller removal/dispatch, preserve replacement autopilot plans,
  and terminate retired combat continuations. Existing phantom-force hulls are
  not automatically repaired or removed.
- Combat and patrol: preserve authored threat priority, finish combat cleanup
  before resuming patrol, reject neutralized targets in regular and Blockade
  detection, correct surrender accounting and isolate radio social effects.
- Navigation and time skips: use local station frames, preserve mission targets
  through SFF placement, repair absolute wait/SOS clocks and persist queued
  command order/callbacks. Bounty departures receive native station refueling.
- Ceres: apply feasible-return/reserve checks at native decision points while
  preserving urgent/combat decisions; constrain BeatManager faction encounters
  to regional infrastructure and plausible relative motion.
- Scavengers: preserve native emergency assistance, throttle failed selection,
  attempt feasible station returns and permit unpowered orbital waiting. These
  intentional simulation policies are documented separately from defect repairs.
- Micrometeoroids: share sensors/PDCs across docked hulls and check the actual
  native burst projectile paths against connected hulls before firing.
- Cargo pods: isolate rating-preview market actors and provide explicit
  `cargopodfix` reconciliation/rollback tooling for existing player-ship records.
- QoL and smaller fixes: assets-only quick PDA hacking with full/custom native
  mod installation support; missing bounty indicators; confirmed temporary
  bounty escort permission; correct hull outline rotation; console filtering.
- Inspection/reference tooling: non-destructive velocity-match/recovery Hold,
  explicit release, expanded AI behavior slices and native ownership inventories
  with assembly-dependent revalidation, focused reproductions and regressions.

## Scope and evidence limits

The three torch repairs released in 0.4.0 and its momentum-aware planning remain
included; the obsolete duplicate pending-playtest README entry is removed.
The bounty escort previously held back is included after the Jian Gonzales live
escort confirmation. Hand-in and timed custody expiry are not claimed as live
tests. Module notes distinguish native execution from Unity fixture boundaries.

The latest live Ceres survey confirmed the loaded ownership-repair build and
found no controllerless thrust in the sampled registry. Seven thrusting regional
ships had living, aboard captains without incapacitation conditions. This is a
post-load observation, not proof that every future transition is correct. It
used the development DLL before the release metadata bump; the packaged 0.5.0
binary is checked offline, not claimed as a separate completed live test.

Neutral ATC rescue under a fictional spacefarers' convention remains a design
discussion. No new regional rescue dispatcher or automated old-save ship pruning
is included. Numerical flight remains outside the behavior graph's semantic
coverage, with its dedicated native audit and tests linked explicitly.

PASS/shuttle large-step integration and the proposed ship mission executive
remain documented investigations/design work, not implemented runtime changes.

## Validation

Preparation checks against the installed game assembly:

- Debug and Release builds pass with zero compiler warnings/errors.
- Forty-three native suite/mode runs pass under bundled Mono, including linked
  and compiled-production ownership checks, patrol/Blockade/surrender paths,
  queue save/load, local frames, SFF completion, scavenger waits/refills, cargo-pod
  isolation/reconciliation, native PDA loading, escort permissions, MM defense,
  hull consumers, economy, signal boxes and towing. Module notes describe fixture
  boundaries; assertion totals include repeated invariants.
- Full startup binding: 263 patch classes, zero failures or warnings.
- Seven graph slices validate; their read-only assembly audits report zero
  changed tracked sources or elements awaiting revalidation. The 26 synthetic
  assembly-review checks and exact-assembly ownership review also pass. This is
  declared-dependency coverage, not a complete world-behavior proof.
- Installer fixtures pass for Settings (16 checks), predecessor rename/rollback
  and assets-only PDA hacking (20 checks), without changing the installed game.
- Custom builder: 517 self-test checks and 36 compiled/finalized/native-bound
  profiles pass, including idempotence and registration-order comparisons.
  Recommended still excludes `ceres-navigation`; Full includes it. The custom
  installer passes 65 disposable-fixture checks, including native mod selection,
  disabled-state preservation, backups and rejected-package safety.
- Source review includes compressed graph baselines and portable PDB document
  names. No author-specific machine paths or generated binaries are selected for
  Git. Broken scavenger documentation links were repaired.
- The Release ZIP contains 21 files and exactly one plugin DLL; both native mod
  metadata versions are 0.5.0. Game dependencies, raw captures and full saves are
  absent. The ignored archive is `dist/Ostrabus-0.5.0.zip`.

Graph dependency baselines and append-only acceptance history are intentional
development reference inputs, rather than disposable live captures.

Proposed commit title:

`Ostrabus 0.5.0: repair AI control, combat and regional traffic`
