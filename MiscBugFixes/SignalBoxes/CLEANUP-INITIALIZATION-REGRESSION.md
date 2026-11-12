# Cleanup publishes an uninitialized upstream component

Discovered during live derelict work, November 11, 2026. **Repaired in the local
Debug build; live retest pending.** The captured stack begins in native `Electrical.SetGPM`,
called by `SignalBoxRepair.Cleaned` line 350 through `SignalBoxCleanupPatch`, then
native `Electrical.CleanUp -> CondOwner.ModeSwitchInternal -> Interaction.ApplyEffects`.
The exception occurred after docking, but this stack identifies item conversion
as the operation that triggers it, not orbital docking itself.

## Cause

After native permanent cleanup removes reciprocal output connections,
`Cleaned` immediately calls `SetGPM` on every resolved upstream Electrical
component. It verifies the receiver's ship is authoritative, but not that the
upstream component has finished binding its CO and property map.

Native `Electrical.SetData` sets `mapGPM` to null before `CheckforGPM` binds the
requested GUI map. The component may already be discoverable on its CondOwner
while this initialization is pending. Native `SetGPM` assumes a usable map and
bound `coSelf`; it immediately indexes the map and may dereference `coSelf.ship`.
Calling it during that interval throws. Native `CleanUp` itself only mutates the
upstream output dictionary; our new persistence call introduces the exception.

Both null-map and unbound-owner states produce the recorded stack. The captured
live stack does not expose which upstream component/field was null. No live
reflection hook was installed to obtain that field; the violated readiness
contract is reproduced independently against the native method.

## Offline reproduction

`Tests/SignalBoxAudit/ProductionBoundaryChecks.cs` establishes a real native
handshake, then places its source in either native initialization state:

- `isInitialising = true`, `mapGPM = null`, CO still bound;
- `isInitialising = true`, CO/ship binding not yet present.

Before the repair, native `receiver.CleanUp(true)` removed the reciprocal output
successfully, then our postfix threw in `SetGPM` in both cases. These failures
were reproduced before changing the production helper. The tests now verify
safe completion and durable cleanup instead of expecting the exception.

## Implemented correction

`Cleaned` now verifies component binding, initialization status and property-map
availability before calling the native serializer. An incomplete source records
the removed endpoint IDs in its existing weakly owned `DeviceState` and waits
through the existing initialization/ship lifecycle notifications.

After native `InitGPMValues` reads its saved map, pending removals are reapplied
before native `SetGPM` publishes it. A preserving Electrical-to-Electrical
conversion copies pending IDs to the replacement's weak state. An existing
lifecycle wake can also publish the pending snapshot when the same component is
ready. IDs clear only after successful serialization, so failures still propagate
without discarding the pending repair. Ready sources continue saving immediately.

The pending list is allocated only for incomplete sources, stores strings rather
than CO/component references, and clears those strings after publication.
Unbound waiters remain weakly reachable until binding or collection. There are
no new Harmony hooks, frame scans or saved fields, no fabricated empty property
maps, and no blanket exception suppression.

The signal-box suite passes **4,980 assertions**, including missing CO/GPM
binding, pending initialization with an existing map, multiple/repeated cleanup,
unrelated connection preservation, initialization before ship readiness, repeated
initialization, explicit reconnection and preserving conversion. A discarded
unbound source is collectable even while its ship remains registered. Existing
gate, queue, delay, saved-state and conversion controls all pass. Full plugin
binding passes for 205 classes with zero failures/warnings; Debug DLL/PDB build
with zero warnings/errors.

This is C# lifecycle bookkeeping. No change to authored signal-box/device JSON,
gate policy or connection-preservation rules is called for. No running-game state
or save was modified during the offline investigation and validation.
