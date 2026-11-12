# Nav hull outlines: investigation and proposed scope

Status: investigated against the shipped 1.0.1.10 assembly/assets. The
[parallel exterior-outline implementation](HULL-OUTLINES.md) now addresses nav
rendering; physical collision rules remain unchanged. This document preserves
the native diagnosis. Offline reproductions and the original edge reference
are in [NavOutlineDiagnosis](../Tests/NavOutlineDiagnosis/README.md).

## Confirmed tracing failures

`Ostranauts.ShipGUIs.Utilities.SilhouetteUtility.GenerateVectorPoints` expands
the supplied floor points to a three-times-resolution occupancy grid.
`FindStartingPoint` searches in input order for an adjacent empty cell, and a
single walk follows from there. It does not systematically enumerate exterior
boundaries. Its local unclaimed-point fallback is not a complete contour scan.

Two bundled-Mono reproductions distinguish this from a missing sensor return:

- A 20-by-10 hull plus a detached docking-port footprint produces **134 points**
  around the port. Reordering precisely the same occupancy to seed the hull
  produces **534 points** spanning the main hull instead. Neither walk reliably
  represents the complete set of disconnected pieces.
- A connected 20-by-10 hull with one missing interior floor tile produces
  **534 points** when seeded at the exterior, or **28 points around the hole**
  when seeded beside the hole. Thus choosing the largest connected component
  or merely preferring floors over docking ports is insufficient.

`GetFloorVectorSilhouette` inserts docking-port points at the front of the floor
list, so their prominence as seeds is explained by actual code, rather than
their names alone. Closed/open port item definitions in `items/items.json` have
seven occupied sockets in a seven-column, two-row layout. Their authored socket
footprints match native extraction at 0/90/180/270 degrees. The CO definitions
mark the ports installed hull/portal equipment. No missing JSON footprint was
found for those ports; this failure belongs to the C# contour mechanism.

The existing renderer creates a **continuous** Vectrosity line. A replacement
must also account for multiple contours: simply concatenating separate loops
would draw artificial bridges between disconnected hull pieces. Independent
edge pairs or separate loops would be required for that exact-contour proposal.
The implementation now traces actual exteriors, removes enclosed holes and
renders multiple components using independent edge pairs. An initial convex
envelope implementation was rejected because it erased exterior shape detail.

## Three separate levels of stale geometry

1. `global::Ship.InitShip` constructs `FloorPlan` from `json.aItems`. It is a
   load-time snapshot. `AddCO`, `RemoveCO` and `UpdateTiles` do not maintain it;
   `RotateCW` rotates the existing points. Native source and shipped IL agree.
2. `Ship.AddCO` and `Ship.RemoveCO` clear `SilhouettePoints` for ordinary item
   changes, including changes unrelated to hull geometry. Clearing this cache
   alone regenerates from the same old `FloorPlan`. Conversely, replacing
   `FloorPlan` without clearing `SilhouettePoints` preserves the old silhouette.
   Both behaviors are reproduced offline.
3. `GUIOrbitDraw.GetLineBodyForShip` obtains a separate scaled list through
   `NavIcon.GetSilhouette`. `SetupShipDraw` uses that list to construct
   `ShipDraw.lineBodySilhouette`. The map reuses existing ShipDraws; the normal
   drawing loop selects/transforms their existing lines. Clearing a Ship cache
   does not replace the already-created nav line or its separate point list.

Consequently, adding an invalidation hook for every removed wall would impose
work without correcting the underlying snapshot/render-lifetime problem.

There is also an input-filter issue: native `GetFloorVectors` classifies items
by their names and ignores installation overrides. The fixture reproduces
identical occupancy for an actual floor-bin definition with `IsInstalled=0`
and `IsInstalled=1`. A current-hull snapshot must distinguish installed structure
from loose salvage, rather than plotting every floor-like item on a vessel.

## Scaling and collision agreement are distinct

`SilhouetteUtility.GetSilhouetteLength` considers only X extent, with integer
conversion of its extrema. `ShipSitu.SetSize` uses that length times 20 as
`Size`, except lengths <= 1 use `MINSTATIONSIZE` (1500).

For synthetic occupied rectangles:

| Occupancy | Native Size/radius |
| --- | ---: |
| 20 by 10 | 380 m |
| 10 by 20 | 180 m |
| 2 by 20 | 1500 m |

`NavIcon.GetSilhouette` copies the cached contour, scales its farthest point to
the supplied radius, closes it, and applies grid rotation. It is a normalized
icon, not an independent measurement of tile lengths in metres. Correcting its
contour will not make its visible edges agree everywhere with a collision disk.

`CollisionManager.CheckCollisions` tests center distance against
`GetCollisionDistanceAU(Ship, Ship)`, which sums the two ShipSitu radii, before
calling `ProcessCollision`. There is no hull-polygon narrow phase there.
Changing ShipSitu.Size or that collision rule also affects navigation,
avoidance, proximity and docking offsets. This investigation does not propose
silently changing them as part of a visual repair.

Follow-up: the [implemented sizing correction](HULL-OUTLINES.md#consistent-hull-sizing)
now uses the cached two-dimensional footprint at a common map scale for ordinary
hull outlines and the optional physical collision model. Native Size and shared
avoidance/weapon/planetary helpers remain unchanged. Stations keep native sizing.
The preceding section records the original native behavior and scope decision.

## Implemented scope

The user requested actual exterior hull detail, excluding enclosed interior
holes, with actual parallel calculation and a guarded cache. The implementation
unions the authored socket footprints, traces their exterior boundaries, omits
interior/nested loops and collapses only collinear runs. Multiple exterior
components are rendered independently, without invented connecting edges.
Current installed socket grids supply materialized geometry; native DataCO
definitions and saved overrides supply shallow geometry. Main-thread collection
is incremental, calculation is on a bounded worker, and native rendering adopts
only current complete results. Interior mutations that cannot affect the filled
exterior need no rebuild; boundary changes coalesce during an open nav session.
The earlier reopen-only refresh suggestion was superseded by this design.

See [production details and validation](HULL-OUTLINES.md). No change to physical
collision radii, native sizing or simulation navigation is included.

## Offline evidence and costs

The harness runs **145 checks** against the game's bundled Mono, including the
native failure cases, cache behavior, relevant IL access paths, authored port
footprints and a reference extractor on six shipped hulls. No Unity scene,
collision patch, save mutation or running-game intervention is involved.

The reference uses half-tile cells, deduplicates occupancy, and emits exposed
edge pairs, including hole boundaries and disconnected sections. It is invariant
under point ordering and duplicate points. Its half-tile alignment limitation
is explicit; other coordinate grids would need accommodation before production.

Sample warmed five-run medians, measured offline on this machine:

| Hull asset | Native tracer | Reference boundary extraction |
| --- | ---: | ---: |
| Light Tug | 0.25 ms | 0.15 ms |
| Heavy Tug 01 | 0.58 ms | 0.60 ms |
| Argute Rapid Courier | 0.35 ms | 0.19 ms |
| Babak | 1.14 ms | 1.35 ms |
| ResAero01 | 0.24 ms | 0.17 ms |
| ResAero02 | 0.39 ms | 0.36 ms |

These are extraction-only timings, excluding current live-hull collection,
Vectrosity uploads/rendering and Unity frame load. They support cached builds,
not per-frame rebuilding or a claimed universal speedup. The reference is
somewhat slower on the largest sample. The bundled Mono per-thread allocation
counter returns no usable counts; the harness probes for that and reports
unavailability, rather than claiming zero allocation.
