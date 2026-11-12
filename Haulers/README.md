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

## Open investigation: Hassan's loss near OKLG

Investigated 2026-11-11 (captures use UTC 2026-11-12), game 1.0.1.10.
This is **not a confirmed stale-cleanup death**; no runtime change was made.

- Hassan Miracle Saunders was moved from O-F8LK to OKLG_ATC, then reused as
  captain of O-JJ3 / OKLG Hauler 74. The previous captain, Finbarr Smith, had
  already been dropped off alive after an earlier delivery.
- The two-hour SFF ended at epoch 65633080969.567657. O-JJ3 was hidden during
  SFF; Orbital recorded `nativeHidden`, with no replacement trajectory or impact
  check. The later pickup and destruction occurred under ordinary simulation.
- O-JJ3 docked with E-S6F / Boaty McBoatface at 65633081072.296524, then was
  observed executing HaulShip toward OKLG at 65633081091.243393.
- At 65633081221.197792 the controller was unregistered while still towing the
  wreck. Player.log records E-S6F destroyed first, then O-JJ3, then Hassan's
  death. There is no intervening recorded undock, station delivery or combat.
- The saved IsStale deadline was 65633083323.4801, about 35 minutes later than
  destruction. No stale-marking log identifies O-JJ3. That known lifecycle
  defect does not explain this death.

The strongest explanation is a celestial collision, probably Ganymed.
`CollisionManager.CheckCollisions(Ship)` destroys every attached ship and then
the checked shallow ship on a body impact. `Ship.Destroy(false)` kills its
remaining crew. This matches the order exactly, but **the captured log contains
neither the caller nor impact coordinates**. The same body-collision branch
also handles asteroid-field impacts; the specific object is not established.
Normal `DockCargoAndDespawn` instead undocks the tow and drops off the pilot.
The inspected IsStale condition and TIsCollisionSafe asset do not define a
hauler retirement/crew-killing policy; the relevant destruction is native C#.

An offline controller model executed shipped HaulShip.RunCommand,
FlightCPU.GetDockingNavPoint and AIShipManager.AIIntercept2 using the saved wreck
offset, hull masses/radii and native body ephemerides. With ideal translation,
no other ship obstacles and zero initial body-relative velocity, it reached
the delivery threshold safely in about 143 game seconds, with about 3.9 km
minimum body clearance. Update intervals through 16 seconds also succeeded;
an artificial 32-second interval crashed. This is sensitivity analysis, **not a
reproduction of Hassan's actual trajectory**: docking-group attitude, real
controller cadence, residual input and intervening avoidance were not replayed.
It does not justify changing the planner or blaming SFF.

Evidence is retained in ignored
`obj/TrafficBaseline/after-sff-20261112T045815Z` (Player.log, AI/docking events,
SFF reports), with the scratch native-controller model in obj/HassanDisposal.
The missing decisive evidence is the Destroy caller and the group's position,
velocity, body clearance and active steering immediately before removal.
Capture those at an opt-in diagnostic boundary if this recurs; do not implement
a forced rescue or a speculative navigation rule from this incident alone.

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
