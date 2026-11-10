# Authored scavenger fuel assistance and broken entry paths

Reviewed shipped 1.0.1.10 assets and native code. The player remembers proximity
fuel requests; those interactions and their callers are still present. This
investigation changes no gameplay behavior.

## Asset wiring

| Asset/table | Entry | Behavior |
| --- | --- | --- |
| `loot/loot.json` | TXTShipStandardInteractions | Includes SHIPSosAskPlayer and the fuel-transfer interactions. |
| `interactions/interactions_ship_to_ship.json` | SHIPSosAskPlayer | Requests player help, applies ShipRequestedHelp and offers affirmative/negative replies. |
| Same | SHIPTransferFuelAllow | CMDTakeFuel queues native TakeFuel. |
| Same | SHIPTransferFuelComplete | Removes ShipRequestedHelp and adds DisembarkAndUndock. |
| `pledges/pledges.json` | DisembarkAndUndock | PLGDisembarkCommand, then PLGDisembarkFinishNextUndock. |
| `loot/loot.json` | TXTStationInteractions | Includes SHIPSosStranded. |
| `interactions/interactions_ship_to_ship.json` | SHIPSosStranded | Regional-station SOS, applying ShipRequestedHelp; dispatch or negative reply. |
| Same | SHIPSOSDispatchHauler | Requires AnyStation, IsNotDocked on caller and ship range below 1,000 km; CMDDeployHauler. |
| `loot/loot.json` | CMDDeployHauler | Text command DeployHauler. |
| `shipspecs/shipspecs.json` | IsNotDocked | nIsDocked = -1. |
| `condtrigs/condtrigs.json` | TIsNotPirateNotRequestedHelp | Forbids CareerPirate and ShipRequestedHelp on station SOS sender. |
| Same | TIsHasNotRequestedHelp | Forbids ShipRequestedHelp on player SOS sender. |

Comms.SendMessage validates receiver interaction availability. ReceiveMessage
applies interaction effects, and AIReply traverses the authored inverse replies.
Sending the station's acknowledgement applies CMDDeployHauler; Comms.ApplyLoot
passes the caller's registry ID to AIShip.AddCommandLoot, whose native factory
constructs DeployHauler and restores that target through SaveData.

DeployHauler checks duplicate assignments, station departure clearance, spawn
availability and reachability, then sends SHIPSetCourseForPickup with the caller
as third participant. HaulerRetrievingPilot.GetTarget reads that participant from
recent station messages and uses its existing pickup/haul/return lifecycle. This
is authored **retrieval/towing**, not an on-site tanker delivering fuel. A failed
reachability check sends SHIPHaulerTargetNotReachable instead.

## Where ordinary emergency behavior fails

1. **Docked empty Hold never enters help selection.** Main order is Undock,
   TargetNewIfNone, FlyTo, Dock, Hold. Below 1 kg without positive rotor thrust,
   Hold keeps returning Running. No SOS check preempts it. Existing native tests
   reproduce 200 expired-timer cycles remaining at Main[4].
2. **LowFuel's failed home plan doesn't trigger RequestHelp.** TargetNewIfNone
   accepts a nonnull home target regardless of GenerateNavData's result. Native
   execution with LowFuel true and a prescribed null home plan accepts the home
   target and makes zero SOS calls. RequestHelp belongs to the failed ordinary
   FindTarget branch, which this low-fuel branch bypasses.
3. **Existing emergency command classes aren't integrated into ScavPilot.** The
   constructed tree contains neither AmIOutOfFuel nor SosOutOfFuel. Moreover,
   AmIOutOfFuel returns Failure when there is no current target; normal Dock
   cleanup clears that target. Merely inserting the pair without fixing that
   context would still miss the docked, targetless scavenger.

Thus proximity hails can still occur when ordinary candidate search fails. Native
tests reproduce that call for a nearby player and an ATC call for a distant player.
The machinery exists; the normal low/empty-fuel execution paths fail to reach it.
ScavPilot's source is identical between the available 1.0.1.9 and 1.0.1.10
decompilations, including its lack of an emergency branch. This is vanilla behavior
that predates Ostrabus's navigation/SFF changes.

## Eligibility and secondary traps

The player-first routing normally requires less than 40 km; RELClose or missing
captain/spec fallback allows 120 km. The player must not be station-docked.
TargetNewIfNone's check looks only at the first connected ship, whereas
SosOutOfFuel uses IsDockedWithAStation; they aren't identical checks for complex
groups. This inconsistency is separate from the established missing entry paths.

The standalone SOS command also suppresses ATC contact if its existing target is
within 5 km, after considering player help. That can matter during an approach or
an injected command with a retained target. It does not explain Host Terrapin's
current silence: its target is null and it remains in Hold.

The station can receive an SOS yet decline dispatch for an attached caller or one
outside its authored 1,000 km range. The range is measured between collision
boundaries, and the comparison is strict: 999 km passes, 1,000 km doesn't.
Callers also ignore a false SendMessage result; station paths unregister the AI
even if no interaction was sent. A recovery fix must distinguish attempted SOS,
delivery, response, dispatched hauler and eventual retrieval.

ShipRequestedHelp has fDuration 0.05 **hours**, nominally three game minutes;
JsonTicker.GetEpochEndS multiplies by 3,600. bPersists is save persistence, not
infinite duration. Declining player help doesn't immediately remove the flag,
but that alone doesn't prove permanent suppression. Shallow condition catch-up
timing isn't established by the simple ticker-duration check.

## Offline checks

`Tests/ScavFuelDiagnosis/SosDiagnosis.cs` passes **23 asset/native assertions**:
the authored links above, real TargetNewIfNone branch outcomes, nearby-player and
ATC send attempts, standalone near-target suppression, the targetless out-of-fuel
predicate, native IsNotDocked matching and native ship-range boundaries.

The fixture substitutes LowFuel, planner failure, candidate failure, captain
lookup, station lookup, delivery and cleanup; geometry and clocks are explicit.
It executes native routing and eligibility methods, not a complete Unity rescue.
No live messages, spawning, refueling or undocking occurred. The asset checks read
the installed files; no game asset copies are bundled in the test.

```powershell
dotnet build Tests/ScavFuelDiagnosis -c Release
dotnet run --project Tests/SaveJsonCompatibility/Host -c Release --no-build -- Tests/ScavFuelDiagnosis/bin/Release/netstandard2.1/ScavFuelDiagnosis.dll sos
```

An optional second argument after `sos` overrides the game installation path.
