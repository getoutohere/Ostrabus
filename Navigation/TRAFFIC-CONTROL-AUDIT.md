# Ranked Ceres traffic investigation

Reviewed on 1.0.1.10, 2026-11-13. The [phantom-control repair](FLIGHT-CONTROL-HANDOFF.md)
is now implemented and tested. Fuel-tolerant travel is preserved; stricter fuel
admission and new civilian rescue policy were not implemented. Live captures are retained in ignored
`obj/BCERTrafficSurvey-20261113T224011Z` and `obj/CeresTrafficInvestigation`.
The recent `apartment` save supplies equipment/configuration evidence.

## 1. O-UH2EC: an abandoned course retains commanded thrust

Four living crew remain aboard. All four have IsHuman and none of the authored
`TIsHumanAwake` blockers: Unconscious, IsDead, Stunned or Recovering. Its saved
equipment includes one navigation console, six RCS clusters and fusion equipment;
its damage category is Used. These are shallow/saved observations, not a live
physical component health inspection.

Native `Ship.NavAIManned` accepts awake crew on a non-derelict shallow hull before
checking materialized nav equipment. Full/Edit ships instead need an actual nav
station in use. Missing consoles, incapacitation, surrender and legitimate combat
damage must not be silently repaired or given replacement controllers.

Live evidence: no NavData, about 19 m/s2 input acceleration, a latched saved torch
flag, and RCS steering. Across 86.19 seconds its velocity changed 1.235 km/s while
RCS fell 2.959 kg. Separation from its scanned enemy later exceeded 51 km/s.

Native reproduction establishes:

- `Ostranauts.Ships.Commands.FlyTo.RunCommand` discards NavData on moving-target
  and collision-driven handoffs to manual/RCS steering without releasing the old
  input acceleration.
- `Ostranauts.Ships.Commands.FlyToPath.TorchOff` depends on reactor GUI values and
  calls SetThrust(0) only in Edit/Full. A shallow hull without a reactor GUI retains
  the old force.
- `ShipSitu.ResetNavData` only removes the route; ordinary TimeAdvance continues
  integrating that force without the retired course's fuel accounting.

Our earlier `EscapeTargetPatch` repeated the incomplete cleanup. The native setter
kept its obsolete route; our first identity repair removed that route but left its
force. The new repair releases both; `CeresCombatRetreat.SelectHome` also uses the
shared explicit stop. Native baseline and repaired controls execute in the test.
The exact historical caller that
initiated O-UH2EC is unknown; this mechanism reproduces without damage or SFF.

Implemented repair: release controls when their autonomous course owner abandons
them; explicit shallow torch shutdown is independent of a materialized GUI.
Our retargeting paths use existing `NavigationRoute.Stop`. Its native operations
clear forces while preserving momentum and fuel. Do not globally redefine
ResetNavData or police ship speed: player manual controls, previews, active
courses and connected towing have different owners.

## 2. O-MG3 / O-VT4H: no civilian recovery continuation

Both pilots are alive aboard. The saved Bulk Lifters include two navigation
stations and ten RCS clusters each, with Used damage category. Both have zero RCS
and Hold active. The log records failed instant docking for O-VT4H but does not
establish its entire fuel history.

`Hold.RunCommand` refuses to finish an expired hold below 1 kg RCS without rotor
thrust. `TargetNewIfNone.RequestHelp` explicitly excludes Civilian, Navy, Pirate
and hauler roles. Our scav recovery intentionally changes only ScavPilot visits
and failed selection; civilians do not inherit it.

Assets reviewed: `SHIPSosStranded`, its `SHIPSOSDispatchHauler` /
`SHIPNegativeNoAction` replies, and `TIsNotPirateNotRequestedHelp`. They provide
generic non-pirate assistance but are not reached through this civilian caller.
JSON cannot override the hardcoded role gate. Extending civilian assistance is
an explicit simulation-policy choice; keep existing asset-driven acceptance,
refusal, dispatch and towing authoritative rather than inventing guaranteed rescue.

The previously documented `CivilianPilot.GetTargetHome` interface mismatch is
also real: its `new virtual` method is not the IAICharacter override, so shared
callers use BaseCharacter's unfiltered nearest station. Native tests reproduce
the different owner filters. This does not prove why these tanks emptied.

First repair force handoffs and unaffordable execution. Current evidence does
not establish fuel-accounting corruption as the cause of these two empty tanks.

## 3. Fuel predictions versus deliberately fuel-tolerant travel

### Civilian Hold follow-up, 2026-11-14

Read-only live/save/asset review and a native offline investigation confirm that
the civilian wait is not generally a lack of maneuver capability. Of ten sampled
Civilian holders, nine had zero RCS and one had 170.975 kg. Six empty holders were
moored to **asteroids**, not shipwrecks; three were free with asteroid targets.
All ten saved controller trees were at `/BehaviorTree/Main` child 4 (`Hold`),
without queued command payloads. Their captains had no incapacitation or
`ShipRequestedHelp` flags. Nine empty hulls were still holding about 2.36 game
hours later; the fueled control was absent from the later registry, with its
disposal path not established by that follow-up.

