# Planetary avoidance: O-ENB / Ganymed

## Astern approach follow-up, 2026-11-19

Heavy Dragon (V-HZ0GD, civilian Jacob Andrade) retreated toward OKLG after
detecting the player as a threat. Its ATC log repeatedly records
`SHIPCorrectingCourse`, emitted by native `FlyToManual.RunCommand` on its
collision-evasion branch. Its recorded 14.2245 m/s2 lateral acceleration matches
the native `AIShipManager.AIIntercept2` half-cap, 1.45 g. This is not evidence
that Ostrabus increased available thrust or added a dedicated anti-boarding AI.

However, the geometry integration preserves the native doubling of the moving
ship's collision-prediction circle and enlarges circles to enclose the corrected
exteriors. That combination can trigger a full dodge during a physically clear
pass. The pre-encounter save's production hull reconstruction gives exterior
radii of 1,046.52 m for Heavy Dragon and 1,145.43 m for Stellar Magnificence;
their native radii were 920 m and 500 m. The resulting AI warning radius is
about 3,239 m, versus 2,340 m under native sizing and a 2,192 m sum of actual
exterior enclosing radii.

Using the same-epoch captured positions and velocities with both ships coasting,
the minimum centre separation during 30 seconds is 2,673.86 m. This is clear
even under the exterior enclosing circles, regardless of hull orientation.
The actual native `CollisionManager.WillCollide` reports false; the production
predictor with current padded bounds reports true; unpadded exterior bounds
report false. A genuine rear-end collision still reports true. The four-check
`Tests/PlanetaryNavigationCheck` diagnostic mode `astern-replay` records this
A/B control. Its private input directory is
`obj/DockingTarget-20261119-V-HZ0GD`.

The live captures were already evasive, and the geometry comes from the
pre-encounter save, so this control does not reconstruct each historical dodge
or prove every warning was false. It does establish that enlarged circular
clearance can produce unnecessary evasions. The native doubling is deliberate
clearance policy, not an arithmetic error; simply ignoring every pursuer behind
the ship would also hide real rear-end threats.

`HullAvoidance` now retains those circles as the cheap candidate filter, then
uses `HullApproach` to confirm a projected conflict against the cached exterior
rectangle covers. This applies when both AI avoidance and hull-shaped collisions
are included and enabled. It follows native route positions and rotations, or
the existing short acceleration forecast, without issuing steering commands.
Forced docking still uses native clearance, port and alignment checks; the
shipped `SHIPForceDockAllow` / `SHIPForceDockAllowLEO` interactions already grant
`TXTDockingClearance`, so no asset change is required.

The read-only saved-hull replay now runs that production path: the padded test
warns, while exterior confirmation accepts the clear pass. Native-method tests
also accept a clamp-ready approach, retain real rear-end/crossing/rotating-hull
warnings and cover both patch installation orders. Planetary avoidance, the
RES1 docking replay, normal/SFF/PASS freight regressions and four custom-build
profiles pass. These results establish the geometry correction, not the complete
moving forced-docking interaction. The live follow-up below exposed that gap.

The confirmation adds no hooks, item scans or saved state. It reuses the hull
solver with bounded work and a 0.25 m trajectory approximation allowance plus
0.01 m numeric margin. Missing geometry, exhausted budgets, unresolved angular
acceleration inside actual enclosing circles, routed multi-hull groups and
unsampled route tails retain conservative warnings. Stations, station-connected
groups and solid-body escape checks keep their existing handling. This removes
false ship-clearance warnings; it does not guarantee docking against a fleeing
pilot or exempt genuinely dangerous approaches from behind.

### Live pursuit: limits of the geometry repair

The player deployed the candidate (loaded plugin MVID
`4607460c-9dd8-42d9-ad0e-16a7511a9eea`) and repeated the Heavy Dragon pursuit.
The pilot first replaced its buoy trip with a return to OKLG, burned at roughly
2.9 g along a new native route, then coasted. At epoch `65635286150.259247` it
announced `SHIPCorrectingCourse`, cancelled that route and applied about 1.45 g
of lateral evasion. It subsequently generated further return routes and made
another evasion announcement at `65635286245.321114`. The player observed the
same disruptive turns. Claiming that the original pursuit problem was fixed
from the earlier offline checks was incorrect.

