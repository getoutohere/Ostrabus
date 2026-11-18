# AI gravity exemptions: investigation and replacement options

Investigated against Ostranauts 1.1.0.2 on 2026-11-18. Native assembly SHA-256:
`133a9cc94ad4f4d5db951b65249b6cdf78f53063419d836b8871b92960ec3c37`.
The original investigation was followed by the two bounded repairs documented
under [implemented follow-up](#implemented-follow-up). The earlier
[docking flag audit](DOCKED-ORBIT-LOCK.md#complete-native-flag-audit) already
catalogued the four writers; this pass examines replacement semantics and
executes additional native/production counterexamples.

## What the three mechanisms actually do

| Mechanism | Motion | Consequence |
| --- | --- | --- |
| `ShipSitu.bIgnoreGrav` | Free integration omits external gravity; control, drag and lift remain separate inputs. An unpowered ship continues at its existing inertial velocity. | Neither holding a relative position nor following an orbit; it can drift away from a moving station or moon. |
| `ShipSitu.LockToBO` | Follows a fixed offset from a reference and replaces velocity with the reference's velocity. | An abstraction for attachment/station keeping, with an immediate velocity change and no propulsion payment. |
| `ShipSitu.LockToBOOrbit` | Imposes native circular relative motion. | Also changes velocity; it does not preserve an arbitrary incoming trajectory. |
| `Ship.LockToOrbit` / `ShipSitu.LockToOrbit` | Registers a generated `BodyOrbit` and follows its analytical trajectory. Our existing conic fitter can preserve a supported bounded trajectory's instantaneous position/velocity. | Useful for passive coasting, but cannot make stationary hovering or an unsafe trajectory into a safe orbit without a maneuver. |

A `BodyOrbit` can be a manufactured orbital station, regional reference,
substation or ground station. It need not be a natural gravity source. Native
`LockToBO` placement and analytical conic fitting therefore need different
reference-selection rules. An unrelated planet or the Sun is not a universal
substitute for the existing local reference.

## Where native AI disables gravity

All four explicit true assignments are in `Ostranauts.Ships.Commands`:

| Method | When | Assessment |
| --- | --- | --- |
| `Hold.RunCommand` | While its wait duration remains. Native help/fuel conditions can prolong waiting. | Largest general candidate for a more coherent waiting model, but some accepted states depend on the exemption to remain aloft. |
| `FlyTo.RunCommand` | AI pause deadline, or docked without secured towing braces; clears route/controls while waiting. | Split free navigation pauses from docking/attachment waits. A common replacement would erase important distinctions. |
| `HaulShip.RunCommand` | Paused haul, after brace setup and clearing existing wreck body locks. | Group/attachment transition, not simply a free ship parked in orbit. Preserve the existing attachment/release repairs. |
| `Dodge.RunCommand` | During its timed combat maneuver. | A brief control behavior, not a suitable place to impose a body lock or passive orbit. Its lost-projectile exit has a separate cleanup defect. |

`Ostranauts.Ships.AIPilots.AIShip.ResetShipState` clears the flag during reset;
ordinary `RunAI` does not reset it on every command completion. Constructors,
copies, save reconstruction and group reconstruction can carry the value.

The installed game JSON contains no `bIgnoreGrav: true` assignment. The inspected
asset logic still owns important decisions: `TIsHasRequestedHelp` tests
`ShipRequestedHelp`, and `TIsAllowsAnchoring` requires `StationAllowsAnchoring`.
The gravity writes, body-lock operations and analytical-orbit release mechanisms
are C#. Asset edits cannot express their cleanup or trajectory handoff.

## Existing locks that should not be confused with exemptions

`Lurk.RunCommand`, `Guard.RunCommand` and `LockPirate.RunCommand` already lock
actual vessels to a body/reference. Our `LocalNavigationFramePatch` changes
local reference selection. Lurk/Guard regard only `bBOLocked` as being in
position, and their activation paths use `UnlockFromBO`; `UnlockPirate` also
uses that release. Simply replacing the lock with `bOrbitLocked` would break
their arrival/release assumptions. Guarding a particular place is also a
different intention from drifting around a planet and away from that place.

Navy/blockade patrol-point generation, pirate target generation,
`KeepAtDistance.SetCourse`, `FlyToPath.CreateNavPointStatic` and deployer
target creation also lock target/waypoint snapshots. Those calls do not mean
the actual pursuing ship is gravity-exempt. `AnchorDockedShip`, in contrast,
really anchors a delivered wreck under the authored station permission.
Preserve these distinctions when counting affected ships or changing physics.

## Executed findings

The offline [gravity-mode study](../Tools/DockingOrbitStudy/README.md) now runs
66 native/production assertions, including the follow-up repairs. It does not
patch the running game.

1. **Body lock changes momentum.** Native `LockToBO` erased a supplied
   10 m/s relative velocity. `LockToBOOrbit` imposed 2.18486646 m/s of tangential
   velocity from rest in the controlled Phobos-mass setup. These operations
   are not a physically equivalent optimization of the same trajectory.
2. **AI RCS does not release an analytical orbit.** Actual `Ship.Maneuver`
   installed nonzero RCS acceleration while `bOrbitLocked` remained true;
   native `ShipSitu.TimeAdvance` continued following the lock instead.
   `UnlockFromBO` did not release it either. Explicit `Ship.UnlockFromOrbit`
   restored response to that acceleration. The study supplies fuel and freezes
   the reference for this isolated control check; no full flight is claimed.
3. **The production fitter preserves a supported bounded input and rejects
   radial rest and escape.** Rejecting these cases is correct: fitting must
   not invent the velocity needed for a safe orbit. A fitted bound ellipse
   still needs clearance checks; bounded does not imply above the surface or
   atmosphere for the whole wait.
4. **Our current free-wait admission is insufficient.** The actual production
   `OrbitalHoldWaitingPatch` accepted a native-eligible free AI Hold at 300 km
   above a constructed airless Mercury-sized body with zero relative velocity.
   It cleared the flag; native gravity selection and `ShipSitu.TimeAdvance`
   crossed the surface after 447.5 seconds at 0.5-second steps, before the
   configured 600-second wait ended. This is a constructed accepted-state
   counterexample, not a recorded NPC death. It excludes atmosphere, collision
   response and later behavior-tree decisions. It extends the earlier group
   counterexample by executing the actual free-wait production postfix.
5. **Dodge can leave the exemption behind.** `WeaponsSystem.SetupProjectile`
   calls `DodgeHandling` for non-missile mass-thrower shots; skilled AI targets
   receive an urgent `Dodge` and the projectile ID. The weapon capability is
   present in `data/condowners/condowners_ship_combat.json`. If a previously
   running dodge finds that projectile missing/destroyed, its early success
   stops controls but leaves `bIgnoreGrav` true. The direct native command
   reproduction passes. `QueuedCommandsNode.RunCommand` calls `OnExit`, but
   Dodge inherits the empty `BaseCommand.OnExit`. Later FlyTo/HaulShip or reset can clear it, so this
   establishes a leaked state, not permanent immunity for every combatant.
6. **Native orbit-entry atmosphere test has an X/Y defect.** `Ship.LockToOrbit`
   passes `(body.dXReal, body.dXReal)` as the body's center to
   `MathUtils.GetDistance`; the second coordinate should be `dYReal`.
   Executing that distance call for reference (1,2) and ship (1+1e-7,2) gives
   about 1 AU instead of 1e-7 AU, admitting a point inside a 1e-5 AU boundary.
   This proves the arithmetic defect, not complete GUI orbit-entry behavior.

The normal docking suite still completes its 834 assertions; its host also
prints a Unity/Mono library mismatch notice after the assertion summary.
These checks use the
actual native assembly and current full Debug plugin, with declared fixture
boundaries; they are not a live fleet census. Live paging did not yield a
reliable census during this investigation, so no current ship distribution,
specific death, performance gain or full SFF/PASS orbit handoff is claimed.

## Collision and simulation implications

Body lock is not a purely numerical substitute. `AIShipManager.IsOnCollisionCourse`
ignores other body-locked/body ships. `CollisionManager.CheckCollisions` treats
body-locked groups specially, including skipping their celestial checks and
skipping pairs when both ships are body-locked. Assigning locks to arbitrary
free ships would also change these protections and avoidance decisions.

Analytical orbit lock still receives celestial collision checks and exits inside
atmosphere; `AIShipManager.LEOForbiddenTarget` also treats orbit-locked crewless
hulls specially. Native orbit creation adds a per-ship body to the system, and
release must remove that generated body without removing a station/reference.
The existing body-unlock and passive-group release paths are insufficient.

Current orbital correction, numerical coasting and SFF coast admission respect
the gravity-ignore bit. Restoring gravity can therefore increase numerical work.
Analytical fitting can move work to entry/exit boundaries, but introduces body
registry/update work. No performance comparison has yet been made.

## Recommended decisions

1. **Repair the two demonstrated C# defects separately:** lost-projectile dodge
   cleanup, and the X/Y orbit-entry atmosphere check. Neither requires inventing
   new AI behavior in hooks or assets. Do not erase a combatant's velocity or
   install a lock as dodge cleanup.
2. **Tighten our existing `orbital-waiting` policy before extending it.** Outside
   the atmosphere is not sufficient admission. Restore physical coasting only
   when the current trajectory is safe over the intended wait, with proper
   reference/clearance handling. Retain the native exemption for unsupported
   hover/unsafe states until there is an explicit maneuver/station-keeping
   design; do not silently circularize or attach them. Apply the same rule to
   the targetless scavenger path using this helper. This fixes our policy's
   unsafe admission, not a claim that all native waiting should be physical.
3. **Prototype analytical free orbit only for passive, independently free,
   unpowered AI with a supported safe trajectory.** First prove release on
   actual control, navigation, combat interruption, docking and player takeover,
   and save/load, controller removal and SFF/PASS behavior. Reuse the conic
   fitter and native generated-body lifecycle. Fitting/revalidation should run
   at relevant transitions, not via a new per-frame fleet scan. Do not promote
   it merely because the initial fit succeeds.
4. **Keep authored attachments; assess guard and lurk intentions separately.**
   A guard assigned to a particular place and a pirate waiting to intercept
   orbital traffic need not share a motion model. The Venus encounter below
   demonstrates why preserving every native lurking lock is not a satisfactory
   design recommendation. A new fixed lock is reasonable only if we deliberately
   choose station keeping as an abstraction and accept its velocity/fuel and
   collision semantics. For existing free Hold/FlyTo states it would need owned
   release before flight: native `FlyTo.RunCommand` rejects body-locked ships.
   That is a behavior design decision, not a harmless flag replacement.
5. **Keep tow pauses and active dodging out of a general conversion.** Their
   existing motion and interruption contracts differ from passive free waiting.

The initial investigation changed no runtime code, settings or assets.

## Implemented follow-up

Only lost-projectile dodge cleanup and conservative waiting admission were
authorized and implemented. Pirate lurking, guarding, towing and general
analytical orbit-lock behavior have not been converted. The native orbit-entry
X/Y atmosphere-check defect remains a separate finding, not part of this repair.

`Navigation/DodgeCompletionPatch.cs` finishes the native successful dodge exit
by clearing gravity-ignore, resetting its native duration sentinel and removing
`DodgeTarget`/`DodgeDir`. This matches native ordinary completion. Active dodging,
failure semantics, control shutdown, unrelated blackboard state and native
combat policy remain unchanged. CustomBuilder exposes it as the independent
`combat-dodge-cleanup` bugfix.

`OrbitalWaiting.Coast` now uses the native fast gravity-selection hierarchy,
detached ephemerides and bound-orbit energy/angular-momentum calculations to
require a finite nondegenerate bound trajectory. Its periapsis must clear the
selected body's radius, outermost loaded atmosphere, hull radius and 1 km
margin. It also preserves exclusion inside the nearest body's atmosphere or
surface when that differs from the dominant gravity source. Invalid, radial,
intersecting, escaping and unsupported states keep native wait physics. There
is no generated body, orbit registration, new saved state, impulse or forced
arrival. Existing eligibility exclusions and the Orbital toggle remain.

This is a conservative two-body admission check, not a forecast of all future
encounters or a guarantee against perturbations/reference changes. It does not
retroactively restore lost velocity or rescue already falling ships. Full native
scavenger continuation tests retain their authored SOS/return semantics, and
repeated actual Hold admission allocates zero managed bytes in the fixture.

The former radial-rest admission is retained as a controlled A/B: clearing the
flag produces the 447.5-second fall; the new production check retains native
waiting instead. Clear bound states still respond to native gravity with the
production orbital correction installed. Live playtesting remains pending.

## Player-reported Venus lurking encounter

The player reported a pirate/bounty target stationary relative to Venus while
VORB was moving at roughly 3 km/s relative to it. Reaching the target required
discarding that orbital velocity, then regaining it to return to VORB. The
specific ship/save has not been identified in this investigation.

The inspected mechanism explains this kind of encounter: `Lurk.RunCommand`
calls `ShipSitu.LockToBO`, replacing the ship's incoming velocity with Venus's
velocity and suppressing free integration. The existing
`LocalNavigationFrame.At`/`Select` deliberately retain a local natural body
when it dominates gravity or belongs to the station's ancestry. They therefore
do not generally replace Venus with VORB. This is not caused by
`bIgnoreGrav`, and does not require an SFF fault to occur.

Revised design recommendation: a free lurking pirate should retain its real
incoming motion and coast while using the existing target scans and combat
tree. A supported safe trajectory could use analytical orbit after the release
requirements above are repaired. Do not snap its velocity to Venus, silently
give it circular speed, or attach every pirate to VORB. If its current trajectory
cannot support the intended wait, reaching a suitable ambush trajectory would
require an actual navigation maneuver; selecting that maneuver is additional
behavior policy. Preserve separate authored wreck/station attachments and
evaluate place-specific guarding on its own merits.
