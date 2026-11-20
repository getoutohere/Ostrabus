# Navy lifecycle prototype qualification

Study disposition: **abandoned on 2026-11-19**. See the [postmortem](../POSTMORTEM.md).
This document preserves historical qualification, not an active adoption plan.

This is an offline replacement experiment, requalified for game 1.1.0.0. Nothing here is
installed, enabled by a module flag, or connected to a running save. The native
behavior graph remains descriptive and unchanged by this experiment.

## What now executes

`NavyPolicyBinding` reads the real `NavyPilot` constructor tree. It visits native
guards and immediate policy actions in their authored order, while movement and
compound combat commands emit explicit mission choices. The reducer owns those
missions; it does not execute the native root alongside them.

The native short-range `KeepRange` calculation, `Shoot`, long-range `SetCourse`,
`FlyTo`, `Dodge`, surrender offer/acceptance, `TargetPlayer`, `TargetATC` and `Hold`
bodies execute under declared world/equipment fixtures. Tests forbid entry into
the opaque `LowFuelRetreat`, `KeepWithinFiringDistance.RunCommand` and
`KeepAtDistance.RunCommand` controllers. Tactical return is instead an explicit
selection/approach/arrival mission. Existing Ceres reserve/home-selection helpers
are called when applicable; the active Ceres-policy case is not yet qualified.

The tested loop is patrol -> threat/combat -> timed dodge -> combat resume ->
native fuel/damage return -> approach -> one confirmed station arrival. Additional
cases exercise accepted/rejected surrender, a long-range tactical waypoint,
missing waypoint construction, reaction reload/target loss, and authored commands.

Equipment eligibility and engagement profile are fixture inputs. `Shoot` runs
with an empty weapon list: requests are observed, but projectile creation,
ammunition consumption, actual combat damage and decoy outcomes are not proved.
Local-body ephemerides and collision radii are also explicit fixture inputs.

## Commands and persistence

`NativeNavyAdmission` diverts `AddCommandLoot`, typed `AddCommand` and urgent
commands only for the adopted actor. Native `Comms.ApplyLoot` still interprets
loot and handles clearance/port branches. Raw arguments, one-shot wrappers,
counterparty and affected character are retained. `ApplyLoot` runs on both send
and receive; its character argument must not be mislabeled as the sender.

The [authored input inventory](authored-inputs.json) parses all 13 `CMD` aliases
in shipped `loot/loot.json` with native `Loot.GetLootNames`, then scans direct
references in core and generated-stakes interaction files. In particular, player
surrender demands also admit boarding, arrest/despawn and scuttle commands.
This inventory does **not** certify recursive loot/condition reachability or
every command another mod could submit.

The fixture executes surrender-offer decisions, target/sensor/shoot primitives,
TakeFuel receipt/transfer/failure handling and explicit ComeHere phases. ComeHere approaches
the player, switches its ordinary continuation to ATC, holds using the native
clock, and resumes that continuation. Race/deployment/scuttle/arrest commands
give native code exclusive authority before forwarding their exact payloads;
their subsystem outcomes are not exercised. Unknown commands stop the offline
adapter explicitly; they are not discarded or declared supported.

The profile DTO names tactical flight state, missile-check interval, reaction
target/direction/remaining time, bounded input identity and each command's
explicit phase/timers. Reload rejects duplicate input identities and mismatched
reaction state. Profile schema 2 also rejects fuel inputs without a receipt
construction record. Historical schema-1 research snapshots are not migrated.
The tagged blackboard codec includes missile ID lists and pilot
skill; it rejects arbitrary objects. No opaque command-tree snapshot is used.

`WeaponsSystem.DodgeHandling` queues a dodge **before** publishing `DodgeTarget`.
Admission therefore defers that read until dispatch in this fixture. Repeated
dodges update context while preserving the running reaction's remaining time;
they do not create nested suspended reactions or restart the timer. Completion,
including native missing-target completion, restores gravity ownership.

These are intentional replacement semantics, not claims about vanilla behavior:

- Mission continuations survive interruption and require revalidation; native
  urgent queue replacement can discard its current command.
- A failed approach/waypoint construction requests policy reconsideration and
  cannot imply successful docking, even where a native sequence advances on failure.
- Actions with possible world effects require confirmation rather than replay
  based solely on their command return value.

## Actual native world entrypoints

