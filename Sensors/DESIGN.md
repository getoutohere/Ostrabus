# Sensor position estimates: design and motivation

## Motivation

Vanilla ordinarily places a detected contact at its exact position until combined
sensor strength falls below a threshold. It then displaces the contact randomly
in any direction by up to about 20% of range. A small signal change can turn a
precise contact into a sprawling noisy cloud. This conflates knowing a bearing
with knowing a distance, and makes equipment behavior feel arbitrary.

We want believable navigation and meaningful sensor upgrades: useful optical
observations should establish bearing much better than range; active ranging and
cooperative broadcasts should improve the missing information. Accuracy should
degrade gradually while preserving early passive salvage and close combat.

Live testing at Mercury exposed the opposite extreme: distant VORB/OKLG wreck
fields became almost perfectly straight radial lines. Good bearings explain their
narrow direction, but the presentation overstated optical certainty at interplanetary
distances. The additional distant scatter gives those fields some width without
returning to vanilla's omnidirectional cloud or weakening nearby sensors.

## Near-realism through cheap abstractions

Use sensor geometry, signal spreading and light travel time to inform inexpensive
scalar calculations. Do not simulate photons, image processing, beam sweeps, gas
fields or individual packets. The game supplies insufficient aperture, transmitter
power, noise or bandwidth specifications to calibrate fictional 2079 equipment.
Numerical reference envelopes, floors and the local navigation prior are explicit
gameplay assumptions, not measurements or rigorous instrument specifications.

Sparse interplanetary gas is not being asserted to produce atmospheric-style
distortion. The extra optical term stands in for weak/unresolved imaging,
background/stray-light contamination and imperfect pointing compensation. Its
magnitude is tuned for believable presentation, rather than derived from gas density.

## Sensor roles and physical basis

| Instrument | Physical basis | Model abstraction |
| --- | --- | --- |
| Optical | Image direction establishes bearing; angular extent and known projected hull size support ranging. | Good angular precision, distance-dependent noise and weak range estimates for unresolved or unidentified hulls. Better units and additional cameras improve precision. Bearing alone never determines range. |
| Radar | Echo propagation time supplies range; active radio supports broad acquisition. | Direct range with coarser bearing. Inverse-fourth-power echo quality abstracts spreading on outward and return paths. |
| Lidar | A directed light echo supports precise range and direction. | Better base range/bearing precision, a shorter useful reference envelope and no explicit beam-sweep/search simulation. |
| IR | Reactors, thrusters and exhaust produce thermal signatures. | Bearing from native propulsion/reactor activity. No ambient hull/cabin-temperature model or direct range measurement is invented. |
| EM | Emissions establish direction; a cooperative timestamp can measure propagation time. | Coarse bearing from total emissions, plus direct timestamp range only after acquiring the transponder's own broadcast. |

Known dimensions improve angular-size ranging, but uncertain hull aspect and size
remain shared errors. Duplicate sensors cannot average away shared size, clock or
calibration uncertainty. Timestamp ranging assumes a common emission timestamp
and synchronized clocks; it includes independent jitter and a shared clock term.

## Transponder acquisition and independent EM propagation

Require a functioning broadcasting transponder, enabled EM receivers, unobstructed
reception and sufficient **transponder-only** EM strength. Sum contributions using
native per-receiver caps and the observer's native threshold: normally 0.30 or
0.25 with sensor skill. Optical visibility, cached identity and unrelated active
sensor emissions do not grant timestamps. There is no arbitrary 10,000 km limit
and no unlimited cooperative ranging.

The separate **EM propagation** option changes outer-range EM falloff from inverse
cube to inverse square in both native strength and detection-distance methods,
affecting player and AI detection. With it off, timestamp acquisition follows the
native inverse-cube rule. Equipment reference ranges and inner-range rules remain
unchanged. See [EM propagation](../EMPropagation/README.md).

