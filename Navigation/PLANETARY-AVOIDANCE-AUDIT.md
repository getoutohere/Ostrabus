# Planetary avoidance: O-ENB / Ganymed

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
