# OKLG scavenger behavior

Start with the [investigation](INVESTIGATION.md), [SOS asset/caller audit](SOS-INVESTIGATION.md), [scenario audit](AUDIT.md) or
[native graph](VANILLA.md). This slice demonstrates the ordinary docked empty-fuel
Hold loop from source and a shipped-assembly reproduction. It does not insert
the desired undock/SOS recovery into vanilla. A separately reproduced shared
flight-accounting defect now has an explicit patched contrast in this graph.

The [revised recovery proposal](REVISED-RECOVERY-PROPOSAL.md) separates narrow
engine repairs from new rescue policy; its narrow repairs are now implemented
and tested in [the native offline harness](../../../../Tests/ScavFuelDiagnosis/RECOVERY.md).

- `vanilla.json` is the native reference; `overlay.json` records the current
  mod's modeled differences and unexpanded dependencies.
- `effective.json`, `VANILLA.md`, `EFFECTIVE.md` and `AUDIT.md` are generated.
- The slice has its own assembly dependency declarations and accepted baseline,
  independent of the retriever slice. Use the shared [revalidation workflow](../REVALIDATION.md)
  with `--model scavenger` on its commands.

From the repository root:

```powershell
dotnet run --project Tools/AIBehaviorGraph -c Release -- validate --model scavenger
dotnet run --project Tools/AIBehaviorGraph -c Release -- scenario empty-docked-hold --model scavenger
dotnet run --project Tools/AIBehaviorGraph -c Release -- scenario failed-selection-can-call-atc --model scavenger
dotnet run --project Tools/AIBehaviorGraph -c Release -- scenario low-home-planner-failure --model scavenger
dotnet run --project Tools/AIBehaviorGraph -c Release -- node scav_hold --model scavenger
dotnet run --project Tools/AIBehaviorGraph -c Release -- source hold --model scavenger
dotnet run --project Tools/AIBehaviorGraph -c Release -- generate --model scavenger
```

Add `--game-source <decompiled-root>` and `--assembly <Assembly-CSharp.dll>` to
verify native fingerprints. Query outputs are JSON. Omit `--model` to use the
existing retriever slice. The shared format, unknown-value rules and bounded
search limitations apply equally here.

The graph has 20 control points, 37 native / 43 effective edges and 19 conditional
scenarios. Its 79 scenario/model checks are separate from the
[native offline tests](../../../../Tests/ScavFuelDiagnosis/RESULT.md).
An empty-fuel state is an explicit initial condition; this is not a reproduction
of the historical fuel-consumption sequence that created every stranded ship.
