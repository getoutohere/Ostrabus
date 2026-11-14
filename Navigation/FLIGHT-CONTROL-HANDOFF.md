# Release abandoned AI flight controls

Native `FlyTo.RunCommand` clears NavData when handing control to its RCS/manual
or pause branch, but leaves the old torch acceleration. `FlyToPath.TorchOff`
also depends on a reactor GUI and only calls SetThrust in Edit/Full. A shallow
AI can therefore keep integrating its abandoned course's torch force while its
RCS tries to steer, without ongoing torch-course fuel accounting.

`FlightControlHandoffPatch` replaces the two native FlyTo route-reset calls with
the same reset plus release of shallow-AI torch input. `ShallowTorchShutdownPatch`
makes an explicit TorchOff release that input even without a materialized GUI.
Native dispatch, RCS commands, outcomes, targets and physical reactor handling
remain authoritative. The native IL is checked for exactly two reset sites.

`ShallowRcsTorchReleasePatch` also covers direct `Ship.Maneuver` calls used by
combat, undocking and tow steering outside FlyTo. It releases unowned shallow AI
torch input only for a positive-duration command with no active NavData. An
owned course remains untouched. This avoids separate combat/hauler steering hooks.

Those three shallow-control hooks use IsAIShip plus Shallow load state. They do not alter Full/Edit reactor
controls, template copies, player manual thrust, or course playback while the
path leaf still owns it. It does not grant fuel, impose affordability/speed/range
limits, change gravity or repair damaged hardware/crew. Empty-tank shallow travel
retains the deliberately fuel-tolerant native/SFF behavior.

The shared `NavigationRoute.StopTorch` checks exact acceleration components and
the saved torch flag before calling native SetThrust(0). Unity Vector2's default
approximate equality is unsuitable for AU-scale acceleration. With no old torch
input, no shutdown call is made. No history, runtime actor table, new saved state,
per-frame callback or world sweep is added. The custom feature is independent
`ai-flight-controls`, categorized as a bugfix.
The shallow hooks run on native control invocations, including Maneuver;
their scope checks are scalar reads with no added per-call allocation. They are
not a zero-cost disabled diagnostic, and do not install a separate polling loop.

Our escape-target and Ceres retreat setters now use the existing full route-stop
operation instead of resetting the route alone. A changed destination releases
old controls; equivalent escape assignment keeps the current course. This
corrects an incomplete handoff in our earlier repairs without changing station
selection, combat/retreat priority or the native pilot trees.

Existing shallow AI already left with phantom thrust is corrected on its next
normal FlyTo RCS/pause or explicit TorchOff invocation. Velocity is retained:
the repair stops further phantom acceleration, not motion already acquired.
Normal planning or an explicitly requested recovery remains responsible for
returning an already fast/distant ship.

## Controller withdrawal and replacement

The same feature includes FlightControllerLifetimePatch.cs. Native withdrawal
already stops RCS/torch and clears the destination, but omitted cancellation of
its executable course. It also accepts stale controller references by hull ID.

- FlightControllerWithdrawalPatch checks exact registry identity, rejects stale
  requests and cancels the withdrawing controller's NavData before native stops
  and notifications. A same-hull replacement remains authoritative.
- FlightControllerDispatchPatch validates the exact controller at native normal
  and SFF manager calls, so detached queue/list entries cannot execute after removal.
  Moving the same instance to another ATC bucket preserves execution.
- CourseEngagementOrderPatch moves publication of a new player plan after the
  old withdrawal and before replacement Auto registration. Its shape is also
  validated before the cancellation prefix can bind, avoiding unsafe partial
  installation after a native handoff change.
- RetiredRetreatTerminalPatch changes only LowFuelRetreat's post-unregister
  Failure return to Finished. Both combat consumers otherwise interpret that
  Failure as permission to resume shooting/steering inside the same invocation.
  Ordinary no-retreat Failure and retreat target selection stay native.

This adds four patch classes covering five methods to the feature's original
three classes. The registry checks are allocation-free linear scans at native
dispatch/withdrawal, not a new actor table, frame callback or world sweep.
No state is serialized. Custom builds selecting only ai-flight-controls contain
all seven classes; the retreat correction composes with navigation-targets and
Ceres without requiring either feature.

Ordinary course completion, direct command calls, physical reactor settings,
momentum/spin and environment forces retain their native owners. Removing a
course before it can resume prevents the captured post-removal expiry leak.
Already uncontrolled hulls with residual force and no surviving course/controller
require explicit recovery; this feature does not automatically move or revive them.

## Layer ownership and validation

The C# defect sits between trajectory playback and control execution. Reviewed
assets include station/ship definitions and TIsHumanAwake; no JSON override can
change these force-reset calls. The native awake-crew and materialized equipment
rules remain unchanged. This is control bookkeeping, not a new AI policy.

`Tests/AiPlannerDiagnosis`, `flight-handoff`, executes the native bodies and the
production patches: 213 checks cover moving/locked targets, collision handoff,
pause, existing stale force without a route, GUI/no-GUI shutdown, template/
Shallow/Edit/Full and AI/player controls, ongoing torch courses, new RCS input,
direct RCS actor/course/load-state/duration combinations, changed/unchanged escape
destinations, retained fuel/momentum/spin and unknown
IL rejection. Existing negative/zero-fuel execution controls remain accepted.
The [test boundaries](../Tests/AiPlannerDiagnosis/FLIGHT-HANDOFF.md) distinguish
native consumers from prescribed geometry, steering, GUI and candidate plans.

Retarget (60), Ceres-priority/reserve (174), and composed flight-fuel/control
execution (23) checks complement this matrix. The combined run removes phantom
force while retaining the manual/planned fuel watermark. Full binding passes
263 patch classes without failures/warnings. The updated controls-only custom
build binds all 7 classes; controls plus navigation-targets binds 17, and controls
plus Ceres binds 45, without warnings or frame calls. Both the compiled full DLL
and controls-only DLL pass the same 234 production behavior checks.
Behavior graphs catalog this numerical
flight boundary explicitly; it is not an expanded model of all ship physics.

The [controller unregistration repair](UNREGISTERED-COURSES.md) is implemented
and exercised by 234 production checks, alongside the original 703 audit
assertions. Native player engagement/disengagement, control-mode changes, JSON
reconstruction, retirement, replacement, normal/SFF dispatch, continuation and
unknown-IL rejection execute. Warmed registration checks allocate zero bytes.

The [ownership and collateral audit](CONTROL-OWNERSHIP-AUDIT.md) also reproduces
stale normal/SFF dispatch and identifies EngageTrip's replacement-plan ordering.
It adds 410 native caller/handoff checks and joint regression runs, and explicitly
rejects changing every ResetNavData call into a physical stop. The implemented
withdrawal repair leaves ordinary course completion unchanged.
An additional 84 continuation checks establish a post-withdrawal LowFuelRetreat
return defect inside both combat consumers. Its terminal Failure is changed to
Finished; the analogous TargetNewIfNone experiment was rejected because its actual
following native leaves do not resume steering.
