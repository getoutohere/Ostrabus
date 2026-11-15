# Selected range incorrectly includes a moored asteroid

November 14, 2026; Ostranauts 1.0.1.10. Confirmed Ostrabus integration defects;
repaired and validated offline, pending live validation.

The player selected O-AWV Accelerationist, moored to C498g-VG4. The nav map
showed it several kilometres away, but target data reported zero range and ETA,
and the mooring distance slider also indicated zero. Paused live coordinates
put its center 4,912.229 m from O-GXD. The asteroid center was 3,721.143 m away.
Both hull displays had clean published geometry and no geometry error.

`HullDocking.RadiusSum` uses `Around` for hulls in different groups. `Around`
encloses every attached member about the selected ship, including its asteroid.
That conservative group circle is useful for relocation clearance, but
`HullDockingGeometryPatch` also supplies it to native
`NavModTargetData.SetStringsVRelShip`. Native UI code subtracts this allowance
from center distance and clamps negative results to zero. The
`NavModMooringControl.UpdateSliders` call goes through `MooringDistance`, which
supplies the same enlarged circle. This is not erroneous stored coordinates,
outline placement, or evidence of a sensor error.

The read-only reproduction links the production helpers and runs the shipped
native `DockGroup` implementation under bundled Mono. Live published hull radii
and captured positions are explicit fixture inputs. The asteroid's 880 m native
radius was checked against `autosave_2_rats`; no save files are modified.

| Quantity | Metres |
| --- | ---: |
| Player-to-selected-ship center distance | 4,912.229 |
| Selected ship-to-asteroid center distance | 2,926.833 |
| Player plus selected hull radius allowance | 2,024.061 |
| Current group-wide allowance | 4,952.258 |
| Resulting current displayed range | 0 |
| Selected-hull circle clearance | 2,888.168 |
| Feature-off native circle clearance | 3,932.229 |

Five checks reproduce asteroid inclusion, false zero range, identical mooring
allowance, ordinary two-hull behavior, and feature-off native fallback. The
ignored diagnostic study and capture are located through
`obj/moored-range-folder.txt` and `obj/MooredRangeCheck`. The historical study
records the original failure; maintained regression coverage is in
`Tests/HullOutlinesCheck/RangeChecks.cs`.

Relevant native asset definitions in `cooverlays_navmods.json` bind target data
and mooring controls to their C# panel classes; the corresponding GUI property
maps contain no range geometry policy. The asteroid cluster definition controls
its generated footprint, not this UI subtraction. This defect belongs in our
C# integration, not an asset override.

The repair distinguishes selected-hull range/approach geometry from a
whole-group enclosure used for safe placement. Audit docking-ring/clamp and
mooring consumers together; preserve complete-group clearance where an entire
formation actually moves. Do not change shared native combat radii or remove
asteroids from physical groups. The 360-degree bearing in this capture is
consistent with the player's heading toward the target; it is not caused by
the range clamp. The zero ETA follows the zero displayed range.

## Proximity alarm uses the old hull allowance

The player additionally reports that the 5 km alarm fires only at about 4 km
of displayed clearance. `CollisionManager.CheckCollisions` still stores the
native `GetCollisionDistanceAU(Ship, Ship)` sum and uses it for both proximity
candidate selection and warning intensity. `HullCollisionScanPatch` replaces
the actual overlap test, but deliberately leaves that radius local unchanged.
`ProximityCollisionPatch` changes the chosen buffer (5/10/15/20 km), not the hull
allowance. Therefore the warning and revised hull-based range disagree even
for two ungrouped ships. This is separate from the oversized moored-group range.

Using the captured published radii as an explicit ordinary-pair fixture gives
2,024.061 m in the readout versus 980 m in the native warning calculation. The
native 5,000 m buffer therefore triggers at 3,955.939 m of revised displayed
clearance. The offline study executes native `CheckCollisions` with the actual
production hull-scan and proximity-range patches: 4,500 m displayed clearance
does not enter `aProxCurrent`; 3,900 m does. Eight checks pass in total. The
candidate check isolates geometry; audio, objectives, acknowledgements and the
materialized player scene are outside this fixture. At the reported roughly
40 m/s relative speed, the native speed multiplier clamps to its minimum of one,
matching the fixture's unscaled 5 km buffer.

The repair must also supply a consistent per-hull allowance to proximity
comparison and warning normalization, without changing the physical collision
test, AI avoidance/group clearance, station policy, mute/acknowledgement logic,
speed scaling or celestial-body warning calculations. The missing geometry
integration is C#, not an asset-defined alarm range defect.

## Implementation and verification

`PairRadiusSum` / `PairDistanceAU` serve selected-target data, station-keeping
data, docking ring/clamp/clearance UI and mooring controls. `RadiusSum`,
`DistanceAU` and `Bound` retain the previous complete-group behavior for native
placement, AI handoff, inspection and avoidance. Both variants keep existing
station/station-chain and excluded-type gates.

The existing `HullCollisionScanPatch` additionally adjusts the two native
ship-warning threshold sums using `WarningThreshold`. The original buffer plus
radius instruction pattern remains available to the independent proximity-dial
patch, including when that patch was installed first. The physical overlap
radius local is unchanged, so this does not enlarge actual impact detection.
Both comparison and intensity receive the same hull allowance. No new Harmony
patch class, persistent state, geometry scan or worker wait is introduced.

The maintained suite adds 45 checks, using native target-data rendering math,
mooring slider/range eligibility and collision warning candidates. Negative
controls restore the old group UI allowance and old alarm threshold to
reproduce both failures. Corrected cases cover all four dial ranges at one metre
inside/outside the boundary, both patch installation orders, feature-off
fallback, station-connected/excluded pairs, preserved AI group handoff and
complete-group clearance, speed-buffer preservation and warmed no-allocation
warning helpers. Text display and slider assignment are UI boundaries; physical
scene drawing/audio remain live checks.

Validation: Debug and Release builds succeed without warnings. The bundled-Mono
suites pass 1,194 hull integration checks (including the 45 new cases), 843 nav
console checks and 886 planetary/station-policy checks. Full-plugin binding
reports 282 classes, zero failures and zero warnings; the existing full-plugin
AI clearance composition passes in both installation orders. Custom-builder
self-tests pass 544 checks. Isolated hull-only and hull-plus-proximity packages
compile, finalize, retain expected patch order and bind all native targets
without failures/warnings. Live playtesting of the new readout/alarm correction
remains pending deployment and reload.
