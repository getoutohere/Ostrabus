# Local faction encounters

BeatManager can stage faction ships beside a player who is still on an
interplanetary transfer, even though the regional stations are millions of
kilometres away. These ships inherit the player's cruise velocity, including
RCS-only hulls. Broad ATC membership is not a plausible operating area.

This is an intentional simulation policy change, not a claim that the native
code failed to implement an authored location constraint.

## Native mechanism and ownership

On 1.0.1.10, `BeatManager.FactionShip` uses the current ATC's stations, Navy caps
and event severity to select one to four actors. It can construct a controller
using a new/reserved hull **or recruit an existing active Navy ship**. It then
places distant actors 500-550 km from the player, copies the player's velocity,
randomizes rotation and assigns a nearby patrol point. Existing actors can lose
their current mission and physical state through this path.

`GUIOrbitDraw` also invokes `DebugFactionShip` when opening navigation with a
pending `IsDueFactionSpawn`. Staging therefore is not confined to the ordinary
weighted event loop. After the Titan finale, `FactionShip` instead calls the
six-ship ambient `FactionBattle`. This is separate from Ceres plot advancement.

Reviewed assets: `plot_manager/pm_settings.json` defines the event weight (0.05),
and `star_systems/star_system.json` defines station influence and Navy caps.
Those interfaces cannot condition this event on physical location/velocity or
preserve a recruited actor's placement. A native JSON override alone cannot
express this change. Installed vanilla assets are untouched.

## Policy

- Eligibility uses physical proximity to regional infrastructure, not ATC
  membership alone. A disk centred on the regional primary encloses each
  station's authored patrol disk and anchored POIs, with the native 550 km
  staging margin. Missing influence uses the native 70 km depot-spawn radius.
  Enclosing the disks includes the space between opposing stations.
- The player must be travelling at most 1 km/s relative to an eligible spawning
  station, and neither the player nor its connected group may be torching.
  Ordinary faction staging also filters the actual depot by relative speed.
- Existing actors are eligible only as nearby, fuel-capable shallow patrols:
  within 550 km and 1 km/s of the player, with no combat, repairs, home return,
  bounty, station visit, docking, queued action/reaction, orbit lock, pause or
  player ownership. All other existing ships still count against native caps.
- Recruited patrols retain position, velocity and rotation. Native selection,
  patrol assignment, event blocking and registration still execute. New/reserved
  hulls retain native placement after passing the location/speed gates.
- A declined event consumes the native event weight and leaves a pending
  encounter pending. It neither constructs actors nor forces a replacement
  outcome. Native urgency, severity, fleet caps and atmosphere checks remain.

No Ceres IDs or static kilometre ceiling are hardcoded. The authored BCER/BCRS/
COHO configuration gives a roughly 42,246 km envelope at the captured baseline;
tests include every Ceres belt cluster and the BCER-BCRS corridor at that epoch,
six hours later and thirty days later. This is a regional encounter envelope,
not a physical sensor range or a new constraint on ship travel.

## Scope, cost and packaging

One patch class binds two BeatManager methods. Placement is changed through a
validated transpiler; incompatible IL fails patch binding rather than silently
applying a guessed edit. Eligibility traverses the small regional infrastructure
list when a faction event is attempted. There is no per-frame monitor, retained
actor collection, saved state or repair sweep.

Ordinary station spawning, freight, bounty scheduling, navigation, combat,
other BeatManager events and `CeresAdvanceSpecial` are not patched here. Existing
pathological ships require a separate explicitly requested intervention.
The custom builder exposes `faction-encounters` as an independent simulation
feature. Pure orbital math helpers are shared, but no Orbital runtime hook is
required or enabled by this feature.

See the [native offline harness](../../Tests/FactionEncounterCheck/README.md),
[machine-readable behavior slice](../BehaviorGraph/Encounters/README.md) and
[pre-reload traffic baseline](TRAFFIC-BASELINE.md).
