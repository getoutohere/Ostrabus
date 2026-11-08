# Crewless civilian AI: O-KFMFL investigation

Observed 2026-11-07 on 1.0.1.10 near Mercury. Investigation used read-only live
inspection and offline saves. Narrow lifecycle fixes are now implemented in
`MiscBugFixes/AIShipLifecyclePatches.cs`; no live state repair or diagnostic hooks
were installed during the investigation.

## Established evidence

- O-KFMFL, Another One Of Life's Little Tests, is HQCH-owned, Civilian AI, Used
  condition, targeting HQCH-SEC2. It has no crew profiles. Its cached AI captain is
  CO-O-KFMFL, the hull CO, with no PersonSpec.
- The latest autosave's ship log records repeated real pilot assignments and
  transfers over the hull's reuse history. The most recent assignment is Hope
  Sanders from HQCH, followed by docking/undocking O-JVV. The hull archive has no
  aCrew entries and no Hope Sanders CO or layout entry.
- The same archive's HQCH-SEC2 hull explicitly includes Hope Sanders in aCrew and
  aCOs. Thus the empty ship/pilot separation already existed before this game load;
  this is not evidence that the current loading operation newly misplaced her.
- Hope Sanders is alive, materialized on HQCH-SEC2 and running WanderSoon. Player.log
  records adding her there during loading. Her death does not explain this case.
- The ship has persistent RCS acceleration about 7.58 m/s², with torch off. Two live
  velocity samples 48.7 game seconds apart yielded acceleration components about
  (+7.12, -2.45) m/s², consistent with active thrust plus other accelerations.
- It receded from the station at 40–46 km/s during inspection, with no stored nav
  plan, while its AI cycled TargetNewIfNone/FlyTo/no active command. Reported RCS
  fuel stayed 1,322.700029 kg, about 88% capacity.

## Relevant native code

- Comms.GetCaptain starts with the hull CO and returns false when no people exist.
  BaseCharacter.AssignCaptain stores that output without requiring success.
- CivilianPilot.CreateAIShip normally assigns a real pilot. Constructing the pilot
  around an existing ship bypasses CreateAIShip and calls AssignCaptain directly.
- AIShipManager.ValidateCrew(Ship) unregisters AI when no alive, conscious, non-player
  crew are present. UnregisterShip stops maneuver/thrust and clears targets. It has
  not protected this registered crewless vessel in the observed state.
- CrewSim.DockAndDespawn normally logs crew transfers, hides the hull and unregisters
  AI. The archived hull is visible/AI-flagged, with no despawn-station marker. There
  is no final Hope transfer/despawn entry in its archived log.
- FlyTo.HandleRCSFuel/ApplyTripFuel charges shallow fuel as progress toward the target
  increases, not directly from accumulated thrust time. Receding motion can therefore
  coexist with unchanged fuel. This is distinct from the missing pilot's history.

## Earlier-save comparison and identified crew-transfer mechanism

The `arrive` hull snapshot is healthy reserve state: hidden, despawned at HQCH,
no crew, no target and no RCS acceleration. Its previous pilot Jeremiah Douglas
had transferred to HQCH. An empty reserve hull is normal and distinct from the
later visible/actively piloted crewless hull.

After that snapshot, the hull is reused for several pilots. `adventure` and `next`
both save it active with Isaiah Trujillo aboard. Later ship logs record his normal
return, then Max Montoya's sortie, then Hope's assignment about 5.3 game hours after
`arrive`. At 6.32 hours it docks/undocks O-JVV. By the later autosave, around 6.87
hours after `arrive`, it is active and empty.

Hope's personal messages and O-JVV's ship log identify her removal:

- Epoch 65632375306.5815: O-KFMFL and O-JVV log docking with each other. O-JVV also
  logs Hope Sanders and Alondra Huynh transferred to HQCH, then Docked & Despawned
  with HQCH. Hope's personal log records arriving aboard O-JVV, despite her prior
  assignment to O-KFMFL.
- Epoch 65632375336.9557: Hope records transfer from HQCH to O-JVV; O-JVV's ship log
  records assigning her approximately 0.033 seconds later.