Native `Hold` normally visits for 100-650 game seconds. At expiry, its raw
`GetRCSRemain() < 1` gate starts another visit instead of allowing native Main to
continue. This repeats indefinitely without refueling, lift or interruption.
`CivilianPilot.FFWD` likewise resets an empty Hold to 1-30 seconds rather than
taking its positive-fuel instant-docking branch. Our clock fixes preserve this
fuel gate; the scavenger visit exception intentionally does not include civilians.

In contrast, native peaceful shallow AI `Maneuver` calls
`RemoveGasMass(..., aiSkip: true)` and can thrust with an empty stored tank.
A ship with a combat target consumes fuel; materialized fuel removal does not
take the shallow skip. Course playback also debits planned fuel independently.
The same raw empty tank can therefore permit travel but permanently veto the
completion of an ordinary mining visit. No active rescue wait explains these
sampled holds. The native SOS response requires an undocked recipient, and its
caller excludes Civilian, so simply broadcasting while moored is not a solution.

An isolated investigation harness executes native Hold, Civilian FFWD and
Maneuver, both with and without the deployed clock repairs: 911 assertions pass.
Four-hour empty/sub-kilogram waits remain Running; a fueled control finishes;
an intentional two-hour visit is preserved; help still vetoes completion.
Captain/help lookup, Unity identity, mass and docked propagation are controlled
fixtures. That investigation applied no candidate production policy patch or
live intervention.

The investigation recommended letting peaceful shallow civilian visit completion follow the
existing fuel-tolerant flight abstraction, while preserving real fuel checks,
help waits, scripted holds and native docking. The implementation below tests
the full continuation and SFF path, including the known home-dispatch/faction
filtering defect above. Hour-scale mining visits are an explicit duration policy
rather than a side effect of empty tanks. It reuses existing command timers and
decision boundaries; no extra fleet scan, journal or frequent route solver is
needed. Neutral ATC recovery remains separate future simulation policy.

Agreed asteroid-visit policy: draw a uniform duration of **5-120 game minutes**
(300-7,200 seconds) once per ordinary civilian asteroid visit. Short visits
represent prospecting that finds nothing worthwhile; longer visits represent
pretend mining. Fuel must not extend that visit indefinitely. Preserve the
remaining duration through SFF and save/load instead of drawing a fresh visit
each time. Station visits, non-asteroid encounters, scripted Holds and actual
assistance waits retain their separate semantics. This range is an agreed design
constraint. It is now implemented as the separate civilian-asteroid-visits
simulation feature, alongside the civilian-visits continuation repair. See
[implementation and native regression scope](../MiscBugFixes/CIVILIAN-VISIT-WAITS.md).
Existing visit remainders are preserved. Only real Asteroid-class attachment
receives the new duration; other ships and detached holds keep native timing.
Empty actors without a safe faction home retain the existing wait rather than
being released into an unsafe retirement. Live testing is pending.

O-1JKY has 53.39 kg RCS. Recorded mass (9577.49 kg), pilot efficiency (1.0), and
relative speed (647.33 m/s) imply about 78.78 kg merely to cancel that velocity
under native CalculateRCSFuelConsumption. Its route ends at -26.02 kg. O-NNH is
a control: same mass, skilled efficiency 0.75, 82.26 kg available, about 59.08 kg
braking cost and positive 22.42 kg predicted arrival. O-UH8J is empty with a route
ending at -168.32 kg. Those are genuine physical budget deficits, but they do not
prove that deliberately fuel-tolerant shallow travel should fail or be rejected.

`TargetNewIfNone.GenerateNavData` rejects a vacuum RCS candidate with nonpositive
arrival fuel. `FlyToPath.RunCommand` calls PlanTrip4 directly and installs any
non-null geometric candidate without that gate. In native tests the SAME proposed
-25/0 kg candidate is rejected by
selection and accepted by execution; a +25 kg control succeeds in both.

The native low-fuel-home branch also keeps home TargetData when its generated
plan is null. Our scav fix validates affordable manual fallback; other roles
do not inherit it. Ceres reserve logic explicitly exempts homeward plans,
retaining native execution's fuel tolerance.

Policy correction: the initial investigation overstated this inconsistency as a
definite reason to reject routes. Native noncombat shallow RCS and our SFF/freight
approaches intentionally tolerate deficient fuel. No execution fuel gate is added.
Any future change to combat reserve/risk policy must separately distinguish those
paths and preserve legitimate travel, native recovery and actor survival.

## Tests and coverage

`Tests/AiPlannerDiagnosis`, mode `flight-handoff`: 213 native reproduction/repair
checks. Actual native consumers, resets, shutdown and repaired escape patch run.
Collision result, target time update, manual leaf, pilot readiness, proposed plan
and GUI/audio are explicit fixture boundaries. The existing stop helper is
tested for force cleanup with unchanged momentum/fuel; an owned course remains
unchanged. This is not a new trajectory solver or proof of each actor's history.

Existing 64 native traffic checks and 228 Hold/sequence checks also pass. Flight
is an external leaf in the current behavior graph, so its transitions do not
establish engine ownership or numerical affordability inside these methods.
The effective model must gain these semantics alongside a production repair.
