# RCS rotation module

A physical **Polaris RCS Rotation Module**, with the same 1x1 console footprint
as DUHRT. Install it in a Polaris nav console, then place **ROTATION** using the
console's native Edit layout. Its frame, arrow sprites and center toggle come
from the shipped DUHRT and Controls prefabs. The physical item reuses the native
1x1 Control Toggle artwork.

The cloned DUHRT readout's CanvasGroup is explicitly made interactive and accepts
raycasts. Its shipped read-only flags otherwise block all nested buttons. Parent
CanvasGroups remain authoritative, so console layout editing still disables normal
button operation. The direction pad uses more of the lower panel, with larger
button faces rather than enlarging only their invisible hit areas. The black
backing matches that control area's bounds, including the bottom button. Arrow
ink is centered using the source sprite's transparent trim and restricted to 35%
of the outer button rectangle, leaving padding inside its visible face. The center
caption is a single unwrapped **HOLD**, inset inside the plastic frame, with
exclusive on/off text visibility.

| Button | With a selected sensors target | Without a target |
| --- | --- | --- |
| Up | Face the target | Reverse heading (180 degrees) |
| Down | Face away from the target | Reverse heading (180 degrees) |
| Left | Turn 90 degrees left of the target direction | Turn 90 degrees left |
| Right | Turn 90 degrees right of the target direction | Turn 90 degrees right |
| Hold Bearing | Maintain the last commanded offset to its original target | Maintain the last commanded absolute heading |

The arrows normally perform one turn and stop rotating. Hold remains active
until canceled; choosing a different sensors contact does **not** retarget it.
Turning Hold off releases control immediately, without a final correction or
braking pulse; any existing angular velocity remains. The center
toggle becomes available after an arrow command and can resume that command.
Disabling hold, completing a turn or manually canceling rotation retains that
last arrow command. Turning Hold back on resumes its original target and offset;
it does not sample a newly selected contact or derive a new heading. The remembered
command is written into both native console settings and the open UI's working
copy, preserving it when the console closes and republishes its settings.
Target disappearance/retirement stops tracking; it does not invent a replacement.

Commands continue after the nav console closes, and survive save/load through a
native Auto pilot's command queue. Manual yaw or attitude-stop input in either
the map or docking controls cancels them, including input from another member of
the same docked group. Translation remains usable. Native docking input adds
assisted yaw during sideways thrust; that synthesized actuator input is replaced
by our PID while rotation is active and does not count as a manual yaw order.
The scoped docking adapter still passes translation through and preserves native
docking input unchanged when rotation is inactive. A new course, Match Speed or
other replacement controller supersedes rotation. Release held thrust before
starting a rotation command. Stations and groups anchored to stations cannot be
rotated by this module.

Working RCS, propellant and an installed, undamaged module in an operational
console are required. Removing/damaging the hardware or losing console operation
stops control. Cancellation does not remove existing angular momentum; native
manual controls or another command can counter it.

## Control and performance

`RotationPid.cs` combines a large-turn acceleration/coast/brake profile with a
wrapped-angle PID for final capture and tracking. Large turns use full native
yaw authority to reach a bounded 0.5 rad/s relative slew speed, coast without
rotational thrust, then brake using stopping distance and a timestep-aware capture
margin. The final PID has conditional anti-windup, a tightly limited integral
bias and gains adjusted for the game timestep. Its final stop
uses a native counter-thrust pulse rather than setting the spin to zero directly.
Only this module's active command owns its angular integration. Native
`Ship.Maneuver` still determines hardware response, fuel consumption, translational
acceleration and dock-group input propagation.

Ordinary ticks combine the PID's yaw and any manual translation into one native
maneuver. Automated yaw is independent of the manual throttle; parking that knob
at zero does not disable rotation. The manual throttle still governs manual
translation. Long time advances use at most 512 angular substeps of up to 0.1
seconds, followed by inertial angular coasting for the remaining interval. They approximate
the turn; they do not promise identical fuel use to thousands of frame-sized
ticks. Linear motion, gravity, collision processing and torch courses remain
owned by the existing simulation. Group rotation runs once at the native
`DockGroup` anchor step, retaining each member's rigid offsets and charging only
the commanded ship's RCS supply. Native towing wear rules remain authoritative.

Hold samples its observer and original target together before the native world
step. The game advances ships before asteroids and dock anchors; reading their
positions mid-step otherwise mixes epochs and can create false cross motion from
shared orbital velocity. Anchored ship/asteroid positions use the native parent
ephemeris plus their saved offset, matching NavPOI rather than a stale world-position
cache. Free targets use the common pre-step snapshot. Body targets use native
ephemerides. Ship contacts retain the sensor offset captured when commanded.

