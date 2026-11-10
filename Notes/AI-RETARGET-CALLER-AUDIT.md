# Native retargeting: reachable stale-route case

2026-11-09, shipped 1.0.1.10. The investigated setter repair is now implemented
in shared [Navigation](../Navigation/ESCAPE-RETARGET.md), with the production patch
installed by the offline regressions. No deployment, asset edit or live
intervention was performed. The earlier
[scope witness](AI-REGIONAL-SCOPE-AUDIT.md) supplied an active route and a target
change. That established a mechanism, not that every native caller reaches it.
The new caller reproduction below establishes a specific native retreat sequence.

## Verdict

There is a C# defect at `TargetEscapeStation.SetTarget`: it changes scan/situation
targets but leaves the old `NavData`, `targetRegID`, `TargetData` and manual trip
prediction. An already-free ship skips `Undock`; its next native FlyTo accepts
the existing route when the escape station is a body-locked/BO destination.
The native police tree can therefore select an escape station while continuing
to follow the prior flight. This survives the current general retarget observer,
because the direct field assignments bypass `TargetData.ApplyToShip`.
Normal plan expiry can eventually clear that route and permit replanning. This
is proof of wrong-course continuation during retreat, not proof of indefinite
runaway, its frequency in a live save, or a new serialization failure.

This is the same class of stale-route concern recorded by the scope audit, now
with a native caller witness. It is separate from the previously repaired
save/load replacement of target ship objects. It is not evidence that all target
assignments need route invalidation or that every AI role is broken this way.

The implemented repair belongs in shared `Navigation`, at the escape command's
actual assignment boundary: use native `TargetData(target).ApplyToShip`, then
invalidate an old route only for a semantic destination change. Reuse the existing
target key and prediction observer. Retain native escape-station choice, command
results and sequence timing. Do not supply fuel, force arrival, replace threat
policy, impose VORB's route envelope globally or invalidate a route by comparing
its endpoint coordinates to the final destination.

## Executed native caller witness

`Tests/ScavFuelDiagnosis/Retargeting.cs`, mode `retarget`, runs **60 assertions**
under the game's bundled Mono. The actual native PolicePilot constructor builds
the tree; selectors, sequences, AIShip.RunAI and command bodies execute.

1. Main[3] runs FlyTo with a prescribed in-progress stored route toward OLD.
2. Threat perception reports a contact at 74.8 km. Native ThreatResponse runs
   InitiateAgainstBiggestThreat, creating InCombat, Combatants and the combat
   target. This command does not reset the existing plan.
3. The fixture changes installed offensive-weapon availability to absent while
   retaining a living captain and usable propulsion. Native combat selection
   rejects the Fight branch and enters its existing Flee sequence.
4. Native TargetEscapeStation chooses OKLG, 149.6 km in the opposite direction.
   The previous Main remains at its existing FlyTo index.
5. On the following AI call, native Undock.CanExecute is false because the ship
   is already free. Actual FlyTo and FlyToPath run and retain the old plan.
6. Native NavData.TimeAdvance moves the ship 74.8 km toward OLD, away from its
   selected escape station. Old target metadata and TripFuelPrediction remain.

This is run first without production patches and then with only the prior
RetargetFuelPatch installed. Both produce the failure. A third run installs a
**production EscapeTargetPatch**: obsolete state clears,
the native target fields agree, equivalent-station reapplication preserves its
new plan, and native retreat advances a supplied replacement leg toward OKLG.
Native failed escape selection still fails and preserves the ongoing flight.
The former test-only prototype has been removed. Further checks exercise the
shared escape leaf for all four affected roles, actual Navy/Blockade native
retreat without this hook, and stable-assignment allocation.

Fixture boundaries are explicit: prior route/time/fuel, world stations, perception,
healthy hull, installed offensive-weapon availability, living captain, zero
decoys and collision result. Sensor activation and Unity maneuvering are stubbed;
the station's ephemeris is held fixed. The test does not simulate the weapon
destruction, manufacture the escape target, replace the retreat/flight commands,
or claim that a physical tank/trajectory planner or a live encounter was tested.
Native target selection chooses from the supplied region. Distances are within
the native threat proximity limit; threat visibility/hostility is an input.

## Caller distinctions and controls

