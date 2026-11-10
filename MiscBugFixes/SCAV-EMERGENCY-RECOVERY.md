# Scavenger emergency recovery

ScavPilot's ordinary Main visit could never finish with less than 1 kg RCS.
Its low-fuel selection branch then accepted a home target without checking
whether the ship could actually reach it. Station SOS attempts also unregistered
the controller, even when the message could not be sent.

`ScavEmergencyRecovery.cs` repairs those engine boundaries:

- Mark only the Hold belonging to an actual ScavPilot Main visit. Its normal
  timer and requested-help check run unchanged; empty fuel no longer blocks
  completion. Main subsequently runs its original Undock command.
- At low-fuel home selection, preserve accepted native plans. For manual
  fallback, use the one-leg native delta-v test and the same
  rotor/interregional-fusion exceptions as AmIOutOfFuel. A null planned route
  remains compatible with affordable manual navigation. No round-trip reserve
  is imposed on a homeward leg. Missing homes safely reach failed selection.
- Keep scav controllers registered across SOS attempts. The helper's boolean
  means retain the controller, not help dispatched. Failed sends create no help
  flag and do not reset player tension. The caller retains its existing
  64-second pause; no separate retry scheduler is added. Active help suppresses
  further requests using the native condition evaluator.

Other roles retain their RequestHelp behavior. Unrelated Hold instances retain
their fuel gate. No fuel is supplied and no connections are forcibly removed.
Visit markers use weak command keys with no crew, hull or controller references;
they are recreated with each pilot, not saved. No new save data is added.

## Asset authority and limits

Player-first proximity routing, SOS interaction IDs, condition/loot effects,
station replies, dispatch range, availability checks, hauler pickup and transfer
pledges remain native. A delivered request does not guarantee a positive reply,
hauler spawn or arrival. The patch does not implement refusal escalation,
guaranteed timed retries, a tanker, extended range or forced cleanup.

Timed help-condition expiry in shallow simulation remains an explicitly
unresolved native boundary. This patch does not run whole-person CatchUp or
invent a replacement lifetime. Native Full undocking prerequisites still apply.

Offline validation is in [RECOVERY.md](../Tests/ScavFuelDiagnosis/RECOVERY.md).
The behavior graph models visit release, home feasibility and send/lifecycle
differences; detailed pause scheduling and active-help re-entry remain documented
boundaries. Production transpilers require exactly one matching native call.
