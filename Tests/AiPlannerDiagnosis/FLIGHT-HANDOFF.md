# Flight handoff and fuel admission investigation

Build `Tests/AiPlannerDiagnosis` in Release, then run:

```powershell
dotnet run --project Tests/SaveJsonCompatibility/Host -c Release --no-build -- Tests/AiPlannerDiagnosis/bin/Release/netstandard2.1/AiPlannerDiagnosis.dll flight-handoff
```

The 213 checks first reproduce native force leakage and GUI-dependent shutdown,
then apply the production control patches and repaired escape setter. Matrices
cover AI/player, moving/locked targets, collision/pause, existing route-less
phantom force, template/Shallow/Edit/Full, GUI/no-GUI, retained fuel/momentum/spin,
fresh RCS input and unchanged active courses. Native integration verifies that
only the new RCS acceleration remains, and missing equipment stays missing.
Changed native IL is rejected.
Direct RCS prefixes are also exercised across actor/load-state, duration and
active-course combinations; physical RCS actuation is an explicit fixture leaf.
The fuel-admission comparison remains an observation: zero/negative-fuel shallow
execution is preserved, not changed into a stricter policy.

`Tests/ScavFuelDiagnosis`, mode `fuel-controls`, also executes the control and fuel
handoff patches together. Its 23 assertions inject obsolete torch force at the
planned-to-manual transition, require its release and verify unchanged fuel
debits, saved watermarks and materialization reconciliation.

See [the ranked audit](../../Navigation/TRAFFIC-CONTROL-AUDIT.md) for live evidence,
asset ownership and exact fixture boundaries, plus the [implemented repair](../../Navigation/FLIGHT-CONTROL-HANDOFF.md). Initial testing
also exposed an assertion pitfall: Unity Vector2's approximate equality treats
AU-scale engine acceleration as zero. These checks compare components exactly.
