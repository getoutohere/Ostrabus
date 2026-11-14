# Unregistered ships retain executable courses and force

Status: implemented in the ai-flight-controls bugfix for 1.0.1.10. The audit has
703 native baseline/candidate assertions; another 234 checks execute the linked
production patches, including native handoffs and rejection of unknown IL.
The production code is also run through the joint reactor, towing, lifecycle,
navigation, Ceres and Scav regression suites. See the audit for exact boundaries.
No migration or live recovery is performed.

## O-Y38JN: observed ownership loss

Outside Context Solution is a Galcon hull previously operated by a Civilian
pilot. The bounded live history records that controller's removal at epoch
65633983784.540115. Later the same hull has a Scav controller:

- 65633984762.677925: Scav evaluating threats, targeting BCER.
- 65633984770.679108: first waypoint of the retained 76-point torch course.
- 65633984770.780159: active command sampled as FlyTo.
- 65633984776.872772: controller unregistered.

The subsequently observed hull is hidden, crewless, unregistered and untargeted,
but the course remains executable. Multiple readings show changing position and
velocity and approximately 19 m/s2 of acceleration. The course predates removal;
it is not evidence of new AI planning afterward.

The course ends at 65633986352.272255. At 65633986458.887009 the live hull has no
NavData, but retains its final braking acceleration
(-8.969858e-11, -9.121680e-11) AU/s2. Its fusion fuel no longer falls. A finite
orphaned trip has become persistent raw phantom force.

An earlier autosave, after the first removal but before Scav reuse, contains a
visible, zero-RCS hull with crew references and no saved course. It does not
establish the second removal's caller or crew disposition. Do not project the
final hidden/crewless state backward onto the first event. The historical recorder
does not retain the unregistration call stack.

At 65633986925.172585 native fleet reuse reactivated the same hull as Navy.
The later complete snapshot has Captain Kelly Norman and three living crew,
visible and registered, with the stray torch input zero. This later legitimate
reuse does not undo the captured defect. Do not treat the ship as currently
empty or apply recovery based only on its earlier snapshot.

Private captures are under ignored obj/CeresSurvey-* and
obj/Y38JN-Investigation-*.

## Native mechanism

1. AIShipManager.UnregisterShip(AIShip), the private overload, calls native
   Maneuver(0,0,0,0,1e-10), SetThrust(0) and ClearShipTarget, removes registry and
   scheduling entries, and publishes AIShipUnregisteredEvent.
2. Those stops clear current RCS/torch force. ClearShipTarget clears destination
   references. None cancels objSS.NavData.
3. ShipSitu.TimeAdvance dispatches retained NavData before ordinary physics.
   No AI registration or visible hull is required.
4. Ostranauts.ShipGUIs.Utilities.NavData.TimeAdvance copies saved position,
   velocity and acceleration into the live situation, undoing the native stop.
   Shallow torch playback also updates stored fusion fuel.
5. Past its final waypoint, playback clears NavData without necessarily clearing
   its last force. Ordinary physics then integrates that force without a course
   or controller to stop it or debit fuel.

The native zero-throttle RCS stop reaches StopManeuver(false,false), which clears
RCS unconditionally. This reproduction proves a later playback write undoing
that successful stop, rather than a zero-throttle readiness failure.

## Assets and existing patches

Reviewed TIsValidStationDropOff and TIsHumanAwake alongside CrewSim.DockAndDespawn,
the Civilian/Scav trees and reserve selection. Drop-off excludes dead,
derelict-victim and OKLG-pirate actors; awake eligibility excludes unconscious,
dead, stunned and recovering actors. These govern crew policy, not navigation
ownership. No asset override repairs the missing C# cancellation.

Native DockAndDespawn is one unregistration caller: it moves/removes crew under
authored rules and hides a hull for reuse. Those policies need not change.

Existing ai-flight-controls patches cover FlyTo RCS/pause exits, shallow TorchOff
and RCS commands without an active course. They deliberately preserve movement
owned by a course. The reproduction also fails with those patches installed.
CrewedAIControl clears routes in its own rejected-crew path, but that does not
cover every native unregistration caller.

## Implemented repair

The [completed direct-call ownership audit](CONTROL-OWNERSHIP-AUDIT.md) reviews
all 31 native withdrawal calls in 27 methods. Native callers, JSON reconstruction
and joint suites now support exact registered withdrawal plus EngageTrip
publication-order repair and normal/SFF identity admission. The continuation
audit also requires correcting LowFuelRetreat's terminal failure return; otherwise
its combat consumer attempts steering after withdrawal in the same invocation.
Ordinary course
completion is deliberately excluded from this repair. Blanket unregister
cancellation remains rejected.

Extend ai-flight-controls at native controller withdrawal: cancel the withdrawing
hull's current course as part of the native stop, before notifications can
reactivate the hull or assign a replacement course. Keep native stops, target
cleanup, registry/queue removal and lifecycle policy. Do not wipe replacement
navigation in a broad postfix after callbacks.

The initial offline candidate reset NavData immediately before unregistration.
The implemented repair checks exact controller registration, cancels only an
actual withdrawal, moves new player course publication after the old withdrawal,
and rejects stale normal/SFF execution. Native private withdrawal otherwise also
removes a replacement by hull identity; the guard prevents that existing hazard.
The production patch changes only the terminal LowFuelRetreat return to Finished
so its consumers cannot resume fighting after retirement. Ordinary no-retreat
Failure remains authoritative; TargetNewIfNone's return remains native because
its following FlyTo/Dock reject the cleared target without steering.
The implementation is [FlightControllerLifetimePatch.cs](FlightControllerLifetimePatch.cs).
The audit records supported scope and completed production integration tests.
Already contaminated live hulls require
explicit recovery or separately justified migration; prevention cannot undo
momentum or repair hulls already outside the registration lifecycle.

No teleport, automatic refuel, deletion, replacement behavior controller or
per-frame world sweep belongs in this repair.

## Related O-71CT

Iron Helps Us Play is a visible Belt Pirate hull with no controller/course and
approximately 10.6 m/s2 of raw RCS acceleration. Its sole living crew member is
unconscious. Live velocity change agrees with stored force while fuel stays fixed.

The same no-course/RCS-force phenotype is reproduced above. However, O-71CT's
captured older torch course ended before removal; a later short RCS course/write
is absent from the history. Its exact resurrection sequence remains unproven.
The separate source/live investigation is retained under obj/O71CT-Investigation-*.

The independent investigation confirms a Galcon Navy attack by O-5S6K before
unregistration, nav-station damage 25 in the later save, one dead crew member and
one unconscious/shocked survivor. Removing this controller is therefore
consistent with legitimate combat disablement; the unexplained behavior is
continued force after removal. A measured 167-game-second interval gives
10.60 m/s2 versus stored RCS force 10.62 m/s2, with fuel fixed at 812.2805 kg.

The remaining native write sites include course playback, Maneuver, situation
copy/restore and dock-group synchronization. No docking links exist here.
The audit found no confirmed shallow Maneuver damage callback that could stop
then rewrite force. Already-dequeued AI references are a separate possible
historical dispatch boundary; native normal/SFF manager reproductions now prove
that detached batches execute removed controllers when healthy crew remain.
The installed crew-readiness guard rejects this particular
unconscious-only crew. Neither possibility establishes the historical writer.

See [the native harness](../Tests/UnregisteredCourseCheck/README.md).
