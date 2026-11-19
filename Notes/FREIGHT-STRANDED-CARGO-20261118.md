# Stranded freight: saved trajectories and retained markets

Investigation date: 2026-11-18. Native game: 1.1.0.2, assembly MVID
`158470fc-8494-4bfe-b750-ad9d18dbf8d0`.

The saved games provide substantially stronger evidence than market totals
alone: four disappeared carriers have saved torch route points deep inside
natural bodies, their captains are absent from subsequent complete saved-person
searches, and their exact cargo stock survives unchanged. Native physical
destruction independently reproduces the retained-market defect. This identifies
a demonstrated unsafe-flight mechanism and its orphaned-bookkeeping consequence;
it does not recover the historical runtime stack of each destruction.

No production patch, live recovery, cargo credit or deletion was made by this
investigation. The earlier survey is [documented separately](EJDR-SURVEY-20261118.md).

## Historical evidence

Each altitude below is a future **saved route point**, evaluated against the
actual native `BodyOrbit` ephemeris reconstructed from that save. Negative values
are below the solid surface, before subtracting the hull radius. Times identify
the deepest sampled point, not an observed impact time.

| Carrier and pilot | Journey | Last sampled save / later absence | Cargo retained exactly | Unsafe saved point |
|---|---|---|---|---|
| O-2P3, Redeem Guzman | MHNG to MLAB | `pathcheck` / `chaos` | 3,300 food | 2,796 km below Mars, 340 s after `pathcheck` |
| O-J3FE0, Jacqueline Neal | EJDR to VORB | `luna` / `fine` | 1,469 He3 | 375 km below Luna, 156 s after `luna` |
| O-KKWJ, Ryan Justin Solis | VNCA to VORB | `update` / `fine` | 692 media + 58 life support | 3,269 km below Venus, 706 s after `update` |
| O-ZNU, Alexa Vera Tran | VORB to MHNG | `chaos` / `coast` | 1,472 media | 5,538 km below Venus, 645 s after `chaos` |

The four retained consignments total **6,991 units**. The earlier survey found
12 markets absent from the November 16 registry but now stranded, totalling
9,885 units; these are four of those 12. This is not a claim that every old
warehouse record has the same cause.

- The earlier records contain living captains aboard their freight hulls and
  active INTERREGIONAL main freight trees, not an ongoing escape. O-2P3, for
  example, still has 1,637.57 kg of RCS fuel.
- The later searches stream the main root and **every** saved ship entry using
  the exact native CO IDs. None of the four captains is merely found at another
  station or aboard a different hull.
- Comparison hashes actor IDs plus sorted, exact `aStock` strings, including
  concrete item names and counts. All four manifests match before and after;
  this is stronger than matching aggregate commodity totals.
- O-2P3's disappearance window is 1,647.02 game seconds. Its planned Mars
  penetration is inside that window. The other saved windows are wider.
- A serialization failure could also omit a hull; the snapshots alone cannot
  rule out every possible alternate removal caller. The unsafe route itself is
  established regardless of that historical attribution limit.

Two other sampled hulls require separate attribution. O-N2V disappears with its
160 industrial products unchanged, but its selected saved route points remain
outside Ganymed; this does not prove the intervening route or later approach was
safe. O-WDN's sampled torch points also remain outside solid bodies. O-AL0's
last point is 11.4 km below Ganymede near its **ground-port** destination JFTS;
legitimate docking handoff must be resolved before calling that shallow endpoint
overlap an independently demonstrated crash. Its 536 textiles survive unchanged
after its hull and pilot disappear. Do not mix these with the four deep
penetrations above.

## Code path and responsibility

1. `Ostranauts.Ships.Commands.FlyToPath.PlanTrip4` asks
   `global::FlightCPU.GetDockingNavPoint` for departure/approach waypoints.
   That heuristic tests straight segments around a selected body. The returned
   route from `PlanTrip4Sub`, which builds velocity-matching and subsequent burn
   segments, is not checked in its entirety against moving solid bodies.
2. `global::AIShipManager.IsOnCollisionCourse` immediately returns false for
   torch flight and otherwise checks loaded **ships**, not a complete planetary
   route. Our changes preserve that torch exclusion. The nearby RCS collision
   forecast and safer escape-side choice do not establish safe interplanetary
   course geometry.
3. Our `AiRendezvous.Arrives` checks finite/ordered samples and final rendezvous
   accuracy. It does **not** check intermediate clearance. Replaying the exact
   four historical courses through the current production validator returns
   **true for all four**. O-ATK and O-TWV's current saved courses also return true.