The last unsteered sample, epoch `65635286147.885857`, has 6,556.36 m centre
range and 156.124 m/s closing speed. Both recorded RCS/torch inputs are zero.
The saved installed hulls, captured orientations and atomic relative velocity
produce no physical contact in the next 30 seconds at that sample. Extrapolating
that coast by 2.37339 seconds to the first announcement gives 6,185.82 m range:
the physical hull sweep and exterior avoidance both then report contact within
30 seconds. This is a bounded coasting reconstruction, not the exact internal
warning call or a replay of all subsequent steering. It shows why removing
inflated-circle warnings alone does not solve a closing docking approach.

The comms capture records `SHIPForceDock` and its `SHIPForceDockAllow` reply
around the first evasion. `Comms` creates native clearance from the reply's
authored `TXTDockingClearance` effect. Nevertheless, the player's manual
crosshair selection leaves `Ship.shipScanTarget` null. Native
`AIShipManager.IsOnCollisionCourse` excludes the other ship when that field
targets the querying ship, but does not use docking clearance or the manual
crosshair for that exclusion. A declared manual docking participant can therefore
remain an obstacle. Extending that exclusion to manual docking clearance would
change how a resisting ship reacts to the boarding vessel; it is a separate
behavioral choice, not another geometry correction.

The native docking event confirms O-GXD ultimately clamped to V-HZ0GD at epoch
`65635286267.307762`; this does not make the preceding control behavior acceptable.
Private captures are under
`obj/ForcedDockAvoidance-20261119/pursuit-20261119-185321`.
`Tests/HullOutlinesCheck` mode `pursuit-replay` reconstructs the two coasting
controls read-only and checks the source archive hash. Its fixture does not
claim to run the entire comms/pursuit/steering lifecycle.

Decision after playtesting: retain the deployed geometry correction and genuine
evasive resistance. The player found the contested approach and risky clamp
enjoyable. Do not add the proposed manual-clearance exemption, freeze the target,
or otherwise guarantee a cooperative approach. No further gameplay change is
requested for this encounter. This decision does not prove every later steering
choice optimal or erase the earlier overstatement of what the tests established.

## Initial accelerating-departure repair (historical)

The first candidate was built/tested offline, then deployed. It failed live on
O-YKWV / Hard Pass through a sustained route/evasion loop. The initial branch
tests below did not cover that loop. The correction and stronger tests follow.
This first candidate extended the existing hook only.

O-YKWV accelerated its departure from a wreck toward HQCH at about 15.17 m/s²
while approaching the player. The paused capture contained a 5.36 km centre
distance and 176.30 m/s relative speed. Native
`AIShipManager.IsOnCollisionCourse` rejected the pair against only 2.05 km of
twice-stopping-distance **before** its own doubled-hull circle predictor could
report a collision. Native `CollisionManager.WillCollide` separately treats
squared relative velocity below about 1e-18 AU²/s² as zero: separated head-on
approaches below about 149.6 m/s are missed. The ship eventually announced
`SHIPCorrectingCourse` about 8.38 game seconds before the player damage event.

`AIShipAvoidancePatch` now removes that one guarded centre/stopping-distance
comparison. `AvoidancePrediction` performs an allocation-free reach rejection
using the actual prediction bounds and relative motion, followed by closest
approach in metres. At least the native default 30-second escape window is
considered; longer native stopping-time horizons remain available. Current
relative RCS/torch/external/lift/drag acceleration is projected for 30 seconds,
then coasted over any remaining horizon. This is a bounded local forecast;
it does not simulate future AI decisions or promise that a multi-stage burn
maintains its current acceleration. Zero-acceleration cases use a direct
future-time closest approach with no game-speed epsilon.

