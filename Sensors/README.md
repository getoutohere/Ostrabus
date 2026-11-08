# Sensors: navigation contact certainty

See [design and motivation](DESIGN.md) for the sensor roles, physical basis,
gameplay safeguards and interplanetary scatter tuning.

The [model and comparison tools](PROTOTYPE.md) share the pure core under `Sensors/Model`.
`SensorContacts.cs` supplies the opt-in live adapter in `Ostrabus.Sensors`.
Consumer behavior and the patch boundary are documented in the
[integration audit](INTEGRATION-AUDIT.md). The revised prototype preserves fresh
contact precision through the base 120 km railgun envelope and uses a
smooth transition outside it.

## Live A/B testing

The PDA **Mod Settings** app has two independent, saved toggles, both default off:

| EM propagation | Contact position estimates | Behavior |
| --- | --- | --- |
| Off | Off | Native detection and position jitter. |
| On | Off | Inverse-square EM reception; native position jitter. |
| On | On | Revised EM reception and sensor-based position estimates. |
| Off | On | Position estimates with native inverse-cube EM acquisition. |

Launch defaults are `[EMPropagation] Enabled` and `[Sensors] Enabled` in BepInEx
config. Toggle during play without restarting. Disabled modules remove their own
Harmony hooks; disabling the display module clears its offsets and cache. Actual
ship coordinates, weapon targeting, native identity, AI logic and ship-target
course plotting remain native. A coordinate course selected from a displaced
contact still uses that selected coordinate, as vanilla does.

The adapter samples each contact at most once per two game seconds, reuses its
package array and track record, and interpolates offsets between samples. Observer
sensor state is refreshed on the same bounded cadence. There is no replay of missed
samples during SFF. Noise is deterministic, bounded, and aligned with radial and
cross-range uncertainty; it never consumes Unity's shared random sequence. Own and
directly docked ships, known stations and fresh contacts within 120 km remain exact.
Asteroids, beacons and projectiles retain native handling.

Outer-system contacts use the same model: there is no 1-billion-km cutoff.
Nonfinite inputs and numerical overflow still fall back safely. Rejected estimates
are cached on the same two-game-second cadence, preserving vanilla's existing
offset and noise timer. A transition from a modeled estimate releases its offset
once; subsequent rejected redraws leave the native sample alone. This prevents
rapid random jumps while paused and avoids allocating a new track every redraw.

Cache capacity is 2,048 contacts, falling back to native behavior if exhausted.
Records are released on ShipDraw destruction, console destruction, scene unload,
observer/world changes, disable and plugin shutdown. These estimates are never
saved. New records and package size changes can allocate; steady cached updates
reuse state. There is no claim of measured live-frame performance yet.

`Tests/SensorRuntimeCheck` exercises actual Harmony enable/disable, native offset
consumers, independent toggles, close-range precision, time jumps and cleanup under
bundled Mono. `Tests/PdaSettingsCheck` verifies the PDA bindings and saved flags.
Regression cases cover fixed-epoch redraws around the old distance cutoff, the
observed Venus-to-SVIR range (~1.49 billion km), and distances through 15 billion
km. They also exercise cached rejection, preservation of an existing native
sample/timer, recovery and a later loss of a valid estimate. Unity RNG is replaced
with a counted deterministic fixture to verify native noise is not rerolled.
The model comparison adds numerical and cross-runtime coverage. Mercury/Venus
gameplay confirmed toggle operation and informed the distant-scatter tuning.
After the SVIR paused-flicker correction, the player reported improved display
behavior. These live observations do not establish every sensor/UI scenario.

## Purpose

Replace vanilla's abrupt transition from exact contact positions to large random
offsets with a gradient of positional certainty. Preserve reasons to upgrade a
sensor package: greater useful range, better acquisition and tracking, complementary
measurements, and improved passive operation. Basic close-range navigation should
remain credible.

The starting point stays close to vanilla rather than introducing a full tracking
simulation. Position certainty should be separate from identification: accurately
locating an unknown ship does not reveal its identity or internal condition.

## Modeling strategy

