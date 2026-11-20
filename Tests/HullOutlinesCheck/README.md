# Parallel hull outline checks

The optional `docking-pair <archive> <firstId> <firstSavedPortId> <secondId>
<secondSavedPortId>` mode reproduces the original [docking integration
regression and validates its repair](../../Navigation/Collisions/DOCKING-COMPATIBILITY.md)
using native placement, saved port rotations and production hull geometry. Saved port IDs
must exist in that archive; a later live conversion may have changed their IDs.
It writes a detached report under ignored `obj/HullPreview/` and verifies the
source archive remains unchanged.

The default suite adds 189 docking assertions using actual native CanDock,
ring math, PlaceAtAirlocks, handoff, group anchor/removal and saved/live port
rotation paths. It preserves clearance/alignment failures and tests loading/grid
changes, feature-off fallback, station groups/restored links, cold bounds and
large-ring normalization. The saved-pair mode repeats these against the real
pair for 378 docking assertions total. Actual native PushbackFrom/safe-coordinate
execution reproduces and repairs the undock radius reset, preserving its 5 m/s
kick. Group-anchor placement is propagated through the native docking group.
Unity curve evaluation, shader IDs, mass
and selected-crew services are explicit fixtures; docking radius and pose math
are native with the production hooks. Scene/mesh upload remains a live check.

The consumer audit adds 61 checks for actual native safe-coordinate searches,
random/belt spawn rejection, exceptions releasing all scope references, complete
group PushbackFrom, two-group undocking, a four-member merge, native tether range
and velocity gates, inspection range versus native flight handoff, actual AI
prediction, warmed candidate allocations, and short relocation invalidation.
Controls restore old self-group radius reads or omit invalidation to reproduce
the corresponding failures. Saved-pair mode totals 1,356 integration assertions.
`Tests/PatchBindingCheck`'s `hull-interop` mode additionally executes the full
plugin's actual AI/evasion/hull composition in both patch installation orders.

Production sources are linked directly into both harnesses. No game assets or
save files are written, and the running game is not contacted.

The exterior-approach audit adds 2,210 assertions across both AI/hull patch
installation orders. These include 1,000 repeated warmed queries per order,
not 2,210 distinct gameplay scenarios. Native `IsOnCollisionCourse` and
`GUIDockSys.CanDock` exercise clear approaches, available clamps, retained
clearance failures, genuine rear-end/crossing threats, rotating hulls, native
planned rotation, whole-course admission, attached members and missing-geometry
fallback, including native float-PI turn direction and unresolved-route failure.
Accelerated controls use an independent densely sampled rectangle
oracle. Unity services for station lookup, RCS capacity, selected crew and port
availability are fixture boundaries; this does not execute the full forced-dock
dialogue or a resisting captain's complete flight lifecycle.

Optional `astern-avoidance <captureDirectory> <saveArchive>` arguments load the
actual Heavy Dragon / Stellar Magnificence exteriors and same-epoch live motion
captures. The padded prediction warns; production exterior confirmation accepts
the clear coasting pass. This deliberately zeros captured acceleration because
the recorded target was already evading; it is not an exact first-dodge replay.
The archive SHA is checked unchanged. The private capture directory is
`obj/DockingTarget-20261119-V-HZ0GD`; these inputs are not bundled fixtures.
See the [evidence and conservative limits](../../Navigation/PLANETARY-AVOIDANCE-AUDIT.md).

`pursuit-replay <captureDirectory> <saveArchive>` diagnoses the subsequent live
failure using the last unsteered sample and first evasion announcement. It uses
the atomic relative position/velocity, saved exteriors and captured orientations:
the physical sweep is clear at the sample, but predicts contact after coasting
forward 2.37339 seconds to the announcement. This is a measured-input coasting
control, not a complete replay of the resisting pilot or its route changes.
Its private input directory is
`obj/ForcedDockAvoidance-20261119/pursuit-20261119-185321`. The previous clamp
fixture starts with zero relative velocity and did not validate this moving
approach. After the live test, the player chose to retain genuine evasive
resistance and the deployed geometry correction, without a manual-clearance
exemption. See the audit for that decision and the earlier validation overclaim.