| Native path | Route ownership/cleanup | Evidence and disposition |
| --- | --- | --- |
| TargetNewIfNone, targetless branch | Reset before choosing; installs the newly generated plan after ApplyToShip | Executed control retains that new plan. Blanket after-call reset would discard legitimate work. |
| TargetNewIfNone, existing scan/situation | Refreshes situation from the selected ship; does not always reset | Not equivalent to choosing a genuinely new goal. Moving-contact manual flight is separately controlled. |
| TargetNearestStation | Unconditional reset before choosing | Executed fallback selection clears the old plan. No extra patch needed for that path. |
| TargetEmergencySignal | Direct special-purpose target plus explicit reset | Executed control clears plan and combat target. Do not infer uniform metadata semantics for coordinate-only goals. |
| TargetEscapeStation | Native direct assignment lacks cleanup; production prefix now repairs the handoff | Confirmed whole police-tree witness, plus Scav/Civilian/Pirate/Police escape-leaf regressions. |
| Navy / Blockade / LowFuelRetreat | Uses TargetNearestStation, which already resets the route | Source audit plus executed Navy/Blockade retreat controls; no new escape hook involved. |
| Free moving-contact FlyTo | Parent takes manual branch and resets plotted NavData | Executed control confirms cleanup before a steering stand-in. A direct setter omission need not survive this consumer. |
| GetTarget / FlyToPath fallback | Acquires target when missing; FlyToPath's existing-plan return precedes fallback | Source review; not a general retarget validator. |
| HaulShip empty/failure | Explicit route/target cleanup; towing owns group steering separately | Source review and existing hauler regressions. Do not invalidate an active group trip indiscriminately. |
| Retriever/deployer/cargo FFWD | Existing-plan early returns or native docking/release cleanup precede ordinary target updates | Source review; does not justify a universal route reset. Existing SFF fixes remain separate. |
| AIShipManager.CheckLocalAuthorityScenario | Patrol-waypoint branch explicitly resets; direct inspection scan-target branch follows native comms | Source review; a scan contact is not by itself a newly committed navigation goal. |
| TargetATC / TargetPlayer / combat standoff setters | Other direct setters with different consumers/entry states | Catalogued, not declared additional reachable defects by this test. TargetPlayer has a CMDTargetPlayer loot definition but no reference to that loot was found elsewhere in the installed data. |
| Cargo intermediate flight | NavData endpoint may be a departure, detour or approach subleg rather than final station | Actual cargo Main/FlyTo/FlyToPath control retains the supplied intermediate leg. Native PlanTrip4's departure/docking-point recursion explains the distinction. |
| Player AutoPilot | Shares FlyToPath; user course planning and selected-contact binding have separate ownership | Actual AutoPilot/FlyToAutoPilot/FlyToPath control retains the supplied course under the escape-only repair. No Unity course-slider/torch-burn test is claimed. |
| NavData SFF completion | May rewrite situation to extrapolated endpoint, then resets its completed plan | Source review; target mutation is not always a new mission decision. |

The last two controls are also why a universal consumer check of
`plan.Destination == ship.shipSituTarget` would be wrong. Semantic goal identity
and a planned segment's endpoint are separate concepts.

## Asset ownership

PolicePilot, ScavPilot, CivilianPilot and PiratePilot construct the relevant
escape sequences directly in C#. TargetEscapeStation itself selects among native
regional stations using combatant geometry; this is not a JSON trigger/pledge
that needs a new behavioral rule. Inspection of installed loot/condition-trigger
definitions found the separate CMDTargetPlayer command loot, not an asset-owned
replacement for escape-route cleanup. Repairing the incomplete C# reassignment
preserves the existing authored behavior rather than duplicating it in a new AI
controller or inventing an asset policy.

## Reproduce and graph scope

```powershell
dotnet build Tests/ScavFuelDiagnosis -c Release --nologo
dotnet run --project Tests/SaveJsonCompatibility/Host -c Release --no-build -- Tests/ScavFuelDiagnosis/bin/Release/netstandard2.1/ScavFuelDiagnosis.dll retarget
```

Build the Debug plugin first if its current target observer/key is not already
available. The host runs sequentially with other bundled-Mono suites.

Existing graph slices keep complete combat/retreat out of their expanded
transitions. The repair is recorded as an unexpanded dependency with native
evidence and assembly bindings, rather than silently adding a universal retarget
rule to those slices. No live reload was needed to establish the control-flow
failure or its production-patch regression.
