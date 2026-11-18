# Docked visits, orbital attachment and gravity-ignore ownership

Implemented and tested offline November 16, 2026 against 1.1.0.0. The full Debug
DLL is built. **Not deployed or live-playtested; no live ship recovery applied.**

## Undocking reference overwrite, November 16 follow-up

The initial repair missed a native undocking transition. `Ship.Undock(Ship)`
calls `GetNearestBO(..., bIncludePlaceholders: false)` and then
`ShipSitu.TryRelockToBO` for an unmanned derelict, even if it still has the
attachment retained during docking. A station-relative wreck can therefore
leave its passive visit with `bBOLocked == true` but a different reference.
The old Phobos fixture supplied Phobos as both the original reference and the
nearest body; checking the final lock flag and reference against that fixture
could not expose the overwrite.

Live/save evidence: E-XMH is locked to **HQCH_DET** and docked to civilian O-S04
in `arrive`, then locked to **Mercury** in `pathcheck`; the current live snapshot
retains Mercury during another civilian visit. V-6BM, J-ZEK and H-QS10 also have
Mercury references. This confirms the saved reference change, not the exact
historical method-call arguments. A compiled-native-method A/B reproduces the
undocking transition with deliberately distinct original/candidate bodies.

`PassiveUndockAttachmentPatch` replaces only Undock's derelict relock call.
If a non-orbit-placeholder wreck is still body-locked and its reference exists,
the helper reports the existing attachment as retained, leaving its reference,
offset, angular rate and phase untouched. Applied control/course release already
clears the lock; those hulls and missing-reference cases keep native candidate
selection and the native speed limit. The existing VORB candidate-selection
patch stays authoritative for free hulls. No global relock override, new saved
marker, attachment history or repair timer is introduced.

The reviewed HQCH environment map 100 already requests body-locked wrecks;
native spawning honors the body's authored reference. JSON does not define the
undocking reassignment, so this repair belongs in C#. Existing misplaced wrecks
are **not** automatically moved or relocked: their earlier reference is not
reliably recoverable from current state alone.

`Tools/DockingOrbitStudy/UndockReferenceChecks.cs` adds **385 assertions**:
the uncorrected control replaces HQCH/HQCH_DET with Mercury and Phobos with Mars;
the repaired control preserves the original reference. Both docking directions,
either departure actor, fixed/circular attachments, retained offset/rate/phase,
released excessive-speed rejection and missing-reference fallback are covered.
The complete suite now passes **834 checks**, including the new hook in the
existing real-ephemeris, native hauler, SFF/PASS and collateral compositions.
Body caches and nearest-body selection are explicit fixtures in this A/B; it
does not claim a full historical Unity replay. Private read-only snapshots and
test output are under `obj/SffStall-20261116/attachment-*` and
`undock-reference-checks.txt`.

The rebuilt full DLL binds all 302 patch classes without failures or warnings.
Standalone attachment and towing packages compile, retain full-build ordering
and bind successfully; the custom builder passes 596 self-checks. The Traffic
model passes 571 checks and its 101 reviewed source/dependency groups and 20
asset entries audit cleanly. The correction is built for deployment and remains
pending live playtest.

**Current verdict:** repair the lifetime of an existing authored body attachment
during a passive visit, with explicit release for applied control or course
execution. Do not create attachments for arbitrary AI holds, remove AI gravity
suppression globally, or force every rebuilt group to gravity-on. The extended
native-ephemeris study demonstrates passive attachment preservation with the
native Hold exemption retained. Production-DLL checks cover the required
control/towing release; preserving the lock alone traps legitimate departure.

The individual-to-group gravity flag transfer is reproduced, but calling that
copy intrinsically defective overstates the evidence: the group may be carrying
an intentional AI wait. What is established is that passive docking clears the
authored attachment, and a later rebuilt group's gravity-free wait cannot follow
the moon. Changing only the gravity flag fixes one Phobos setup but is unsafe as
a general rule. The production repair preserves the existing attachment instead.

## Complete native flag audit

All native true assignments are in four C# command methods; no installed data
JSON sets `bIgnoreGrav: true`. Constructor/copy/save methods also carry the bit.
`ShipSitu.TimeAdvance` consumes it only in the free numerical branch. Body and
orbit locking have their own earlier branch and do not need this bit.

