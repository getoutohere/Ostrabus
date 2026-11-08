# Departure waypoints bound to an unrelated planet

Investigated against 1.0.1.10 on November 8, 2026. Implemented in
`DepartureWaypointPatch.cs`, namespace `Ostrabus.ShipNavigation`, with companion
SFF continuation in `Orbital/Economy/FreightRoute.cs`. No live ship was mutated
during development.

## O-LVKJ

MHNG Hauler 57 carries 633 electronics from Qiantangmen (MHNG) to Port Yangshan
(MTRS). Live epoch 65632488089.090065: target references identify a valid MTRS
orbital situation; this is not the cleared-body-reference defect addressed by
ShipReplacementTargetsPatch/navfix. It is roughly 234 million km from MTRS.

Its saved course starts at epoch 65632458219.2274 and ends at 65632493769.0837:
9.875 hours total, 94.67 minutes remaining at the captured epoch. All 86 saved
waypoints have zero TorchCycle and unchanged fusion fuel. Planned RCS falls from
2,177.99 to -11,800.08 kg: a 13,978.07 kg expenditure against about 2,248 kg capacity.
The live tank is effectively empty. Native shallow fuel allowances permit course
playback despite this; they do not make its geometry or fuel budget sensible.

## Source mechanism

`FlyToPath.PlanTrip4` creates a local departure leg when torch travel begins
inside a station's no-wake zone:

1. `CreateBorderSituBetweenPoints` chooses the nearby station and a point on its
   350 km boundary (300 km plus a 50 km margin).
2. PlanTrip4 copies the origin and shifts it 1.1 times toward that boundary.
3. For the AI's short departure leg (`flag4`), it calls
   `navDataPoint.ObjSS.LockToBO(navDataPoint.ArrivalTime)` and recursively plans
   an RCS course to this intermediate point.
4. That implicit `ShipSitu.LockToBO(double,bool)` resolves
   `GetNearestBO(..., includePlaceHolder:false)`, excluding the station's own
   authored orbit. `LockToBO(BodyOrbit,double)` then replaces the waypoint's
   velocity with the selected body's velocity and stores an offset from it.

For MHNG, an independent solar-orbit station, the nearest eligible celestial
body is Earth, about 198.9 million km away at departure. The local waypoint is
therefore attached to Earth, rather than MHNG. The ship tries to match Earth's
motion on RCS just to clear its departure station, wasting hours and its tank.

## Offline evidence

Captured the live AI/navigation state and examined the latest autosave's complete
O-LVKJ course. Reconstructed saved BodyOrbit parameters and hierarchy under the
game's bundled Mono and executed native waypoint construction/binding/time updates.
The station lookup uses the observed MHNG as a deterministic fixture boundary.

The resulting departure waypoint is 385.08 km from MHNG. Advancing its Earth-bound
offset to the saved arrival epoch places it within 3.84 km of the saved endpoint,
with a velocity discrepancy of only 0.0053 m/s. This closely identifies the
reference-frame error; it is not an exact replay of historical planner inputs.

A native PlanTrip4 RCS control, using captured origin/acceleration/cruise limit,
puts the MHNG-bound 385 km hop at about 9.78 minutes. The Earth-bound control takes
8.36 hours. This isolated fixture suppresses obstacle detours and fuel bookkeeping;
its duration does not exactly reproduce the historical 9.875-hour course. The
saved course itself establishes actual historical duration and planned fuel use.

Working artifacts: obj/LVKJ-save.json, obj/LVKJ-world.json,
obj/freight-eta-O-LVKJ.json, obj/LvkjDiagnosis, obj/lvkj-native.log.
Save contents and machine-specific paths remain outside tracked documentation.

## Cause-level correction

For this planner-created departure waypoint, the patch uses the departure station's actual
orbital reference and preserve its local offset through native binding methods.
Do not reinterpret the point as a fixed offset from an unrelated planet. Scope
the correction to that explicit no-wake departure branch, preserving ordinary
planetary binding, obstacle avoidance, player courses and interplanetary legs.
The underlying source branch is native; SFF's historical-clock helper does not
change its choice of reference body. One specific binding call is replaced;
the rest of PlanTrip4, including its planetary detours, stays native. The helper
identifies the depot from the original origin using the same nearest-station
query as native border construction. Missing/removed stations and absent orbital
references retain native fallback. No ordinary-frame hook, cache or retained
ship references are introduced.

