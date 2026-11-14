# Navigation ownership and collateral audit

Status: audited repair implemented in FlightControllerLifetimePatch.cs. The shipped 1.0.1.10 assembly
reproduces withdrawn-controller execution and abandoned-course force. The
native harness includes 209 mechanism checks, 410 caller/handoff checks and 84
active-invocation continuation checks, with candidates and counterexamples to overly broad
repairs. Another 234 checks execute the actual production patches, including IL
rejection and zero-allocation registry checks. No migration or live recovery is performed.

The completed direct-call audit supports a narrower boundary than the initial
proposal: cancel only an actually registered controller's abandoned plan during
withdrawal, fix the player replacement-course publication order, and prevent stale
normal/SFF dispatch. LowFuelRetreat's specific post-withdrawal return is corrected:
ordinary failure otherwise resumes combat in the same invocation.
Do not add a global course-completion force reset. Native already commands both drives to stop and clears the destination
during removal; completing that cancellation preserves its existing intent.

## Contract

Withdrawing a controller must revoke its future execution and its abandoned
course. It must not remove a newly assigned controller's course or input.
Cancelling a course releases its commanded force; it does not cancel velocity,
legitimate damage-induced spin, gravity, lift or drag. Replanning, manual control,
station keeping, docking and towing retain their respective native owners.

This is bookkeeping, not a change to who fights, retreats, docks, asks for fuel,
travels between planets or transfers passengers. Reviewed asset entries
TIsHumanAwake and TIsValidStationDropOff retain their authored exclusions. Neither
entry owns command scheduling or force cancellation. No JSON policy override is
proposed.

## Confirmed boundaries

1. AIShipManager.UnregisterShip(AIShip) stops current RCS/torch input but leaves
   executable NavData. Playback restores the stopped input. Native expiry can
   then remove the plan while retaining final force. The
   [O-Y38JN investigation](UNREGISTERED-COURSES.md) records both stages live.
2. AIShipQueue.Dequeue builds a detached batch. RunAIQueue does not revalidate
   each controller before invoking RunAI. A preceding actor can unregister the
   next actor; the next actor still executes. AIShipManager.FFWD has the same
   issue with a copied list. Native reproductions execute both manager methods
   with the existing crew execution guard installed and healthy crew aboard.
3. NavModCoursePlot.EngageTrip publishes NavPlan into objSS.NavData before
   unregistering the previous controller and adding Auto. An unconditional plan
   reset in UnregisterShip would erase that newly published player plan when a
   previous controller exists. The newer caller suite executes native EngageTrip,
   Auto construction, saved command loading, registry insertion and playback:
   withdrawal alone fails; moving publication after withdrawal preserves the new
   course. It covers absent, Civilian and Auto predecessors and RCS/torch plans.
   UI rendering, captain selection and audio/log output are fixture boundaries.
4. Unregister callbacks run after native stops. The callback test assigns a new
   course there: pre-withdrawal cancellation retains it, a broad postfix reset
   destroys it. This is a deliberate reentrancy control, not evidence that normal
   gameplay presently registers a replacement there. The native event listener
   found in source is arena removal, not navigation.
5. The private UnregisterShip overload accepts a detached old AIShip and removes
   entries by hull identity, including an already-registered replacement. Native
   tests reproduce replacement registry and force loss; an unqualified cancellation
   prefix also loses its new plan. The implemented withdrawal prefix rejects that
   stale request before any stop or plan reset.
6. LowFuelRetreat unregisters at fuel exhaustion, then returns Failure. Both
   KeepWithinFiringDistance and KeepAtDistance interpret Failure as no retreat
   needed, retain their combat target and proceed to firing/steering in the same
   invocation. Withdrawal cancellation and next-dispatch admission alone cannot
   stop this. Native consumer tests reproduce attempted shooting and steering
   after real withdrawal. Changing only this terminal return to Finished prevents
   fall-through; ordinary no-retreat Failure still permits legitimate combat.
   The existing LowFuelRetreatProgressPatch does not close this terminal path.

The implemented queue guard checks exact controller identity in the existing registry at
the two manager dispatch calls using allocation-free loops. It does not impose
a registry requirement on arbitrary direct RunAI calls or course playback, and
does not maintain another registry. Native and candidate tests cover removal,
same-hull replacement and a replacement remaining independently executable.
This proves the admission mechanism, not every scheduler interleaving or
O-71CT's historical writer.

## All 31 direct native withdrawal calls