The range/proximity audit adds 45 checks for the
[asteroid-moored selected-range and late-alarm defects](../../Navigation/Collisions/MOORED-TARGET-RANGE.md).
Native target-data, mooring-slider/tether and collision-scan bodies execute;
only text/slider assignment is stubbed at the UI boundary. Negative controls
reproduce both failures. Checks cover four dial ranges just inside/outside each
boundary, both dial/collision installation orders, feature-off and station
fallbacks, preserved AI group handoff/clearance, native speed buffers and no
warmed per-candidate allocation. The default integration total is 1,194 checks.

The docking-permission audit adds 37 checks for the
[forced-docking/displayed-range disagreement](../../Navigation/Collisions/FORCED-DOCK-RANGE.md),
bringing the default integration total to 1,231. It executes native
`Interaction.IsWithinShipRange` before/after the production patch, reproduces
the reported 4.21 km rejection, and tests 1 m inside/outside all 11 shipped
positive-range docking-clearance grants. Other interaction effects, stations,
station-connected groups, feature-off, asteroid mooring, missing participants
and native zero/negative/already-docked range semantics are checked separately.

Rotation regressions execute the shipped Q/E input command bodies and
`Ship.RotateCW`. The unpatched control reproduces a stale outline despite an
in-place floor-layout turn: both the floor-list identity and final grid rotation
remain unchanged. Corrected cases cover an asymmetric perimeter through four CW
turns, CCW turns, docking-style grid changes, partial-capture cancellation and
unobserved hulls. Ordinary heading changes neither rebuild nor reupload geometry.
Selected-ship/typing services, camera/audio/work bookkeeping and Unity transforms
are fixture boundaries; the native commands, grid/floor rotation, production
invalidation, worker publication and Vectrosity assignment execute. The prepared
floor/socket-quadrilateral fixture isolates cache rotation from full scene item
loading; existing live-socket capture tests cover that separate boundary.

From the repository root, run sequentially:

```powershell
dotnet run --project Tests/HullOutlinesCheck/Core -c Release
dotnet build Tests/HullOutlinesCheck -c Release
dotnet run --project Tests/SaveJsonCompatibility/Host -c Release -- Tests/HullOutlinesCheck/bin/Release/netstandard2.1/HullOutlinesCheck.dll
```

The standalone .NET 8 suite compiles Envelope/ContourBuilder/HullWorker without
any Unity/game reference. Its **60,687 checks** exercise actual concurrent calculation and an
independent outside-air flood-fill oracle on 200 generated hulls, ordering/duplicate
invariance, concave bays, exterior loops, enclosed holes/nested islands,
normalization, immutable result reuse, queue bounds, stale reservation identity,
worker/scheduler failures and cancellation during calculation. A controlled
builder barrier proves that main-thread cache operations finish while calculation
is blocked, and that its subsequently released stale result cannot publish.

The Mono harness repeats those checks on the shipped runtime and adds **1,102**
checks for shallow JSON overrides, installed/contained/placeholder filtering,
cardinal rotations, six shipped hull assets resolved through native DataCO
construction, current materialized socket grids and root-relative coordinates.
Large captures yield across steps; cancelled/submitted collectors clear their
native references. A GC fixture confirms they do not strongly retain replaced
native CO arrays.

