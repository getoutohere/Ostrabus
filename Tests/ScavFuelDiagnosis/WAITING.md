# Scavenger selection, return and orbital waiting checks

Run `ScavFuelDiagnosis.dll vorb-hold` through the existing bundled-Mono host.
156 cumulative assertions currently pass: the 23 native asset/SOS checks plus
133 continuation, clock, return, exclusion, allocation and condition checks.

Native contrast establishes premature retirement, expired positive deadlines
bypassing Hold's speed check, pause countdown and gravity exemption. With the
actual production patches installed, native Main remains at TargetNewIfNone
after failed selection. Repeated calls respect the existing 64-second deadline;
active help suppresses sends, and retry resumes after expiry. Old detached
Main[4] saves release the invalid visit; actual docked visits retain their timer.

Command progress is serialized using native GetSaveData/LoadSaveData; the two
native ship timer fields roundtrip through JSON. ScavPilot.FFWD and the actual
OneShotWrapperCommand preserve the selection step. A six-hour epoch advance
permits a fresh attempt. No custom command, timer or save type is introduced.

A valid registered station and accepted native one-way plan produce normal
TargetData/NavData assignment and TripFuelPrediction. Failed fallback remains in
selection and uses authored SOS; non-scav roles do not gain the fallback. Repeating
1,000 unchanged selection waits makes zero extra planner/message calls and
allocates zero managed bytes in the fixture.

Orbital waits retain gravity only for eligible free AI ships. Controls cover
loaded atmospheric layers, surface positions, actual dock links, body/orbit locks,
grounding, input/RCS/lift/drag, player ownership, hidden hulls, missing body,
combat blackboard/target, navigation plans, torch use, stations, non-AI hulls and
the existing Orbital switch being off.
The native absolute pause stays unchanged while visit duration still counts down.

The installed ShipRequestedHelp definition supplies its real duration. Its native
JsonTicker survives a JSON roundtrip; before/end/after/SFF cases exercise actual
Condition.Update on the narrowly expired condition. Full actors, unrelated
conditions, repeating or absent timers remain untouched. Repeated expiry is
idempotent; the actor clock and other condition ages do not advance.
An integrated selection test also runs the actual native help-condition evaluator:
the active flag suppresses a request, then expiry permits retry without a supplied
boolean help outcome.

Explicit fixture boundaries: candidate failure and accepted/failed planner output,
speed inputs, nearest-body lookup, message delivery and help application, Unity
maneuvering/equality, and condition removal's UI/scheduler effects. Native Main,
selection caller, targetless FlyTo/manual failure, docking eligibility, Hold,
Condition.Update, JSON DTOs and command payload handling execute. These checks
do not replay the historical random search, integrate the live trajectory, load
a full Unity save scene or prove a future rescue will succeed.

The existing `recovery`, `fuel-patched`, `refuel`, `replenish`, `retarget` and
`scope` modes remain separate regressions. See [the implementation](../../Navigation/SCAVENGER-WAITING.md).