4. Our `FreightRoute.Prepare` can sample a stored course and return while that
   course is still in progress without checking its intermediate planetary
   clearance. Its collision-aware `SffDynamics` section covers the later manual
   approach, not those stored torch segments. `SffSimulation.Prepare` likewise
   excludes hulls with `NavData`. Consequently the existing skip repairs do not
   supply the missing whole-course check; tick boundaries can affect whether an
   unsafe passage is noticed or skipped.
5. `global::CollisionManager.CheckCollisions` destroys a shallow hull when it
   intersects a solid body, using `global::Ship.Destroy(false)`. This removes
   both the hull and its onboard people.
6. `Ship.Destroy` calls `global::StarSystem.RemoveShip` immediately, but neither
   unregisters the market. `StarSystem.Update` has a later destroyed-hull cleanup
   that does call `Ostranauts.Trading.MarketManager.UnregisterShip`; a hull already
   removed from `dictShips` is absent from that scan. Its separately saved cargo
   market therefore survives without a carrier.

The failure is in C# navigation and lifecycle mechanisms. The relevant
`star_systems/star_system.json` entries distinguish natural bodies, MLAB as a
Mars ground station, EJDR as a Luna port, VNCA as a Venus station and VORB as an
orbital station. Those distinctions are meaningful inputs, not reasons to edit
the assets to prevent ordinary trading. Market/cargo JSON supplies commodity and
pod definitions; it does not implement the missing trajectory or destruction
cleanup checks. Existing authored destinations and freight behavior should stay
authoritative.

The prior lifecycle audit correctly listed direct destruction as a possible
retained-market path but did not establish its caller for its historical three
ships. This investigation adds different saved cases and native reproductions;
it does not retroactively relabel those earlier ships as proven collision losses.

## Current fleet exposure

The `roll` save, epoch **65635037926.438**, contains all 20 freight hulls from the
latest survey. Seven have future saved route points inside solid bodies:

| Carrier | Body | Deepest sampled point below surface | Game hours after `roll` |
|---|---|---:|---:|
| O-ATK | Mars | 786 km | 4.18 |
| O-TWV | Venus | 3,528 km | 10.14 |
| O-2DM | Venus | 3,454 km | 14.18 |
| O-GQN | Venus | 3,189 km | 34.33 |
| O-PW28 | Venus | 3,138 km | 38.35 |
| O-5VZD | Venus | 3,298 km | 54.41 |
| O-KYR | Venus | 2,845 km | 90.70 |

This predicts invalid route geometry, not seven inevitable deaths: later AI
decisions can still replace a route. A follow-up live read at epoch
**65635039854.34301** confirmed O-ATK and O-TWV still have the same saved route
endpoints and dates. At that read, O-ATK's below-surface sample was about 3.65
game hours ahead. No intervention was performed.

The previous survey's closing speeds, living pilots, future arrival dates and
matching cargo inventories were insufficient evidence of healthy navigation.
Whole-path body clearance is the missing test.

## Reproduction and repair boundary

The durable [freight study tool](../Tools/FreightLifecycleStudy/README.md) extracts
the evidence from original archives without changing them. Raw extracts,
fingerprints, geometry and test logs are under ignored
`obj/FreightLifecycle-20261118/`; large save copies are not added to the repository.

- The native Mono destruction study passes **eight assertions**, demonstrating
  physical hull removal with retained cargo, explicit market cleanup, and an
  unload/despawn control. Scene/Unity presentation boundaries are replaced;
  native destruction, hull removal and market methods execute normally.
- The `freight-rendezvous ROOT.json SHIP_ID` mode in `AiPlannerDiagnosis` executes
  the current production `AiRendezvous.Arrives` against native deserialized route
  points and reconstructed station ephemerides. All six selected bad routes
  pass the existing validator.
- Geometry is a point test, not continuous collision simulation. It includes
  small solid bodies and excludes placeholder station/region circles; it refuses
  attached future points rather than using their cached coordinates as free
  flight. No ship radius is subtracted and asteroid-field rock encounters are
  not modelled.

The repair must admit a physically clear route and let the existing navigator
construct valid departure/approach detours, including already saved courses and
their SFF/PASS playback. Merely rejecting a course forever would replace lost
freighters with stationary ones. Whole-course validation belongs at planning/
admission or restored-course boundaries, not in a per-frame scan of every
interplanetary route.

Market cleanup belongs at genuine, registry-owning physical retirement. Native
unloading/replacement also calls destruction, so an unconditional market delete
on every `Ship.Destroy` is unsafe. A repair must preserve unload/reload and
replacement identities and the established successful-delivery/overflow contract.
Correct cleanup alone would hide the unsafe flights while still losing cargo;
it must not substitute for fixing navigation. Legitimate physical losses are
also distinct from administrative restoration of shipments lost to a confirmed
simulation defect. No automatic destination credits are proposed here.