Integration tests install the actual Harmony patches. Native silhouette,
draw-selection, tile-mutation, ship-destruction and nav-destruction entry points
execute the production callbacks. Game-object equality, fixture conditions,
and transform properties are engine boundary fixtures;
the underlying Unity scene mutation bodies are skipped. The tests confirm
unchanged outlines are not reuploaded, strict interior salvage avoids dirtiness,
boundary removal invalidates even after IsInstalled is cleared, pending geometry
retains the prior line, and published geometry reaches the draw callback. The
actual production Assign body executes with the shipped Vectrosity property
setters, ResetLine, Resize, ZeroVertices, colors, UVs and triangle calculations
intact. A native fixture reproduces the original empty-list mode-switch
exception before checking the corrected resize-first sequence. Shared point
buffers, all three native join modes, empty/odd/even starting markers, maximum supported sizes, subsequent
growth/shrink and empty-hull marker fallback are covered. Engine scene objects
and the final GPU upload remain outside this fixture.
Detached sections have no artificial joining edges.

Zoom checks exercise the unpadded 26/20 map-pixel entry/exit thresholds,
rotation-invariant scale, conservative viewport culling and invalid inputs.
Constant-zoom pan fixtures use four RectTransform pivots, with hulls across the
full drawing area and partly/wholly outside every edge. The centered-pivot
fixture reproduces the former on-map selection failure before the corrected
0..width/height bounds are used; native Toggle entry tests also pan the same
hull above and below the map center.
Native selection entry tests preserve own-ship/signature/visibility gates while
allowing useful large hulls beyond 2.5 km. The actual DrawVectorLine prefix runs
with only its scene-drawing body skipped, verifying that detailed lines lose
padding while symbols, fallback markers and unrelated lines retain their scale.
Weak line tags are cleared on scene/save reset.

The shared [physical collision model](../../Navigation/Collisions/README.md)
adds 13,473 numeric cover/intersection/separation/sweep checks on Mono, with one
additional warmed-allocation check on CoreCLR. Random filled-exterior covers and
1,000 rotated rectangle pairs use independent oracles, including the new
future any-hit path. Curve-error padding, exhausted budgets, missing geometry
and subsequent physical queries verify that prediction state cannot leak into
impact response. Native CheckCollisions
runs with the real physical branch hook; clear hulls retain proximity warnings,
real and swept hits reach the response hook, and toggles, coincident centers,
station-docked exemption, class/SFF/PASS/teleport exclusions and reference release are
checked, including exception cleanup, disabling a partial capture, invalidating
short-PASS history even for unobserved hulls, preserving following ordinary sweeps, and avoiding
sweep replay of the native separating displacement. The response test boundary preserves native normal inputs and bounce
scaling, while downstream damage/crew/Unity side effects are not emulated.
All six shipped assets must produce valid covers. Optional saved-hull replay
also checks its cover and verifies the input archive SHA is unchanged.

An optional save archive and ship ID after the DLL argument replays that saved
hull read-only, checks the archive's SHA, and exports normalized old-convex/new-
exterior preview data under obj/HullPreview. The replay loads the shipped asset
definitions; additional third-party part definitions must also be available for
such a fixture. No input archive is rewritten.

Unity Vectrosity mesh upload and visual presentation are not emulated. Full-plugin
PatchBindingCheck validates coexistence with all other installed Harmony patches.
`GET /ui/hulls` supplies live observability without installing diagnostic hooks.

Sizing checks compare horizontal/vertical hulls, proportional enlargement and
worker revisions for uniformly resized geometry. Native-runtime checks verify
that the rendered edge and physical contact use the same radius even outside
the original circle, native AI `WillCollide` results and radius fields remain
identical, stations keep their scale, and 10,000 cached size reads allocate zero
bytes. The API reports native and corrected radii separately.

Supply a save archive and **two** ship IDs after the DLL argument to compare
their actual installed footprints read-only, including a quarter-turn replay
of each and an unchanged archive SHA. This mode prints dimensions/radii and
does not export a preview. The Stellar Magnificence/Missing Magpie reproduction
has native radii 500/960 m and corrected footprint radii 1129.25/996.39 m.

See [production behavior and ownership](../../NavConsole/HULL-OUTLINES.md) and
the separate [native defect diagnosis](../NavOutlineDiagnosis/README.md).