`NativeWorldTimeBridge` runs `StarSystem.Update`, the `GUIFFWD.FFWD` iterator and
the passenger `CrewSim._TeleportCO` iterator under bundled Mono. The current
world-service pass routes their world call through a coordinator outside the
native Update prefix chain, then invokes actual Update once per segment. SFF's
native activation flag and pre-update scheduler yield execute. Iterator tails after
the world call are cut out because they perform Unity UI/scene/crew operations.
An ABI-checked destruction stub records accidental entry; its invocation count
must remain zero.

Native `AIShipManager.Update`, its queue and dispatch budget run. Only the owned
actor's `AIShip.RunAI` is redirected; `AIShip.FFWD` cannot enqueue a second
controller for it. World time is partitioned at the native route boundary, with
one bounded immediate arrival transition before residual time advances. Existing
native fuel settlement remains authoritative; there is no second fuel ledger.

The longer test crosses the native catch-up threshold of **3,600 seconds**,
advances a 40,000-second route through normal/SFF/PASS calls and consumes exactly
five fixture fuel units with one arrival. Another reload case consumes the
remaining route plus residual world time once. An unrelated player NavData route
keeps native motion and shallow torch-fuel playback. That is an isolation test,
not a materialized reactor/torch patch compatibility test.

The [world-service integration](WORLD-SERVICES.md) now also executes the existing
Orbital/SFF/Economy scopes, stock algorithms, production timeline and unrelated
native dispatch under controlled data/scene fixtures. Combat bulk advancement
is refused before integration. This is an offline gate,
not a chosen gameplay restriction or synthetic combat simulation. Physical
scene bodies, actual collisions, market data, candidate trade routes and post-world
crew outcomes retain explicit fixtures. PASS tests use a non-player passenger to reach
the native shared time call without invoking scene UI.

## Remaining runtime gates

These are concrete unfinished interfaces, not reasons to redo the passing cases:

1. **Shared playback completion:** scope ownership now passes the bounded
   [world-service cases](WORLD-SERVICES.md), including route loss, holding,
   retirement, partial player playback and unrelated freight. A negative control
   still reproduces native non-GUI route-completion fallthrough. Repair that
   playback-to-physics handoff and qualify materialized/tow-group completion.
   A [separate production repair](../../ROUTE-COMPLETION.md) now exists; this
   experiment still runs its unpatched negative control. Integration remains.
2. **Real storage and dialogue effects:** receipt-time construction, native
   transfer accounting under tank providers, selected authored replies and
   received/in-flight message reload now pass. Materialized gas storage,
   personality/recency scoring, actual crew effects, arbitrary dialogue chains
   and Unity event fanout are not covered by those providers. See
   [the exact qualification](COMMAND-EFFECTS.md).
3. **Materialized save/arrival and handback:** the native collector, worker,
   wire parser and authored mixed crew-disposition branches now run under
   explicit providers. Ordinary main-patrol handback also survives a native
   archive. Actual Unity scene/crew lifetimes, company policy, multi-actor
   activation and disabling during arbitrary combat/directive phases remain.
   See [SAVE-ARRIVAL.md](SAVE-ARRIVAL.md).
4. **Equipment and region qualification:** exercise real weapon/decoy eligibility
   and remaining native authored branches offline, then validate materialized
   navigation, scene transitions and combat in Unity. Active Ceres policy and
   equipment behavior must be covered before a Navy-wide opt-in build.

No production patch can be removed on the strength of this milestone alone.
The constructor/dialogue gate has now been qualified for the bounded cases in
[COMMAND-EFFECTS.md](COMMAND-EFFECTS.md). The [native save/arrival port](SAVE-ARRIVAL.md)
now qualifies a bounded integration path. The next tasks are integrating the
separate playback repair and qualifying equipment/materialized abilities;
existing world services remain in place.

## Evidence

The combined reviewed snapshot is [test-results.json](test-results.json).
[Conformance](conformance-results.json) covers all 27 declared phase edges;
the 64 seeded adversarial walks are not exhaustive model checking. Results bind
the native assembly, existing plugin, model and ability-contract hashes.

Source anchors: native `NavyPilot` constructor; command classes named above;
`WeaponsSystem.DodgeHandling`; `Comms.ApplyLoot`; `AIShipManager.FFWD/Update`;
`StarSystem.Update`; `GUIFFWD.FFWD`; `CrewSim._TeleportCO`; `loot/loot.json`;
`interactions/interactions_ship_to_ship.json`; generated
`tsv/output/stakes/interactions/interactions_STKShipSurrender_PlayerDemand.json`.
Repository integration anchors are `Orbital/OrbitalPatches.cs`,
`Orbital/SffSimulation.cs`, `Orbital/Economy/SffEconomy.cs` and the retained
navigation/fuel classes listed in the result snapshot.
