# Cached parallel nav hull outlines

Always enabled. This repairs nav-map silhouettes that trace only a port or an
internal hole, and replaces stale load-snapshot geometry for materialized ships.
See the [native investigation](OUTLINES-AUDIT.md) for reproductions and the
separate circular-collision / X-only sizing findings.

The worker traces the **actual exterior perimeter** of the installed footprint.
Straight edges, corners, projections and open recesses are preserved; only
collinear runs are collapsed. Enclosed interior holes are omitted. Disconnected
sections have separate exterior loops, drawn as independent edge pairs without
phantom connecting lines. This rendering layer changes no physical rules; the
separate optional [hull collision model](../Navigation/Collisions/README.md)
reuses its filled geometry. The earlier convex envelope was rejected
because it obscured recognizable exterior geometry.

Native map position, clockwise grid compensation and contact
identification/visibility remain authoritative. Ordinary ship hulls share one
footprint scale; stations retain native radius normalization. ShipSitu.Size and
all shared combat/radius helpers are unchanged.

## Consistent hull sizing

The native `SilhouetteUtility.GetSilhouetteLength` measures only X extent and
`ShipSitu.SetSize` multiplies that length by 20. Fitting every outline into that
radius makes a horizontally arranged ship appear much larger than an equivalent
vertical hull. The saved Stellar Magnificence/Missing Magpie pair demonstrated
500 m versus 960 m native radii despite comparable overall hull lengths.

For ordinary non-station hulls, the already-computed envelope supplies the
farthest exterior point from its bounding-box center. Its cached radius times
**40 map metres per tile** sets both outline axes. This retains the game's
deliberately exaggerated navigation scale, approximately its previous scale for
long horizontal hulls; these are not the physical 0.32 m tile dimensions. A
quarter turn cannot change apparent size, and doubling a hull doubles its drawn
dimensions. Uniform growth now changes the drawing revision even when normalized
contours match. Interior salvage still avoids unnecessary captures.

Visibility and viewport tests use the same cached radius. Before first capture,
the saved header's two-dimensional grid diagonal (current live grid dimensions
for a materialized hull) supplies a cheap conservative
visibility/warmup estimate; missing dimensions fall back to the native radius.
No item scan, additional worker job, per-frame allocation or Harmony hook is
added. Stations, substations, body orbits and non-ship classifications retain
native sizing. No radius is written to a ship or save.

The optional physical hull collision model uses this same corrected scale for
its broad phase, contact geometry and response. Its broad phase also admits
contact outside the old X-only native circles. Disabling it still restores
native circular collisions, which can disagree with a detailed hull outline.
Planetary impacts and weapon hit tests continue to read unchanged native radii.
With hull collisions enabled, docking/tether range, placement, safe searches and
native flight-to-docking handoffs use cached corrected bounds. AI prediction and
its existing safe-evasion check receive matching hull/group enclosures without
altering the shared combat predictor; see the [consumer audit](../Navigation/Collisions/CONSUMER-AUDIT.md).
The offline suite compares
native `WillCollide` results before/after geometry publication and verifies that
the shared radius fields/helpers were not modified.

## Outline visibility and scale

This intentionally replaces the native 2.5 km map-range cutoff with a screen-size
rule. An identified, visible hull enters detailed mode at an unpadded bounding
diameter of 26 map-canvas pixels, and remains detailed down to 20 pixels. These
are the same logical map units used by native icon padding; the render-texture
display can scale them on the physical screen. Hysteresis avoids repeated
symbol/outline switching near a zoom boundary. Large wrecks can remain detailed
farther out than small hulls. There is no new fixed kilometre cutoff.

The native own-ship exception or shipSignature > 80 is still required. Faint,
unidentified and invisible contacts gain no additional information. A cheap
projected-circle/viewport test excludes wholly offscreen hulls before requesting
a snapshot. Its bounds are the native drawing area's 0..width/height, independent
of RectTransform pivot. SolarToCanvas coordinates must not be tested against
pivot-local rect.xMin/yMin/xMax/yMax: that incorrectly hides onscreen hulls when
panning across part of the display. Map rotation is included in the projection scale. No extent scan or
point upload is needed when zooming.

