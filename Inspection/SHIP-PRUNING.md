# Explicit ship pruning

`POST /ships/{exactRegistryId}/prune` removes only the named hull, after native
`TIsNPCPrunable` checks on all its resolved crew. There is no distance/faction
restriction. Player ships, stations/body objects, active plot ships and unresolved
or protected crew remain excluded.

Ordinary pruning refuses docked/tethered ships. `force=true` additionally permits
**native detachment before pruning**, rather than skipping destruction safeguards
or removing a whole connected group:

```powershell
# Preview: checks everything, reports pruneableCrew and detachFrom, changes nothing.
Invoke-RestMethod -Method Post 'http://127.0.0.1:18764/ships/O-Q4V/prune?dryRun=true&force=true'
# Recheck, disconnect natively, recheck protections, then remove this hull only.
Invoke-RestMethod -Method Post 'http://127.0.0.1:18764/ships/O-Q4V/prune?confirm=PRUNE&force=true'
```

This does not require an AI controller. Connected peers and their crew are left
in the simulation; prune another named hull separately if desired. Removing the
hauler still deletes its own pruneable crew, as ordinary pruning does.

## Detachment contract

Before mutation, walk the connected graph using resolved direct links, without
the native recursive GetAllDockedShips call. Every connected hull must be shallow,
present in the registry, and outside player/station/body/active-plot protection.
The bounded preflight accepts at most 32 hulls and requires reciprocal, acyclic
single connections. Orphaned group membership, unresolved links, duplicate links
and cycles are refused: native ResetDockedCache recursively traverses those links
before undocking and cannot safely handle all malformed graphs.

After the target's crew preflight, release its tow braces and call Ship.Undock,
or Ship.UnMoorFrom for MP| mooring ports. Native code owns reciprocal links,
mooring hardware and DockGroup teardown; the endpoint does not edit these
collections. Verify the target is disconnected and no surviving member's docking
links/group still references it. Then rerun ordinary ownership/plot/crew checks
and use the existing single deletion path, preserving native destruction cleanup.

`dryRun=true` reports `force`, `detachFrom` and the target's `pruneableCrew`, with
no undocking or deletion. Actual success reports `detachedFrom`, `removed` and
`removedCrew`. A native detach exception, partial detachment or remaining reverse
reference returns HTTP 409 without deleting crew or hull. `detachedFrom` reports
completed direct separations. Detachment is not rolled back if a later native
callback changes eligibility or another step fails; the response exposes the
partial result. A protected surviving peer group is never recursively pruned.

The normal loopback/Origin/method checks, confirmation, main-thread queue and
generation/timeout protections apply. Force is a strict boolean, defaults false,
and is accepted only on this endpoint. Nothing is saved or scanned periodically,
and no Harmony hook is added. The running process needs the updated DLL loaded
before accepting the new parameter.

## Offline checks

`Tests/ShipPruneCheck` runs 156 assertions under bundled Mono, covering raw HTTP
parsing, default behavior, dry runs, confirmation, all protections, indirect player
connections, malformed/oversized graphs, failure/exception ordering, post-callback
revalidation and exact-target deletion. Native Ship.Undock and DockGroup.UndockShip
execute for a pair and for a leaf of a three-ship chain; surviving links, group
membership and crew are checked. Gravity/ephemeris, mass and presentation are
fixture boundaries. Crew eligibility/destruction and hull destruction are explicit
test doubles; loaded Unity scene teardown and mooring hardware destruction are
not claimed as native offline executions.

The transport checks also reproduce and repair the pre-existing query-whitelist
omission for `targetKg`. `ScavFuelDiagnosis refuel` now validates full raw request
parsing before numeric validation (31 checks), rather than inserting that parameter
directly into a test query collection.
