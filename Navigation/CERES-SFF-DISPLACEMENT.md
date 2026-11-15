# Ceres SFF displacement: two separate defects

## Evidence

The November 14 comparison uses the pre-skip `ai` save (epoch
65634045475.3376), the recorded 7,200-second skip beginning at
65634045808.995117, and paused inspection at 65634053088.797806.
These are external save/API observations, not shipped player fixtures.

Seven civilian hulls had native mooring links to asteroids, but were physically
about 134,925-134,941 km from their attached asteroid:

| Civilian | Attached asteroid |
| --- | --- |
| O-326 | C670p-5E3W |
| O-7X75 | C108a-T2A7 |
| O-UTZP | C319z-TKJG |
| O-HA8 | C561l-GVMB |
| O-CJ4B | C233p-PX7J |
| O-BDJ | C208m-XVZ |
| O-PDM | C208m-3EA |

The asteroids themselves match their saved body hierarchy's end-of-skip
ephemerides. They did not drift away. The enormous separation is approximately
two hours of heliocentric translation stored as a docking-group member offset.

Storm Adventure / O-KGEE was already bad before this skip. Its stored RCS vector
was approximately (-3.814619e-11, -3.994418e-11) AU/s2, or 8.26 m/s2. The same
vector and fuel amount appear in the earlier baseline and pre-skip save while
velocity increases outward. Live inspection finds one living crew member,
Redeem Orianna Bowers, with `Stunned`. The pre-skip hull JSON also records
`Stunned=1.0x1` with condition age zero on that same person. The cached AI captain is the ship CO
fallback, not evidence of an actual second pilot.

## Stale target coordinates during instant mooring

`NavData.TimeAdvance` ordinarily overwrites `Ship.shipSituTarget` with a temporary
completion point during SFF. Our destination-preservation transpiler correctly
kept the real target, but omitted a coordinate refresh. Native
`Dock.ResolveInstantly` calls `PlaceOrbitPosition` using that situation without
the `UpdateTime` performed by ordinary `Dock.RunCommand`.

The preserved asteroid target still described the beginning of the skip. The
incoming ship was placed there, then moored to the real asteroid at the end of
the skip. Native docking retained the resulting huge offset. This is a
regression in our integration with native instant mooring.

The existing completion helper now updates the preserved target to the current
epoch before returning. Target identity, body binding, authored destination,
native final course placement and native mooring stay authoritative. This needs
no new hook. Ceres asteroid/station JSON already defines the moving hierarchy;
changing those assets would not repair stale coordinate use.

## Pilot refusal leaves an abandoned RCS command

`Ship.NavAIManned` evaluates native `TIsHumanAwake` and, where appropriate,
navigation hardware. That trigger forbids `Stunned`, `Unconscious`, `IsDead`
and `Recovering`. The assets already express the intended rule.

With no remaining NavData, `FlyToPath.RunCommand` nevertheless calls `TorchOff`
and returns success when no AI pilot is available. It does not stop the prior
RCS maneuver. Its parent can advance toward docking while physics continues
integrating the old acceleration without a new steering command.

The validated flight transpiler changes just this refusal return for autonomous
AI: native `StopManeuver(false, false)` releases RCS/angular acceleration and the
native cached Running status keeps flight pending. Player takeover and
current-player/non-AI controls retain the native return. Existing NavData is
handled earlier by native code and remains executable unattended.

The existing SFF adapter also honors that native availability gate for otherwise
eligible manual travel. It coasts an unavailable pilot's ship, releases the old
RCS maneuver at application, and suppresses instant docking for that skip.
Native AI retries when a pilot becomes available. There is no new controller,
saved state, recorder, refuel, range cap or forced return. Legitimate plotted
interplanetary courses remain excluded from this adapter; INTERREGIONAL
membership is not used as a substitute for control ownership.

The repair does not heal stunned crew, advance their biology or erase velocity
already acquired. The Stunned asset has a short duration; prolonged presence on
a shallow crew is a separate concern, and does not establish the original cause
or expiry mechanism. Existing bad momentum requires explicit recovery if desired.
Reloading the pre-skip save avoids recreating the seven bad mooring offsets,
but that save already contains Storm Adventure's excess velocity.

## Validation and model limits

`Tests/AiRouteCompletionCheck` executes native course completion and instant-moor
placement with real locked-body ephemerides. It reproduces stale placement, then
checks refreshed mooring, unchanged target identity and body binding at
60-second, two-hour and six-hour boundaries. It also reproduces native
false-success/residual RCS and tests the repaired refusal, momentum preservation,
cached status, player controls and existing-course bypass.

`Tests/OrbitalCheck` tests the full SFF capture/application/catch-up path for an
unavailable pilot: ballistic motion, zero abandoned RCS, unchanged fuel and no
instant arrival. Existing navigation, civilian, Ceres, scheduling and economy
regressions remain separate evidence, rather than proof of the whole simulation.
An attached ship's authorized pilot is also checked: an incapacitated member
must not veto that pilot's existing travel command.

The traffic graph's flight and SFF nodes now pin these native/patch boundaries
and pilot trigger assets. Its lifecycle edges do not calculate physical
coordinates, docking offsets or health timers. Those were gaps in the earlier
analysis; source references do not turn the projection into a physical simulation
or replace native reproduction tests.

## Live validation

Reloaded the exact pre-skip save at epoch 65634045475.3376 with the repaired
build, confirmed matching installed/build DLL hashes and enabled orbital/SFF
correction, then captured a read-only fleet baseline. The repeated skip began at
65634045477.591431, advanced 7,200 seconds, and ended paused at
65634052677.591431.

All seven visitors moored to the expected asteroid IDs. Their measured physical
separations were 3.19, 1.73, 1.81, 1.93, 1.63, 1.47 and 1.99 km respectively,
instead of the previous approximately 134,930 km. All seven captains were alive
and their controllers remained registered.

O-KGEE had zero RCS and torch acceleration afterward, retained its BCRS target
and registered FlyTo controller, and consumed no additional fuel. The SFF report
explicitly records `mode=coast`, `awaitingPilot=true`, applied motion, no instant
arrival and no impact. The crew member remained alive and Stunned. Its range
increased from approximately 378,682 to 939,536 km because the saved excess
velocity remained: the repair did not invent braking or an available pilot.

The SFF report recorded zero failures and one suppressed instant catch-up. An
ordinary civilian return also completed: O-PDB0's controller retired, its hull
survived, and Joseph Guerra was alive at BCER. No recovery, velocity matching,
refuel, pruning or trace activation was performed during this validation.
This validates the measured mooring and pilot-refusal boundaries, not every
possible SFF, combat or crew-health interaction.
