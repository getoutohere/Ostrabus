# Native mixed-navigation fuel accounting reproduction

This is a vanilla accounting defect reproduced offline against 1.0.1.10, not a
gameplay patch and not a claim about the historical cause of a particular ship.
No Ostrabus navigation/SFF patches are installed in this harness.

## Mechanism

`FlyTo.RunCommand` can choose different execution paths on successive ticks:

- An orbit-locked/body target with no collision course uses `_flyToPath`.
- Otherwise it clears the route, runs `HandleRCSFuel`, then `_flyToManual`.

`NavData.TimeAdvance` charges planned travel by reconciling current tank contents
down to the route's interpolated **absolute** FuelLevel. It doesn't update the
FlyTo TripFuelDTO's progress.

`FlyTo.ApplyTripFuel` charges manual travel by subtracting the increase in
distance-based progress multiplied by the cached trip budget. It doesn't know
that planned travel may already have paid for the intervening distance. The same
TripFuelPrediction ID preserves that old manual baseline across a planned segment.

Consequently, a collision-avoidance switch back to manual mode can subtract fuel
for travel already charged by the planned path, even before the ship moves again.
This mechanism doesn't require docking, materialization, save/load or SFF.

## Reproduction and controls

The test executes native parent `FlyTo.RunCommand`, its private accounting methods,
`NavData.TimeAdvance`, `Ship.GetRCSRemain` and `Ship.RemoveGasMass` unchanged.

| Boundary | Fuel |
| --- | ---: |
| Initial contents, manual budget 80 kg | 100 kg |
| Manual accounting at 25% progress | 80 kg |
| Planned segment covers 25% to 75%, ending at FuelLevel 30 | 30 kg |
| Collision check selects manual again, at the same position | **0 kg** |

The final manual call requests another 40 kg: `(0.75 - 0.25) * 80`. Native
RemoveGasMass clips that debit to the remaining 30 kg. This is an additional debit
at the already-accounted position, not propulsion expenditure during a new maneuver.

Controls confirm that repeating a planned update at the same epoch doesn't
repeatedly debit fuel; repeated manual calls at unchanged progress are also
idempotent. A fresh prediction ID starts a new manual baseline without charging
past progress. Manual-only travel to 75% leaves 40 kg instead of emptying the tank.

Nine assertions passed. The native parent dispatches the expected manual/planned/
manual leaves in response to prescribed collision-check outcomes.

The production handoff patch is now implemented. `fuel-patched` runs the same
native reproduction and preserves 30 kg at the manual handoff. Its 22 assertions
also cover later manual consumption, a second handoff, legitimate exhaustion,
prediction changes, saved handoff ownership, Full-to-shallow ownership and native
loading after unpatching. Physical-tank spending in that additional ownership
check is a prescribed boundary input; the native manual watermark is exercised.
See [implementation and persistence contract](../../MiscBugFixes/AI-FLIGHT-FUEL-HANDOFF.md).

## Boundaries and limits

Collision results, positions/times, route waypoints and their fuel levels are
prescribed inputs. Leaf steering/path generation is replaced with counting Running
commands; collision handoff radius is zero and ShipSitu.UpdateTime is suppressed.
These stand-ins isolate accounting and do not establish a particular natural
trajectory, collision event, or frequency in a live world. Unity equality,
Maneuver and other existing fixture boundaries are described in [RESULT.md](RESULT.md).

The result is a concrete engine-code defect with a native caller path. It is not
proof that O-KA2 encountered this sequence. Its earlier transition history isn't
present in the retained saves.

```powershell
dotnet build Tests/ScavFuelDiagnosis -c Release
dotnet run --project Tests/SaveJsonCompatibility/Host -c Release --no-build -- Tests/ScavFuelDiagnosis/bin/Release/netstandard2.1/ScavFuelDiagnosis.dll fuel
```
