# Sensor integration audit — 1.0.1.10

Source audit and shipped-assembly fixtures informed the optional runtime adapter.
Display and EM propagation hooks are now installed only while their PDA toggles
are enabled. Paths below are relative to the
decompiled source root; line numbers identify decompiled code only.

## Observed consumers

| Consumer | Verified behavior | Integration consequence |
| --- | --- | --- |
| `ShipDraw.GetPositionOffset(double)` | Returns absolute actual position plus offset; visible contacts zero their offset. Parameterless overload returns offset alone. | Replace both views consistently, without changing actual `ShipSitu`. |
| `GUIOrbitDraw.UpdateShipDraws` (~2094) | Supplies real range only for faint contacts; visible ones receive zero. | Compute observer-to-target range explicitly; the argument is insufficient for a gradient on visible contacts. |
| `GUIOrbitDraw.FindNearestShipDraw` (~3901) | Hit-tests predicted actual position plus the cached offset. | Keep selection at the rendered marker by maintaining one cached offset per observer/contact. |
| `GUIOrbitDraw.DrawCrossHair` (~1871) | Refreshes selected `NavPOI.SensorOffset` from `ShipDraw`. | Selected crosshair, marker and hit-test must share the same offset without independent random draws. |
| `NavPOI.GetSXY` / `GetVSXY` | Position includes sensor and user offsets, including ship prediction/body-lock branches. Velocity remains actual. | Preserve native velocity initially; do not imply that this prototype estimates velocity. |
| `NavModTargetData.SetStringsVRelShip` (~245) | Uses estimated position for distance/bearing and position-dependent approach readouts; relative velocity comes from actual velocity. | Existing readouts naturally follow the marker; no new uncertainty UI required. |
| `NavModCoursePlot.PlanTrip` (~316) | For ship targets, copies actual `objSS`, adds user offsets, and binds `shipScanTarget`/`targetRegID`; it does not apply `SensorOffset`. Non-ship point destinations use `GetSXY`. | Preserve ship-target course behavior. A cosmetic observation must not enter saved routes or displace real stations. |
| `FlyToAutoPilot.RunCommand` / `AutoPilot.GetTarget` | Rebind target situation to selected ship's actual situation; `TargetData(Ship)` retains the actual `objSS`. However, `FlyToPath.RunCommand` keeps running existing NavData rather than continuously replanning it. | Do not equate a live target reference with a continuously corrected intercept. |
| `NavModMooringControl` | Uses target ship's actual situation for mooring. | Leave docking geometry and eligibility untouched. |
| `NavModWeaponsControl` / `WeaponsSystem` | Auto-fire arc/range uses actual target position. Projectile leading uses actual position and velocity in `GetExactFiringAngle`. | Preserve automatic combat. Manual aim can still follow the displayed crosshair; fresh close-range contacts therefore remain exact. |
| `ElectronicSystems.GetSensorRangeKm` | Maximum individual sensor detection range, not the nav display's summed signal. AI threat/missile commands consume it through `GetVisibleShipSignatures`; distance-keeping and ATC use it too. | Do not globally replace sensor strengths or detection range APIs. |

The native offline fixture executes `NavPOI.GetSXY` and `GetVSXY` on a synthetic
unlocked ship with distinct observation/user offsets, verifies exact composition,
and verifies `TargetData` still references the unmodified authoritative situation.
It does not execute Unity rendering, body-orbit lookup or a full plotted trip.

## Detection versus certainty

Preserve native discovery/occlusion/identity decisions and known-position exceptions.
`VisibleFromNavStation` uses summed signature against the skill-adjusted threshold;
this also controls exact versus faint presentation. The adapter decouples
*position uncertainty* from that exactness switch while retaining
native visibility and identification. The optical relative-speed penalty remains
in native detection/identity, but must not be reapplied as geometric bearing noise.
Changing detection or AI perception would be separate scope.

The model assumes an acquired usable measurement. An enabled instrument alone must
not reveal a hidden/occluded ship. Timestamp reception is now calculated separately
from general visibility: broadcasting state plus enabled EM-only receiver strength,
using inverse-square propagation and the native skill-adjusted threshold. Cached
identity or optical detection is not evidence of current transponder reception.
The lightweight adapter still preserves native contact eligibility and adds no
packet scheduling or beam-sweep simulation.

## Concrete integration boundary

Maintain one observer-specific contact record: offset, uncertainty axes, sample time,
and a reused measurement schedule. Supply both `ShipDraw` overloads from that record.
Convert model metres to native AU once. Apply radial and perpendicular noise in the
observer/contact basis, so good bearing survives poor range. Reuse a stable noise
sample between observations; do not draw noise in render/readout calls. A bounded
sample distribution must be calibrated to the reported RMS rather than confusing
one-sigma noise with a maximum radius. Retire records on observer/contact removal,
load/reset, disable and shutdown; no serialization of these ephemeral estimates.

Keep actual velocity, ship-target planning, AI perception, projectile logic and SFF
route playback native. Charted stations, own/docked ships and other explicitly known
positions remain exact. A future decision to navigate toward uncertain ship positions
must address acquisition/arrival behavior explicitly; it is not needed for this pass.

The cached observation adapter and saved PDA controls are implemented. Offline
fixtures cover actual native offset consumers, toggle equivalence and cleanup.
Live Mercury/Venus use confirmed the new display and PDA A/B controls. Interplanetary
scatter was increased after distant wreck fields appeared too neatly aligned.
The paused SVIR flicker was reproduced offline and corrected: model range
coverage now includes the outer system, and rejected samples preserve the native
noise timer. These observations do not constitute a full renderer or torch-trip test.
No identified consumer requires changing AI navigation or automatic weapon accuracy.

## Qualification after reported short arrival

The player reported a vanilla torch arrival far short of a high-uncertainty target.
The audit does not establish the cause of that particular trip. A direct ship-target
plot initially copies actual target state, but this does not prove that every nav
selection or completed route reaches the actual target:

- `GUIOrbitDraw.UpdateUIs` converts a selected ship to `new NavPOI(sx,sy)` when its
  visibility becomes None. Those coordinates come from the offset-bearing GetSXY.
  A subsequent plot follows the point branch, including the uncertain coordinates.
- Click selection can also fall back to an ordinary coordinate when it does not
  hit a ship/body/stellar-object marker.
- Rebinding `shipSituTarget` during autopilot does not itself regenerate an existing
  NavData plan. Target motion that diverges from the planned trajectory is distinct
  from sensor error and needs separate attribution.

Thus the earlier blanket claim that torch plotting is unaffected was too strong.
Preserving native consumers preserves these branches, including a coordinate course
derived from an uncertain contact. Neither a constructor-free coordinate fixture
nor initial-target inspection validates a full torch trip. The current live player
inspection has no active route and cannot reconstruct the reported completed trip.
