# Retained world-service integration

Status: offline integration experiment, requalified for 1.1.0.0. No production patches, native assets,
persisted configuration or deployed behavior were changed. The separate descriptive AI
behavior graph was not edited. Results are part of [test-results.json](test-results.json).

## Executed mechanisms

The harness now installs the real, already-built Orbital/SFF/Economy patches in
the offline Mono process. Their scope creation, motion propagation, production
timeline, manifest transfer, stock/cache operations and disposal execute. Native
`StarSystem.Update`, AI scheduling, command dispatch and NavData playback remain
in the call path. The tested plugin hash is recorded with the native assembly
and model hashes; the experiment does not rebuild or deploy that plugin.

`WorldServiceFixtures` supplies synthetic world/market data, a stationary
reference body, candidate trade routes, crew-transfer outcomes and presentation
stubs. Producer rates and cargo quantities are test inputs, not changes to game
economy policy. The trade-route fixture returns no departure opportunities:
scheduled dispatch checks run, but spawning a new freight hull is not covered.
An existing freighter uses the actual `HaulerCargoPilot` and production manifest
transfer. The unrelated-dispatch probe tests native scheduling/catch-up isolation;
it is not a second complete native-role qualification.

## Two reproduced integration defects

**Coordinating from inside a world prefix produced a phantom interval.** The old
prototype prefix recursively ran the real update, advanced the clock, then
returned false. Harmony still ran a later production scope prefix. That prefix
prepared a new interval starting at the already-advanced epoch, then disposed it
without executing its timeline. The baseline economic report showed an interval
8,200 -> 15,400 instead of the requested 1,000 -> 8,200.

The current experiment uses production **`SffAdvance`**, outside
`StarSystem.Update`'s prefix chain, as the sole GUI/PASS jump coordinator. The
offline iterator transpiler now preserves that call and cuts only the scene tail.
Per-segment hooks admit/observe executive work without recursively advancing the
world. Native update, existing prefixes/body/finalizers, the real GUI SFF flag
and real pending-end-turn scheduler drain run. There is no second experimental
PASS scope opener or jump splitter. The normal fixture driver still partitions
ordinary updates at the adopted actor's route boundary.

**A stale native status could reacquire navigation after route loss.** Existing
`SffSimulation.Prepare` infers travel intent from the native active command and
ship-global target. A negative control deletes the adopted actor's route before
scope preparation while leaving its old FlyTo status. Without a binding, the
motion service independently produces a partial approach. That is appropriate
for native-owned actors, but conflicts with executive mission ownership.

The experimental binding scopes `IsTravelCommand` classification while preparing
an executive-owned group. It declines native travel inference for that actor,
while allowing the existing service to propagate physical coast. It does not
erase the status, change the target, reset velocity, assign a destination or
replace non-adopted actors' policy. The negative control becomes a physical coast
with the binding enabled. The native status remains available for native handback.

The Unity `UpdateShip` fixture also replaces only the unavailable **body**, using
a transpiler. A prefix that performs movement and returns false would prevent
later freight-ownership prefixes from doing their job. Retained prefixes can
therefore suppress this physical body when the economy already owns the carrier.

## Shared interval contract

| Case | Mission/control authority | Physical/accounting authority |
| --- | --- | --- |
| Adopted actor with NavData | Executive/native flight ability | Native playback and existing settlement watermark; bulk motion excludes the route. |
| Adopted actor loses route | Executive decides whether to retry/reconsider | Existing motion service can coast; it cannot infer an independent approach from old status. |
| Adopted actor is holding | Explicit authored directive and native Hold timer | Native ignore-gravity behavior is retained; no second travel/arrival controller. |
| Managed freighter | Existing native role and manifest policy | Existing economic flight scope; native physical playback/catch-up is suppressed for that window. |
| Freighter outside conservative scope eligibility | Existing native role | Native playback resumes using its existing settlement watermark. |
| Unrelated player course | Existing player/autopilot controls | Native NavData playback; executive ownership binding does not intercept it. |
| Combat bulk travel | No replacement outcome selected | Offline admission refuses before integration. Combat behavior is not redesigned here. |

GUI SFF and PASS use the same production coordinator and world prefix to open
their scopes. PASS uses the actual production pending-turn wrapper. A passing
check verifies one scheduler drain, two service scopes for a single window, one
jump-boundary generation enter/exit and no remaining jump/coordinator scope.
The harness does not fake `GUIFFWD.Active` for PASS. Existing module enable flags
still gate the service factories.

