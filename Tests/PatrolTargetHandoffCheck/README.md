# Navy combat-to-patrol target handoff reproduction

Offline investigation against the installed 1.0.1.10 Assembly-CSharp.dll, running
under the game's bundled Mono runtime. This does not patch the running game or
install any gameplay correction.

```powershell
dotnet build Tests/PatrolTargetHandoffCheck -c Release
dotnet run --project Tests/SaveJsonCompatibility/Host -c Release --no-build -- Tests/PatrolTargetHandoffCheck/bin/Release/netstandard2.1/PatrolTargetHandoffCheck.dll
```

The shared host stages one Mono runtime directory. Run it sequentially with
other native test projects.

161 checks pass. The tests execute the actual Navy constructor and native
selector, sequence, sticky sequence, AmIInCombat, AreThreatsNearby,
InitiateAgainstBiggestThreat, IsTargetNeutralized and TargetNearestStation.
Unity object comparisons, captain/signature/spatial lookup boundaries and the
physical flight output are controlled fixtures. TargetNearestStation executes
its own fallback selection and TargetData.ApplyToShip against a fixture station.
Other competing world-dependent action leaves receive controlled results.

The comparison establishes:

- An untouched ordinary native patrol strands pending combat bookkeeping and
  resumes flight against the abandoned combat target after losing detection.
- The same failure occurs after production PatrolAdmissionPatch admits a raw
  Navy patrol into ordinary MainBehavior.
- The current production NeutralizedThreatPatch acquisition exclusion does not
  repair an empty-detection path. Saved InCombat=false states also persist.
- The production constructor-local repair makes native cleanup and station
  selection run once. It handles live loss and the equivalent saved false flag,
  without resetting ordinary target selection or replacing the flight command.
- The repair preserves native save paths/payloads byte for byte, peaceful
  station travel, dormant combat references, active combat, mixed active/disabled
  encounters and an overriding urgent queue command.

The suite links the production source and verifies both direct configuration and
the actual Harmony constructor postfix. Its two native ConditionCommand gates
call the original native gate objects and admit pending nonempty Combatants for
the original cleanup continuation. It also checks idempotence and rejection of
an incompatible tree before either gate is changed. The running game is not
modified by the offline process.

The test controls flight output and does not reproduce physical acceleration,
the specific historical unregistration callback, actual travel/fuel ledgers or
station arrival. Blockade and other pilot roles are not included in the passing
count. See [the investigation](../../Navigation/PATROL-TARGET-HANDOFF.md) for
the owning C# and JSON review, interactions and remaining limits.