Angular rate is measured between successive world observations, once per world
step. The controller extrapolates that measured angular motion within its angular
substeps, instead of treating repeated substeps as new stationary observations.
Matched tracking motion coasts; it is not repeatedly braked to zero. Static
capture uses 0.01 degrees, with a 0.03-degree wake threshold to avoid boundary
chatter. It does not run a new sensor model or refresh a closed console's sensor
estimates. Rapid flybys can exceed rotation authority. Long timeskips approximate
changing line-of-sight motion from the available observation; they do not secretly
simulate the target's full future path.

There is no plugin Update callback, recorder, timer or background thread. The
native Auto scheduler owns command lifetime. Eight Harmony patch classes provide
UI creation, command reconstruction, SFF queue preservation, manual cancellation,
coherent world-step observations, physics integration and controller withdrawal. The small runtime lookup contains
active rotation commands only and is cleared on world replacement/plugin teardown.
When the feature is omitted from a custom build, these hooks are not registered.

## Native assets and installation

Full `Deploy-Mod.cmd` installs the **OstrabusRotation** native mod alongside the
plugin, honoring custom Mods locations and existing disabled states. Enable it
in the native Mods menu if previously disabled. Custom builds include it through
the independent QoL feature **rotation-module**, which requires **ai-flight-controls**
for the shared controller-lifetime protections; the PDA Settings app is optional.

Normal and damaged item IDs:

```
ItmOstrabusNavRotation
ItmOstrabusNavRotationDmg
```

The module is added to the same 19 vendor loot pools as DUHRT, at the corresponding
probabilities/quantities, and to the native nav-module editor pool. Existing shop
inventories need a native restock to acquire it. Normal damage/restoration maps
preserve its item identity and swap the UI to an offline panel when damaged.

The native JSON interface replaces named loot records; it cannot append entries.
`Update-StockAssets.ps1` rebases just those 20 rows from the installed 1.0.1.10
assets. All original entries/fields are preserved. Another mod overriding these
same loot names can conflict by load order. Rebase and revalidate after game updates.

This feature adds a real item definition, so saves containing that item need its
native assets. Switching to a custom build without rotation retains previously
installed definitions and removes only future stock additions. The item survives
but its custom console UI/controller is unavailable; native UI may warn about the
missing prefab while it remains fitted. Remove it from the console if necessary.
Fresh omission installs no rotation assets. Reselecting rotation or full deployment
restores stock. Completely removing its native assets while retaining its items
in a save is unsupported. The active command itself is a native primitive payload;
without the plugin's command factory, native loading skips the unknown command.

## Validation

`Tests/RotationModuleCheck` executes the shipped native maneuver, shallow RCS fuel,
angular/linear physics, Auto command tree, command-factory loading and three-ship
dock-group integration under bundled Mono. It covers direction/wrap semantics,
normal-step equivalence, original-target hold, pause/zero fuel, parent/power/damage
changes, manual cancellation, controller replacement, full behavior-tree JSON
roundtrip, SFF queue stability, completion/retirement, and static turns from
frame-sized through hour-long ticks. Unity object/time, rendering/audio-adjacent
condition effects and docking gravity/wear boundaries are stubbed. Failures logged
by production code fail the harness rather than silently passing a stopped turn.
It also reads the installed `resources.assets` DUHRT prefab to reproduce the
noninteractive readout CanvasGroup, executes the production repair with Unity
property setters stubbed, and verifies that parent/Edit-mode gating is respected.
The native docking KeyHandler is also executed with stubbed input/time boundaries:
its generated sideways-thrust yaw reproduces the old cancellation, and the repaired
adapter preserves all four translation directions while explicit yaw/attitude input
still cancels control. Last-command tests cover repeated hold toggles, completed
turns, UI working-map publication and native Auto recreation after manual cancellation.
Continuous-target tests cover angular rates from 0.0005 to 0.4 rad/s at 0.02-1
second ticks (under 0.04-degree settled error), both free-ship update orders,
and a parent-anchored asteroid with 20 km/s shared motion plus 20 m/s cross motion
(under 0.006 degrees after settling). A static hold spends no fuel after capture.
The native 180-degree profile test completes in about 8.6 seconds, including about
6 seconds of zero-thrust cruising, with full-strength acceleration and braking.
Release checks reproduce the previous unwanted braking puff, then verify no fuel
use, unchanged spin, immediate manual-control handoff and preserved command memory.
The fully loaded ship's native audio path stops all three RCS emitters and starts
none when hold is released, both while stationary and while tracking in rotation.

`InstallCheck.ps1` uses disposable layouts to check fresh omission, definition
retention, stock removal/restoration, disabled-state preservation and path rejection.
The custom builder checks standalone composition/dependencies and full-hook binding.

These offline checks do not render Unity prefabs or simulate fully loaded tank
connections. Live acceptance still needs the 1x1 layout/buttons, control feel,
manual cancellation in both consoles, console-close persistence and a saved/reloaded
hold checked in game.
