# Micrometeoroids

An always-on replacement for micrometeoroid detection/interception, with fair
target selection for BeatManager events. See [DESIGN.md](DESIGN.md) for the agreed
probabilities and formulas, preserved verbatim from the design discussion.

## Behavior

BeatManager keeps its native event timing, tension roll and exclusions for the
player being on/docked to a station, using a torch drive or in atmosphere. Once
it generates a strike, the target is selected from `CrewSim.GetAllLoadedShips()`:
fully loaded, non-destroyed, non-removing, visible-to-system hulls with tiles.
Selection is weighted by `nCols * nRows`. Derelicts and unattended hulls qualify.
Changing the occupied hull does not change that target distribution. If there
are no eligible scene hulls, the original target is retained. The native impact
log is redirected to the selected ship too.

Environmental Earth strikes and the native debug spawn retain their explicit
targets. All calls through `StarSystem.SpawnMicroMeteoroid` use the new defense.
No extra events are scheduled, and the native impact damage, sounds, camera
shake and time-reset behavior remain in place. The original impact log can
still be written for an event that was intercepted, matching vanilla behavior.

The unconditional 50% defense bypass is removed. Installed, powered, undamaged,
non-Off radar/lidar units contribute only while their corresponding sensor-On
condition is present. Each qualifying physical unit is counted separately;
orientation does not matter. PDCs use the same operational requirements and
contribute integrated sensing through a 90-degree cone centered on their native
firing direction, independently of ammunition, reload or jam state. Detection
is shared across the struck ship's connected docked group. Each materialized
member contributes its own operational sensors and PDC sensing, using that
member's position and orientation against one common world-space incoming
fragment. Undocked nearby ships do not participate. Shallow members are never
loaded to obtain equipment or ammunition.

Upon detection, each covering, ready PDC gets one native burst. Actual firing
uses the native weapon arc and range, honors Ships Only mode, and preserves
native reload, jam checks, ammunition selection/consumption and audiovisual
effects. All eligible guns get their burst even if another gun succeeds: these
represent simultaneous defensive fire, not multiple reload cycles. Each burst
is scored independently **after successful discharge**, using its actual native
ammunition-list count and launch speed. Empty/failed discharges get no roll.
Native jamming rules are preserved rather than reinterpreted by this module.

Before discharge, the existing ammunition observer checks the selected burst
against connected hulls. `BurstSafety` reuses native mount-position and aiming
helpers to reconstruct projectile spawn/velocity, then tests future paths for
the ammo's lifetime against the same circular bounds used by native projectile
collisions. The normal 20mm definition creates three 80-metre-radius proxies
with a 0.6-second lifetime; these are abstract burst objects, not individual
physical 20mm bullets. Later proxies receive native 130-metre index spacing
along absolute world velocity, plus lateral jitter bounded by a 20-metre margin.
The interlock draws no additional random numbers and creates no projectiles.

A connected hull elsewhere in the quadrant no longer vetoes a clear burst.
Rounds can still threaten hulls beyond the fragment, up to their expiration;
a hull safely behind outgoing rounds does not obstruct their future flight.
Any proxy's potential hit with a connected hull vetoes the whole burst. The
normal native ammo-selection roll occurs, but blocked rounds are not consumed,
do not trigger jamming/reload and receive no interception roll. Empty native
ammo selections do not count as safety blocks. Shallow connected hulls remain
obstacles; native noncolliding/destroyed/removing hulls are excluded.

This explicitly extends the original per-hull defense policy for docked/towed
assemblies. Native projectile spawning, damage, collision and ordinary combat
fire remain unchanged; connected ships gain no projectile immunity. The native
docking group supplies transitive membership, with legacy docking-list fallback
and duplicate filtering. No towing-brace requirement is imposed on defense.

The temporary target retains vanilla's 5 km stand-off and approximately 750 m/s
relative approach. These drive the native firing effects/trajectory, not an
additional physical warning-time model. There is no simulated fragment mass or
size. Interception success is the agreed probabilistic abstraction.

## Implementation and diagnostics

- `MeteorBypassPatch`: replaces only the integer bypass roll in
  `StarSystem.SpawnMicroMeteoroid`, leaving the native outcome branches intact.
- `MeteorDefensePatch`: replaces `WeaponsSystem.TriggerMicroMeteoroidDefense`.
- `MeteorBurstCapturePatch`: observes the ammunition list returned by native
  `GetAmmoShot` inside `Shoot`. The list and its contents are not changed.
- `MeteorTargetPatch`: changes only the successful event branch's spawn call
  and log receiver in `BeatManager.Micrometeoroid`.

One `[Micrometeoroids]` BepInEx log line per resolved event reports the target,
incoming angle, group/defender counts, detection probability/result, withheld
guns, bursts, rounds and interception. A withheld-gun line names the firing and
obstructing hulls. The explicit strike API also returns `groupShips`,
`defendingShips` and `blockedGuns`.
It does not add player-console spam. Native successful-interception feedback
is retained. No save fields, persistence, background work or Update hooks are
added. The ammunition observer adds one cheap context check per native weapon
burst; allocations/scans for this module happen only during MM events. The
short-lived firing context is restored in `finally`, including on exceptions;
no ship/character references or ammunition lists are retained between events.

## Offline validation

For in-game testing, explicitly request `POST /micrometeoroids/strike` on the
localhost inspection API. It runs on Unity's main thread, calls BeatManager's
actual MM event method and returns the target, detection and firing outcome.
It forces selection of this event, not detection or interception. Native
eligibility still applies, including station/docked/torch/atmosphere exclusions.
The direct call refreshes BeatManager's cached station flags as GenerateTension
does, then restores those flags and its temporary event roll in `finally`.
Event weights, timers and ongoing tension scheduling are not changed. This is
one real event per POST: ammunition can be expended and hulls can be damaged.
No target or outcome can be forced through this endpoint.

```powershell
Invoke-RestMethod -Method Post http://127.0.0.1:18764/micrometeoroids/strike
```

`Tests/MicrometeoroidCheck` runs against the game's assembly and bundled Mono.
It executes actual native `ShootAuto`/`Shoot` and `SetReload` with fixture Unity
effects, ammunition supply and destruction, verifying that misses still discharge,
consume ammunition and reload. It also covers partial/empty bursts, jammed,
reloading and Ships Only guns, independent redundant bursts, sensor power/install
state, quadrant versus firing-arc coverage, exception cleanup, area-weighted
selection and the actual patched BeatManager method's chosen target/log and
station exclusions. `Tests/PatchBindingCheck` checks all production hooks bind.
Group controls cover either impact hull, shared detection, reversed orientations,
a common world target with each gun's native weapons system, three-hull chains,
duplicate memberships, blocked ammunition/reload/rolls, shallow obstructions,
native projectile spawn/aim/collision parity, actual salvo offsets, short
lifetimes, clear off-path hulls, hazards beyond the fragment and peer-shot
exception cleanup. The November 13 replay of the paused O-GXD/M-TZ3T formation
uses the saved gun mount and inspected solar coordinates: its first native
proxy clears, but the two offset proxies trigger native collision predictions.
Thus that specific burst is still withheld for a demonstrated projectile hazard.
The complete harness passes 316 checks. No live postpatch MM is claimed.
The Debug build and all 245 patch-class bindings pass without warnings. Isolated
`pdc-defense` and `mm-fairness` custom builds compile, preserve patch ordering
and bind their expected three/one patch classes independently. No new Harmony
hook, saved data or persistent runtime recorder was introduced for the safety
replacement.

The native scene visuals and effects still require normal in-game observation;
the offline fixture does not host a Unity scene.
