# Bounty station departures

`Ostranauts.Systems.BountyManager.MoveTargets` periodically selects an eligible
nonstation bounty and calls `SpawnBountyTarget`. That method boards the same
person into their reserved hull and gives the existing hull to its native pilot
constructor. Supplying a hull bypasses `CreateAIShip`, including its `AIRefuel`
station service. The depletion from the previous outing survives every relaunch.

`TargetNewIfNone.RunCommand` correctly chooses home below the native low-fuel
threshold. It can immediately park the target again, creating a repeatable
station/ship/station loop. Anthony Rosario's B-MC6 exhibited four such cycles;
the inspected hull retained 656.062 kg of 7,493.647 kg capacity (8.755%). The
25% return threshold already rejects that level, independently of delta-v.

`BountyDepartureFuelPatch` replaces the two `AddCrew` call sites inside the bounty
spawn method with a wrapper that retains the native transfer, then invokes
`Ship.AIRefuel` after a successful station-to-shallow-flight-hull embarkation.
This restores the existing departure service rather than overriding low-fuel
policy. It runs before controller construction. The second call site supports
station-only person relocation and is excluded by the same destination guard.

There is no per-frame observer, persistent state, new behavior controller, or
forced departure. Native scheduling, eligibility, role, patrol, custody, placement
and docking remain authoritative. Already-aboard restoration, transfers from
free ships, station destinations, destroyed hulls and non-Shallow destinations
are excluded. Native `AIRefuel` assigns the shallow RCS counter; it does not
repair equipment, fill physical canisters or replenish fusion reactants.

The C# path owns the missing service. The authored `TIsUnderArrestAlive` and
`TIsValidStationDropOff` JSON conditions remain unchanged. This is distinct from
the deferred reserve-hull teleport issue.

An existing parked bounty recovers on its next eligible native relaunch after
the new build is loaded. Fully materialized ships keep native handling.

## Validation and graph omission

See [native harness](../Tests/BountyDepartureCheck/README.md) and the
[bounty graph](BehaviorGraph/Bounty/README.md). The graph now distinguishes the
bounty manager's existing-hull activation from ordinary pilot ship creation,
and explicitly represents the unrepaired immediate-return cycle.

Previously, the graph's departure/retirement coverage described ordinary traffic
and selected role commands. The separate bounty scheduler and its constructor
shortcut were not transition-expanded. Native role coverage did not establish
caller coverage; ordinary departure servicing was incorrectly assumed to apply
to this caller too. Passing those scenarios could not detect the missing step.
The omission was in our model, not a random game outcome. Assembly dependency
fingerprinting detects changes to declared boundaries; it cannot discover an
entry point that was never declared.

Validation: 166 native/asset assertions and 76 graph expectations pass. The
custom builder passes 498 self-tests and 33 compiled/verified/native-bound
profiles, including this feature alone and full builds, with no binding failures
or warnings. The Debug plugin builds cleanly. The new bounty assembly audit
reports zero unresolved selectors, changed sources or revalidation flags.
