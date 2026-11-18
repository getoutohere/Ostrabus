# Scavenger recovery and believable orbital waiting

The [VORB investigation](../Venus/SCAVENGER-HOLD-AUDIT.md) exposed a missing
continuation in our earlier assistance repair. This implementation distinguishes
engine repairs from two intentional simulation policies, each separately
selectable in CustomBuilder. No device setting or new world-state journal is used.

## Engine repairs: scav-assistance and ai-hold-clock

ScavEmergencyRecovery retains the actor at native TargetNewIfNone after selection
fails. It returns Running instead of falsely succeeding without a target. The
existing absolute fAIPauseTimer throttles retries for 64 game seconds; repeated
ticks do not search, plan or resend. Active authored help suppresses attempts.
Existing valid targets continue through the original tree. Old detached
targetless Main visit Holds finish back into native Main, including after load;
actual docked visits, unrelated queued Holds and other roles keep their behavior.

AiHoldClockPatch repairs shared Hold's pause comparisons and removes countdown
writes to the absolute deadline. Its independent visit duration still counts
down. The patch also catches up only an expired shallow ShipRequestedHelp
condition using its saved native JsonTicker and Condition.Update. Its duration,
removal and successor effects remain authored. No whole-person CatchUp, clock
change or unrelated health/needs tick occurs. Unknown, missing, repeating and
queue timers are left alone; Full actors retain normal condition scheduling.

The native timer and command payload persist through save/reload/SFF. No saved
mod type/key or private retry scheduler is added. SOS range, sender eligibility,
replies, failed sends, refusal, hauler availability and dispatch remain native.
A successful message never means successful rescue. Failed selection logs now
describe retained scav controllers accurately; other roles retain native wording.

## Simulation policy: scav-return

After the ordinary random FindTarget search exhausts its candidates, try the
pilot's existing native GetTargetHome destination (currently its nearest station,
not a forced registered home). Require an existing visible docking-capable station
with the same registry identity and an accepted native GenerateNavData one-way
plan. Carry its actual fuel prediction into normal selection, flight and docking.
No round-trip reserve is added, and no route/fuel/propulsion success is invented.
Successful native jobs remain preferred. Combat and non-scav roles are excluded.
An unavailable return simply falls through to existing assistance/retry behavior.
The nearest station need not be the only reachable station; this is a bounded
fallback, not an exhaustive planner or a ban on legitimate regional travel.

## Simulation policy: orbital-waiting

Admission correction (2026-11-18): being outside the atmosphere was not sufficient.
A native/production counterexample showed the former rule admitting a stationary
ship that falls into an airless body. The policy now requires a supported bound
two-body trajectory whose closest approach clears the body, loaded atmosphere,
hull radius and a 1 km numerical margin. Radial rest, intersecting and escaping
trajectories, and invalid or unknown data retain native waiting physics.
See [the gravity-exemption findings and implemented correction](AI-GRAVITY-EXEMPTIONS.md#implemented-follow-up).

Eligible free, unpowered AI Hold and targetless scavenger selection waits retain gravity.
Position, velocity and fuel are untouched: waiting is orbital coasting, not
station keeping or an invisible orbit lock. Existing normal and SFF orbital
integration can then act. Custom builds include the Orbital, coasting and SFF
prerequisites. This policy follows the existing Orbital on/off setting, so turning
orbital correction off also restores native wait physics.

Preserve native waits for atmospheric/surface positions, docking groups,
body/orbit locks, grounded ships, hidden or player-controlled/owned hulls,
active navigation, thrust/lift/drag, combat and unknown body data. Atmospheric
eligibility uses loaded layer outer bounds conservatively, without allocating a
GasContainer. No per-frame world scan, retained hull collection or background
controller is added. Eligibility uses native free-flight gravity selection's
hierarchy walk, detached body position/velocity evaluation and scalar orbital
math at existing command boundaries. It neither fits nor registers an orbit.
This is conservative admission, not collision avoidance or a proof against
later encounters, perturbations or changes of dominant gravity source.

## Validation and inspection

See [native checks and explicit fixture limits](../Tests/ScavFuelDiagnosis/WAITING.md).
The inspection API already exposes active command, target/plan, fuel, gravity
flag and native timers. NPC `/cos/{id}/conditions` now includes read-only
`helpCooldown` with saved start/end, world time remaining, actor clock and age.
It does not expire flags or run evaluators. No recorder is required.

Live playtesting of this build remains pending. No existing NPCs were teleported,
refuelled or pruned during implementation.