`HullAvoidance` copies numeric acceleration into its existing scratch situations
and invokes the corrected predictor only when the AI fix is installed. The
independent hull toggle still controls its geometric bounds. Native candidate
exclusions, `FlyTo` route interruption/manual steering/resumption, escape
direction selection and planetary safety checks remain in place. Shared native
`WillCollide`, physical collision response and projectile/combat callers are
unchanged. JSON collision-safety conditions still own their existing new-player
and undocking grace exemptions; the defects are C# numeric/admission mechanisms.

Evidence: `PlanetaryNavigationCheck avoidance-repair` passes 39 checks, including
native failure controls and actual native `FlyTo` branch execution before/after
patching. The default planetary suite passes 886 checks and its patrol/returning
hauler arrival replays. `PatchBindingCheck hull-interop` executes the full DLL
in both patch-installation orders, including low-speed/accelerating enlarged
hulls, hull-toggle independence, native station/docking exclusions and exception
scope cleanup. `HullOutlinesCheck` passes its geometry/physical/docking/range
regressions. These are offline results, not an in-game acceptance claim.

## Sustained route/evasion regression and correction

The deployed guard correctly interrupted an unsafe course, but native FlyTo
accepted another course through the same obstacle when the current dodge looked
clear. Escape selection also forecast the acceleration being replaced, allowing
the next evasive thrust to reverse the last one. Hard Pass oscillated and
exhausted its fuel. Counting dispatches into stubbed command leaves did not test
whether sustained native flight could complete; that verification was inadequate.

The corrected candidate is built and tested offline, not yet deployed. It adds
one guarded Harmony target, `Ostranauts.Ships.Commands.FlyTo.RunCommand`, to the
existing `AIShipManager.IsOnCollisionCourse` hook. It uses no actor registry,
maneuver timer, saved state or parallel behavior controller.

- `AvoidanceRouteForecast` follows native NavData position interpolation through
  braking/turns instead of extrapolating a route's current acceleration. Free
  motion retains the 30-second acceleration forecast followed by coasting.
- `AvoidanceFlightPatch` inspects a newly generated course before accepting it.
  For the nearby encounter, inspect the remaining waypoints; ordinary flight
  checks retain local lookahead. The nearby envelope uses hull clearance, native
  reaction/stopping time and physical RCS reach, so distant speculative meetings
  do not cause immediate evasion.
- Reject unsafe courses through FlyTo's existing manual/fuel-handling branch.
  Preserve the candidate only through that call's second collision query, so
  clearing NavData does not substitute the old dodge for the rejected course.
- Choose the native perpendicular escape using relative lateral velocity,
  independent of the thrust being replaced. The existing planetary and alternate
  obstacle checks remain responsible for vetting that direction.

Flight/query scopes restore in finalizers, including exceptional exits. Group
anchor offsets remain relative to the hull owning the course. Locked route points
preserve their exact manufactured/natural reference and use detached ephemerides
without changing live body caches. Native station, docking, target, torch and
visibility exclusions remain authoritative. Shared combat/history predictors and
physical collision response are unchanged. Inspected JSON collision-safety and
undocking grace conditions already express their intended exemptions; these
defects belong to the C# prediction and route/manual handoff mechanisms.

`PlanetaryNavigationCheck departure-replay` executes actual native FlyTo,
FlyToPath planning, FlyToManual, AIIntercept2, NavData playback and ShipSitu
advancement continuously. The previous-repair control reproduces **701 replans
and 1,398 thrust reversals**, failing to arrive in 1,200 simulated seconds. The
corrected head-on case arrives with **two accepted routes, three reversals and
1.16 km minimum hull clearance**. Native planned-flight debits fall from about
6,188 kg to 666 kg in this fixture. This is not a complete fuel-efficiency figure:
the engine double does not debit manual/rotational maneuver fuel.

