# Scavenger empty-fuel hold reproduction

Executed offline against shipped 1.0.1.10 Assembly-CSharp.dll using the game's
bundled Mono, with no Ostrabus gameplay patches installed in the harness.
Assembly SHA-256:
`8058f7a07f93d5d44678aaf8d7f749050c0d9b1c9ed1c7419eb521ddcd2097e7`.

The harness constructs an actual native ScavPilot with an existing fixture ship,
extracts its actual Main sequence and confirms its five leaves are
`Undock, TargetNewIfNone, FlyTo, Dock, Hold`. Neither `AmIOutOfFuel` nor
`SosOutOfFuel` occurs anywhere in the constructed tree.

| RCS kilograms | Help condition | Expired native Hold result |
| --- | --- | --- |
| 0 | absent | Running |
| 0.999 | absent | Running |
| 1 | absent | Finished |
| 10 | absent | Finished |
| Any of those four values | present | Running |

At Main index 4, zero fuel, no rotor thrust, no pause-release exception and no
external preemption, 200 calls separated by 700 epoch units all return Running.
Main remains at 4; observed native Undock/TargetNewIfNone entry counters stay zero.
Restoring 10 kg and expiring the timer releases Main to index 0. The next native
sequence call executes a substituted counting Undock leaf. That last check tests
sequence advancement, not a physical undock.

Separately, native `JsonShipSpec.Matches` with `nIsDocked=-1` (the authored
IsNotDocked dispatch specification) rejects an attached fixture and accepts it
after disconnecting. This is not a complete comms or station-dispatch test.

Docked Hold's actual ScavPilot.FFWD returns that same Hold, and running its native
one-shot wrapper reports Finished without releasing Main[4]. Positive rotor thrust
does release the empty Hold. The pause/zero-timer exception returns Failure and
resets the native sequence to index 0.

228 assertions passed, including the 200 repeated-cycle assertions. These are
not 228 distinct world scenarios.

## Fixture boundaries

Unity object equality uses reference identity. Maneuvering is suppressed; captain
lookup and the help trigger return fixture values. Hold's random timer is fixed
at 100. The fixture provides shallow fuel through native GetRCSRemain, a real
docked dictionary, native Main/command objects and manually advanced epoch time.
Higher-priority branches are not executed: this reproduction exercises Main
directly, under the explicit no-interruption assumption.

There is no simulation of flight, fuel consumption, physical docking/undocking,
comms response scheduling, rescue arrival or person AI. Fuel exhaustion is an
initial state, not an outcome manufactured by the test. Source review establishes
the surrounding selection and help paths separately.

## Reproduce

From the repository root, with the configured game installation:

```powershell
dotnet build Ostrabus.csproj -c Debug
dotnet build Tests/ScavFuelDiagnosis -c Release
dotnet build Tests/SaveJsonCompatibility/Host -c Release
dotnet run --project Tests/SaveJsonCompatibility/Host -c Release --no-build -- Tests/ScavFuelDiagnosis/bin/Release/netstandard2.1/ScavFuelDiagnosis.dll
```

Run bundled-Mono suites sequentially because the host shares a staging directory.