- Epoch 65632375337.2895: the two hulls log undocking.
- Epoch 65632376223.0944: Hope arrives at HQCH-SEC2 aboard O-JVV and is transferred
  there. Her live and saved placement both agree with this destination.

Native `CrewSim.DockAndDespawn` gathers `shipAI.GetPeople(bAllowDocked: true)` and
transfers/unclaims those people, but hides/unregisters only `shipAI`. Native
`Ship.GetPeople(true)` explicitly includes people on directly connected hulls.
This explains O-JVV taking O-KFMFL's pilot along during station-return cleanup,
leaving the other hull active and empty. It matches both ships' logs and the pilot's
own messages; no death or speculative save-load relocation is required.

There is a relevant validation gap: shallow `Undock.AttemptToUndock` directly calls
`Ship.Undock` and pushback, whereas the materialized CrewSim undock path contains
`AIShipManager.ValidateCrew`. The native undock command does not apply that crew
validation in its shallow branch.

The particular caller initiating O-JVV's station cleanup is not preserved as a call
stack in these saves. Determine intended connected-hull cleanup semantics before
patching; do not globally exclude docked crews without considering legitimate
towing/group returns. No replacement pilot, pruning or repair has been applied.

## Timing / instant-docking investigation

The ordering may be the primary defect rather than the general crew collection
contract. Native Dock.RunCommand rejects an already-docked ship before attempting
another docking action. Dock.ResolveInstantly does not perform that same check.
CivilianPilot.FFWD calls InstaDock (and thus Dock.ResolveInstantly) when its current
FlyTo/Dock command has no NavData, retaining the independently chosen station target.
This provides a concrete route by which one ship's station-arrival shortcut can
execute after another ship has attached to it, then station cleanup collects both
crews. The save logs establish the same-timestamp rendezvous and cleanup, but do not
prove the FFWD call path was the caller in this particular incident.

Prefer validating this command-ordering hole offline and preserving connected-crew
transfer semantics where a legitimate group return needs them. A fixed delay alone
does not establish that it is safe to finish arrival; recheck actual docking state
when the arrival action executes. Keep the hull visible if genuinely left crewless;
crew validation must stop control/thrust, not erase it.

## Save/load and ATC/reuse boundary audit

A streaming inspection of the main world JSON (not just hull archives) found:

| Save | Saved ATC | O-KFMFL AI record | Hull state |
| --- | --- | --- | --- |
| arrive | HQCH | Absent | Hidden reserve at HQCH, empty, no thrust |
| next | HQCH | FlyTo, region/home HQCH | Active, Isaiah Trujillo aboard |
| autosave_2_next | HQCH | FlyTo, region/home HQCH | Active, no crew, persistent RCS thrust |

O-JVV is registered in arrive/next, and absent from the later saved AI list after
its successful returns. These records are internally inconsistent for O-KFMFL
before the current restart: load does not need to invent a pilot transfer to
reproduce it. Previous load/transition involvement is not thereby ruled out.

Native lifecycle observations:

- AIShipManager.ApplySaveData calls AddAIToShip(..., gameLoad:true), restores the
  blackboard and behavior-tree state, and registers the AI without ValidateCrew.
  BaseCharacter.AssignCaptain can accept the hull-CO fallback when no person exists.
- ATC RegionCleanup uses CrewCleanup with GetPeople(false), then detaches/destroys
  retiring AI hulls. It does not use DockAndDespawn's inclusive crew collection.
  The unique Docked & Despawned log and ship-specific personal messages identify
  the latter operation in this incident; do not conflate the two cleanup paths.
- AIShip.FindShipForFaction considers a hidden non-station, non-bounty hull reusable.
  It does not reject a hull still docked to another ship. DockAndDespawn does not
  detach the pair before hiding its hull, and ResetShipState does not detach it
  before reuse. The logs show actual reuse of O-JVV before separation, not just a
  theoretical missing guard.
