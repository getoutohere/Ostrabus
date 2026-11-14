# Disabled AI hulls are repeatedly reacquired as combat threats

Status: implemented for 1.0.1.10; native reproduction and production checks pass.

## Observed cases and owning mechanism

CCRE Navy O-TUH5N (The Terror Of Freedom) repeatedly acquires pirate O-60W
(Deserted Leopard), then clears combat and targets BCRS. The retained trace has
50 alternating state transitions over about 14 game minutes. O-1WFF (Halberd)
also retains that pirate as a target while running a patrol continuation.
Galcon Navy O-P65 (Pizza Time) exhibits the same acquire/drop contradiction
against the newly unregistered pirate O-1C7AL (Angel Of Repose), after firing
six recorded shots. These are distinct from failed surrender-message accounting.

`Ostranauts.Ships.Commands.IsTargetNeutralized.RunCommand` explicitly removes a
combatant when it is marked IsAIShip, has LoadState below Full and has no
AIShipManager registration. When no combatants remain it clears the combat lock
and removes Combatants/InCombat. `AreThreatsNearby.RunCommand` does not apply
that exclusion, so hostility can immediately reintroduce the same hull.
`InitiateAgainstBiggestThreat.RunCommand` then locks it again. Both decisions are
C# command logic; neither delegates this lifecycle distinction to an asset trigger.

`AreThreatsNearbyBlockade.RunCommand` implements a separate detector and never
calls the regular detector's surrender/acquisition helper. Its independent native
reproduction confirms the same fresh acquire/neutralize/reacquire contradiction.
Both detectors now share the lifecycle and pending-combatant eligibility rule.

The native neutralization test also considers current detection, surrender and
captain state. This investigation does not replace those rules with a general
no-controller immunity policy.

## Controller absence is not a damage diagnosis

Native removal paths include:

- `Ostranauts.Ships.DamageSystem.ApplyDamageToCell`: crossing a nav station's
  damaged threshold unregisters the pilot, clears its course, starts drifting
  and disables the transponder antenna. Component removal also unregisters
  on nav station damage.
- `AIShipManager.ValidateCrew`: unregisters when nobody aboard is alive,
  conscious and outside the player's crew. Death and knockout call this path.
- Target-selection/planning failure, exhausted-fuel recovery, explicit RemoveAI,
  docking/retirement, regional cleanup, manual/autopilot control transitions and
  destruction also unregister controllers. Absence alone cannot distinguish them.

`AIShipManager.UnregisterShip` stops maneuver/torch commands, clears navigation
targets, removes controller/queue entries and ordinarily records IsStale. It
retains the hull, crew, faction, IsAIShip flag and existing combat-target reference.

Ordinary nav-console power loss has no direct controller-removal call in the
reviewed paths. The native TIsStationNavOn asset requires installed nav equipment
without IsOff; materialized nav availability and physical operation can change
without unregistering the ship. Shallow NavAIManned instead checks awake crew
and abstracts the physical station. Do not equate absence with power loss, or
claim full and shallow navigation have identical equipment checks.

The inspected saved O-60W has two ItmStationNav items, each with StatDamage=25.
The installed definition's StatDamageMax is 6. Its saved IsStale timestamp and
damage overrides predate this survey. Both navigation units meet the native
disabled criterion; live crew include two dead, two unconscious and two awake.
This supports legitimate combat disablement without reconstructing the exact
unregistration callback. The older saved O-1C7AL nav is undamaged and predates
its latest battle, so it cannot establish that ship's current nav health.

The damage handler's nav flag is set by any newly damaged nav station; its
unregister branch does not check for another functioning console. That is a
separate redundancy concern, not evidence that every missing controller is a
correct physical-disable decision. O-60W's two damaged consoles do not provide
a surviving-backup counterexample; that path would need its own reproduction.

## Implemented repair and boundaries

`NeutralizedThreatPatch` aligns acquisition with the existing native
neutralization decision. It adds an exclusion inside AreThreatsNearby's private
HasSurrenderedToUs helper, where true already means to skip a candidate. This
does not fabricate surrender records. The guard reuses the controller lookup
already performed there: an IsAIShip hull below Full with no controller is
excluded from new acquisition. It queries live registration, so reactivation
restores eligibility without a remembered exclusion or timer.