**Use realistic or believable sensor phenomena to choose the algorithm and values,
then implement them with compute-cheap abstractions that produce good-enough results.**
The model should explain its behavior through sensor geometry, signal quality,
speed-of-light delays and imperfect knowledge of target motion. It need not reproduce
the underlying physical processes in detail.

Use inexpensive distance/error curves, light-travel-time arithmetic and scalar
confidence weights. Represent a contact with a small reusable state such as range
uncertainty, bearing uncertainty, an error offset and measurement timestamps. Avoid
photon, image, beam-sweep or detailed filtering simulations. Numerical tuning can
serve game balance while retaining an understandable physical basis.

Design for hundreds of contacts. Separate measurement updates from rendering:
schedule observations in simulation time, cache sensor-package properties until
equipment/state changes, and cheaply extrapolate between measurements. Spread work
across frames under a bounded budget; accelerated time must not cause unbounded
replay of missed observations. Prefer allocation-free updates after initial setup.
Profile dense scenes before claiming acceptable cost or choosing an update cadence.

## Sensor roles

| Sensor | Primary information | Agreed direction |
| --- | --- | --- |
| Optical | Bearing; angular size and visual features | Good bearing at ordinary ranges. Positional uncertainty is primarily range uncertainty, with accuracy degrading with distance. Introduce appreciable bearing error only at extreme ranges. |
| Active radar | Range and broad acquisition | Good ranging. Account for signal travel time and uncertainty in the contact's motion when estimating its present position. |
| Lidar | Precision active range and bearing | Precise tracking of acquired contacts, useful for docking, interception and recognizing maneuvers; shorter useful reach and weaker broad-area search than radar. |
| IR | Reactor, thruster and exhaust signatures | Remains primarily a detector of operating propulsion/reactors. At close range, exhaust and heat plumes can provide a plausible bearing. |
| EM | Emitter direction; timestamped transponder ranging | Primarily bearing from received emissions. A receivable, cooperative transponder also supplies precise ranging through its timestamp. |

Lidar's narrow directed beam motivates high precision and limited search coverage.
For the initial lightweight model, represent acquisition difficulty through signal
quality rather than implementing beam sweeps or a new search interface. Optical or
radar acquisition should complement lidar's precision role. Exact ranges, falloff
and acquisition bonuses remain to be tuned.

## Optical ranging and known hulls

At 30 km, an arbitrary 3% position error means 900 metres of displacement. That is
not an acceptable default for a usable optical track in the proposed model.

A telescope measures angular extent as well as bearing. If a transponder identifies
the make/model, known dimensions support angular-size ranging:

`distance ≈ projected hull size / angular size`

Known hull dimensions should therefore substantially improve optical range
certainty, particularly nearby. Aspect angle and deviations from the nominal hull
limit exactness. An unidentified contact can still have a good bearing while its
range is less certain.

Optical tracking should impose a **maximum error cap** when its observations are
usable. This is a minimum quality guarantee, not a minimum error preventing better
measurements from improving the result. Tens of metres at 30 km was discussed as
an illustrative gameplay target; no numerical accuracy curve has been adopted.

## Transponder assumption

Assume cooperative transponders broadcast a universal emission timestamp and that
navigation clocks are synchronized well enough for useful one-way light-time ranging:

`propagation distance = speed of light × (reception time − emission time)`

Receiving a usable broadcast thus supplies precise range, even without transmitting
an interrogation. Optical or EM direction combines with that range to locate the
contact. The broadcast can also supply make/model for optical interpretation.

This is an explicit game-world assumption about synchronization and usable message
timing. It is not a claim that bearing alone determines range. Flying dark removes
this cooperative ranging advantage. Exact timing error and minimum precision remain
design choices.

### Agreed transponder acquisition rule

Timestamp ranging requires a functioning broadcasting transponder, an enabled EM
receiver package, unobstructed reception, and sufficient **transponder-only EM**
strength against the observer's native detection threshold (normally 0.30, 0.25
with the sensor skill). Use inverse-square EM propagation. Optical visibility,
cached identity, and unrelated active-sensor emissions do not grant packets.
There is no separate 10,000 km limit. The live adapter must derive broadcasting
from actual transponder/antenna state; offline cases supply that state explicitly.

