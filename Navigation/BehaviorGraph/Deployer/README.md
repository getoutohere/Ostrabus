# Deploying-hauler scope reference

Read the [continued audit](../../../Notes/AI-REGIONAL-SCOPE-AUDIT.md), including
native caller contracts, source/asset distinctions and fixture boundaries.

This slice models current deploying-hauler release/empty recovery, target/route
identity and fuel addition at VORB versus other homes. The overlay includes the
implemented shared refill repair and single general empty-home recovery path;
remaining regional route-guard scope is still explicit.

```powershell
dotnet run --project Tools/AIBehaviorGraph -c Release -- validate --model deployer
dotnet run --project Tools/AIBehaviorGraph -c Release -- scenario refill-other --model deployer
dotnet run --project Tools/AIBehaviorGraph -c Release -- scenario saved-targetless-other --model deployer
dotnet run --project Tools/AIBehaviorGraph -c Release -- generate --model deployer
```

Use `--game-source <decompiled-root>` to check native source fingerprints and the
standard assembly revalidation commands with `--model deployer` to inspect
future game updates. The new slice has its own dependency manifest/baseline.

Native diagnostic fixture:

```powershell
dotnet build Tests/ScavFuelDiagnosis -c Release
dotnet run --project Tests/SaveJsonCompatibility/Host -c Release --no-build -- Tests/ScavFuelDiagnosis/bin/Release/netstandard2.1/ScavFuelDiagnosis.dll scope
```

70 assertions cover equivalent-target controls, shared refill accounting,
unchanged regional retarget scope, refill/validation ordering and
complete ordinary Main progression without the VORB home-target shortcut.
The earlier bad-state refill witnesses were converted to regressions; remaining
retarget scope witnesses are not approval of that gap. See the
[repair and detailed tests](../../RCS-REPLENISHMENT.md).

The first graph audit and the recent conditional-candidate review did not expand
this slice. Use this reference instead of inferring deployer behavior from the
retriever graph. Physical flight remains native/opaque here.
