# Instant-dock and missing crew-save warnings

Diagnosis against game 1.0.1.10, November 14. No production repair or warning
suppression is implemented by this investigation.

## Confirmed instant-docking dispatch defect

The console named O-D2VN / Bizarre Malady and O-G0MU / Millennium Funk.
Their pre-SFF asteroid targets were C438z-2QU and C189c-L0L. The pre-test `ai`
save contains both as materialized ShipType 9 asteroids using ClusterS01, with
no docking-port array. These are valid authored mooring destinations.

`Ostranauts.Ships.Commands.Dock.RunCommand` chooses mooring when the target
has no ordinary docking ports. `Dock.ResolveInstantly` instead chooses mooring
only when `shipScanTarget` is null. An already-materialized portless asteroid
therefore enters `AttemptToDock`; native `CanDockWith` finds no open ports,
clears `shipScanTarget` and `shipSituTarget`, and returns Failure. It leaves
`targetRegID` behind. `CivilianPilot.InstaDock` logs the failure.

The immediate post-SFF captures have exactly those cleared pointers and retained
asteroid IDs. Both civilians later selected BCER again and had live return
courses and living captains. This is a failed visit followed by replanning,
not evidence of a permanently frozen ship.

An isolated shipped-assembly reproduction passes four contrasts: ordinary
materialized mooring succeeds, instant materialized docking fails, its cleared
pointer/retained-ID signature matches, and an unmaterialized asteroid uses
instant mooring successfully. Maneuver, ephemeris, station classification,
target-lock validation and final physical mooring are explicit fixture
boundaries; the native dispatch, port checks and target clearing execute.

The appropriate repair is to make instant dispatch honor the ordinary
mooring/docking distinction. This belongs to C#, not an asset change adding
docking ports to rocks or a replacement civilian behavior policy.

## Confirmed stale crew-save references

`global::Ship.SpawnItems` emits the missing-save warning when an item/crew
placement has neither a live entry in `DataHandler.mapCOs` nor a backing entry
in `DataHandler.dictCOSaves`. It skips the placement. Crew01 is the native crew
definition; this is not the benign gas-container initialization message.

Comparing raw hull JSON in `ai` and the later `rats` save establishes:

| Hull | Stale crew placement | Corresponding hull CO save |
| --- | --- | --- |
| O-RUN | Caitlyn Preston | Absent in both saves |
| O-DF4 | Ray Silva | Absent in both saves |
| O-KGEE | Maria Eduarda Adriana Hines | Absent in the pre-test save |
| O-NAJ0 | Jack Dominguez, Ostenfosh Robinson, Angelica Hernandez | Present before; absent in rats |

None of those names is present in the main world's global CO-save array in
the examined saves. Absence from the main array alone is not an exhaustive
search of every other hull archive. The current live CO lookups are also absent.
The live ships have different, living crews; the obsolete entries remain in
`JsonShip.aCrew`. O-KGEE's obsolete Maria entry predates its explicit pruning.

The damage includes orphan inventory references: the later hull JSON has 56
item descendants beneath Caitlyn's missing root, 56 beneath Ray's and 155 beneath
the three missing O-NAJ0 crew roots. Loading also logged unprocessed subitems for
O-NAJ0 and O-DF4; the descendant counts are not asserted to equal those total
warning counts.

`Ship.GetJSON` clones cached JSON for shallow hulls. `Ship.SaveCOs` seeds its
crew list from `jShip.aCrew` and adds current crew, so obsolete placements can
survive subsequent saves. `Ship.RemoveInternalSocial` normally removes a
departing shallow person's cached crew entry. The transition that bypassed or
failed that cleanup, or removed the actor save, is not yet established.

Historical social copies still mark Ostenfosh and Angelica alive. Such copies
can be stale and are not proof of a currently living actor elsewhere. Conversely,
an absent CO does not prove legitimate death. Do not recreate people from a
generic Crew01 definition or erase unresolved entries merely to quiet the log.
Trace removal, transfer, retirement and serialization ownership before choosing
a lifecycle repair. In particular, resetting every shallow crew array would
discard legitimate saved-only state.