Discontinuous intervals use production's maximum six-hour window. Predicted
impact decisions and native course completion can split it earlier. Completion
is offered at the next representable epoch after arrival, preserving native
strict comparison semantics. Native dispatch runs between windows. Only the
remaining requested interval advances, with whole-jump role-callback eligibility
retained by the production coordinator.

Native AI dispatch can leave a freighter reporting `TargetNewIfNone` after one
successful main-sequence child. Existing economy eligibility deliberately declines
that status, so a subsequent window can use native playback. The test verifies
that the paid-clock watermark survives this ownership handoff: partial RCS and
fusion fuel agree, the manifest remains aboard, and native playback never runs
while that same carrier is owned by an economic scope. No policy change to
freight eligibility was introduced to make the test pass.

## Passing cases and practical limits

- Real GUI SFF scopes open at the requested start/end, exactly once per service
  and native segment; the phantom post-update window cannot recur unnoticed.
- Equivalent two-hour normal/SFF/PASS advancement produces six test units and
  the same native float fuel remainder, without premature Navy arrival.
- An adopted lost route coasts; a native holding directive retains its timer,
  pose and ownership. The corresponding stale-status negative control navigates.
- Unrelated player shallow torch-route playback executes once and retains its
  native motion/fuel result. An unadopted actor receives and executes its native
  catch-up wrapper under ordinary queue scheduling.
- Navy station arrival and an unrelated freight delivery each retire once. Four
  cargo units arrive before the next factory cycle; one is consumed into product
  and three remain. A short initial segment and later economic/native ownership
  handoff do not double-charge the freighter.
- Two-day PASS is split into eight six-hour service windows, produces 48 test units
  and executes 24 due dispatch checks. Rebinding a saved Navy route preserves
  remaining fuel and residual world time without restoring stale scopes.
- Injected physical-body failure exercises real service finalizers. Injected
  arrival failure retains a correlated uncertain effect, releases scopes, blocks
  residual advancement and cannot silently replay crew transfer. Partially
  applied world jumps are not rolled back or automatically retried.
- Disabling the underlying production module prevents scope creation through
  the shared PASS path too.
- A predicted-impact preview changes no live clock, fuel, route or policy. A
  capable registered actor gets an intervening decision; an unavailable pilot
  does not. The ordinary outward fixture target then runs through actual shared
  SFF/native dispatch. This does not certify every random patrol destination or
  resolve an uncontrolled impact into an invented outcome.

This qualifies the stated fixture profile, not every world skip. Materialized
reactors/tow groups, real scene/crew effects and full role/region coverage remain
open. Native human end-turn queues are empty in these fixtures; their scheduler
drain executes, but populated human-work completion is not qualified here. Story
encounters, achievements, physical collisions and the iterator scene tail have
explicit excluded bodies. The jump-boundary invalidation signal executes; actual
Unity collision history is not reproduced.

The route-completion repair is now installed in this experiment. Its A/B control
gives an unrelated AI route `-1e-12 AU/s^2` braking, a 300-second remaining route
and one 600-second normal world update. Without the repair it gains about
**89.76 m/s** despite one playback invocation; with the repair excess speed is
below `1e-5 m/s`. The old player-RCS control is historical: 1.1 now explicitly
stops player RCS during that playback branch, invalidating that particular
reproduction. Changing the control to AI follows the native source difference;
it does not weaken the excess-motion assertion.

Production mechanism: [RouteCompletionPatch](../../ROUTE-COMPLETION.md).
[Command receipt and selected dialogue effects](COMMAND-EFFECTS.md) are now
qualified under explicit providers. The [native save/arrival port](SAVE-ARRIVAL.md)
now exercises the real collector/writer and mixed crew-disposition branches;
actual scene/equipment outcomes remain. Runtime adoption still requires the
full acceptance checklist; no production hook is removable yet.

Implementation: `NativeWorldTimeBridge.cs`, `RetainedWorldServices.cs`,
`WorldServiceFixtures.cs`, `WorldServiceChecks.cs` under
`Tools/ShipExecutiveStudy/Prototype/`. Production mechanisms read/executed:
`Orbital/OrbitalPatches.cs`, `Orbital/SffSimulation.cs`,
`Orbital/Economy/SffEconomy.cs`, `FreightRoute.cs`, `CargoDelivery.cs`,
`EconomyPatches.cs` and `MarketStockPatches.cs`.