The 93 sustained assertions include mirrored approaches, moving traffic, two AI
cadences, a clear pass, the actual full DLL, hull toggle on/off and both hook
installation orders, including the existing flight-fuel, thrust-release and
rendezvous patches. The admission transpiler runs before the existing reset-site
rewriter, so its fallback still executes that thrust-release repair in either
installation order. The 53 focused checks cover local/admission horizons,
distant speculative encounters, group offsets, manufactured reference identity,
body-cache preservation and exceptional scope cleanup. A thousand complete
candidate checks took about 54 ms in bundled Mono with zero warmed allocation.
The existing 886 planetary checks and patrol/returning-hauler replays remain
required. Unity/input, presentation and eligibility are explicit doubles; these
results are controlled native execution, not a live acceptance claim.

The station attachment/orbit repair is separate.

Implemented in `AIShipAvoidancePatch.cs` and `ConstantAccelerationApproach.cs`,
2026-11-09. Deployed and under live testing; the follow-up below records an
additional group-overlap case not resolved by the initial repair.

## Finding

O-ENB (Police, NASS Patrol 88) was leaving the player's station-side docking
group for J-2VC (Minarchist). At epoch `65632876957.406082` its center was about
245 m above Ganymed's 17.5 km radius, with about 147 m/s inward radial velocity.
Its stored RCS acceleration pointed toward the body. Fuel was 96.6% full and
the living pilot remained aboard. There was no active NavData.

The player's group contains a large wreck, O-F4C6, attached through E-7HAP to
OKLG|RES_1, plus O-GXD. Native `DockGroup.CalculateAnchorSize()` reconstructs
a 6.32 km enclosing circle when anchored at the apartment. This deliberately
conservative obstacle includes empty space around and between the hulls.
The actual runtime anchor is not exposed by the current inspection snapshot;
the reconstruction uses the apartment anchor, rather than claiming to read it.

**Subsequent policy change:** station-attached hulls are now deliberately excluded
from ship collision/avoidance scans; see [station-docked traffic](STATION-DOCKED-COLLISIONS.md).
The original analysis and prior prototype results below remain historical
evidence. Future-only/body-safe repairs still apply to free-space obstacles;
current production replay results are in the policy document.

The exact captured thrust direction is reproduced by **native ship avoidance**,
not ordinary steering toward Minarchist. Two defects interact:

1. `CollisionManager.WillCollide()` accepts collision intervals within
   `[-maxTimeFrame, +maxTimeFrame]`. `AIShipManager.IsOnCollisionCourse()` uses
   this shared function as a future warning. A departing, separated ship can
   consequently keep evading a collision that would have occurred in the past.
   The reconstructed encounter reports closest approach 13.49 s in the past,
   inside a 38.61 s prediction window.
2. Native `GetEscapePoint()` chooses between perpendicular 5 km evasion points
   using the other ship's relative motion. Neither this choice nor its
   subsequent `AIIntercept2()` call checks whether the evasive maneuver enters
   a planetary body. The initial overlap with the group's enclosing circle
   also legitimately invokes avoidance, so removing only past warnings is
   insufficient.

`FlyTo.RunCommand()` clears its plotted route on an avoidance warning and calls
`FlyToManual`; that command repeats the avoidance check and steers toward the
escape point. This explains both the absent route and the observed acceleration.
`HaulShip` also uses the same avoidance mechanism, so the defect is not police
specific. Native torch-drive ships are excluded by `IsOnCollisionCourse()`.

The native planetary detour helper checks straight origin/waypoint segments;
at the captured position it accepts the direct line to Minarchist with 184 m
surface clearance. That is not proof of a dynamically safe maneuver, and it
does not protect the separate ship-evasion branch.

## Evidence and replay

The latest quicksave was copied into ignored diagnostic storage before any
reload or intervention. It matches the live ship coordinates and target and
contains 20 recent trajectory samples spanning 38.76 s. No game state was
changed during this investigation.

The offline probe runs against the shipped 1.0.1.10 assembly using its bundled
Mono runtime. It reads 226 saved ship geometries and saved direct docking links;
reconstructs the connected group's native enclosing radius; and executes native
`IsOnCollisionCourse`, `WillCollide`, `GetEscapePoint`, and `AIIntercept2`.

