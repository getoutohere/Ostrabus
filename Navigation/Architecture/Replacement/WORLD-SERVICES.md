# Retained world-service integration

Status: offline integration experiment. No production patches, native assets,
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

The coordinator now runs **outside** `StarSystem.Update`'s prefix chain. Offline
call-site bindings route the SFF/PASS iterators' world call through that port;
each segment then invokes actual `StarSystem.Update` once with its existing
prefixes/body/finalizers in order. The ordinary fixture driver uses the same
port. Native iterator preamble and the real GUI SFF flag still run. No claim is
made that the call-site binding is already installed in production.

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

GUI SFF uses the existing production world prefix to open its scopes. Production
PASS still opens none, as a negative control verifies. The experimental PASS port
opens the same service scopes explicitly after observing the world and disposes
them in the same economic/physical order. It does **not** fake `GUIFFWD.Active`.
Existing module enable flags still gate the service factories.

Discontinuous intervals are bounded to at most one day per service window, within
the existing economy duration limit. Native AI dispatch runs between windows;
this is not repeated world advancement without command handoff. Route arrival
can split a window earlier. Only the remaining interval advances afterward.

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
- Two-day PASS is split into two daily service windows, produces 48 test units
  and executes 24 due dispatch checks. Rebinding a saved Navy route preserves
  remaining fuel and residual world time without restoring stale scopes.
- Injected physical-body failure exercises real service finalizers. Injected
  arrival failure retains a correlated uncertain effect, releases scopes, blocks
  residual advancement and cannot silently replay crew transfer. Partially
  applied world jumps are not rolled back or automatically retried.
- Disabling the underlying production module prevents scope creation through
  the experimental PASS port too.

This qualifies the stated fixture profile, not every world skip. Remaining work
includes materialized reactors/tow groups, real scene/crew effects, shared playback
completion semantics outside GUI SFF, collision-history discontinuities, and full
role/region coverage. The earlier PASS investigation in
`Orbital/PASS-TIME-SKIPS.md` remains relevant; opening service scopes alone is not
a complete repair for native route-completion fallthrough. This experiment does
not install that repair or alter player torch drive execution.

An explicit negative control gives an unrelated route a braking acceleration of
`-1e-12 AU/s^2`, a 300-second remaining route, and a 600-second PASS interval.
The native successful-completion branch clears its route and returns false;
`ShipSitu.TimeAdvance` then applies the leftover acceleration for the full 600
seconds. It gains about **89.76 m/s** despite one playback invocation. This is a
known native defect, not an executive/SFF-scope duplicate writer. The test is
expected to reproduce it and is explicitly marked as an unresolved blocker.

Production follow-up: [RouteCompletionPatch](../../ROUTE-COMPLETION.md) now repairs
this native completion handoff, with its own native physics and interoperation
regressions. This retained-service experiment does not bind that separate patch;
its 89.76 m/s negative control remains evidence of the unpatched native path.
Integrating the repair into this experiment is still an adoption gate.

The separate playback repair still needs integration into this experiment.
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
