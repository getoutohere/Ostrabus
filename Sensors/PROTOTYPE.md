# Offline sensor uncertainty prototype

`Model/ContactModel.cs` is a pure scalar model shared by the offline tools and
used by the opt-in live adapter in `SensorContacts.cs`. The comparison executable
itself installs no hooks. This is an uncertainty-envelope model, not a discovery
system or time-series motion tracker. See [live controls](README.md#live-ab-testing).

## Run and inspect

Requires the .NET 8 SDK and the configured game installation:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Tools/SensorComparison/Run.ps1
```

The script builds the native oracle and comparison tool, then writes ignored
artifacts under `obj/sensor-comparison`:

- `index.html`: standalone package/target selectors, comparison table and log plot.
- `comparison.md`, `comparisons.csv`, `comparisons.json`: numerical results.
- `native-baseline.jsonl` and `native-metadata.json`: native scores, cutoff locations,
  source assembly hash and bundled-Mono timing.
- `model-settings.json`: the prototype's exact parameter values.

The scenarios combine four fits and seven target/track states, sampling from 100 m
through 100 million km, including reception boundaries. Fits are a basic optical/IR/EM starter, a mixed active
upgrade, the surveyed Stellar Magnificence package, and that package with its active
sensors removed. Targets include unknown dark wrecks, known silent hulls, cooperative
broadcasts, hot dark ships, fast steady motion and uncertain/stale motion estimates.

## Model equations

Use metres, seconds and radians internally. Range and cross-range uncertainties are
separate one-sigma model scales. The reported positional RMS is
`sqrt(rangeSigma² + crossSigma²)`, not a worst-case bound.

For an active sensor, use a cheap echo-quality proxy:

`q = strength × crossSection × (baseRange / range)^4`

For passive IR/EM, use `q = signature × (baseRange / range)^2`. The supplied heat
signature represents reactor/thrust/plume activity; this prototype adds no ambient
hull-temperature model. Quality multiplies each sensor's measurement noise by
`sqrt(1 + 1/q)`. These reference ranges retain familiar equipment values but do not
claim to be calibrated physical link budgets or new detection thresholds.

Optical bearing noise is distance driven:

`sigmaAngle = baseAngle × (25 / instrumentBaseRangeKm) × sqrt(1 + range / (200 × instrumentBaseRangeKm)) / sqrt(crossSection)`

Beyond one million km, add the bounded interplanetary optical scatter term in
quadrature before fusion and angular-size ranging. Its motivation, equation and
Mercury–Venus tuning are recorded in [the design](DESIGN.md#fusion-light-delay-and-scatter).

Optical angular-size range noise follows `range² × sizeAngleError / projectedSize`.
Combine this with a shared hull-size/aspect uncertainty: 0.1% of range for a known
model, 20% for an unknown model. Size-angle measurement assumes centroiding at 5%
of the bearing-noise scale; this is a tuning assumption requiring review.

Measurements of the same component combine through inverse variance. Shared size,
clock and calibration uncertainty is not eliminated by installing duplicate units.
Range always retains a coarse 10%-of-range prior, so adding a weak observation
cannot worsen an existing estimate. Angular error saturates at a broad unresolved
bearing rather than letting the small-angle approximation grow without bound.

### Starting numerical assumptions

| Instrument | Base range sigma | Base bearing sigma |
| --- | ---: | ---: |
| Radar | 10 m | 60 arcsec |
| Lidar | 0.5 m | 0.5 arcsec |
| Optical (25 km reference unit) | Angular-size derived | 0.2 arcsec |
| IR | No direct range | 30 arcsec |
| EM | No direct range without decoded timestamp | 900 arcsec (0.25 degrees) |

The 50 km reference optical units halve the base angular noise. These are fictional
equipment assumptions for comparison and balance, not measured 2079 specifications.

A decoded transponder timestamp has 20 ns independent timing jitter per receiver,
plus 20 ns shared clock uncertainty. One-way range uncertainty uses `c × timingSigma`;
the shared term is approximately 6 m and cannot be averaged away by duplicate EM
units. Another measurement type can improve on it. Timestamp reception is now
derived from enabled EM-only reception against the native skill-adjusted threshold;
the scenario input now specifies broadcasting state rather than successful reception.
The prototype does not simulate clock synchronization, individual packet arrivals,
message cadence or identity authenticity. Timing information fades through the last
2% of signal margin above the acquisition threshold to avoid a positional-error jump.

The report compares three configurations: native sensors and offsets; inverse-square
EM with native offsets; and the overhaul with inverse-square EM and gated timestamps.
Additional distances sample immediately around each package's reception boundary.

### Keep passive-only salvage practical

The early OKLG game must remain playable without radar or lidar. An explicit shared
local-navigation prior caps usable optical range uncertainty at:

- Through 250 km: `max(0.25, 0.02 × rangeKm)` metres.
- Beyond 250 km: `5 + 0.01 × (rangeKm − 250)^2` metres.

Apply the cap once to the combined optical estimate, not independently per camera.
It requires an enabled optical observation and does not bypass occlusion. This is
deliberately a gameplay abstraction supporting the local salvage experience. It is
not evidence that one bearing can physically determine an unknown wreck's distance.
Future integration can anchor the prior in track history or surveyed local space.
The radius and coefficients are the current gameplay tuning, not physical sensor specifications.

The fresh-contact combat safeguard makes the starter fit exact at 30 km and
through 120 km; beyond that, the model blends in through 240 km. At 250 km,
starter regression cases remain within roughly 5.3 m. The local-navigation prior
supports passive salvage outside the exact-contact envelope without granting
precise ranging at interplanetary distances.

### Light time and motion

Use `c = 299792458 m/s`. Active acquisition has round-trip latency `2 × range / c`.
At reception, the reflected target state is approximately one light-time old, just
as an emitted optical/transponder observation is. Its effective age is
`sampleAge + range / c`.

Add shared motion variance to each estimated axis only once after sensor fusion:

`motionVariance = (velocitySigma × age)^2 + (0.5 × accelerationSigma × age²)^2`

Velocity uncertainty describes unknown speed/heading; acceleration uncertainty
describes unobserved maneuvers. Raw relative speed alone does not add error. The
10,000 km active round trip is only about 67 ms; kilometre-scale errors there should
come from weak measurements or stale motion knowledge, not exaggerated light delay.

## Vanilla comparison and validation

The native oracle runs actual shipped `Radar`, `Lidar`, `Optical`, `IR` and `EM`
signal methods under bundled Mono. It uses synthetic signatures and constructor-free
sensor fixtures with explicit range/strength/on fields; it does not instantiate
Unity equipment or run the nav renderer. The cutoff is found numerically using the
native 0.30 threshold. Detection/identification changes are not evaluated here.

Vanilla's offset distribution is calculated from the inspected `ShipDraw` and
`MathUtils` code: faint maximum radius `0.2 × range`, expected magnitude two-thirds
of that maximum, and radial RMS `sqrt(8/15) × maximum`. Above the native visibility
threshold, the ordinary contact has zero offset. This allows an RMS-to-RMS comparison
instead of comparing a proposed standard deviation against vanilla's maximum.

The proposed model is executed under both bundled Mono and .NET 8 and compared
across all scenarios. Tests cover finite output, added-sensor monotonicity, local
passive navigation, known-size ranging, timestamp requirements, shared clock limits,
occlusion, inactive sensors, known-position exceptions, age and velocity behavior.
Neither test oracle simulates correlated real-world sensor errors or packet decoding.

Revised checks: 5,476 assertions with 756 native/model comparisons passed, plus
native combat-boundary, EM distance and NavPOI fixtures. Outer-system regression
cases cover the former 1-billion-km boundary through 15 billion km, with numerical
overflow rejected instead of a solar-system distance cutoff. In one offline run the shared arithmetic
took about 78 ms for 100,000 evaluations of the 20-channel package under bundled
Mono (roughly 0.27 ms for 500 such evaluations). One million .NET 8 evaluations took
about 433 ms and allocated zero bytes after setup. These measurements exclude game
state extraction, tracking-state management, rendering and Unity GC; they are not
a live-frame performance guarantee. The bundled Mono allocation counter is not used.

The current fit at 1,400 km against an unknown dark wreck gives approximately 10 m
RMS versus vanilla's 204 km; a starter passive fit gives about 13 km. This demonstrates
the current upgrade incentive and smooth uncertainty. The optional runtime model
has since been exercised in gameplay; absolute accuracy remains a balance choice.

## Revised bearing and combat safeguards

Optical bearing now grows with `sqrt(1 + range / (200 * instrumentBaseRangeKm))`.
The old angular uncertainty grew quadratically with distance, making cross-range
error grow cubically. The revised curve keeps a useful bearing at extreme ranges;
range ambiguity, rather than arbitrary sideways displacement, dominates dark wrecks.

Bearing correction alone could not fix the starter's extreme-range regression:
its old coarse range prior allowed 20% one-sigma range error, already greater than
vanilla's total 14.606%-of-distance RMS. The revised shared coarse prior is 10%.
This is an explicit gameplay estimate, separate from the unchanged 20% unknown
hull-size uncertainty. It is not additional information extracted from bearing.
Fresh starter results are checked against native faint-contact RMS across the
scenario matrix. Stale tracks can worsen as unobserved motion accumulates.

Fresh acquired contacts remain exact through **120 km**. Between 120 and 240 km,
multiply the computed uncertainties by `t*t*(3-2*t)`, with
`t = clamp((rangeKm-120)/120, 0, 1)`. Beyond 240 km use the full model. This preserves
close-range gameplay without introducing another position jump. Aged/lost tracks
do not receive the fresh-contact safeguard. No-sensor and occluded observations
still yield no estimate; the live adapter must preserve native contact eligibility.

The radius comes from the shipped `condowners_ship_combat.json`: railgun
`ItmShipWeaponMassThrower02` has `IsShipWeaponArcRange=1.0x120000` (metres).
`Ship.UpdateCrewSkills` sets `WeaponsSystem.fRangeModGunner` to 1 or 4 when
`SkillOpsGunnery` applies. `NavModWeaponsControl.TargetLockHandler` multiplies mass-thrower
range by this modifier before automatic fire eligibility: maximum 480 km. By design,
the model clamp uses the unmodified 120 km base range, not this skill bonus. The
native harness checks the asset and actual `WeaponsSystem.IsPointInView` geometry
one metre inside/outside the extended firing radius. This is the automatic firing envelope, not
the maximum distance a projectile can travel or a restriction on manual firing.

At 100,000 km against an unknown dark wreck:

| Package | Range sigma | Cross-range sigma | Total RMS | Bearing sigma |
| --- | ---: | ---: | ---: | ---: |
| Starter passive | 9,949.88 km | 444 m | 9,949.88 km | 0.917 arcsec |
| Stellar Magnificence | 50.693 km | 78.5 m | 50.694 km | 0.162 arcsec |
| Vanilla faint contact | — | — | 14,605.93 km | — |

Reports expose bearing sigma separately so large radial uncertainty cannot be
mistaken for poor directional accuracy. These are synthetic reference targets,
not guaranteed bounds on every live contact.