The assembly contains 31 direct calls in 27 containing methods, including the
Ship forwarding overload and three developer methods. Every site has a disposition
and evidence level in the machine-readable
[withdrawal review](../Tools/NavigationOwnershipAudit/withdrawal-review.json).
The scanner's --check-review checks the exact assembly hash, every caller and its
call count. Any assembly change marks the review for revalidation. This is coverage
of direct calls, not proof of every runtime interleaving.

The source audit covers crew loss, navigation damage, piracy/boarding termination,
fuel distress, failed planning/patrol, station retirement, regional destruction,
destroyed scheduler entries, reactor safety abort and player Auto transitions.
EngageTrip is the only reviewed direct caller publishing a replacement NavData
before withdrawal. Other player modes construct replacement controllers afterward
and use separately stored references/throttle, not the old plotted course.

Native registration calls were also reviewed: save restore, collision recovery,
bounty/missile creation, station comms and player modes. The existing controller-
identity patch preserves unsaved reuse across regions. PlayerAttachedRegion moves
the same controller between buckets without unregistering; identity admission must
permit that. The caller suite verifies migration retains both execution and plan.
The inspection API's missing-freight cleanup removes orphan records only after the
hull is absent; it is not an active flight handoff.

AIShipManager initialization replaces the registry during world/load setup. It
does not pass through UnregisterShip and must not become a reason to erase restored
plans. Native JSON reconstruction and registration are exercised in the caller
suite; the complete Unity save-loading coroutine is not.

## Already executing callers

A registry check at dispatch cannot stop commands already running inside the
withdrawing invocation. A global exception/unwind or post-frame sweep is not
justified. The continuation audit makes these distinctions:

| Continuation | Disposition |
| --- | --- |
| LowFuelRetreat -> short/long combat steering | Confirmed harmful fall-through. Native tests cover both consumers, terminal/nonterminal conditions and existing retreat-progress absent/present. Correct the terminal return at its owning leaf. All seven native combat-role constructors put these consumers at the end of their attack branches. |
| TargetNewIfNone failure -> StickySequence -> FlyTo/Dock | A counting stand-in proves traversal, not harmful flight. Actual native FlyTo/Dock reject the cleared target without control output. The experimental status change is unnecessary and excluded. |
| HaulShip fuel retirement -> DockCargoAndDespawn | Cleared target makes cargo docking return Failure before transfer/control. Preserve status and native continuation. |
| FlyToPatrol rejected-route retirement -> Dock | Cleared target makes Dock.CanExecute false. Preserve status. |
| RemoveAI / Blank / PirateShakeDown normal execution | Finished stops the relevant selector/sticky step. Auto removal and pirate main shakedown are terminal children. Preserve results. |
| DockAndDespawn callers | Docking/cargo cleanup may finish bookkeeping; Scav/Civilian FFWD may return Hold for the retired queue. No new thrust/course is written there; later stale execution is rejected. |
| Damage / destruction / validation / reactor abort | Preserve remaining bookkeeping, destruction, explicit zero-input writes and damage-induced Drift spin. |
| PiratePilot.FFWD after PirateShakeDown | Source shows later Undock/target/instant-placement calls. Current crew integrity stops the ordinary empty-crew case; unusual crew/faction combinations need separate reachability work. This continuation publishes no new NavData or positive thrust and is outside this acceleration repair. Do not alter it speculatively. |

Retreat tests substitute prior retreat flight, sensor measurements, weapon firing
and final steering output. Native withdrawal, consumer branching and the zero-input
Maneuver stop execute. They establish attempted control after authority loss, not
a complete physical fight or O-71CT's historical writer. The real missing-target
FlyTo/Dock negative control prevents treating every continued leaf as a defect.

## Rejected broad repairs

- Do not gate all NavData playback on AI registration. Native Auto, prediction,
  reconstruction and dock-group movement have different ownership boundaries.
- Do not change ShipSitu.ResetNavData into a universal physical stop. It is a
  plan-only primitive with no-plan callers. Manual input must survive those.
- Do not clear all forces after every behavior command, return code or retarget.
  A leaf can hand control to another leaf during the same call; freshly written
  RCS input must survive.
- Do not put cancellation in an unconditional unregister postfix. Later course
  assignments and callbacks can own replacement input.
- Do not treat lack of a controller as permission to erase momentum, delete a
  hull, retire crew or move it home. Legitimate combat disablement remains valid.
- Do not reset every member or anchor of a docked group. Existing native stops
  already propagate input; extra changes require group-specific evidence.

## All 28 direct native reset callers