| Writer | True-assignment condition | Purpose in the current code |
| --- | --- | --- |
| `Ostranauts.Ships.Commands.Hold.RunCommand` | While its wait timer is running | Gravity-free waiting; timer/target logic is separate. |
| `Ostranauts.Ships.Commands.FlyTo.RunCommand` | AI pause or docked without secured braces | Stop controls while waiting; gravity is disabled as an additional side effect. |
| `Ostranauts.Ships.Commands.HaulShip.RunCommand` | Hauler pause | Same waiting shortcut, after native wreck-release/brace setup. |
| `Ostranauts.Ships.Commands.Dodge.RunCommand` | During its brief dodge window | Suppress gravity while the native dodge maneuver runs. |

AI reset/lifecycle paths set the bit false; none of these are needed to evaluate
asset triggers or represent an authored new boneyard. Disabling it is not a
replacement for active hover/steering, nor does it itself pick a target.

Our existing `Navigation/OrbitalWaiting.cs` already restores gravity for some
unpowered free AI holds and scavenger selection waits, but explicitly excludes
docked ships, combat, active forces, player ownership, locks and atmospheres.
It therefore does not cover the demonstrated group leak or the other native
writers. `OrbitalSimulation.Correct`, `CoastIntegration` and `SffSimulation`
respect the bit, so retaining it also excludes the affected motion from our
compensating mechanisms. Normal-time higher-accuracy `CoastIntegration` excludes
docking groups independently; shared gravity correction still applies to them.

## Physical A/B result and narrower repair direction

The extended study uses captured Sol/Mars/Phobos orbital parameters, actual
`BodyOrbit.UpdateTime`, `StarSystem.UpdateDockingGroups`, the native gravity
selector and the current production `OrbitalGravityPatch`. A passive two-hull
group starts 70 km from Phobos with the moon's velocity. No lock-preservation
prototype is installed during these comparisons.

| Ten-minute coast | 0.5 s steps | 5.333 s steps | Native relock |
| --- | ---: | ---: | --- |
| Shared gravity-ignore flag true | 285.925 m/s mismatch | 285.925 m/s mismatch | Rejected |
| Shared gravity-ignore flag false | 0.178 m/s mismatch | 4.128 m/s mismatch | Accepted |

The gravity-disabled control has exactly unchanged velocity: there is no
invented thrust impulse. The active-gravity group follows the moon closely
enough for the existing relock gate. Actual SFF preview excludes the flagged
group and admits the unflagged group to its existing free-coast model.

Implementation boundary following the guidance and attachment audits:

- Do not remove the four command exemptions globally or normalize every free
  group to gravity-on. A successful Phobos coast is not proof of orbital safety
  at other positions and velocities.
- Do not simply preserve an old anchor's ignore flag through reconstruction:
  that can still turn an intentional wait into unsafe free fall. The copy is an
  observed transition, not sufficient proof that overriding it is the repair.
- Preserve an existing non-station body attachment across passive docking and
  tether reconstruction. Release it for actual applied movement or an authorized
  engaged course, retaining the initiating input and current group velocity.
  Keep native conflicting-anchor precedence, station/ground/mooring constraints,
  native undock/relock eligibility and brace requirements.
- Do not attach previously free ships, repair already-drifting hulls by snapping,
  rewrite FlyTo/HaulShip steering, or replace native visit/assistance decisions.
- The existing `OrbitalWaiting.Coast` guards do not test free-trajectory safety.
  Its undocked Hold behavior also warrants review before extending it.

Assets already express the boneyard attachments; docking clears them in C#.
Moving attachment release from a passive link change to actual movement is an
explicit simulation rule and must be documented as such; a spawn-time JSON flag
alone does not prove the developers intended that exact release rule. The study
has not deployed a repair or changed a live flag.

## Passive attachment comparison with real ephemerides

`AttachmentPhysics.cs` adds 84 assertions across 12 combinations: both docking
directions, static or circular native body attachment, and 0.5 s / 5.333 s steps
over ten minutes or one 7,200 s step. Actual native Dock, Hold, MoorTo, UnMoorFrom,
group integration and Undock run with the compiled production docking patch.
The locked member is also advanced as in `StarSystem.UpdateShip`, before the
native group pass. Sol/Mars/Phobos use the captured ephemerides.

All combinations retain the native Hold exemption, keep the passive wreck on
the reference attached trajectory (zero reported endpoint error), and leave the
departing visitor unlocked while the wreck retains native attachment. The
7,200 s case checks the native physical boundary, not GUI SFF behavior-tree
catchup, fuel accounting or an assertion that the visitor's timer lasts two hours.
The existing SFF capture check separately confirms fixed groups defer to native
following; prior release checks exercise current towing/completion patches.

