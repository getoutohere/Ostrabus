# Bounty lifecycle preservation checks

Run the actual game assembly and production Harmony patches under the bundled
Mono runtime, without touching a live save:

```powershell
dotnet build Tests/BountyPreservationCheck -c Release
dotnet run --project Tests/SaveJsonCompatibility/Host -c Release -- Tests/BountyPreservationCheck/bin/Release/netstandard2.1/BountyPreservationCheck.dll
```

174 bounty assertions plus 26 crew-retirement assertions currently pass. Unpatched controls reproduce deletion for both
native rejection causes and loss of the original hull on regional cleanup.
Patched controls verify native station transfer and due-pass reboarding of the
same CO and hull, including composition with bounty departure fuel servicing.

The suite executes native `CrewSim.DockAndDespawn`, `AIShipManager.AIShipCleanup`
and `CrewCleanup`, `DerelictCleanup`, `StarSystem.CleanupStaleShips` and its worker,
`BountyManager.MoveTargets`, `EvaluateAllBounties`, `TryToArrest`, reserve selection
and shipped condition triggers. It checks ordinary NPCs, dead bodies, native
custody/load/ownership gates, four-hour parking, contract expiry despite leftover
hull markers, living/dead hand-ins, transfer exceptions and guarded IL matching.
Native bounty save/restore retains the same target and hull IDs and needs no
additional persisted fields.

Unity rendering, physical CO transfer, controller construction, payout presentation
and destructive teardown are instrumented boundaries. This suite verifies those
boundaries are reached; it does not claim to run physical movement or combat.
Direct `Ship.Destroy` and `CondOwner.Destroy` remain reachable, and the production
feature does not hook the native `Kill` setter.

`RetirementChecks.cs` also executes native `StarSystem.TryRecycleCrew` before and
after [CrewRetirementPatch](../../Navigation/CREW-RETIREMENT.md). It reproduces
full-station failure, then verifies native multi-person transfer, station priority,
least-excess overflow selection, exact faction matching and disposal/destination
exclusions. It composes with bounty preservation and executes native
`AIShipManager.SpawnPassShip` to prove ordinary overflow residents remain prunable
while the bounty actor and reserved hull remain protected. Only container
enumeration is substituted for that population job; the shipped trigger,
scheduler and removal loop execute. Physical movement and teardown retain the
scene boundaries described above. No death veto, save edit or new NPC registry
is used by the production feature.