Tools/NavigationOwnershipAudit inventories every direct native ResetNavData call
in the installed assembly. This table classifies purpose; it does not claim 28
defects or 28 fully simulated callers. Source review is not live reproduction.

| Containing method | Classification and disposition |
| --- | --- |
| Dodge.RunCommand | Native-tested windup removes plan before replacement RCS; old torch input survives. Terminal/pause branches need exit controls before repair. |
| FlyTo.ResolveInstantly | Instant placement then plan reset. Preserve placement and SFF semantics. |
| FlyTo.RunCommand | Deliberate path-to-RCS/pause handoff. Existing ai-flight-controls covers both reset sites; preserve fresh RCS and fuel accounting. |
| FlyToAutoPilot.ResolveInstantly | Instant placement/reset with player/AI distinction. Preserve Auto/SFF callers. |
| FlyToAutoPilot.RunCommand | Abort/pause resets. Joint physical reactor, fuel correction and Auto tree validation required. |
| FlyToPath.RunCommand | Arrival/failed-plan exits explicitly stop RCS and call TorchOff. Existing shallow TorchOff repair covers the gap; keep ongoing plans. |
| FlyToPatrol.RunCommand | Stopping-distance completion resets after base FlyTo without explicit RCS stop. Steering/next-tree-step reproduction outstanding. |
| HaulShip.RunCommand | Empty-group/target failure resets without stop in this leaf. Reproduce continuation and towing propagation before changing it. |
| KeepWithinFiringDistance.RunCommand | Deliberate combat steering handoff. Existing Maneuver integration releases shallow torch when RCS is written; earlier failure needs caller analysis. |
| TargetEmergencySignal.RunCommand | Native-tested direct target change drops plan but retains torch input; bypasses TargetData.ApplyToShip. Preserve signal policy and next flight leaf. |
| TargetNearestStation.RunCommand | Native-tested no-station failure drops plan but retains controls. Successful target changes can pass existing observer; avoid double-stopping replacement input. |
| TargetNewIfNone.RunCommand | Replan/install boundary. Failure explicitly stops both drives; success publishes DTO plan. Preserve new plan and TripFuelPrediction. |
| Undock.AttemptToUndock | Explicit RCS/torch stop and cancellation. Preserve native push-off and subsequent crew validation. |
| ScavPilot.InstaDock | Failed instant dock clears target/plan. Source-only exit; native docking/SFF policy stays authoritative. |
| DamageSystem.ApplyDamageToCell | Equipment damage invalidates plan. Nav-station loss unregisters then applies Drift, which changes spin. Preserve legitimate damage outcome. |
| AIShipManager.CheckLocalAuthorityScenario | New patrol placement/task assignment. Preserve authority and encounter policy. |
| BeatManager.FactionBattle | Encounter placement/task assignment. Preserve explicit new trajectory. |
| BeatManager.FactionShip | Encounter placement/task assignment; same distinction. |
| BeatManager.Pirate | Encounter placement/task assignment; same distinction. |
| CrewSim.DoLoadGame.MoveNext | Version-gated pre-0.15.0.27 migration, not recurring reset of all current saved courses. No migration change proposed. |
| TestFFWDShipBehavior.ScavInstaDockDerelictTest.MoveNext | Native developer test, not shipping behavior policy. |
| TestFFWDShipBehavior.ScavInstaDockTest.MoveNext | Native developer test, not shipping behavior policy. |
| NavData.TimeAdvance | Native-tested expiry force leak. Also destroyed-owner/reactor-abort exits. Separate scratch/live, replacement identity, normal/SFF and reactor controls required. |
| FlyTo.CanExecute | Proximity/stopping predicate resets plan; source-only cancellation with possible next-leaf continuation. Do not make every predicate a physical stop. |
| FlyToAutoPilot.RequirementsMet | Fuel/reactor invalidation; existing fuel-cache correction required. Shares Auto abort boundary above. |
| GUIStarSystemMonitor.OnResetTarget | Native developer UI; preserve debugging behavior unless explicitly scoped. |
| NavModCoursePlot.Engage | Player disengagement clears target/plan before conditional Auto removal. Distinct from EngageTrip replacement ordering. |
| DamageSystem.DebugAttackComponent | Developer equipment-damage path, not independent proof of gameplay reachability. |

Direct writes and address accesses were also inventoried for ShipSitu.NavData,
vAccIn, vAccRCS and fA. CopyFrom, DockGroup.SyncShipInput/TimeAdvance, placement,
JSON reconstruction and planner waypoint generation are distinct writers.
Address access includes reads; the scanner is not an alias analysis or proof of
complete semantic coverage. AIShip.SetupAI has only three constructor call sites
in this inventory; native role replacement constructs another AIShip.