- Checking Init alone suggests stale queue risk because the readonly queue is not
  explicitly cleared there. However Ship.Destroy unregisters AI during old-world
  teardown, and queue Fill clears both queues. Do not claim a demonstrated
  cross-load stale-controller bug without testing those complete paths.

The existing journey journal ends before the Hope/O-JVV incident, so it cannot
correlate that operation with a particular SFF/ATC transition. A lifecycle-correct
fix should address retirement/reuse ordering and controller/crew validity at stable
boundaries, preserving genuine transfers and visible hulls. No fixed delay,
unconditional crew exclusion, or claim that the instant-dock shortcut alone explains
the historical event is justified by the archived logs alone.

## Offline reproduction and implemented prevention

`Tests/AIShipLifecycleCheck` executes the shipped methods under bundled Mono.
With two connected shallow hulls and one pilot each, it demonstrates:

1. Native normal docking rejects a stale station target while already attached.
2. Native instant docking instead reaches station cleanup, whose actual crew
   enumeration contains both pilots. The fixture replaces the final station
   cleanup boundary to observe the collected party without needing a Unity scene.
3. Native CivilianPilot.FFWD reaches that same path from FlyTo with no NavData.
4. Native AIShip.FindShipForFaction selects a hidden hull that is still attached.

This reproduces a concrete route to the observed sequence; it does not recover an
unrecorded historical call stack. Save/ATC timing remains relevant when reasoning
about interrupted operations, but neither a new JSON format nor a periodic cleanup
sweep is needed to fix these command/lifecycle preconditions.

Implemented fixes:

- `InstantDockStatePatch`: apply normal docking's already-attached cleanup/failure
  transition to instant docking. Clear the obsolete target so the native behavior
  tree can finish the rendezvous, hold and undock, rather than repeatedly attempting
  the old station arrival. Independent arrivals still complete normally.
- `DetachedReserveHullPatch`: add a detached-state requirement to the existing
  hidden-hull selection predicate. Keep native faction filtering, preferred station
  and random selection, without moving or changing the hidden hull. It becomes
  reusable after separation.
- Validate human-operated AI on AddAIToDict, RunAI, FFWD and shallow undocking.
  Require an alive, conscious, non-player crew member actually belonging to that
  hull, matching native crew validation policy. Do not accept a stale roster entry
  whose CO now belongs to a station or another ship.
- If nobody can operate it, use native unregistration, clear remaining RCS/input
  acceleration and NavData, and resume ordinary gravity. Keep visibility, hull,
  position and velocity intact. Remove only the newly introduced IsStale disposal
  marker; preserve pre-existing disposal policy. No arbitrary replacement pilot,
  forced station arrival or hull deletion.
- Auto, Station, Missile and Arena controllers are excluded. A surviving relief
  crew member can keep a vessel operational. Legitimate explicit connected-party
  station cleanup retains its native crew collection behavior.

The guard scans existing PersonSpec entries without allocating a GetPeople result
list on scheduled AI invocations. It has no new Unity Update hook or retained ship
registry. Factory filtering runs within native selection; registration/undocking
checks occur at existing lifecycle boundaries.

33 offline checks pass, including before/after native reproductions, ordinary
station arrivals, reserve reuse after detachment, controller registration, loss of
crew after registration, SFF execution suppression, surviving relief crew, exempt
controller types, visible-hull preservation and disposal-marker handling. Graphics,
physics controls and condition writes use documented boundary stand-ins in the
fixture; a complete Unity save load is not claimed by those tests.

## Cause versus containment: caller and continuation audit

The instant-docking change fixes a command precondition defect, rather than
detecting and repairing a missing pilot afterward. A passive recipient of docking
can still have its own station-return FlyTo/Dock leaf as AIShip._activeCommand.
AIShip.FFWD dispatches that saved/current leaf to CivilianPilot.FFWD without
rerunning its CanExecute check. If there is no NavData, the pilot invokes
Dock.ResolveInstantly. That method bypasses the already-docked rejection present
in Dock.RunCommand and reaches the station branch of AttemptToDock, which calls
DockAndDespawn. The latter gathers both directly connected crews but retires only
the caller's hull. A normal incoming rendezvous therefore exposes an invalid
station-arrival transition; station cleanup need not have been explicitly queued
by another system or caused by a newly invented save-load relocation.

