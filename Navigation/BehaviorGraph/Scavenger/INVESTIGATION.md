# OKLG scavengers: fuel, docked holds and assistance

This slice records native 1.0.1.10 behavior. The user's intended recovery is to
undock a stranded scavenger and request regional ATC help. That is a proposed
behavioral repair, not an edge inserted into this native graph. No gameplay fix
is implemented for that Hold policy. The separately diagnosed shared flight-fuel
accounting fault now has a gameplay fix and an explicit graph contrast.

## Established failure mechanism

ScavPilot's normal Main is `Undock -> TargetNewIfNone -> FlyTo -> Dock -> Hold`.
StickySequenceCommand preserves its current child while that child returns
Running. A successful derelict visit leaves the ship in Main[4].

Hold waits for its timer to expire, then keeps Running if the captain has
ShipRequestedHelp, or if GetRCSRemain is below **1 kg** and rotor thrust is not
positive. It resets the timer to negative infinity, causing another randomized
wait on the next call. It does not request help or advance to Undock. This is a
kilogram threshold, not the bingo-delta-v check or LowFuel's 25%/speed reserve.

The actual native ScavPilot tree and Hold/sequence bodies reproduce that repeating
state offline: [native results](../../../../Tests/ScavFuelDiagnosis/RESULT.md).
A funded hold finishes, a rotor exception can
bypass the mass gate, the pause/zero-timer exception can release it, and priority
commands/combat/threat behavior can intervene. Therefore the claim is a missing
ordinary recovery path under stated conditions, not an inescapable whole-world
deadlock.

The help-request decision in TargetNewIfNone lies beyond Hold and the next Undock.
AmIOutOfFuel and SosOutOfFuel classes exist, but are absent from the constructed
ScavPilot tree. A source search finds no direct construction of them in shipped
pilot trees. AIShip can accept externally injected commands; the graph preserves
that as an explicit external event, not an automatic emergency monitor.

## Assistance exists, but has prerequisites

- Failed ordinary candidate search can enter TargetNewIfNone.RequestHelp. A nearby
  scav first tries the player if the player is not docked to a station: ordinarily
  under 40 km, or 120 km for close relation/missing captain/spec fallback.
- Otherwise it attempts SHIPSosStranded to the nearest regional station, falling
  back to the current ATC ID. In the relevant OKLG region this normally means OKLG;
  the code is not hardcoded to its station of origin.
- SendMessage checks recipient interactions and can return false. Callers do not
  inspect that result. Station paths unregister the caller even if sending failed.
  A send attempt is distinct from delivered SOS and successful rescue.
- The station's SHIPSOSDispatchHauler reply requires `ShipTestThem=IsNotDocked`.
  The referenced shipspec is `nIsDocked=-1`, verified with native Matches. A docked
  caller cannot pass that reply's eligibility gate. Undocking is consequential.
- Even an explicitly injected SosOutOfFuel can return without contacting ATC when
  its current target is within 5 km. It checks the nearby-player option first.
- A successful station response queues DeployHauler. Duplicate assignments, busy
  departure area, spawn availability and bingo reachability still matter. The
  authored response is a **retrieval/tow**, not a guarantee of an on-site refuel.
  Continue that role in the separate retriever graph.
- A player declining the authored fuel-help reply does not itself remove the
  ShipRequestedHelp condition immediately. Its authored duration is nominally
  three game minutes; `bPersists` does not mean permanent duration. See the
  [SOS audit](SOS-INVESTIGATION.md) for the timer and shallow-processing limits.
  Fuel transfer completion removes it through the
  authored loot. Fuel restored without removing that condition can still leave
  Hold waiting. The graph includes both cases.

## What this does not explain about fuel exhaustion

There are real affordability checks. ScavPilot.GetTarget randomly considers
eligible derelicts within about 5,000 km, using outbound BingoFuelCheck. The
shared FindTarget free-target branch then checks outward and homeward delta-v;
the orbit-locked branch generates/validates an outbound route. These must not be
erased to manufacture a story that scavengers never budget fuel.

There is another source-confirmed hole: when LowFuel chooses GetTargetHome,
RunCommand accepts a nonnull home TargetData even if GenerateNavData returns
null. The DTO's FuelCostRCS also stays at its default zero in that branch. This
bypasses the ordinary candidate-search failure/help path. Null route is not proof
of impossible travel: FlyTo can use manual steering, and physical outcomes are
not modeled here. A missing home can reach null handling outside this slice.

Docking does not magically empty fuel in this graph. A case beginning at empty
Hold demonstrates failed recovery **after reaching that state**. It does not
establish whether arrival burns, physical tanks, prediction errors, damage,
serialization, SFF or player intervention caused a historical ship's exhaustion.
Investigating that provenance remains separate work.

Follow-up [fuel provenance investigation](FUEL-PROVENANCE.md) now includes the
selected ship's live/saved state and a native reproduction of duplicate accounting
across planned/manual navigation. Historical attribution remains open; that
mixed-accounting witness is now transition-expanded with an explicit native-test
arithmetic precondition and a patched contrast. It doesn't assert that every ship
or collision follows that witness.

## Effective behavior and scope limits

The modeled ordinary Hold path is unchanged by the current mod. Shared target
replacement and prediction invalidation fixes remain relevant navigation
dependencies, but do not add a scav fuel-emergency branch. The overlay records
them as unexpanded boundaries, not as a claim that every gameplay hook has been
modeled. Retriever-specific SFF handoff and deployer recovery do not apply to
ScavPilot. Docked Hold's own SFF path resets its timer and queues a one-shot Hold;
it does not release Main[4] or request assistance.

No desirable outcome is made an invariant. Failed rescue, depleted fuel,
interruption and legitimate successful trips remain possible. The source review,
native reproduction and graph scenario checks are separate evidence levels.

Native test source/results: [ScavFuelDiagnosis](../../../../Tests/ScavFuelDiagnosis/RESULT.md).
