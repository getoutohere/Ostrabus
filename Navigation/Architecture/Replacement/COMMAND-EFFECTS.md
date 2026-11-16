# Command receipt, fuel transfer and dialogue qualification

This is an offline replacement-executive milestone, requalified for game 1.1.0.0. It changes
the research adapter and its fixtures, not installed patches or game assets.
The descriptive behavior graph is separate. These results do not authorize
runtime adoption or removal of existing fixes.

## Construction is an effect boundary

Native `TakeFuel(IAICharacter)` calls `Ship.SyncFuel()` and sets the ship's
absolute pause to `StarSystem.fEpoch + 0.5`. Deferring construction until dispatch
changes when fuel is reconciled; calling it again during reconstruction can
discard transferred fuel and restart the wait. The earlier prototype did both.

Raw admission now lets native `AIShip.AddCommandLoot` execute its factory,
constructor and argument setter at receipt, then redirects the resulting
`AddCommand` into the executive inbox. Typed inputs have already been constructed
by their caller and are captured at that boundary. The receipt record retains
native command state separately from raw loot arguments.

`Comms.ApplyLoot` also probes condition-loot names, such as `TUpAltruism`, through
the same factory. A null result there remains a native no-op, even with a full
inbox. It is not a missing AI directive. Unsupported actual command bodies stay
in the inbox and stop execution explicitly; direct raw factory rejection is
reported to its caller.

Restoring `TakeFuel` allocates a command without executing its constructor,
initializes the reviewed `BaseCommand` fields, and applies native `SaveData`.
This is a deliberately narrow adapter: the assembly hash and the exact declared
`TakeFuel` field layout are checked. It is not arbitrary private-object graph
serialization. Native status objects remain lazily initialized as usual.

The same restoration path is used when forwarding an already admitted command
after exclusive handback to a native subsystem. Handback must not mean receipt
of a second command. Invalid saved fuel receipts are rejected before execution.
Profile schema is now **2**; old research snapshots are rejected, not migrated.

The native ship owns its shared pause and docking clocks. They restore with the
fixture world. An old per-command sample cannot overwrite a later constructor
write. Reaction checkpoint samples are refreshed from that world too. This
preserves the native shared-clock behavior; it does not silently reinterpret
the native use of absolute and duration-style timers.

Raw full-inbox admission refuses before invoking the factory. A typed caller
has already run its constructor before calling `AddCommand`; this adapter does
not claim it can roll those effects back after a capacity rejection. A runtime
admission design must account for that distinction.

## Fuel evidence

`NavyFuelChecks` compares native `TakeFuel`/`SyncFuel` with executive execution
using an explicit physical-tank and dock-connectivity provider. The initial
fixture tank has 100 units and a shallow cache of 20. Native construction
reconciles it to 20; subsequent native cycles transfer 30 then 10 from a donor
with 120, reaching the fixture minimum of 60. The resulting recipient/donor
total of 140 stays conserved during transfer. The initial 80-unit reconciliation
is the characterized native effect, not a new claimed conservation fix.

The executive produces the same timestamped additions/removals as the
uninterrupted native reference. Checks cover:

- Receipt versus delayed dispatch, raw versus typed input, and multiple queued
  receipts sharing the native pause.
- Fresh-object reload before first dispatch and midway through transfer,
  including actual provider-state serialization rather than retaining tank objects.
- Receipt during an urgent reaction, reload, reaction completion and transfer.
- Full raw inbox rejection before construction and malformed receipt rejection.
- Exclusive native takeover with a queued fuel command and no constructor replay.
- Native missing-donor failure and completion behavior.

Actual materialized gas containers, donor exhaustion within one transfer cycle,
pressure/temperature changes and fuel UI are outside this provider qualification.
No production fuel behavior was changed by this pass.

## Authored dialogue remains authoritative

`NativeDialogueFixtures` loads shipped interaction, condition-trigger, loot,
person-spec and ship-spec JSON into native data registries. No definitions are
rewritten. Native `Interaction.GetReply`, role swapping, `Triggered`, relationship,
person-spec and target-lock tests run, as do `SurrenderConsiderPlayerDemand` and
native `Comms.SendMessage(Interaction)`, `ReceiveMessage` and `ApplyLoot`.

The tests establish native candidate ordering for ceasefire violation, cooldown
and unbroken targeting; the friendly allowance; and no response for an expired
request or a recipient failing the relevant conditions. Missing asset lookups
are checked rather than accepted as successful evaluation.

A shipped boarding allowance applies `CMDComeHere` to its **sender**, the
responding captain, and retains the player as counterparty. The executive restores
that directive into its explicit approach phase without resending the reply.
Its actual approach/ATC/hold behavior has separate native ability tests.

Personality utility scores and recency inputs are controlled fixtures; actual
crew `ApplyEffects`, translation/message rendering and UI notification are
environmental seams. Their invocation counts are observed, but their complete
effects are not simulated. Arrest/scuttle/deployment outcomes remain unqualified.
This is selected branch coverage, not certification of every conversation.

## Reload before, during and after delivery

The fixture uses native `JsonCommData`, `JsonShipMessage` and
`JsonInteractionSave` representations. Restoration runs native comms DTO loading
and interaction post-load binding against fresh canonical crew objects. Condition
and relationship inputs for the two fixture captains are saved separately.
It restores chain context and logs without invoking send/receive effects.

Native `StarSystem.SendMessage` runs against its actual pending-message
dictionary, including ID-based deduplication. Pending DTOs survive reload too.
Delivery event fanout is a fixture that obeys the native strict
`epoch > AvailableTime` boundary; it is not a qualified Unity event lifecycle.

Tests save before request arrival, after receipt but before reply selection,
while the reply is in transit, and after completion. They verify canonical role
references, retained message identity and one application per reception. A
second delivery attempt or reload cannot reissue the command or receive effect.

This pass uses a bounded fixture world checkpoint. The subsequent
[native save/arrival integration](SAVE-ARRIVAL.md) exercises the native collector,
worker and wire parser under providers, with broader crew-disposition and patrol
handback cases. Actual scene outcomes and arbitrary handback remain gates.
The separate route-completion repair also needs integration in
the retained-service experiment; its historical negative control is left intact.

## Reproduction and evidence

Run `Tools/ShipExecutiveStudy/Prototype/Run.ps1` from the repository root. The
combined reviewed snapshot is [test-results.json](test-results.json); the direct
[authored inventory](authored-inputs.json) includes hashes for the dialogue
registry sources. Assembly/model/ability-contract bindings remain in the results.

`NativeClock` supplies an ABI-checked rotation entry solely because JIT compilation
of paused `FlyToManual` resolves that Unity function. Its invocation count must
stay zero; it provides no substitute steering physics. The existing excluded
scene destruction entry is guarded in the same way.