The additional native-controller checks set an active station-return FlyTo on an
attached hull and invoke AIShip.FFWD itself. Unpatched, this collects both pilots
for station cleanup. Patched, it clears the obsolete target without collecting
either crew. The native queued continuation drains, and the native sticky sequence
can proceed to rendezvous Hold, with ordinary Undock still eligible. It does not
require a new replacement behavior tree or a timer-based repair sweep. Engine-only
authority/tow-brace queries are fixture boundaries; scheduling and command logic
are shipped methods.

All direct DockAndDespawn call sites were reviewed. DockCargoAndDespawn and
SurrenderArrestAndDespawn have explicit connected-hull handling, including
detaching/processing companion hulls. Deployment failure and dispatch failure
callers return freshly created ships. TargetNewIfNone's no-target fallback and
ATC cleanup are separate paths. None is evidence that a civilian rendezvous should
transfer the neighboring ship's pilot and leave that ship active. The patch does
not change those explicit group-return or rescue contracts.

The detached reserve predicate independently prevents incomplete retirement from
being reused as a new sortie. Crewless-controller validation is containment for
already-corrupt saves and other legitimate/illegitimate crew-loss events; it is
not presented as the fix for Hope leaving her hull. The archived logs do not retain
the historical caller's stack, so the reproduced SFF path is a demonstrated cause
of this failure class, not proof of the exact historical call stack.

## Civilian rendezvous versus intentional NPC travel

The game does redistribute actual NPCs. Do not describe all civilian traffic as
cosmetic or assume that stopping a ship must destroy its occupants.

- CivilianPilot.CreateAIShip calls AIShip.AddCrew with regional reuse enabled.
  AddCrew first tries an existing eligible resident at the preferred POI, then
  eligible regional stations (with population safeguards), and creates a new NPC
  when necessary. Station arrival through DockAndDespawn transfers surviving,
  eligible occupants into the destination station. Consequently even a one-pilot
  civilian sortie can move an existing person between stations.
- AIShipManager.SpawnPassShip is a separate population-balancing transport path.
  It selects an overcrowded station and an underpopulated destination, creates a
  HaulerCargo controller named PASS Transport, and moves up to four qualifying
  NPCs aboard in addition to its pilot. The destination is assigned explicitly;
  HaulerCargoPilot finishes through DockCargoAndDespawn. These are not the surveyed
  one-person Civilian controllers.
- AIShipManager.HandleTransits uses the authored JsonTransit network and eligible
  population. For materialized origins it assigns EmbarkCommand; otherwise it
  directly transfers the NPC. The shipped Mercury transit entries connect HQCH
  and HQCH_DET, not arbitrary O-* hulls or HQCH-SEC/HQCH-NAV markers.
- PledgeEmbark can walk to an accessible materialized destination, or use a transit
  kiosk and JsonTransit connection. PledgeDisembark walks off the specified owner's
  ships. Docking supplies physical access when materialized, but neither pledge is
  assigned by the civilian rendezvous loop. Requested ferry service is another
  separate, explicit rider/destination mechanism.

The civilian main loop contains only Undock, TargetNewIfNone, FlyTo, Dock and Hold.
Its non-station Dock branch connects hulls and clears the target; Hold times the
visit. Neither selects passengers, assigns an embark/disembark pledge, matches a
travel destination, or transfers crew between shallow hulls. Ship.OnDock's native
listener search found the return tutorial, not a passenger-exchange handler. The
police ShakeDown command does have explicit officer movement across attached hulls,
but that is a different controller/command path.

This supports preserving legitimate station travel, transit, rescue, cargo and
materialized walking behavior while rejecting a retired reserve hull as a generic
civilian destination. Hidden alone is not a universal synonym for retired: any
future target-selection fix should distinguish non-station reserve hulls from
hidden station/interior entities used by legitimate systems. No such additional
selection patch was implemented as part of this source audit. No purposeful
passenger handoff was found that explains targeting a hidden, crewless reserve.