The completed harness passes 449 assertions against the production DLL. It also
executes retrieving-pilot HaulShip steering, paused-haul saved-state/group
restoration, and civilian Hold/Undock through the actual SffAdvance GUI and PASS
adapters with real ephemerides. Both adapters advance two hours with a native
locked group, no motion fallback, and subsequent ordinary native AI turns finish
the wait and undock. Calendar/scene/economy, external threat/help state and angular
steering are fixture boundaries. This is not a full in-game acceptance test.

Native HaulShip clears member body locks before checking its pause deadline.
Retaining only the group lock would therefore create a save/reload regression.
The production haul transpiler defers that member write while the soft group
attachment is retained; actual applied steering releases anchor and members
together. Native environment metadata cleanup, brace setup, fuel gates and target
selection still execute.

## Active guidance and unsafe Hold counterexample

`FlyTo.RunCommand` and `HaulShip.RunCommand` already clear `bIgnoreGrav` during
active navigation; their pauses set it true. Their manual steering calls
`AIShipManager.AIIntercept2`, which derives RCS inputs from displacement, relative
velocity, a capped acceleration and stopping distance. It does not read gravity
or target acceleration. `Ship.Maneuver` converts those inputs to propulsion; it
does not add gravity compensation. Feedback may correct velocity disturbances,
but these methods do not establish gravity-aware trajectory safety.

`FlyToPath.PlanTrip4` queries gravity to identify the nearby body/atmosphere;
`PlotBurnLoop` constructs kinematic position/velocity waypoints without gravity
integration. While playback is active, `ShipSitu.TimeAdvance` returns through
`NavData.TimeAdvance` before the free-motion branch. Enabling numerical gravity
does not turn that planner into an orbital planner, and adding it during playback
would change a separate mechanism. Our SFF `SffFlight.Steering` explicitly uses
target acceleration minus free acceleration; that is not the native manual
controller. The existing avoidance repair bounds a particular evasion maneuver,
not idle Hold stationkeeping.

`Hold.RunCommand` stops propulsion. `Hold.CanExecute` can accept a small velocity
difference from its target without validating a free orbit. `HoldSafety.cs`
constructs a state 300 km above Mars, above all native atmosphere layers, with
Mars's velocity. Actual native Hold accepts it. Native group integration with
the current production gravity correction then gives:

| Constructed ten-minute Hold | Result |
| --- | --- |
| Gravity ignored | Still approximately 300 km above the surface. |
| Gravity enabled | Gravity-only trajectory crosses the surface after 435.5 s. |

This is a controlled counterexample, not an observed AI incident or proof that a
full behavior tree naturally selects this precise state. Aerodynamic response,
collision resolution and subsequent AI decisions are not modeled in that
counterexample. It proves that Hold acceptance and being initially outside an
atmosphere are insufficient criteria for enabling gravity safely. The earlier
Phobos A/B started with the moon's velocity and demonstrates only that setup.
That stage passed 104 checks, including 14 new Hold-safety assertions.

## Why the velocity mismatch grew

`Hold.RunCommand` sets its own ship's `bIgnoreGrav` flag. This initially does not
change the existing group anchor. Native `Ship.UnMoorFrom` rebuilds the remaining
pair by adding the civilian first; `DockGroup.UpdateAnchor` chooses that member
and `Anchor.CopyFrom(..., bKinematicsOnly:false)` copies its gravity-ignore flag.
The wreck's own flag can remain false while the shared movement ignores gravity.

`StarSystem.UpdateDockingGroups` still computes and supplies gravity, but
`ShipSitu.TimeAdvance` discards it when that copied flag is true. The large
speed is relative to Phobos: the group's velocity stays constant while the
moon continues around Mars. Two captured live samples 85.63 game seconds apart
have identical group velocity; detached ephemeris reconstruction gives roughly
110 and 150 m/s mismatch at those times. The approximately 190 m/s departure
mismatch is a reconstruction, not a measured docking impulse.

The extended study adds eight checks executing native Dock, Hold, MoorTo,
UnMoorFrom and group movement without the lock-preservation prototype. Initial
group gravity is enabled; tether removal copies the civilian's flag onto the
anchor. Supplied gravity then produces no velocity change. Clearing only the
anchor's gravity-ignore flag restores the expected acceleration. The complete
study at that stage had 70 checks, including the historical 62-check alternative.

## Earlier lock-lifetime hypothesis

The installed `blueprints/environments/environments.json` defines the Phobos
rings in maps 1002/10020 with `bIsBodyLocked: true`, zero random velocity and
60-150 km spawning radii. `SolarSystem.EnvironmentEntities.SpawnDerelicts`
honors that flag with `ShipSitu.LockToBO` and `SetupDerelictOrbit`. These assets
already request persistent attachments; changing them is not the repair layer.

