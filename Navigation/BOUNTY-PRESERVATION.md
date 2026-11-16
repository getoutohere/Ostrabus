# Preserve bounty actors and their ships

Classification: **simulation improvement**, independently selectable as
`bounty-preservation` in the custom builder; included in Recommended and Full,
excluded from Bugfixes. No new save fields, polling, controller or event journal.

An active bounty reserves the actual target CO and the hull named by
`JsonBountySave.strTargetRegId` against administrative disposal. A target can
disembark, remain at a station, and later reboard that **same hull** through
`Ostranauts.Systems.BountyManager.MoveTargets` / `SpawnBountyTarget`. Native
timing, candidate order, custody, player ownership and scene-loading gates remain.
This preserves the option to depart; it does not force departure.

## Evidence and layer choice

The `jank` save proves the motivating loss: E-HJPR's navigation log records Liam
Charles vanishing at exactly the station retirement timestamp; `strAIDespawnedAt`
is MVOL, the surviving hull is hidden, the bounty remains, and the CO is absent.
See [the incident record](../Notes/SIMULATION-GAPS.md). Which rejection predicate
failed is not recoverable from the available preincident saves.

Native C# and shipped assets were inspected together:

- `CrewSim.DockAndDespawn` catches up each arrival, unclaims its departing hull,
  then destroys arrivals rejected by `TIsValidStationDropOff` **or** station
  reputation. Successful arrivals use the native station transfer branch.
- The shipped trigger forbids `IsDead`, `IsDerelictVictim` and `IsOKLGPirate`.
  Changing that JSON alone cannot affect reputation rejection, ownership release,
  regional hull destruction or stale cleanup.
- `AIShipManager.CrewCleanup` already relocates regional bounty NPCs to stations,
  with an ATC fallback, but unclaims their ship. Its caller `AIShipCleanup` then
  destroys the hull. Subsequent native bounty activation can spawn a replacement;
  it does not guarantee preservation of the original ship and its inventory.
- `StarSystem.CleanupStaleShips` protects player/plot actors, but not bounty actors
  or their empty reserved hulls. After three game hours, unregistration's stale
  stamp can send those hulls to destruction, even while their captain is ashore.
- `AIShipManager.SpawnPassShip` uses `TIsNPCPrunable`; this asset already forbids
  `IsNotTrafficAI`, which native bounty creation sets. Ordinary overpopulation
  pruning and passenger redistribution therefore already exclude active targets.
- `AIShip.FindShipForFaction` already excludes `IsBountyTargetShip` from reserve
  reuse. No additional traffic-selection policy is necessary.

The requested policy spans C# disposal boundaries that have no native JSON
extension point. The implementation uses three Harmony patch classes covering
five methods. No vanilla asset replacement or global death/destruction hook is
installed.

## Implementation boundaries

1. At station retirement, a rejected active target branches into the **existing**
   acceptance block. Native movement, logging, station claim, company rules and
   `WanderSoon` setup still execute. This deliberately allows these arrivals to
   be retained at the station despite administrative rejection; faction relations
   and subsequent encounters are unchanged. Dead targets remain dead bodies.
2. The two administrative crew-transfer paths retain an existing claim on the
   bounty's assigned hull. They neither invent a missing claim nor reclaim a
   captured ship. Ordinary claims are released normally.
3. Regional AI cleanup parks and unregisters the original reserved hull after
   native crew relocation. Its native stopped controls and registration cleanup
   run. Regional derelict disposal leaves a reserved hull in place. An unrelated
   hull carrying an active target is also protected against those deletions.
4. Main-thread stale-cleanup snapshot creation excludes active reserved hulls
   and targets' current carriers before the native worker receives its list.
   This is one transient ID set and list per native sweep, not a retained index.
   Worker threads never read the bounty registry. Work is O(contracts + ships).

The current bounty registry determines protection. It is not based solely on the
hull condition: native `EvaluateAllBounties` can remove expired contracts without
clearing that flag. Expiry, failed contracts and accepted payouts release this
policy's protection automatically. Existing deliberate `bRemove` commitments are
not reversed.

Combat, physiology, celestial impacts, scuttling, player actions, world teardown
and accepted bounty hand-ins are outside the intercepted call sites. They retain
their native consequences. The feature does not resurrect previously deleted
people or regenerate their missing inventory.

## Verification

[BountyPreservationCheck](../Tests/BountyPreservationCheck/README.md) executes
native methods before and after the production patches, including retirement
rejection, original-hull reboarding, regional cleanup, a four-hour stale interval,
native expiry, custody gates, living/dead hand-ins, population/reuse asset rules,
exceptions and changed-IL guards. Unity rendering, physical placement and
destructive teardown are explicit test boundaries; actual combat damage is not
simulated by this suite.

The bounty behavior graph now exposes administrative actor deletion and original
hull loss separately from station parking and reactivation. Its conditional
scenarios describe these boundaries; they are not a whole-game completeness claim.

Native reference: 1.1.0.0 `Assembly-CSharp.dll`, SHA256
`a79a5081d9c94307a45fe1d0b55ff1aa1d0af6ea8dfce1f9acc5961cf260ef07`.
