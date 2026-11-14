# Native bounty departure checks

Run from the repository root:

```powershell
dotnet build Tests/BountyDepartureCheck -c Release --nologo -v quiet
dotnet run --project Tests/SaveJsonCompatibility/Host -c Release --no-build -- Tests/BountyDepartureCheck/bin/Release/netstandard2.1/BountyDepartureCheck.dll
```

166 checks pass. The shipped assembly's actual `BountyManager.SpawnBountyTarget`
is invoked before and after applying the production transpiler. `Ship.AIRefuel`, shallow fuel
accessors, `AIShipManager.LowFuel`, and `TargetNewIfNone.RunCommand` execute
natively. The baseline retains B-MC6's observed 8.755% fuel and selects home; the
repair fills the native capacity and permits ordinary target selection.

Cases cover Navy/Police/Pirate/Scav/Civilian activation, missing/new hulls,
airborne restoration, every materialization state, station-person moves,
already-aboard targets, failed/exceptional transfers, native blocked departure
eligibility, and unexpected transpiler layouts. Tests read the shipped arrest
and station-drop-off condition definitions; they do not rewrite those assets.

Unity object comparison, physical transfer/logging, hull construction, reset,
visibility, placement and controller registration are fixture boundaries.
The ordinary target candidate and sufficient delta-v are prescribed: this tests
the native home-versus-normal selection decision, not numerical route feasibility
or the entire military tree. Retirement cycles are separately evidenced by the
live log; the harness does not claim to execute physical docking/CO retirement.