The native `Ship.Dock(Ship, bool, string, string)` overload calls
`objSS.UnlockFromBO()` on every non-body ship whose docking links change.
Initial group formation recognizes the locked wreck. The reciprocal call on
that wreck then clears its lock, and `SolarSystem.DockGroup.UpdateAnchor`
copies an unlocked member into the anchor. This is reproduced with the actual
compiled docking method and group formation, without any production patches.
Both call directions clear the lock, so that transition does not depend on which
hull is the scene host. Mooring is not necessary for that transition. This does
not establish that native unlocking is itself the defect; the independent
gravity-enabled coast above recovers native relocking without changing it.

`Ostranauts.Ships.Commands.Hold.RunCommand` stops maneuvering and implements a
visit timer; it does not maintain the changing velocity of a moon. The group
therefore leaves its prescribed track. On undocking, the native
`ShipSitu.TryRelockToBO` correctly refuses a velocity mismatch above its
100 m/s limit. Raising that limit or periodically snapping wrecks back would
erase legitimate motion and repair the consequence rather than the cause.

Live evidence: O-TYUW visited O-8MCY and then O-6CZK. The saved O-6CZK was
Phobos-locked before the visit, but was unlocked afterward. O-TYUW subsequently
departed for MTRS-15B. O-6CZK remained unlocked and survived its Phobos pass.
The reconstructed velocity mismatch at departure was about 190 m/s. The
reconstruction uses detached ephemeris calculations; it is not a recording of
the undocking method's exact historical arguments.

## Attachment candidate and required release integration

1. Replace only the unconditional docking-time unlock expression with a helper
   that retains an existing soft body attachment when the other endpoint has
   no competing fixed attachment. Keep native target precedence when both are
   fixed. Preserve reciprocal/group construction rather than restoring a lock
   afterward. Do not turn ordinary ships into `bIsBO` stations.
2. At `DockGroup.SyncShipInput`, release a soft attachment when actual RCS,
   torch or angular acceleration is applied. Advance the attached pose first,
   release the anchor and attached soft-locked members together, and retain the
   input that initiated release. Zero-input Hold/Stop and residual spin must not
   release it. Stations, grounded ships and actual mooring targets retain their
   existing constraints. Manual unbraced thrust retains native wear/damage rules.
3. Adapt the existing towing member/group playback callbacks before their
   `FindSource`/`DeferMember`/`PrepareStep` checks. A valid, uniquely owned engaged
   course can release the soft attachment. Unsecured torch courses use the
   existing abort; conflicting course owners are not arbitrated by this fix.
   Admit a pending course even if the first skip ends past arrival, so the
   existing completion/remainder-coasting repair retains authority.

This uses four native hook targets (the docking overload, undocking derelict
relock, group input and the HaulShip member-unlock write), plus changes inside
existing towing callbacks.
The helper operates on the
current docking group at explicit docking/control/course boundaries. No global
registry, per-frame fleet scan, new AI controller, saved marker or repair timer.
The custom-builder feature `passive-docking-attachment` is a simulation
enhancement because it explicitly changes the attachment release boundary.
`torch-towing` requires it for the shared course-release integration. The feature
does not require `orbital-waiting` or the optional orbital integrators.
The standalone passive-docking
repair must not require optional hull collisions or torch fuel corrections.

## Interaction with current fixes

| Area | Evidence / required treatment |
| --- | --- |
| Civilian visits and wait continuation | Retain the native Main children and current visit-duration policy. The attachment changes the physical result of Dock/Hold; it does not pick jobs or force a return. |
| Hull outlines, physical collisions, docking clearance, avoidance | No radius/shape or placement override. Actual station-collision policy rejects both members of the wreck visit as station exemptions. Whole-plugin hull/AI transpiler-order checks pass. Native body locks still have their ordinary planetary/fixed-object handling; this is not a claim that preserving a flag has no classification effect. |
| Towing playback | Actual current production member/group hooks execute in the study. Preservation alone breaks them; explicit pre-playback release restores their route pose. Native brace checks and existing unsecured-course abort are exercised. |
| Route completion / phantom thrust | Actual current completion patch executes with towing. The first update can finish beyond arrival; the candidate completes and coasts only the remainder with zero residual thrust. |
| RCS/torch fuel calculations and departure mass | No formula, fuel reconciliation or mass override. Soft body locking does not make a wreck a station or mooring boundary for departure mass. |
| VORB anchoring | Keep `VorbUndockAnchorPatch`'s reference selection and native relock-speed test. The proposal does not select a replacement reference or expand acceptance speeds. |
| Native haulers | Actual retrieving-pilot Main/HaulShip runs with real RCS steering. A paused haul retains member save data; restored group and subsequent steering release correctly. Station arrival/retirement policy is unchanged; a complete live hauling job is not claimed. |
| SFF and PASS | Actual shared adapters, native civilian role/queue/hold/undock and native group motion execute. The fixed group follows the body during the skip, then finishes its native visit on ordinary turns. No new scheduler; scene/economy are explicit fixtures. |
| Save reconstruction | Native `ShipSitu.GetJSON`/constructor retain the lock/reference/rate; rebuilding the native group picks up that attachment. No new migration schema or custom saved state. |