Native DrawSystem adds roughly six map pixels of minimum-size padding to known
ship icons. A weak tag on each successfully populated detailed VectorLine makes
DrawVectorLine use the metre-to-AU scale alone for that line. Symbols, pending
markers, empty-hull fallbacks, status lines, weapon arcs, projections and body
orbits keep their native scaling. Zoom selection uses the unpadded size so this
padding cannot cause a contact to qualify for detail. Reset discards the tags;
they contain no ship, NPC or renderer-owner references.

## Geometry and refresh

Rendering requests work when patched `ShipDraw.ToggleSilhouetteDrawMode` selects
a detailed silhouette for a qualifying onscreen contact. The optional collision
module can also request geometry for nearby physical candidates with the map
closed; both share the same worker and collection budget. Until the first result is ready the native symbolic ship marker
is used. A previously drawn outline remains visible while a replacement is pending.

- Materialized hulls: read the native CO dictionary's reference array and current
  installed structural items. Use the actual rotated `Item.aSocketAdds`, current
  dimensions, and root-relative positions used by the tile system. Exclude loose,
  stacked, contained and placeholder items. No container traversal or GetJSON call.
- Shallow hulls: resolve saved items through native DataCO definitions, authored
  socket grids and installation overrides. Parent/slot-contained items are
  excluded. The stale `Ship.FloorPlan` is not used as geometry input.
- Structural classification uses hull/floor/wall/docking/rotor labels and the
  native MassThrower family. Missing definitions or invalid grids retain the
  prior outline / symbolic fallback and produce a deduplicated BepInEx warning.

`Ship.UpdateTiles` marks existing cached geometry dirty. It does not start a job
for every removed part, nor create a cache for ships whose outlines were never
requested. A strict interior removal cannot affect the filled exterior and is
ignored. Interior additions are ignored only when their conservatively expanded
bounds fit inside the clean outline; the expansion covers native half-tile
snapping and float-to-grid recovery. Box containment also checks perimeter
intersections: four inside corners alone cannot exclude a concave notch running
through the box. Boundary changes coalesce. Source load state, floor-snapshot reference
and grid rotation are also checked before capture/adoption.

## Main thread and worker ownership

Only the main thread accesses ships, COs, transforms, definitions and Vectrosity.
One incremental snapshot is active at a time, with at most one collection step
per UI frame. Each step targets **0.75 ms**, checks its time every 32 operations,
and stops after roughly 2,048 operations. This is a cooperative budget: a single
native property/definition lookup is not preemptible. Large socket grids are
budgeted as well as item records. Collection retries coalesce over 0.5 seconds
of unscaled UI time; this does not advance simulation time or run while the map
is closed.

The active collector keeps a weak owner and a weak reference to the native CO
array, so an inactive panel cannot keep removed people/items alive. If that
native array has been replaced and collected, the partial snapshot is cancelled
and recollected. Any retained socket templates / saved JsonItems contain asset
or numeric metadata, not live world objects. Closure, destruction, transit and
save replacement explicitly release partial capture state.

One ThreadPool callback processes the bounded queue serially, giving actual
parallel computation without an idle polling thread. Jobs contain detached
double-precision coordinate buffers, numeric cache entries and revision tokens.
They never retain Ship, CO, Transform, ShipDraw or renderer objects. Occupancy
union, boundary tracing, enclosed-loop removal and normalization run outside the
cache lock. Short managed locks reserve work, dequeue, publish immutable results,
and transfer pooled-buffer ownership. The main thread never joins/waits for a
calculation. Expensive computation is never performed inside the lock.

There are at most four reserved requests and 131,072 input corners per snapshot.
Results retain the native tracer's 16,000-vertex ceiling before renderer upload.
The sparse half-tile occupancy grid is capped at 262,144 cells; it does not
allocate an array spanning the distance between detached fragments. Grid
alignment permits at most .005 tile of float-coordinate recovery. Misaligned,
nonrectangular or otherwise invalid socket quads are rejected, not inflated or
smoothed. Directed boundary walks preserve point-contact topology; negative
winding loops are holes. A bounding-volume hierarchy identifies nested islands
inside filled holes without an all-pairs contour scan.

