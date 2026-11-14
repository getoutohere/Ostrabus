# Unregistered course playback investigation

Run with the shipped 1.0.1.10 assembly under the bundled Mono runtime:

~~~powershell
dotnet build Tests/UnregisteredCourseCheck -c Release
dotnet run --project Tests/SaveJsonCompatibility/Host -c Release --no-build -- Tests/UnregisteredCourseCheck/bin/Release/netstandard2.1/UnregisteredCourseCheck.dll
dotnet run --project Tests/SaveJsonCompatibility/Host -c Release --no-build -- Tests/UnregisteredCourseCheck/bin/Release/netstandard2.1/UnregisteredCourseCheck.dll caller-audit
dotnet run --project Tests/SaveJsonCompatibility/Host -c Release --no-build -- Tests/UnregisteredCourseCheck/bin/Release/netstandard2.1/UnregisteredCourseCheck.dll continuation-audit
dotnet run --project Tests/SaveJsonCompatibility/Host -c Release --no-build -- Tests/UnregisteredCourseCheck/bin/Release/netstandard2.1/UnregisteredCourseCheck.dll production
dotnet run --project Tests/SaveJsonCompatibility/Host -c Release --no-build -- Tests/UnregisteredCourseCheck/bin/Release/netstandard2.1/UnregisteredCourseCheck.dll production-external bin/Debug/netstandard2.1/Ostrabus.dll
~~~

Run the shared native host sequentially with other suites.

The default mode passes 209 assertions; caller-audit passes 410 and
continuation-audit passes 84. These baseline/candidate modes deliberately leave
the production ownership hooks uninstalled in their offline process. The explicit
cancellation counterfactual clears NavData immediately before native unregistration.

The production mode passes 234 additional assertions using the actual linked
production source. It exercises native withdrawal with no explicit test-side plan
reset, coast beyond the old plan's expiry, native UI/control handoffs, private stale
removal, normal/SFF detached dispatch, JSON reconstruction, terminal/nonterminal
combat behavior, registry migration and missing-registry controls. It composes
with the existing shallow/crew/retreat-progress patches, checks allocation-free
warmed registry reads and rejects malformed native IL shapes.
The production-external mode loads the same seven classes from a compiled DLL
instead of applying the linked copies. Both the normal Debug DLL and generated
controls-only DLL pass the same 234 assertions, including a direct delegate to
that DLL's registry check for allocation validation.

The matrix covers RCS/torch routes, visible/hidden hulls, native versus explicit
cancellation, and the three existing production ai-flight-controls patches
absent/present. Native Maneuver, StopManeuver, SetThrust, ClearShipTarget,
registry/queue removal, ShipSitu.TimeAdvance and NavData.TimeAdvance execute.

Native cases establish that unregistration clears controls but retains NavData;
playback restores movement/force without a controller; natural expiry clears the
route but retains force; and subsequent physics accelerates without fuel use.
Existing flight-control patches do not close this boundary. Cancellation prevents
the sequence while preserving velocity, spin, free coasting and fuel.

Fixtures replace Unity identity, docking/redraw propagation, condition writes and
waypoint UpdateTime. Rotor readiness is zero and no physical reactor is
materialized, using the shallow fuel-storage path. Course geometry is prescribed
rather than planner-generated. These checks establish the native defect and
candidate effect, not every caller, O-71CT's historical write, save migration or
completed production-patch compatibility.

Additional checks execute native normal and SFF scheduling with the production
crew guard installed and healthy crew aboard. Removed controllers still execute
from detached batches, including after same-hull replacement. An offline manager
dispatch candidate validates exact controller identity without adding another
registry and prevents those writes while allowing the replacement to run.

The continuation audit executes LowFuelRetreat inside both actual combat steering
consumers. Native withdrawal succeeds, but Failure lets them attempt further
shooting/steering in the same invocation. A terminal-return-only candidate stops
that while preserving non-retreat combat. Existing LowFuelRetreatProgressPatch is
tested absent/present and does not close the gap. Prior retreat flight, sensors,
shooting and final steering output are explicit fixture boundaries.

A counting next leaf shows failed TargetNewIfNone can advance its sticky parent.
The actual next native FlyTo/Dock instead reject the missing target without
steering. Its experimental status rewrite is excluded from the proposed repair:
a continued leaf alone is not proof of resumed control.

Collateral controls cover a replacement course assigned during an unregister
callback, unrelated hull/manual controls, retained external force/lift/drag, and
the EngageTrip publish-before-withdrawal call sequence. The original default
mode is a sequence reproduction. The newer caller-audit mode executes actual
native EngageTrip, Auto construction, saved command loading, registration and
playback. A broad unregister postfix or unqualified plan reset would erase valid
replacement navigation.

Native Dodge windup, no-station selection failure and emergency-signal retarget
also reproduce plan removal with retained controls. These are leaf mechanisms,
not proof that every caller leaves force indefinitely. See the
[ownership audit](../../Navigation/CONTROL-OWNERSHIP-AUDIT.md) for per-caller
classification and production integration boundaries.

Caller-audit runs four configurations: native; unsafe withdrawal cancellation;
cancellation plus native EngageTrip assignment reordering; and exact-registered
withdrawal plus that ordering repair. It also executes native station-keeping
and held-thrust switches, invalid-reference/disable/PDA/debounce branches,
player disengagement, empty-crew station retirement, explicit/expired/fuel
terminal leaves, native JSON course/controller reconstruction and same-object
regional migration. A stale private withdrawal removes the replacement controller
in native code; the last candidate rejects it before stopping anything.

The caller fixtures substitute captain selection, command-name lookup to native
types, UI/audio/log output and lamp images. Native mode selection, queued commands,
Auto construction, save conversion and registration execute. Empty-crew retirement
does not simulate crew transfers. Missing Unity internal-call diagnostics can be
printed outside the engine; assert process exit/completion rather than treating
those runtime messages as a game defect.

Install the actual production hooks while executing another existing native
suite in the same process:

~~~powershell
dotnet run --project Tests/SaveJsonCompatibility/Host -c Release --no-build -- Tests/UnregisteredCourseCheck/bin/Release/netstandard2.1/UnregisteredCourseCheck.dll production-integration Tests/TorchTowCheck/bin/Release/netstandard2.1/TorchTowCheck.dll
~~~

Arguments after the target DLL are forwarded to its entry point. Target suites
retain their own fixtures and production patch setup. Production integration changes
private registered withdrawal, EngageTrip publication order and normal/SFF
manager dispatch, plus the specific terminal retreat return in the final mode.
It does not change ordinary course completion, global playback
eligibility or physical reactor input. Harmony registrations are checked after
the target returns. See the ownership audit for completed integration suites and
their exact scope. The offline process is modified; game processes, installed
assemblies/assets and saves are untouched.

The older candidate-integration and candidate-terminal-integration modes retain
the test-only candidates for comparison. Production-integration installs all four
new production patch classes and verifies their five target methods remain patched
after the target returns. TargetNewIfNone's return stays native in each mode.

See [the investigation](../../Navigation/UNREGISTERED-COURSES.md).