Sum enabled EM receiver contributions using the existing per-receiver 0.31 cap
and the transponder's native 0.6000000238418579 emission component. Keep total EM
emissions separate for general detection/bearing. Timing noise also uses the
transponder component, so unrelated emissions cannot improve timing precision.
The position estimator fades timing information through the last 2% of signal
margin above the threshold to keep uncertainty continuous at packet loss. Reception
itself uses the agreed threshold. Motion tracking and past-packet retention are
outside this stateless prototype.

## Light delay and estimated present position

Radar and lidar both have round-trip propagation delay. Transponder reception has
one-way delay. These measurements describe a past target state.

Navigation can extrapolate a tracked contact using its estimated velocity. Unknown
heading/speed, uncertain motion estimates, and maneuvers since the observed state
increase uncertainty in its present position. A steadily moving, well-tracked target
should not receive large arbitrary errors just because it is moving quickly.

## Combined certainty and presentation

- Combine complementary information: optical/EM bearing, active ranging, and
  cooperative transponder timing should improve the final position estimate.
- Make certainty vary continuously with useful signal quality and distance.
  Crossing vanilla's visibility threshold must not automatically erase all error.
- Additional sensors should help below and above the old cutoff, with diminishing
  returns. The exact stacking rule has not been chosen.
- Keep errors temporally coherent and blend measurement corrections. Avoid repeated
  arbitrary marker teleports. A lightweight bounded model is sufficient initially.
- Preserve exact positions for the player's own ship, docked connections and
  reliably charted station locations where the existing game already knows them.
- Require enabled, usable sensors and preserve physical occlusion. Installing a
  device alone does not grant observations through a planet.
- Initially retain native identification behavior and scope the overhaul to player
  navigation. AI perception and weapon accuracy are separate decisions.

An uncertainty ellipse and a more elaborate acquisition/lost-track model were
discussed, but are not required for the first implementation. There is no decision
to add a new nav-console interface.

## Vanilla baseline and integration points

Investigated against game 1.0.1.10:

- `ElectronicSystems.GetSignatureStrength` sums enabled sensor contributions.
- `GUIOrbitDraw.VisibleFromNavStation` compares the sum against 0.30, or 0.25 with
  `SkillOpsSensors`, subject to known-object and visibility exceptions.
- `ShipDraw.GetPositionOffset` gives visible contacts zero offset. Faint contacts
  receive an offset up to approximately 20% of range, biased toward larger values
  (mean magnitude approximately 13.3%). The offset changes after a random interval
  from 5 to `7 + rangeKm / 5` game seconds.
- Radar/lidar use base-range divided by distance inside base range and its cube
  outside base range. Current duplicate contributions add linearly.
- Optical strength has a steep relative-speed penalty and a fore/aft bearing bonus.
  Its treatment needs review when separating detection from measurement accuracy.
- IR's current signature uses RCS acceleration and fusion-reactor running state;
  it does not model general hull/cabin temperature.
- `ElectronicSystems.GetSensorRangeKm` takes the best individual sensor range,
  while nav visibility sums strengths. Audit consumers before changing either.
- `NavPOI.GetSXY` includes sensor offsets while `GetVSXY` uses actual ship velocity.
  Audit readouts, selection and course plotting so estimated positions are consumed
  consistently without accidentally changing AI navigation or player torch playback.

The earlier example curve (10%, 3%, 0.5%, 0.1%, 0.02% at increasing relative signal
levels) was an initial discussion aid, not accepted tuning. It must not override
the subsequent optical and transponder accuracy requirements.

## Implementation and validation constraints

Expose this as an optional realism enhancement; configuration/UI details and launch
defaults are not yet decided. Preserve vanilla behavior when disabled and introduce
no save dependency without a separate explicit decision.

Cache per-contact calculations and reuse state where practical. Avoid repeated
allocations in nav rendering, use simulation-time measurement updates, and release
contact references on removal, scene/save changes and module shutdown. Diagnostic
hooks should be temporary and removable.

Before implementation, choose the numerical curves and define how conflicting or
missing measurements combine. Validate geometry, monotonic improvements, stacking,
light-delay extrapolation and limiting cases offline. Exercise native rendering/
navigation integration against the shipped assembly, then use bounded live captures
to verify marker behavior, readout consistency, performance and disabled equivalence.