Input buffers and contour workspace are reused. Up to four numeric buffers are
pooled; reset releases that pool/workspace when safe. Output arrays are allocated
when the raw perimeter changes. Identical results reuse the immutable outline;
equivalent normalized drawings with the same footprint size retain their drawing version. Steady drawing does
not queue work, rebuild a polygon or upload unchanged Vectrosity points.
The renderer adopts independent edge pairs in Discrete mode. Before editing the
shared point buffer, native `VectorLine.Resize(0)` synchronizes the empty list
and its cached count; only then is the mode switched and the perimeter copied.
Assigning an empty `points2` list alone leaves the old cached count: `ResetLine`
allocates empty mesh arrays and its nested `SetColor`/`Resize` calls then reach
`ZeroVertices` with that old count, throwing on first detailed rendering.
Synchronizing first also keeps odd initial markers and large perimeters out of
the wrong mesh layout. Shared corner endpoints remain identical;
no outline smoothing or vertex-count target is applied.

## Cancellation, lifetime and save safety

- Each ship object has its own weakly keyed source entry. Reused call signs do
  not share entries. Every job carries world generation, revision and reservation
  identity. Stale submissions cannot cancel a newer reservation.
- Changes during collection discard the partial capture. Changes during worker
  computation reject its result at publication. Retiring a ship makes its entry
  unusable and cancels its active collector.
- Scene unload and finished save-load events advance the generation, clear the
  weak caches, cancel collection, and invalidate queued/in-flight results.
- Nav SaveAndClose / destruction cancels partial collection. Numeric worker jobs
  can finish without a UI; there are no Unity continuations to run after closure.
- Plugin teardown stops submission and invalidates all results. An already-running
  bounded numerical calculation may finish, discard its output, and return its
  buffer. It cannot call Unity or publish to the retired generation.

Six Harmony classes cover seven targets: silhouette initialization, native
silhouette selection, detailed-line scale, tile changes, ship destruction, and nav close/destruction.
No additional Update component, periodic timer or world-wide ship scan is added.
No custom save fields, native geometry mutations or asset deployment are required.
Removing the mod restores native rendering and does not affect save loading.

## Inspection and validation

`GET /ui/hulls` reports worker generation/queue/reservations, completed/discarded
jobs, failure counts, capture progress, zoom thresholds and up to 16 cached ships in the current
nav UI. Per-ship details include published/applied versions, component and
rendered-point counts, line mode, actual outline selection, detailed-scale tag, source type,
clean/dirty status, input count, missing definitions, rectangle-cover piece/error
counts and optional physical collision query statistics. It does work only
on request through the existing inspection dispatcher.

[Tests/HullOutlinesCheck](../Tests/HullOutlinesCheck/README.md) runs **60,687**
geometry/ownership/concurrency checks and **1,102** default snapshot/native-integration
checks under bundled Mono. The pure worker suite also runs on .NET 8. They
also run 12,465 filled-cover/contact/sweep checks (12,466 on CoreCLR including
the warmed allocation check); see the separate collision model documentation.
The outline checks include an independent outside-air flood-fill oracle on 200 generated hulls,
concave bays, detached pieces, holes/nested islands, half-grid overlaps,
malformed quads, dirty/destroy/reload/stop during an
in-flight calculation, queue bounds, stale tickets, failure recovery, partial
capture cancellation and GC, authored grids, current live sockets, and actual
patched native draw/mutation/close entry points. Renderer tests reproduce the
original empty-list exception using the shipped Vectrosity library, then run
production adoption with its actual property setters, ResetLine, Resize,
ZeroVertices, colors, UVs and triangle calculations. They cover shared point
buffers, all three native join modes, empty/odd/even initial markers, the largest supported edge list, and
subsequent growth/shrink/fallback updates. Zoom tests cover hysteresis, useful
large-hull visibility beyond 2.5 km, constant-zoom panning across four viewport
pivots and every edge, offscreen/faint/unknown contacts, the native
own-ship exception, rotation, patched native selection/scale entry points,
unchanged unrelated-line scaling and scale-tag reset. Actual Unity mesh upload and
appearance remain live checks.

Six shipped hull assets retain 18–176 exterior vertices. A read-only replay of
the user's saved O-GXD retains 78 exterior corners versus its earlier 13-point
convex shape; a comparison preview is generated under obj/HullPreview. The save
archive's SHA is checked before/after. These tests
do not establish a live frame-rate improvement or claim zero allocations. The
separate native diagnosis harness remains available as evidence of the original
tracer/cache defects; its scratch-allocating edge reference is not production code.
