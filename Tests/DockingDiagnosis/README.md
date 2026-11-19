# OKLG docking CODE 7400 diagnosis (2026-11-04)

Live API confirmed O-GXD comms repeatedly received SHIPDockDenyPortDoesNotFit after selecting E1C Primary. No live game state changed.

Offline replay uses ships/O-GXD.json and ships/OKLG.json from Saves/wtf/wtf.zip (20:00 save), extracted to obj/*-docking.json. Native bundled Mono, GridUtils.CreateFullGrid / GetIncomingDockRotation / CreateOffset / CanOverlayBOnA, base CO conditions and item footprints loaded from installed assets. Fixtures use shallow saved geometry, no spawned COs, no crew obstacles or docked hulls; overlay loot is omitted. Native Unity quaternion icall warning is emitted while compiling grid methods; all replay operations complete.

Primary: e1c35023-795c-46bb-b9d1-5f2e46cab410, ItmDockSys02Closed at (120,7.5), 180deg.
Secondary: 9cf10b75-a9cb-436c-85d8-225fbe97e912, ItmDockSys03Closed at (118,59.5), 0deg.
Station ports: faae703b... (primary), 679382e7..., eb0d51c8....

Original saved geometry: Primary fails all three. First two have exactly one clash each: towing brace 308f0834-36ba-4c06-be8e-edb1755eb871 at (120,8.499999), colliding with station Blank clearance. Offline changing ONLY brace Y to 8.5 makes Primary fit first two ports. Third has additional errors from near-integer wall coordinates (129,8.999999) and (130,8.999999).

Offline snapping both hulls' positions ONLY when within 0.0001 of a half-grid coordinate makes Primary fit all three. Secondary fits first two with original and corrected geometry; third retains a real mass-thrower/clearance conflict.

Mechanism: GridUtils.CreateDockingPortGrid checks near-integer fractions using epsilon, but generic footprint/neighbor coordinates are then truncated via (int). e.g. 8.499999 - 0.5 lies below 8 and truncates to 7, incorrectly adding occupancy to the adjacent tile. Full and shallow branches both contain the integer-cast pattern. Blank cells are reserved clearance: only Blank/Blank and Blank/docking-system overlap is allowed. Do not simply allow occupied cells to overlap Blank.

Recommended patch scope: correct numerical conversion while constructing the docking-fit grid for both Full/Shallow representations. Preserve world positions, native collision rules, port choice, and genuine overlaps. No patch implemented by this diagnostic.

Reproduce: build Tests/DockingDiagnosis then run via Tests/SaveJsonCompatibility/Host. DOCK_SNAP_BRACE=1 or DOCK_SNAP_ALL=1 chooses diagnostic coordinate corrections in the offline copy only.

## Implemented correction and regression checks

Production code now uses a scoped coordinate-read correction in
MiscBugFixes/DockingGridPrecisionPatch.cs. The native grid builder remains
unpatched: even an identity Harmony transpiler changed its output in the
offline host, so that implementation was rejected.

Set DOCK_TEST=1 before running this executable through the bundled Mono host
to install the actual production hooks and run regression checks. This must
happen before grid code is first JIT-compiled, matching plugin startup.

The saved shallow grids must match complete vanilla grids built from exact
coordinates; Primary fits all three ports and Secondary's third-port weapon
obstruction remains. Controlled loaded-object fixtures test the native Full
branch with stand-ins for Unity position reads, plus scope nesting/exception
cleanup and unchanged source data. Engine icall warnings are expected outside
Unity; no live scene or save is modified.

## RES1 accumulated-coordinate reproduction (2026-11-19)

Run `Tests/DockingDiagnosis/Run-Res1.ps1` from PowerShell. The compressed,
geometry-only fixtures under `Tests/Fixtures/Docking/Res1` reproduce the player's
two ports, OKLG, RES1 and RES1's complete connected docking group. The script
expands them under `obj`, then runs separate vanilla and production-patched
processes through the game's bundled Mono and installed asset footprints.

RES1's sole free port is at Y=-107.4998; nearby floor pieces also accumulated
translation error exceeding the original 0.0001 bound. The temporary native
grid loses that port. Partial normalization also loses two OKLG ports, explaining
the native `GridUtils.CreateFullGrid` error about its connection to O-GXD.
The revised bound is below 0.001 tile (0.32 mm), confined to docking-fit reads.
No transforms, saved coordinates, rotations or native overlap rules change.

The vanilla replay must reproduce RES1's missing primary port; the corrected
native fit must accept both player ports at RES1's free primary port and keep
OKLG's genuine third-port obstruction. Connected ships remain in the fit grid.
The replay also checks unchanged saved geometry and scope cleanup, and runs
numerical bounds plus loaded-object grid tests including +/-0.0002 tile errors.
Unity transform reads in those loaded-object tests remain controlled fixtures.
