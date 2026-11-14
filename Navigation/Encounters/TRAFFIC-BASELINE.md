# BCER arrival baseline, 2026-11-13

Captured paused at epoch **65633872851.578644**, inspection generation 7, after
the four explicitly selected convoy ships were pruned. Raw local captures are
retained in ignored `obj/BCERTrafficBaseline-20261113T215851Z`.

- Registry: 264 ships, 31 controller entries, 42 faction/BCER-related records.
- Twelve Navy patrols: eight BCER and four BCRS, all `FlyToPatrol`, no combat
  targets or docking links, roughly 91-100% RCS fuel.
- Their speeds relative to BCER span 0.08-0.74 km/s; BCRS ships legitimately
  operate about 20,000-25,000 km from BCER.
- One active local civilian, E-PUPW, was travelling toward BCER with 90.4% RCS.
- Four interregional freighters were separately recorded: O-6Q85 (BCER-VORB),
  O-7BQ2 (BCRS-MVOL), O-MJE (BCER-EJDR), O-7HD (BCRS-SVIR). Long-distance
  freight is legitimate and outside the encounter policy.

Patrol IDs: O-P5U, O-GR3, O-Q1M, O-YMYG, O-100C, V-3RR, O-5CR, O-CT6EL,
O-1WFF, O-RCD, O-TUH5N, O-GRJC. Raw records include fuel, nav, ownership,
docking, combat and recent AI events; unavailable hidden-state fields are not
evidence that a registered hull is active in space.

The pruned convoy comprised O-YV7, O-DA2, O-CG7L and O-5GBK, totalling 21 crew.
Each passed the prune preview without force, then returned `removed=true` from
the explicit prune call. Evidence is in ignored `obj/BCERConvoy-20261113T213804Z`.
No other actors were repaired, moved or pruned during the baseline.
