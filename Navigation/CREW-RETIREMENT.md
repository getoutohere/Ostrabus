# Retiring pilots return to population-managed stations

This simulation improvement changes only the station-capacity decision in native
`StarSystem.TryRecycleCrew`. A full compatible station can accept the crew of an
ordinary stale ship being retired offscreen. It does not preserve every NPC from
population management or physical death.

## Native failure and policy

Native `StarSystem.Update` calls `TryRecycleCrew` for a hull marked `bRemove`, then
destroys the hull regardless of the recycling result. Recycling rejects stations
at or above `MaxPopulation`. Thus a living pilot can die with an otherwise
disposable hull because every compatible station is full. The initial
[population audit](../Notes/NPC-POPULATION-BALANCE-20261119.md) records live examples
and the separate recruitment-source defect.

Capacity is already a soft limit elsewhere: native station arrival can admit
crew above it, and `AIShipManager.SpawnPassShip` periodically redistributes or
prunes eligible residents. This feature deliberately applies that model to
routine stale-ship recycling. Overflow admission is a simulation policy change,
not a claim that the native capacity comparison computes incorrectly.

This selection uses direct C# population and faction comparisons, not an asset
trigger that can express a retirement-only exception. Raising station capacities
in JSON would also alter unrelated population management. The integration is
therefore local to this C# mechanism; recruitment and pruning assets stay native.

## Implementation and boundaries

`CrewRetirementPatch.cs` has one guarded transpiler on `TryRecycleCrew`. It changes
the station-list preparation and this method's capacity comparison; native exact
faction matching, crew identity, ownership release/claim, catch-up, movement and
interaction setup still execute.

Normal stations with room retain their original order and priority. If no such
station shares the retiring hull's faction, full eligible stations are tried in
order of least population excess, with native order breaking ties. A recipient
must be a visible, non-removing, offscreen station with docking ports and a
positive finite population capacity. A hidden holding reference, unmanaged
substation or player-loaded station does not acquire overflow admission.

The source must be a non-docked, offscreen mobile hull marked for removal with an
`IsStale` timer at least three game hours old. Other removal reasons without that
retirement state retain native capacity handling. No `Destroy` or `Kill` hook is
added, and no removal flag is cancelled. If no compatible eligible station exists,
the native failure remains; this feature does not invent a destination or a
holding pool.

The effective unlimited capacity exists only on the stack for this one recycling
comparison. It does not change `StationMaxPopulation`, create save fields, or
grant population-cleanup immunity. Ordinary returned pilots can be recruited
again or selected by native population management. Overflow has no new hard cap;
its reduction uses the existing 24-36-game-hour management schedule, so temporary
excess residents can remain until that schedule runs.

This path enumerates the existing station list and sorts only its crowded
eligible entries during retirement. It adds no frame callback, background NPC
simulation, retained actor collection or recurring world scan.

## Existing protections

[Bounty persistence](BOUNTY-PRESERVATION.md) already reserves active targets and
their assigned hulls across administrative cleanup and supports later departure
in the same ship. Player crew and active plot actors also have native stale-ship
protections. These are specific lifecycle protections, not universal immunity
for every acquaintance or historical person. This feature does not broaden them.

## Validation and packaging

The [bounty/lifecycle harness](../Tests/BountyPreservationCheck/README.md) executes
native `TryRecycleCrew` before and after this patch, native population culling and
the shipped `TIsNPCPrunable` trigger. It checks successful multi-person handoff,
ordinary station priority, stable least-excess fallback, exact faction matching,
source/recipient exclusions, native ownership/catch-up, missing destinations and
continued protection of active bounty actors/hulls. Scene movement, container
enumeration for population culling and destructive teardown are explicit fixture
boundaries. This is deterministic native-code validation, not a live playtest.

Custom builds select independent simulation feature `ai-crew-retirement`; it does
not require identity repair or bounty persistence. Full builds include it. The
separate bugfix `ai-crew-recruitment` prevents temporary source filtering from
changing the shared registry. Neither feature rewrites assets or requires a save
migration.
