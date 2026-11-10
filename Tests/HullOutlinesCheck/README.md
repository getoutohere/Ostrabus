# Parallel hull outline checks

Production sources are linked directly into both harnesses. No game assets or
save files are written, and the running game is not contacted.

From the repository root, run sequentially:

```powershell
dotnet run --project Tests/HullOutlinesCheck/Core -c Release
dotnet build Tests/HullOutlinesCheck -c Release
dotnet run --project Tests/SaveJsonCompatibility/Host -c Release -- Tests/HullOutlinesCheck/bin/Release/netstandard2.1/HullOutlinesCheck.dll
```

The standalone .NET 8 suite compiles Envelope/ContourBuilder/HullWorker without
any Unity/game reference. Its **60,677 checks** exercise actual concurrent calculation and an
independent outside-air flood-fill oracle on 200 generated hulls, ordering/duplicate
invariance, concave bays, exterior loops, enclosed holes/nested islands,
normalization, immutable result reuse, queue bounds, stale reservation identity,
worker/scheduler failures and cancellation during calculation. A controlled
builder barrier proves that main-thread cache operations finish while calculation
is blocked, and that its subsequently released stale result cannot publish.

The Mono harness repeats those checks on the shipped runtime and adds **834**
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
adds 12,465 numeric cover/intersection/separation/sweep checks on Mono, with one
additional warmed-allocation check on CoreCLR. Random filled-exterior covers and
1,000 rotated rectangle pairs use independent oracles. Native CheckCollisions
runs with the real physical branch hook; clear hulls retain proximity warnings,
real and swept hits reach the response hook, and toggles, coincident centers,
station-docked exemption, class/SFF/teleport exclusions and reference release are
checked, including exception cleanup, disabling a partial capture, and avoiding
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

See [production behavior and ownership](../../NavConsole/HULL-OUTLINES.md) and
the separate [native defect diagnosis](../NavOutlineDiagnosis/README.md).