- Observed acceleration unit vector: `(0.7534412335, -0.6575152528)`.
- Native evasion replay: `(0.7534412034, -0.6575152873)`.
- Future-only prediction removes the current false warning.
- Controls retain future threats and existing overlap, reject past-only threats
  and misses, and leave direct calls to the shared `WillCollide` unchanged.

A controlled approach replay starts from the near-stationary saved trajectory
sample 26.77 s before capture. It executes native FlyTo/FlyToManual/FlyToPath,
planning, waypoint advancement, body ephemerides, and evasion selection:

| Variant | Result |
| --- | --- |
| Native | Body impact after 26.38 s |
| Future-only ship prediction | Body impact after 26.88 s |
| Future-only plus body-aware evasion prototype | Native FlyTo finishes after 137 s; minimum hull clearance about 497 m; minimum wreck clearance about 3.64 km |

All variants retain Minarchist as the target. The successful prototype does not
teleport, refuel, retire, prune, change faction, or order the pilot home.

This subsequent replay is **not** a full Unity/save replay. Initial velocity is
estimated from neighboring trajectory samples; the scheduler calls FlyTo once
per game second and integrates at 0.125 s. Maneuver translation, safe-pilot
eligibility, comms presentation, priority-queue notifications, and the unused
torch UI limiter are fixture boundaries. The group is translated with native
ephemerides while retaining its saved rigid offsets. RCS acceleration is modeled
at the native local navigation cap; the prototype does not validate fuel costs.
These boundaries do not apply to the separate current-state evasion-direction
match above.

Local artifacts are under `obj/PatrolCollision-20261109-145646`,
`obj/CollisionDiagnosis`, `obj/collision-avoidance-native.txt`, and
`obj/collision-forecast.txt`. The prototype's body test samples a constant-thrust
forecast; this is an offline experiment, not an approved production algorithm.

## Implemented repair and boundaries

Repair native navigation geometry in the Navigation module. Police target choice
and inspection behavior are authored in the C# pilot tree; the reviewed ship and
`star_systems/star_system.json` assets supply hull/body geometry, not an alternate
planet-safe evasion evaluator. There is no evidence that changing the JSON
behavior definitions would repair these arithmetic/geometric faults.

1. Change only AI prediction's use of `WillCollide` to consider future encounters.
   Preserve warnings for existing overlap. Do not globally change the shared
   function: projectile collision checking intentionally looks across an elapsed
   interval and must retain its historical checks.
2. Before accepting a ship-evasion maneuver, evaluate its motion against nearby
   solid bodies and prefer a safe alternative. Preserve avoidance of the actual
   ship obstacle; a planetary check alone must not blindly select another
   direction that causes a genuine ship collision. Reuse native maneuvering and
   routing after selecting the safe direction. Preserve the original choice
   when safe and the original target throughout.
3. Production uses bounded, allocation-free closest-approach evaluation, rather
   than the prototype's dense sampling. A quadratic relative trajectory has a
   cubic distance derivative; split it at its turning points, then bisect its
   monotonic root intervals (at most three, 36 iterations each). Include both
   endpoints. Tests compare this minimum with an independent dense oracle.

Both changes use **one guarded transpiler**, on the existing AI avoidance method.
It checks one native collision call, one evasion selection, and the obstacle
local's type before changing IL. It does not patch the shared collision function
or add per-frame observers. The body check runs only when ship evasion is chosen.

The native dodge is retained when safe. An unsafe dodge can be reversed only if
the opposite direction clears every nearby solid body and avoids the triggering
ship obstacle. An already overlapping docking-group circle permits escape without
deeper penetration than the native choice; outside that circle the replacement
must remain clear. If both directions are unsafe, keep native handling rather
than inventing another controller or turning off collision damage.

Use the native half-thrust evasion cap and collision radii, including connected
hulls. The local forecast spans twice the native prediction interval (normally
60 s, capped at 120 s). Body position/velocity come from detached ephemerides and
their current orbital acceleration. This is a short quadratic forecast, not a
new orbit planner or proof of safety through every subsequent maneuver. It does
not change gravity integration. Grounded vessels retain native handling;
atmospheres are not treated as solid surfaces, and station placeholders/asteroid
fields are excluded from the solid-body test.

