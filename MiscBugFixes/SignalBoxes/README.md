# Signal-box repairs

The November 11 [cleanup-before-initialization correction](CLEANUP-INITIALIZATION-REGRESSION.md)
defers upstream publication until its owner/property map is bound, preserving
native removals across initialization and Electrical-preserving conversions.

Implemented November 10, 2026 against Ostranauts 1.0.1.10. The
[original audit](../SIGNAL-BOX-AUDIT.md) records seven independently reproduced
native defects. These are always-on correctness repairs in a full build;
custom builds select `signal-boxes`. Diagnostic routing remains the separate
`electrical-logs` feature.

## What changes

- Changing a gate schedules native evaluation instead of leaving its old result.
- New/re-enabled branches respect the gate, master override and branch switch.
- Explicit disconnection reaches an existing receiver after the output map entry
  is removed, preventing an input that permanently appears connected.
- Incoming and outgoing pending messages survive immediate save/reload;
  delivered messages are removed from saved state instead of replayed. Restoring
  the same component twice does not append duplicate messages.
- Connection audits wait for reconstruction or preserving mode changes to finish.
  Removing inputs schedules gate evaluation; settled legacy inputs with no
  reciprocal source output are retired only when no delivery is pending.
- Permanent device cleanup saves reciprocal output removals on upstream sources.
  Incomplete sources retain only removed endpoint IDs until native initialization
  or an existing lifecycle wake can publish them; old saved maps cannot restore
  the deleted outputs. Unrelated links and later explicit reconnection survive.
- New connection labels containing `,` or `#` are rejected and the previous text
  is restored in the GUI. Those characters delimit vanilla saved connections.

The fault is in C# event delivery, persistence and lifecycle bookkeeping. The
shipped `ItmElectricalBox01` variants and `GUIBreaker` already express the intended
controls. No asset rewrite or replacement signal controller is needed. Native
truth tables, no-input fallback, power gating, processing delays, callbacks and
`Powered` behavior remain authoritative. An ordinary device is included when its
connections or pending messages identify a signal-box relationship; unrelated
electrical networks retain native behavior. A participating component keeps the
bookkeeping repairs for its lifetime so deleting the last box cannot leave a
stale saved queue. This association holds no reference to that box.

## Endpoint and conversion boundaries

A routing ship is authoritative only when it is the current registered Full ship,
finished loading and not destroyed. Components must be initialized and bound to
their current owner/ship. A saved ID alone never proves that a missing device is
alive. Missing endpoints in a settled ship follow native cleanup; temporary
absence retains the original messages in the native queue, including their
timestamps and callbacks.

`Ship.InitShip`, `Ship.AddCO`, electrical GPM initialization, mode-switch completion
and cleanup wake affected queues. Native `UpdateManual` then processes them;
there is no missing-endpoint polling loop. A preserving `CondOwner.ModeSwitch`
is scoped through its success and exception paths.

Native conversion policy is preserved: Electrical-to-Electrical replacement carries
identity, connections and GUI property maps; conversion to a form without
Electrical deliberately disconnects. The assets contain 11 loose PDC/launcher
definitions that retain Electrical. Loose signal boxes and loose fusion laser
arrays do not; this repair does not invent connection preservation for them.

## Saves, lifetime and cost

The repairs use existing native GUI property-map fields and queue encodings.
There are no custom saved types or new save dependencies. Removing the mod leaves
vanilla-readable state, with vanilla defects restored. Previously damaged,
ambiguous labels cannot be reconstructed. Existing ghost inputs are repaired
when a native audit reaches settled reciprocal endpoints, not by a global sweep.

Eleven patch classes target 13 native methods. None targets a per-frame Update.
Dispatch scopes and scratch lists are reused; outgoing serialization is refreshed
on mutations. Relationship checks inspect existing maps/queues. Lifecycle wait
state uses weak-key tables and weak endpoint references; pooled dispatch scopes
clear game references even after exceptions. There is no permanent strong ship
registry, timer, worker thread or background scan. Native methods still allocate;
this is not a claim of allocation-free electrical processing.

## Validation

`Tests/SignalBoxAudit` passes **4,980 assertions** under the bundled Mono runtime:
67 original audit/logging/offscreen checks, 2,417 independent prototype checks,
and 2,496 checks with the actual production patches and diagnostic hooks installed.
The original native failures run before any behavior repairs.

Coverage includes all four gates and two-input states, gate/master/branch changes,
power interruption/restoration, native receiver delays, disconnect/reconnect,
save/reload before and after delivery, pending queue order/timestamps/callbacks,
mixed available/unavailable outputs, stale inputs, endpoint authority, moved and
replaced owners, teardown, conversion, GUI label restoration and garbage
collection of parked endpoints. Both candidate and production passes execute a
deterministic 300-event three-box network against an independent acyclic truth
model.

Cleanup regression controls include missing CO/GPM binding, an initialization
flag with a valid old map, multiple/repeated removals, unrelated output survival,
initialization during ship reconstruction, repeated initialization, explicit
reconnection, preserving component conversion, and garbage collection while the
owning ship remains registered. No additional Harmony hooks or polling were added.

The native public ModeSwitch wrapper executes with fixtures for Unity-dependent
placement/validation work; success and exception finalizers are exercised.
Reconstruction completion, identity, clocks, lookup, condition storage and power
availability have explicit fixtures. These tests do not reproduce Unity rendering
or a complete live power grid, and cyclic networks receive no new semantics.
No live save was changed or gameplay session tested for this implementation.

The current full plugin binding audit passes (205 patch classes, zero failures/warnings).
A custom build selecting only `signal-boxes` and `electrical-logs` binds all 13
selected patch classes, with no frame calls or PDA asset dependency.
