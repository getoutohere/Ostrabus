# RCS delta-v budget and pilot efficiency

Vanilla `Ship.CalculateRCSFuelConsumption` and `Ship.Maneuver` multiply consumed
fuel by `fFuelEfficiencyMod`, normally 1 or 0.75 from `Ship.UpdateCrewSkills` and
`SkillOpsSpaceship`. But `Ship.DeltaVRemainingRCS` omitted that multiplier when
converting fuel back into a usable velocity budget.

At 0.75, spending the entire advertised delta-v consumed only 75% of the fuel.
The advertised budget therefore understated usable delta-v by 25%; correcting it
raises the skilled budget by one third. Native LowFuel/BingoFuel checks, patrol
eligibility, recovery/pruning estimates and the player's displayed delta-v can
all consume this getter. Fuel already stored is not changed.

`InvalidRcsBudgetPatch` now divides the native result by the positive finite
efficiency multiplier, then retains the existing finite/nonnegative normalization.
Invalid efficiency or propulsion reports zero usable budget. It uses the existing
getter hook, adds no cache or save fields, and applies equally to player and AI
ships. Native tank scanning and actual fuel consumption retain their mechanisms.

The shipped-assembly checks in `Tests/AiPlannerDiagnosis` (`ceres-chain` mode)
reproduce the original mismatch by temporarily unpatching the getter, then verify
that the corrected getter inverts native consumption for both skill multipliers.
The additional matrix covers AI/player flags, Shallow/Edit/Full modes and 0/1/300
kg supplies. Full/Edit enumeration is an explicit fuel-source fixture; the
budget/consumption arithmetic executes in the native assembly. Zero-thruster and
invalid efficiency controls retain safe zero budgets.

This general repair ships with `navigation-targets` independently of Ceres policy.
The same feature also includes the general Navy candidate affordability repair
and `LowFuelRetreatProgressPatch`: failed native retreat approaches no longer
advance to docking, and failed docking returns to approach at native cadence.
Those progression checks retain original failure reproductions and exercise both
Ceres and unrelated-faction commands without replacing their behavior trees.
