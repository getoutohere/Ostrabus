# Cross-role and station-scope audit

Run `ScavFuelDiagnosis.dll scope` through the bundled-Mono host after building
this test project in Release. See RESULT.md for host invocation.

70 assertions cover the implemented refill repair and remaining scope boundaries:

- Seven role/home combinations receive the same prescribed native stored plan.
- Consolidated general target-prediction clearing, VORB route cleanup and shared refill
  observers run as production Harmony patches.
- Actual AIRefuel and NavData.TimeAdvance now retain the refill for every role/home
  case, without changing the plan's expenditure. The preceding audit recorded
  the original refill-erasure witness before this repair.
- Equivalent target reapplication preserves valid state; a new goal can retain
  an old plotted route outside the current regional scope.
- One prescribed negative arrival budget becomes affordable after a refill, but
  shared accounting translates it before the unchanged VORB validation runs.
- General deployer recovery reaches home-targeted FlyTo from empty Main[1] and
  saved targetless Main[3] at VORB, OKLG and a third home, without the VORB FlyTo
  prefix. Native selectors, sequence, eligibility and failure transitions execute.

World positions, capacities, live-plan/refill coincidence and goal changes are
explicit inputs. Valid-target physical flight is a Running fixture. Targetless
native FlyTo failure, plan advancement and shallow fueling are real shipped code.
Presentation/condition/captain and collision-distance boundaries are stubbed.
HQCH is a counterfactual home control, not a claim of a vanilla wreck-deployer fleet.

Refill bad-state assertions have been replaced with desired-behavior regressions.
The remaining old-route retarget witness is retained outside VORB; that behavior
was not generalized in this change. `replenish` adds accounting edge cases and
`refuel` installs the shared observers to check explicit API coexistence.

See [continued audit](../../Notes/AI-REGIONAL-SCOPE-AUDIT.md) and the
[deployer graph](../../Navigation/BehaviorGraph/Deployer/README.md).

Mode `retarget` separately runs 60 native caller/production-patch controls; see the
[caller audit](../../Notes/AI-RETARGET-CALLER-AUDIT.md). It establishes a police
escape sequence without treating this generic setter fixture as universal
native reachability. The former prototype is now a production escape-setter repair.
