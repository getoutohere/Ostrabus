# Revised recovery proposal

Implemented in [ScavEmergencyRecovery](../../../../MiscBugFixes/SCAV-EMERGENCY-RECOVERY.md).
The following records the approved scope; validation results are in
[RECOVERY.md](../../../../Tests/ScavFuelDiagnosis/RECOVERY.md).
The previous five-stage rescue proposal mixed repairs with new behavioral policy.
Start by making the existing behavior reachable, with no separate rescue
controller, retry timers, parallel JSON schema or global stranded-ship sweep.

## Code/data boundary

ScavPilot constructs its Main, priority, combat and threat branches in C#.
The reviewed JsonShipSpec schema has no ship fuel-feasibility predicate or pilot
tree definition. The known condition/pledge assets don't install its missing
emergency transition. Editing those assets alone cannot repair this control flow.

SOS eligibility, dialogue, replies, help condition, station range, DeployHauler
command and transfer-completion pledge already exist in JSON. Keep those
definitions and native evaluators authoritative.

## Proposed repairs

1. **Finish an empty scavenger's ordinary visit.** At its normal Hold completion
   boundary, empty fuel must not prevent Main returning to its existing Undock.
   Scope this to Scav Main's visit, preserving help-in-progress behavior. Do not
   globally change Hold, manually unlink ships or invent a replacement sequence.
2. **Recognize an unaffordable return.** Evaluate a suitable homeward candidate
   using native fuel/propulsion feasibility. The existing AmIOutOfFuel predicate
   cannot classify the targetless state left by docking cleanup. A genuinely
   unaffordable return should enter existing RequestHelp instead of becoming an
   accepted navigation target.
3. **Correct request-result/lifecycle handling.** SendMessage returns success or
   failure, but callers ignore it; station paths unregister even after an unsent
   request. Exercise that boundary before enabling rescue. Failed transport must
   not masquerade as assistance arranged or cause premature cleanup. Delivery,
   station response, actual dispatch and arrival are distinct outcomes.

Important correction: **null NavData alone is not fuel infeasibility**. Native
FlyTo supports legitimate manual fallback. Preserve it when propulsion/fuel
permits, including existing rotor/fusion exceptions. Evaluate a homeward leg,
not an unnecessary additional round trip. These distinctions belong in the
offline tests before choosing the implementation.

The shared selection boundary and scavenger-specific Hold change have different
scopes. Review other roles before altering their retirement/assistance behavior:
the native helper intentionally excludes several roles from SOS.

## Policy excluded from this bugfix

Retain the current player-first routing, native interaction validation, station
eligibility and hauler availability/reachability/duplicate checks. Native pilots
own pickup/towing; native transfer and arrival mechanisms own completion.
Successful rescue must not become a forced outcome.

Player-refusal/silence escalation, guaranteed retries, refusal memory, rescue-wait
timeouts, new retirement rules, longer ATC range and tanker delivery are separate
enhancements. They are not all present in current JSON. If desired, author their
state and eligibility through assets, with necessary native integration, instead
of silently implementing policy inside Harmony callbacks. A condition change
alone also cannot restore an already-unregistered controller.

ShipRequestedHelp's duration is nominally three minutes. However,
CondOwner.UpdateManualDeferred skips ships below Edit, and advancing Clock.Epoch
doesn't itself call Condition.Update. CatchUp can process conditions separately;
this review doesn't establish a reliable shallow retry schedule. Don't invent a
retry controller based on that duration or run whole-person CatchUp merely to
expire this one help flag.

## Validation before implementation/deployment

Extend the graph and existing native/asset SOS tests for expired empty visits,
affordable home returns, legitimate manual/rotor/fusion paths and unaffordable
returns reaching existing player/ATC interactions. Exercise failed sends, negative
station responses and dispatch failure explicitly. Keep acknowledgement distinct
from actual dispatch. No live encounter needs to be manufactured for the routing
tests. The Debug DLL now includes the narrow recovery repair; no running-game
state was modified during implementation.
