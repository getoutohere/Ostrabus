# Preserved Ostranauts 1.0.1.10 baseline

This snapshot freezes the accumulated Ostrabus work before production changes for
Ostranauts 1.1.0.0. It retains Ostrabus metadata version 0.5.0; this is a source
checkpoint, not a new numbered release or a compatibility claim for 1.1.

The preserved game assembly used for the freeze checks has SHA-256
`8058f7a07f93d5d44678aaf8d7f749050c0d9b1c9ed1c7419eb521ddcd2097e7`.
Set `OSTRANAUTS_DIR` to a preserved **1.0.1.10** installation before building or
running this snapshot. The normal Steam installation may now contain a newer
game; the default path alone does not select the compatible version.

## Accumulated work captured

- Native route-completion, SFF destination, incapacitated-pilot and civilian-visit
  timing repairs, with Ceres attachment/displacement and freight lifecycle work.
- Cargo loading/delivery corrections, bounded market handling, exact-quantity
  recovery APIs and associated diagnosis/reference documentation.
- Fresh AI crew identity protection, configurable feature packaging and the
  separate random NPC gender-distribution simulation change.
- Selected-hull range/proximity consistency, updated hull/docking integration,
  console history retention and the physical RCS rotation module.
- Updated traffic behavior/dependency graphs and immutable accepted snapshots.
- Offline executive architecture, prototypes and conformance studies. These are
  reference tools, not a replacement AI controller installed in the game.
- The independently packaged Apartment Refuel Terminal companion, its stock and
  installer integration, and its native fixture suite. It is optional, excluded
  from omnibus patch discovery, and remains **pending live acceptance testing**.

Existing module documents retain their own scope and live-validation limitations.
This checkpoint does not promote pending work to playtested or release-ready.

## Freeze validation, November 15, 2026

Debug and Release rebuilds pass for the omnibus and optional companion. The
selected freeze run completes 32 successful build/run steps using the preserved
installation, verifying the actual game-DLL hash in each native harness output.
Mono-hosted checks run sequentially to avoid sharing its runtime files concurrently.

| Check | Result |
| --- | --- |
| Whole-plugin plus optional companion binding | 286 omnibus + 5 companion classes; no failures, disabled patches or warnings |
| Console history | 3,058 checks |
| AI identities | 8 native reproductions + 122 repair checks |
| Route completion/recovery | 661 checks, including 295 shared playback checks |
| Orbital/SFF/economy | 4,855 checks |
| Hull integration | 1,194 checks |
| Freight recovery | 98 checks, including 43 quantity-recovery checks |
| Civilian waiting | Native suite passes |
| Regional lifecycle/cleanup | 1,524 checks |
| Random population distribution | 23 checks |
| RCS rotation | 337,520 checks |
| Torch/towing | 343,871 checks |
| Optional apartment refuel | 239 checks |
| Custom-builder self-test | 559 checks |

These are selected regression checks appropriate to the accumulated work, not a
claim that every exploratory harness or installer combination was rerun. Earlier
module-specific evidence remains documented separately. The freeze verification
does not deploy anything, write game assets, modify saves or validate 1.1.0.0.

The initial 1.1 audit is separate work and is deliberately excluded from this
baseline commit. Migration remains deferred. Keep old/new source and asset
baselines separate when continuing compatibility work.