## Audited implementation boundary

The supported direction keeps caller integration visible:

1. At the private native withdrawal method, require the exact AIShip object to
   remain registered. A stale request must not stop or unregister its replacement.
   Cancel that current controller's plan alongside the existing native stop, before
   notifications. Move EngageTrip's new plan publication after withdrawal and
   before native replacement registration in the same change.
2. Revalidate controller identity at normal/SFF manager dispatch calls. Identity
   by hull/callsign is insufficient after controller replacement.
3. Correct only LowFuelRetreat's post-unregister Failure return to Finished.
   Both callers already respect a non-Failure retreat result. Keep ordinary
   no-retreat Failure, native target selection and normal cleanup unchanged.

Do not include a global NavData.TimeAdvance completion patch. Cancelling the
abandoned route before it can resume prevents the captured expiry leak after
removal; the native removal already stops force if the route expired earlier.
Ordinary completion under a still-current controller is a separate contract and
remains unchanged. Likewise, do not change ResetNavData, globally gate playback on
registration, clear physical reactor settings, or reset dock-group anchors.

Other reset-only leaf findings above remain separate investigations rather than
automatic additions. Existing physical engines retain their native inputs and
fuel accounting; absence of an AI is not a blanket ban on physical/manual thrust.

Existing saves with resurrected force and no surviving owner/course cannot be
repaired by prevention alone. Migration or recovery needs separate evidence and
scope; none is installed here.

## Validation and limits

Tests/UnregisteredCourseCheck passes 209 mechanism, 410 caller and 84 continuation
assertions against the shipped assembly under bundled Mono. Native unregister,
scheduling, FFWD, RunAI, playback/expiry and selected leaves execute. The original
matrix includes existing flight controls; dispatch checks include crew protection.
The caller suite executes player engagement/disengagement, valid and invalid
station-keeping transitions, held-thrust/PDA/debounce controls, retirement with an
empty crew list, terminal leaves, civilian distress, JSON course/Auto restoration,
same-controller regional migration, missing registry and stale private withdrawal.
Four modes compare native, unsafe blanket cancellation, publication-order repair
and exact-registered-withdrawal repair. Candidates are test-only.

Fixtures substitute Unity identity, rotor availability, equipment propagation,
condition storage, short elapsed time, no-station lookup and waypoint ephemeris.
Healthy crew deliberately admit old controllers through the production guard.
Gravity/lift/drag tests preserve input fields, not a planetary/atmospheric flight
simulation. No-station lookup proves the failure path, not its normal frequency.

Candidate runs are retained separately from production runs. The table below is
rerun with the actual linked production patches installed, not the test-only
candidate helpers. production-test-results.json records those runs and verifies
that all five production target methods retain their hooks after each suite:

| Suite | Passing assertions |
| --- | ---: |
| Torch native oracle: physical fuel/reactor, manual isolation, independent SFF clocks, preview/control and reconstructed courses | 402,667 |
| Towing: normal/SFF playback, brace release, grouped offsets, station departure, mass and reconstruction | 343,871 |
| Existing crew/docking lifecycle, patched mode | 52 |
| Flight handoff/fuel tolerance | 213 |
| Regional cleanup, retained controllers and scheduling | 1,524 |
| Controller identity/native registration/save/cargo cleanup | 187 |
| Asset/native SOS and Scav recovery, cumulative | 43 |
| Ceres combat retreat, reserve and navigation chain | 174 |

The full plugin binds 263 patch classes without failures/warnings. Custom builder
self-tests pass 517 checks; isolated controls, controls plus navigation-targets,
and controls plus Ceres compile and bind 7, 17 and 45 classes respectively. The
compiled full DLL and controls-only DLL each pass the same 234 behavior assertions
by loading their actual patch classes into the native host. These reruns are
parity controls, not additional distinct scenarios. No custom build adds frame calls.

Large totals are repeated numerical invariants across scenarios, not hundreds of
thousands of distinct gameplay cases. The suites retain their documented fixture
boundaries; a fixture that substitutes a native call does not prove that call's
behavior. Expected missing Unity internal-call diagnostics occur in bundled Mono
outside the engine; exit codes and assertions passed. No live save was modified.

This establishes a source-audited, offline-tested repair for the reviewed
1.0.1.10/current-Ostrabus paths. It is not a guarantee about arbitrary third-party
mods or every Unity interleaving. Historical damage
does not imply that every unregistered ship should become flyable again.