Timing information fades through the last 2% of signal margin above the reception
threshold. Passive range uncertainty can still rise steeply as timing is lost.
This stateless model does not retain old packets or implement motion tracking.

## Fusion, light delay and scatter

Keep range and cross-range uncertainty separate and fuse independent measurements
by inverse variance. Apply shared floors once. Total positional RMS is
`sqrt(rangeSigma² + crossSigma²)`, not a bearing error or maximum displacement.
At reception a reflected observation is about one light-time old; two light-times
describe active acquisition latency. Uncertain velocity and acceleration increase
current-position uncertainty with age. Raw known speed alone is not an error source.
Live velocity and motion prediction remain native; the model does not measure them.

The renderer uses deterministic independent bounded components with unit variance,
aligned with the line of sight. One component perturbs range, the other bearing.
The offsets represent observations, not changes to the actual orbit. Reusing the
noise sample avoids independent random jumps on every frame/readout.

Additional interplanetary optical scatter starts at **1 million km**. Let
`u = max(0, rangeKm - 1,000,000) / 59,000,000`. For each optical receiver add in
quadrature an angular standard deviation of:

`6 degrees * (25 / instrumentBaseRangeKm) * u / sqrt(1 + u*u) / sqrt(crossSection)`

The term starts continuously, saturates and is applied before fusion. Nearby
optical values remain unchanged. At 60 million km, the surveyed six-camera Stellar
Magnificence fit gives about **1 degree** bearing uncertainty for the reference
target, approximately **1.05 million km cross-range**. Starter optics are less precise;
target aspect and other measurements affect actual results. These are one-sigma
reference values, not hard per-contact bounds or a new detection radius.

The first live tuning used one-tenth this angular term. At Mercury–Venus distance,
its roughly 60:1 radial-to-cross-range error ratio still looked like a straight
line. The revised reference ratio is about 6:1: an elongated cloud rather than a
needle. This is a deliberate presentation/gameplay adjustment, not a newly measured
physical distortion constant. Spatial comparisons must use equal scale on both
axes; an RMS-versus-distance plot alone hides the shape of the uncertainty.

## Gameplay and integration boundaries

- Fresh usable contacts remain exact through **120 km**, the strongest railgun's
  base auto-fire range, blending into the full model over 120–240 km. The gunnery
  skill's fourfold range bonus is intentionally excluded from this safeguard.
- Preserve the local passive-navigation prior for early boneyard salvage and a
  coarse range prior when measurements are weak. These are gameplay abstractions.
- Keep native discovery, occlusion, identity and hailing behavior. Own/docked ships
  and known stations stay exact; asteroids, beacons and projectiles remain native.
- Use one cached offset for drawing, hit-testing and selected-target readouts.
  Never write an observation into authoritative ship coordinates.
- Keep velocity, AI decisions, automatic weapons and torch plotting native.
  Ship-target plots initially use actual target state; coordinate targets derived
  from the display retain those coordinates as in vanilla. No continuous torch
  rendezvous replanning or prediction of future maneuvers is added.

## Cost, lifetime, controls and validation

Reuse a bounded contact cache and observer package array. Sample at most once per
two game seconds, interpolate offsets, and skip missed SFF observations. Avoid
steady per-frame allocations and do not consume Unity's shared RNG. Release state
on contact/console destruction, scene/world/observer changes, disable and shutdown.
No estimator state is serialized into game saves.

Both independent PDA options default off, persist to BepInEx config and remove their
Harmony hooks when disabled. They support vanilla, EM-only and combined A/B tests.

Offline tests compare shipped sensor methods, numerical boundaries, stacking,
acquisition, continuity, near-range safeguards and allocations. Native runtime
fixtures exercise actual hooks, offset consumers, toggle equivalence and cleanup.
The comparison app includes interplanetary distances. Live observations validate
presentation and practical behavior; arithmetic tests and plausible physics alone
do not establish measured live-frame performance.