## Offline evidence and limits

[DockingOrbitStudy](../Tools/DockingOrbitStudy/README.md) originally passed **449 checks**:
the original 188 controls plus production course-refusal, three-member/residual-spin
controls, station-collision exemption controls and native hauler/SFF/PASS lifecycle
integration, plus the independent collateral review below.
It reproduces both the original lock loss and the control-freezing regression
of a naive preservation patch. The candidate covers both docking directions,
body-relative following, native undocking, a third ship tethering/unmooring,
saved-situation reconstruction, true circular orbit phase/momentum, real-input
release, station/ground/asteroid constraints, native hauler member release, SFF
capture, current towing playback and post-completion coasting.

Unity object equality, selected-crew and mass services, audiovisual/logging/fee
boundaries, AI lookup and wear services are fixtures. The isolated release study
supplies controlled body poses; the physical comparisons execute actual native
ephemerides. Native docking, group updates and ShipSitu movement execute. The
assertions do not claim full scene behavior, damage outcomes,
economic delivery, a full save-loader run or complete GUI/PASS simulation.

The traffic graph now has a separate physical projection for attachment loss,
Hold/tether reconstruction, body following versus externally sampled free-coast
mismatch, control/course release, station/free-hull exclusions and paused-haul
save restoration. Eleven scenarios distinguish native behavior from the
effective overlay. It does not infer a numerical trajectory or declare all
unlocked visits unsafe. Reviewed native method and Phobos environment JSON
dependencies participate in assembly invalidation.

Already-drifting hulls are a separate recovery decision. Their current motion
does not prove whether a visit, player towing or combat caused the displacement.
This preventive repair must not automatically relock or move them.

## Independent collateral review

Rechecked November 16 against the current full production DLL. The original 227
assertions passed before adding new controls. Two gaps were established and fixed:

- **Permanent identity guard:** the release helper originally checked physics
  flags and mooring status, but not native `IsSubStation()` / `IsGroundStation()`
  identity. Existing departure-mass protection already honors those identities.
  A constructed body-locked substation with `bIsBO == false` reproduced an
  unintended release. Skyscraper classification also belongs to the native
  ground-station predicate, independently of `bGrounded`. A shared `CanRelease`
  guard now protects manual input and course entry consistently, before any
  brace abort. These are boundary reproductions, not reports of stations moving
  in the player's save; normal stations with `bIsBO == true` already passed.
- **Custom towing dependency:** `torch-towing` could omit `route-completion`.
  A native A/B with a 100-second course inside a 300-second non-GUI step produces
  about 561 km of endpoint/coast error without the existing repair, and zero
  reported error with it. Custom towing now requires that repair. The full build
  already included it; no second completion mechanism or new hook was added.

`CollateralChecks.cs` adds 222 assertions. These execute actual native
`Ship.SetThrust`, the production group-mass conversion and native
`AccrueTowGroupLoad`, comparing unpatched versus preserved attachment, both
docking directions, either driving hull, and secured/unsecured braces. They
verify initial zero commands, initiating force, shared velocity, unbraced wear,
braced protection, torch shutdown, released saved state and anchor rebuilding.
Damage beyond the native wear threshold remains outside these controlled runs.

Existing regressions pass: towing 343,871; orbital/SFF/PASS 5,121; economy 1,494;
route/recovery 689; docked gravity 47; bounty preservation 174; and the complete
hull geometry/range/proximity suite. The full-plugin AI/hull avoidance check
passes in both installation orders. Full binding covers 297 classes without
errors or warnings. Receipt/logs: `obj/AttachmentReview/`.

The Traffic graph has two additional permanent-identity boundary scenarios and
529 total expectations/safety checks. Its assembly dependency manifest now
tracks the native station identity methods used by release. No additional
runtime hook, population scan, save marker or AI policy was introduced by this
review. The Debug build is prepared; deployment and live acceptance remain pending.