The native torch exclusion, player plotted courses, target policy and docking
behavior remain intact. There is no polling, saved state, persistent reference
to ships, refueling or forced home-order policy. This prevents the reproduced
bad avoidance decision; it cannot promise rescue from an already unavoidable
collision at the current paused position.

## Production validation

`Tests/PlanetaryNavigationCheck` links the production source into a bundled-Mono
harness and uses the compact, crew-free geometry fixture
`Tests/Fixtures/Navigation/PlanetaryEvasion.json`. Run from the repository root:

```powershell
dotnet build Tests/PlanetaryNavigationCheck -c Release
dotnet run --project Tests/SaveJsonCompatibility/Host -c Release --no-build -- Tests/PlanetaryNavigationCheck/bin/Release/netstandard2.1/PlanetaryNavigationCheck.dll
```

844 assertions include 800 randomized closest-approach comparisons, static and
moving ship threats, multiple bodies, both unsafe alternatives, connected hull
extent, physical versus atmospheric boundaries, grounded/placeholder/torch
exclusions, invalid prediction input, clear-space controls, patch-shape refusal,
body-cache preservation and zero steady-state helper allocation. The native
current-state replay matches the captured acceleration direction. The full
production transpiler is applied in the approach test, which completes FlyTo
with the original target for all three plausible anchored members of the saved
group (apartment, O-GXD and E-7HAP). Fixture boundaries described above still apply.
This is offline validation, not a completed live test.

## Live follow-up: returning hauler O-BFZ2

On the new build after reverting to `home`, O-BFZ2 (OKLG Hauler 22) is selected
at epoch `65632876148.745743`, about 1.189 km above Ganymed and descending at
0.718 km/s. It is registered as HaulerDeployer, Donovan Moran is alive aboard,
fuel is about 3175 kg, target is OKLG, and NavData is absent. Current thrust
projects surface crossing in about 1.66 s; this is not a successful prevention
result for the initial patch.

Reconstructing the apartment-anchored group gives a native collision warning at
2.028 km center distance against a 6.88 km combined group/expanded-hauler radius.
Native `WillCollide` against the actual large wreck returns false: it is 5.688 km
away and the combined radius is 2.58 km. The other three player-group members are
body-locked/body objects, which native IsOnCollisionCourse ordinarily excludes.
This is an overlap with the enclosing circle, so the future-only repair correctly
retains the warning according to that circle. The approximate circle includes
empty space around the real hulls.

The returning hauler's stored 86-point route from `home` ends at an intermediate
waypoint, epoch `65632876107.7472`. Evaluating the remaining stored segments with
native Ganymed ephemerides gives about 20.75 km minimum hull clearance. This rules
out that stored leg as the direct impact path; it does not establish the safety
of subsequent replans or reconstruct their entire decision history.

The new current-state evasion calculation points roughly in the observed thrust
direction, but does not match it exactly (unlike the earlier patrol capture).
Do not claim a full native decision trace for this hauler from that comparison.

Originally proposed repair candidate: keep the enclosing circle as a cheap broad phase, then
test the relevant actual group hulls before treating its empty interior as a
collision or rejecting an alternate dodge. Preserve legitimate avoidance of the
large wreck and native target/body-lock exclusions. This needs a native approach
replay from the earlier saved plan and genuine connected-hull collision controls;
do not just shrink/ignore all docked groups or force station arrival. The current
already descending state may be physically unrecoverable through normal thrust.

Ignored follow-up evidence: `obj/lawndart-*.json`, `obj/hauler-plan-check.txt`,
`obj/hauler-current-check.txt`, and the temporary native probes in
`obj/CollisionDiagnosis`. At capture there was no additional runtime patch or live
intervention. The subsequently requested station-docked policy now resolves the
return approach in controlled replay; see STATION-DOCKED-COLLISIONS.md. The proposed
hull-by-hull refinement above was not implemented.
