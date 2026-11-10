# Shared AI planned/manual fuel handoff

Vanilla FlyTo can alternate between planned flight and manual steering, including
when collision avoidance interrupts an orbit-target route. NavData charges the
planned segment by its absolute waypoint fuel levels; the cached manual ledger
doesn't advance with that segment. Resuming manual accounting can charge the same
travel again. The native reproduction drains 100 -> 80 -> 30 -> 0 kg, with the
final debit occurring at unchanged position.

`AIFlightFuelHandoff.cs` marks native planned dispatch and establishes the existing
manual ledger's distance reference without debiting fuel. On return to manual
FetchFuelData, it advances the progress watermark for the same prediction to the
current high-water progress before native ApplyTripFuel runs. Subsequent manual
travel still incurs native charges. A new prediction retains its own accounting.
Native route/collision choices, fuel costs, retreat rules and fuel exhaustion are
not replaced.

The same ownership rule applies to materialized manual flight: Full ships consume
physical tank gas while native ApplyTripFuel skips its debit. Its postfix now
advances the existing serializable progress watermark without removing gas, so
later shallow execution doesn't charge that physical-tank travel again.

The hook is in shared FlyTo, not ScavPilot. No sweeper or automatic refuel is
introduced. The separate empty-Hold recovery gap remains unchanged.

## Lifetime, saves and overhead

A ConditionalWeakTable associates a small planned-mode marker with a FlyTo
instance. Its values contain booleans/strings, no hull, crew or controller
references. Command collection releases the entry. There are no new timers,
listeners or global lists of game objects.

Steady planned ticks read the prediction and marker; reflection/progress work runs
at mode/prediction transitions. Steady manual ticks perform a weak-table lookup
after native FetchFuelData. The Full-only watermark uses cached field-reference
delegates rather than boxed reflection. No new recurring allocations occur once initialized.

When saving during planned ownership, two optional strings extend native FlyTo's
five-value command payload. Loading restores the handoff marker. Vanilla reads
the first five values and ignores these extras; this is covered by a native load
with the production hooks uninstalled. No custom serialized CLR type or loader
dependency is introduced. Saves predating the fix remain loadable; lost historical
fuel isn't reconstructed. An old unmarked save cannot reveal a mode handoff that
already occurred before the mod began tracking it.

The transpiler matches exactly one native planned-command dispatch and preserves
its labels/exception blocks. A changed match count fails patch installation for
review rather than patching a guessed location.

## Validation

[Native accounting study](../Tests/ScavFuelDiagnosis/FUEL-ACCOUNTING.md): baseline
9 assertions reproduce the debit; patched 22 assertions preserve the 30 kg,
subsequent spending, repeated handoffs, genuine exhaustion, changed prediction,
save/load, Full-to-shallow accounting and vanilla loading of extended payloads. Geometry/collision outcomes
and steering leaves are explicit fixtures. These are not live-trajectory tests.

Build the Debug plugin and Tests/ScavFuelDiagnosis, then run the bundled Mono host
with `fuel` or `fuel-patched` after the test DLL. The independent default test mode
still reproduces the native empty-fuel Hold loop.
