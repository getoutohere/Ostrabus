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
is shared by the selected vessel's equipment, not between separate hulls.

Upon detection, each covering, ready PDC gets one native burst. Actual firing
uses the native weapon arc and range, honors Ships Only mode, and preserves
native reload, jam checks, ammunition selection/consumption and audiovisual
effects. All eligible guns get their burst even if another gun succeeds: these
represent simultaneous defensive fire, not multiple reload cycles. Each burst
is scored independently **after successful discharge**, using its actual native
ammunition-list count and launch speed. Empty/failed discharges get no roll.
Native jamming rules are preserved rather than reinterpreted by this module.

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
incoming angle, detection probability/result, bursts, rounds and interception.
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

The native scene visuals and effects still require normal in-game observation;
the offline fixture does not host a Unity scene.