For already saved bad departure legs, replacing the future binding rule alone
does not repair their existing interpolated points. Any explicit recovery should
clear/replan the demonstrated bad course without teleporting the ship, changing
its intended destination or refilling fuel. This is not a navfix-corrupt-target
case. A course endpoint near the departure region is not by itself sufficient
to classify every legitimate intermediate leg as corrupt.

LVKJ is not proven permanently stuck: when its current course expires, native
NavData clears it and FlyToPath can plan another leg toward the retained MTRS
target. It is already far outside MHNG's no-wake zone, so a subsequent torch
course may succeed. Its current course-end timestamp is not a delivery ETA.

## Consequence audit and SFF continuation

- Native `flag2` is `!NavPlayerManned`, not a strict NPC ownership test. Scope a
  correction to `IsAIShip` vessels so an unattended player hull's plotted course
  is not inadvertently changed. SFF freight factories plan before AI registration,
  so registration alone cannot be the eligibility gate.
- Use the departure station's own valid reference, including its full body
  hierarchy and local offset; do not substitute its parent planet. Cover BCRS's
  BCRS_Anchor hierarchy, independent solar-orbit stations, ground/rotating
  stations and VORB's eccentric orbit. Do not globally change LockToBO or enable
  placeholders for gravity and collision calculations.
- A corrected moving endpoint is not a collision-free path guarantee. Retain
  native planetary detours and atmospheric checks. Test the path, especially
  ground stations and rapidly changing VORB motion, rather than only its endpoint.
- Nearby station no-wake bubbles can overlap. Clearing the selected departure
  station does not necessarily clear every other station. Preserve native
  all-station no-wake checks and ensure subsequent planning makes progress.
- Confirmed companion issue in our former SFF economy: FreightRoute.Prepare assumed the
  end of any supplied course begins final RCS approach to the cargo destination.
  A departure-only course instead needs another interplanetary planning stage.
  Correcting departure duration makes crossing that stage inside a six-hour skip
  more likely. Continuation now replans through native PlanTrip4 at the completed
  waypoint's historical epoch whenever torch-capable/reactor-running freight
  still lies beyond the native 5,000 km torch-planning cutoff. It also plans when
  such freight has no stored course. At most eight continuations are permitted
  per freight preparation; invalid, non-advancing and over-budget chains reject
  before committing the virtual flight. Local non-torch approach stays in the
  existing bounded SFF navigator. This flaw existed independently of the native
  reference-frame defect.
- Existing bad courses remain saved as explicit points; the creation fix cannot
  retroactively correct them. Any recovery must preserve actual motion and cargo
  and establish a bad frame, rather than rejecting all intermediate endpoints.

Planning uses detached origin snapshots and carried fuel budgets, not temporary
teleportation of the live hull. Existing historical-clock/station restoration
surrounds every stage. Completed/partial stages carry their combined budget to
Apply once, preserve the real station target, and retain ordinary NavData for a
course that extends beyond the skip. An already expired course replans from the
current hull position, never from its old endpoint. There is no save format change.

## Verification

- `Tests/DepartureWaypointCheck`: 69 bundled-Mono checks. Actual native PlanTrip4
  departure branch reproduces an 8.36-hour RCS hop before patching and 9.78 minutes
  after patching. Native binding/time updates cover MHNG, BCRS, VORB, MTRS and OKLG
  at four orbital phases. Both player-manned and unattended non-AI courses retain
  native results. Missing station/reference fallback is covered.
- Fixtures provide deterministic no-wake/depot selection, nearest-body selection,
  acceleration/fuel bookkeeping and obstacle-free geometry. These tests establish
  reference-frame and planner behavior, not a complete live atmospheric departure.
- `Tests/OrbitalCheck ... economy`: 1,443 checks, including partial and completed
  continuation, several intermediate stages, cumulative budgets, exact virtual
  departure clocks, expired courses, absent courses, bounded retries and
  null/throwing/non-advancing plans with restoration.
- Existing orbital suite: 4,850 checks. Global Harmony audit: 141 classes bound,
  zero failures or warnings.

Existing saved LVKJ points are deliberately not rewritten or discarded by this
patch. Native completion, or SFF continuation after that leg, allows a subsequent
course toward MTRS. The root inventory lists the general navigation correction.
