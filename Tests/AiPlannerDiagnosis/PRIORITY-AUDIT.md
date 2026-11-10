# Pending-return priority regression

Run after building this project in Release, using the bundled-Mono host:

```powershell
dotnet run --project Tests/SaveJsonCompatibility/Host -c Release --no-build -- Tests/AiPlannerDiagnosis/bin/Release/netstandard2.1/AiPlannerDiagnosis.dll audit-priority
```

The original audit reproduced a priority bypass. Its assertions now require
the repaired behavior. The suite passes **93 checks** covering:

- Real Navy, Pirate and Police constructors register their ordinary mission
  correctly, including Navy's FlyToPatrol subtype.
- Native queued commands and native AmIInCombat eligibility precede return.
- Threat/support branches precede return and quiet return resumes afterward.
- Combat and pending return survive native Blackboard serialization/reload.
- New requests preserve an active reaction's target until return gains control.
- Existing flight/dock stage, failed docking, manual/connected control, native
  queue migration and candidate/fuel regressions remain covered.
- SFF retains native handling for an active queued/combat command; an actual
  return leaf does not acquire a duplicate queued copy.

Native AIShip, SelectorCommand, SequenceCommand and queue dispatch execute with
the production patches. Physical flight/docking results are fixtures. Threat
and support eligibility use explicit boolean gates; combat uses the shipped
AmIInCombat predicate. Real pilot constructor checks verify mission registration,
not full combat execution. Captain/asset lookup during construction is stubbed.

The ordinary `ceres` mode also uses the updated native selector fixture. See the
[audit recommendations](../../Notes/AI-BEHAVIOR-PATCH-AUDIT.md).
