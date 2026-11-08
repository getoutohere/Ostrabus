# Ostrabus 0.2.1

Target: Ostranauts 1.0.1.10 (`public_test`), BepInEx 5, Windows.

- Filter foreign-ship needs/leisure/social targets before autonomous selection
  when disembarking is forbidden. Works independently of AlternativeWork;
  manual/remote interactions and native emergency permission overrides remain.
- Stop stack merge cleanup subtracting cargo that remains present, and propagate
  live condition-driven mass changes through inventory/character accounting once.
- Extend `massfix` to reviewed anatomical parts and human aggregates as well as
  containers. Plan ownership trees before applying corrections; preserve actual
  limbs, traits and equipment, report uncertain cases, and restore a tree if a
  write fails. Scope remains materialized world ships.
- Tolerate tiny floating-point discrepancies in the Venus dolly weight allowance
  so inventory changes do not disable the entire allowance.
- Add native offline regressions and read-only character/save-graph audit tools.

The project, BepInEx plugin and native PDA metadata now report 0.2.1. Existing
plugin/config/PDA identifiers remain unchanged. No new custom save fields, save
conversion, configuration reset or automatic historical mass repair is required.
To repair existing corruption explicitly, run `massfix` after installing/loading.

Validation covers native assembly control flow with engine/service adapters:
67 autonomous disembark checks, 599 crew-rest regressions, 475 standard mass
checks (710 with the detached saved-inventory graph), 105 remote-barter checks,
77 Venus dolly checks, 47 PDA checks and 146 Harmony classes bound without errors
or warnings. The detached graph run corrected 14 objects, conservatively skipped
22, and made zero corrections on a second pass; input hashes were unchanged.
These are offline results; the expanded repair is not claimed live-validated.

Proposed commit title:

`Prepare Ostrabus 0.2.1: repair mass accounting and filter autonomous targets`

Generated binaries, release ZIPs, private saves and audit captures are excluded
from source control. Preparing this snapshot does not create a commit/tag or
publish/push a release.
