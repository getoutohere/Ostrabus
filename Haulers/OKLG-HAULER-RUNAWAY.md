# OKLG hauler runaway: diagnosis

Confirmed November 4, 2026. Investigation only: no runtime patch changed.

Subsequent repair is implemented in [Haulers](README.md): generalized
home-return handoff and scoped orphaned-maneuver cleanup. The evidence and vanilla
reproduction below describe the original failure; no live momentum repair was made.

The haulers are thrusting continuously with an orphaned RCS command, rather than
coasting at a mysteriously assigned velocity. O-X5GYK, O-4WTF and O-TVG6 have no
active AI command/target/route but retain approximately 1.901698e-10 AU/s^2 of
RCS acceleration (28.449 m/s^2). That adds approximately 102.4 km/s per game hour.

Two live snapshots roughly 640 game seconds apart give acceleration vectors
matching the stored RCS vectors. The residual magnitude is about 0.001228 m/s^2,
consistent in magnitude/direction with solar gravity. Fuel remains approximately
99.78%, 99.89% and 99.94%. Raw evidence is under
`Orbital/LiveTest/OKLG-OUTER-SURVEY`, particularly LATCHED-RCS-EVIDENCE.json.

## Native failure chain

1. `HaulerDeployingPilot` defines Main as Dock -> HaulShip -> AnchorDockedShip ->
   FlyTo. Its GetTarget, when the ship has no tow, returns its home station.
2. `Undock.Cleanup` (also used by AnchorDockedShip) calls ClearShipTarget but
   does not stop maneuvering. ClearShipTarget only clears the four target fields.
3. With no tow, `HaulShip.RunCommand` can obtain/apply the valid home target, then
   immediately enters its allDockedShips.Count==0 failure branch: ResetNavData,
   ClearShipTarget, Failure. The intended home destination is discarded again.
4. FlyTo falls through to FlyToManual; its first no-target branch returns Failure
   without stopping RCS. StickySequenceCommand/AIShip.RunAI can consequently report
   no active command while leaving the previous vAccRCS untouched. Re-running the
   sequence does not recover the home target or cancel acceleration.
5. ShipSitu.TimeAdvance integrates vAccRCS every update, independently of AI
   command state. It does not perform fuel accounting or expire control inputs.
6. Ship.Maneuver deliberately calls RemoveGasMass with aiSkip for shallow,
   noncombat AI. That branch returns the requested gas without reducing stored
   fuel. FlyTo instead debits TripFuelPrediction as trip progress increases.
   With no target/progress, that accounting cannot pay for the orphaned thrust.

The fuel bypass is part of the existing abstract AI travel model. Its combination
with uncancelled thrust and lost return intent causes this failure; changing fuel
physics alone would merely strand ships after exhausting fuel.

## Asset corroboration

Steam StreamingAssets/data:

- `loot/loot.json`: RandomHaulerShip selects Light Tug.
- `ships/Light Tug.json`: bFusionTorch=false, fFusionThrustMax=0, eight RCS
  thrusters, nine distributions, shallow mass 6827.87 kg and capacity
  3372.141126 kg. These match the inspected hull's fields.
- `star_systems/star_system.json`: OKLG starts with StationAllowsAnchoring.
- `condtrigs/condtrigs.json`: TIsAllowsAnchoring requires that condition.
  AnchorDockedShip therefore leaves delivered wrecks anchored while the tug
  becomes free. This explains why the released wreck need not share the runaway.

## Reproduction and limits

HaulerRunawayChecks executes the shipped Undock cleanup, full deployer Main
sequence, AIShip.RunAI, RemoveGasMass and ShipSitu.TimeAdvance under bundled Mono
with Orbital disabled. It seeds the measured RCS vector and supplies an empty-tow
pilot fixture returning the home target, matching the reviewed native GetTarget
branch. After 100 native AI cycles, the active command/target are null and thrust
remains. One native hour then adds 102.416389 km/s with no fuel debit. No native
command/motion/fuel methods are stubbed. This reproduces the failure chain, not
the original maneuver that first wrote that particular vector.

All 4,259 offline checks pass. Reproduce with the standard OrbitalCheck build and
bundled-Mono host commands. VANILLA-REPRODUCTION.json contains the result.

The exact last maneuver that wrote each live vector was not recorded. O-X5GYK's
history independently shows derelict E-0SR released, FlyTo, then no command about
eight seconds later. This missing first-write observation does not affect the
confirmed ongoing acceleration mechanism or native failure reproduction.

## Repair direction

Preserve/restore the empty deployer's home-return target, extending the existing
VORB-only recovery where appropriate. End the failed maneuver when navigation
relinquishes control with no successor; do not indiscriminately zero RCS every
frame or modify player controls, NavData or combat behavior. Already-runaway ships
also need a deliberate recovery policy: stopping thrust does not erase momentum.
No such runtime fix or live recovery was performed during this investigation.