Existing Combatants are allowed one ordinary evaluation before exclusion.
Without this exception, AreThreatsNearby would return Failure before the combat
tree reaches IsTargetNeutralized, stranding the old combat target. Instead,
native neutralization removes the disabled foe, clears or retargets the lock,
and lets the authored continuation run. Later scans cannot reacquire that hull.
This also releases an old Combatants entry when InCombat was already false.
Other pilot trees retain native cleanup/continuation for existing combatants;
the repair does not introduce a replacement combat controller for them.

Preserve Full/manual ships, active AI controllers and non-AI hostile static
objects such as stations and beacons. A blanket requirement for every target
to have a ship AI would wrongly protect those objects and player-operated ships.
Native PDC missile defense can also execute without a ship controller; that is
separate from active hostile ship acquisition and remains outside this repair.
Edit follows the existing native below-Full boundary; it is not given a new
combat capability or a separate damage policy. Fully materialized ships can
still be operated manually without a ship AI. Power state, backup consoles,
weapons and crew damage remain native; this repair does not revive disabled
equipment or diagnose the reason a pilot was removed.

The regular detector's transpiler validates exactly one native controller lookup
and reuses its result. Blockade's transpiler validates the native null/destroyed/
projectile candidate guard and its foreach continuation, then adds a candidate
check at that boundary. It keeps the original enumeration, faction rules,
range/torch exclusions, warnings and enemy-list publication. Distinct Harmony
labels resolving to the same native continuation are accepted; changed control
flow is rejected. There is no replacement detector or copied signature collection.

Blockade queries registration only for a fresh AI-marked below-Full candidate;
Full/manual, non-AI and already-pending combatants add no registry lookup. The
regular detector adds none beyond its native lookup. Neither path allocates
per contact or maintains a disabled-target cache. Restoring a controller restores
eligibility immediately.
No update hook, actor table, saved field, permanent disabled flag, world sweep
or forced movement is added. Custom builds expose independent bugfix feature
`ai-neutralized-targets`; it needs neither Ceres policy nor the surrender fixes.

Reviewed assets: ItmStationNav/Off/Loose definitions, their damage interaction
loot, TIsStationNavOn, TIsNavStationInstalled and TIsHumanAwake. Their equipment
and crew definitions do not express the controller-registry contradiction.
For Blockade, SHIPBlockadeComing, its IsBlockader/TIsBlockader gate and native
ship-interaction loot were also reviewed and checked against installed JSON.
The restricted-area warning has no reply choices or loot effects; its authored
policy does not express controller lifecycle eligibility and remains unchanged.

## Offline evidence

`Tests/ShipSurrenderWorkCheck ... neutralization` executes the native
AreThreatsNearby, InitiateAgainstBiggestThreat and IsTargetNeutralized methods.
66 assertions reproduce six acquire/drop cycles and pin the matrix of Shallow,
Edit and Full, IsAIShip true/false, and controller present/absent. A Full ship or
non-AI hull remains a combatant without a controller; an unregistered AI-marked
Shallow/Edit hull is acquired then dismissed.

`patched-neutralization` first runs those 66 baseline assertions, then enables
the production patch. The combined 170 checks cover the acquisition matrix,
existing/stale combatants reaching native cleanup, repeated scans without
reacquisition, loss/restoration of a controller, mixed active/disabled foes,
non-AI static targets, Full/manual scope, native surrender exclusion, unchanged
sensor contacts, one controller lookup per contact and rejection of changed IL.

Visible contacts, proximity, hostile faction score, living captain, Unity
identity and controller lookup are fixtures. The actual acquisition, selection,
blackboard edits and neutralization bodies execute unchanged. This is not a
complete battle, electrical failure or nav-damage simulation. Private live/save
captures are retained under ignored obj/SelectedCombatAudit-*.

`Tests/BlockadeTargetHandoffCheck` passes 360 native baseline/production checks.
The first 152 demonstrate the handoff repair and the still-native acquisition
contradiction; the remainder enable the production acquisition transpiler and
verify lifecycle scope, repeated scans, pending cleanup, saved false flags,
controller loss/reactivation, active/mixed combat, warning/reentry behavior,
native unarmed retreat, exact save payloads, bounded lookups and rejected IL drift.
The actual constructor hooks and detector/neutralization bodies execute. An
isolated custom build includes and binds both acquisition patch classes under
the existing `ai-neutralized-targets` feature.
