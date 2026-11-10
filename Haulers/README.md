# Station-hauler fixes

For immediate completion through vanilla station/population mechanics, use the
[station-arrival API](../Inspection/STATION-ARRIVAL.md). It supports local haulers,
police and salvagers, checks crew preservation, and reports native tow delivery
and hull retirement outcomes. This is the preferred rescue operation when the
goal is returning existing NPCs to station life immediately.

The inspection [rescue API](RESCUE-API.md) can explicitly detach stranded local
haulers from abandoned wrecks, repair their approach state and request a native
return home while preserving their existing crew. Wreck pruning is separate.

## Retriever SFF continuation

`HaulerRetrieverSffPatch` corrects the native shallow retriever handoff after an
instant SFF pickup. Native code queued HaulShip for one invocation while leaving
Main at outbound FlyTo; the next AI tick could plot a route while towing instead
of sustaining the ordinary group-steering haul. The patch advances the verified
native Main sequence to HaulShip and suppresses that disposable queue entry.
Native hauling, arrival, docking, fuel accounting and save serialization remain
responsible for the journey. No group-physics or navigation-planner hook is added.

Only visible shallow AI retrievers attached exclusively to non-player derelicts,
with no active plotted approach and the exact expected Main sequence at FlyTo or
Dock, qualify. Materialized ships, player-owned tows, failed unattached pickups,
other controllers and unknown tree layouts are excluded. No polling, saved keys
or retained ship/command references are added; inspection occurs only in the
existing retriever SFF callback. This does not repair already-corrupt momentum.

See [saved evidence, native reproduction and validation](OKLG-RETRIEVER-RUNAWAYS.md).

## Deploying-hauler recovery

`Ostrabus.Haulers` generalizes the former VORB home-return patches to
deploying haulers at every station. Always active, as the former patches were;
independent of the experimental Orbital toggle. Venus-specific route validation,
and manual navigation fallback remain in Venus. RCS refill accounting now lives
in [shared navigation](../Navigation/RCS-REPLENISHMENT.md), independent of role/home.

After a successful derelict release or empty HaulShip failure, an eligible empty
hauler's native pilot supplies its target. Recovery verifies that the target is
the valid registered home station and applies it through TargetData.ApplyToShip.
It retains the original command result, allowing the normal behavior tree to
continue into FlyTo. A previously present target at the start of HaulShip does
not prevent recovery if native empty-haul cleanup discards it.

Abandoned RCS is stopped using Ship.StopManeuver before that return handoff. An
AIShip.RunAI postfix also handles existing idle shallow deploying haulers: after
the AI has no active command/reaction or pending/current queued work, it cancels
orphaned RCS and restores a missing home target. It skips plotted courses, torch
input, tow groups/docked ships, body/orbit locks, hidden pooled hulls, non-AI/player
control, other pilot types, combat and surrender. The idle safeguard excludes
Full/Edit ships. Return handoff retains the former Full-ship support, with player
navigation control excluded.

Cleanup changes RCS acceleration and angular acceleration only. Native behavior
preserves position, linear/angular velocity, fuel and the rest of the simulation.
An already-runaway vessel keeps its speed; future native return navigation must
cope with its fuel/delta-v constraints. This module does not teleport ships, erase
momentum, refill tanks or impose its own route planner.

Missing/destroyed/hidden homes leave the ship without an invented destination;
abandoned RCS can still be stopped. Valid successor commands, queued work and
existing targets are preserved by the idle guard.

Cross-call derelict-release markers and counters use weak Ship keys. Values store
only flags/counters/epoch/reason, never ship, crew or command references. Scene
unload/plugin destruction clears the table. Anchor command exceptions clear the
pending marker and propagate unchanged. No scanning background loop was added;
work occurs at existing command/AI boundaries. Cached unchanged idle checks
allocate no managed memory in the offline regression.

`GET /ships/{id}/ai` includes `haulerRecovery`, null before an event, with return
and stopped-maneuver counts, pending-release flag and last event epoch/reason.
Reading it does not create state or evaluate the AI. Events also log with
`[Hauler Recovery]`.

Offline checks use actual patched HaulShip.RunCommand and AIShip.RunAI under
bundled Mono, with pilot/world fixtures for OKLG, VORB and another home. They
check null/existing initial targets, idle saved-state recovery, successor/control
exclusions, missing homes, release completion/exception cleanup, retained momentum,
inspection counters and allocation. The separate vanilla reproduction runs before
these patches are installed and still confirms the original runaway.

Build/run:

```powershell
dotnet build Tests/OrbitalCheck -c Release --nologo
if ($LASTEXITCODE -eq 0) {
    dotnet run --project Tests/SaveJsonCompatibility/Host -c Release -- Tests/OrbitalCheck/bin/Release/netstandard2.1/OrbitalCheck.dll
}
dotnet build Ostrabus.csproj -c Debug --nologo
```
