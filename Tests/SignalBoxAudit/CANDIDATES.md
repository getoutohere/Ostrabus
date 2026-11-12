# Offline signal-box fix candidates

November 9, 2026 prototype record. These candidates remain independent, test-only
hooks excluded from the plugin. November 10:
[production repairs](../../MiscBugFixes/SignalBoxes/README.md) are now implemented
and run as a separate harness phase after the prototypes are removed.

Run the harness using the commands in
[the audit](../../MiscBugFixes/SIGNAL-BOX-AUDIT.md). Output is captured locally in
ignored `obj/signal-box-candidates.log`.

## Candidate mechanisms

`CandidateHooks.cs` installs temporary Harmony hooks only inside the offline Mono
process. Device membership is explicit fixture data, allowing negative controls
against unmarked electrical devices. This is not a proposed runtime identity test.

1. **Configuration/input changes:** schedule native reevaluation after either gate
   setter changes mode, or audit changes the number of inputs. Respect an already
   pending deadline. Do not resolve synchronously or replace native truth tables.
2. **Disconnect:** allow the terminal Disconnect message through after its output
   mapping was removed, but only remove an existing reciprocal input. Do not let
   On/Connect bypass ordinary output membership. Power gating remains in the native
   dispatch loop, and delivery still uses native AddSignal and receiver delay.
3. **Persistence:** serialize the actual remaining outgoing queue, including empty,
   after native processing and initialization. Already-delivered messages disappear
   from saved GPM; genuinely deferred messages remain and are scheduled normally.
4. **Effective output:** queued On becomes Off if gate status, master override or
   branch permission forbids it. Native gate status stays independent of the master
   override. This covers new branches, branch re-enabling and master re-enabling.
5. **Labels:** the candidate rejects new `#`/`,` labels before native RenameConnection.
   Existing native encoding stays unchanged; Unicode/spaces round trip normally.
   This is a prevention option, not lossless escaping or repair of old corruption.

The outgoing persistence hook is needed both after resolution and initialization:
native initialization clears serialized sendQueue after loading it, even if those
runtime messages remain pending. Saving before delivery must not lose them.

## Executed checks

The original/prototype phases pass **2,484 checks**: 67 audit/logging/offscreen
checks plus **2,417 candidate checks**. A further **2,496 production checks** bring
the current total to **4,980**. Original native failures are reproduced before
installing the candidates, so the baseline remains independently observable.

- All four gates, all two-input boolean combinations, master/branch permissions,
  powered/unpowered dispatch and later power restoration.
- Both gate-setting entry points and delayed normal UpdateManual resolution.
- Audited input changes and preservation of an existing future processing deadline.
- Disconnect while powered/unpowered and receiver present/temporarily absent,
  pending-queue reinitialization, later delivery and no stale replay.
- Queued On/Connect cannot recreate a wire after removal.
- A configured 3.1-second receiver delay applies to connection and disconnection.
- Label validation/native round trip, and unmarked-device negative controls.
- A deterministic 300-event three-box network, checked against an independent
  acyclic truth model after each settling period: input changes, gate/master/branch
  changes, power interruption, disconnection and reconnection. Native message,
  queue, gate and serialization methods execute throughout.

Engine identity, clock, condition storage, lookup and logging are explicit fixtures.
This does not execute Unity rendering, GUI events or Powered's complete electrical
network. Cyclic networks/oscillators are not assigned new semantics by this model.

## Endpoint authority resolved in the offline model

Native Ship.InitShip clears `_bDoneLoading` before spawning and sets it only after
reconstruction finishes. The test verifies that ordering in the installed method's
IL. LoadState alone is insufficient: it is assigned before that completion flag.

A ship map is authoritative only for the current registered ship object, fully
loaded, finished reconstructing, not destroyed/tearing down, with the Electrical
component bound to its actual owner. An endpoint within a scoped mode switch or
still initializing its GPM is temporarily unavailable even on an otherwise ready ship.
Offscreen copies, shallow hulls, replaced generations and stale cached owners do
not meet this condition. IDs in saved layouts are not treated as proof of life.

Missing/non-electrical endpoints in that settled routing domain can retire a
disconnect and be pruned normally. Temporary absence instead defers pruning/delivery
and parks the durable queue; native UpdateManual does not poll the missing endpoint.
Completion events wake the waiting sender. Prototype tests invoke that wake at the
modeled completion boundary; the production phase also tests installed lifecycle
hooks, including native mode-switch success/exception completion.

239 added authority checks cover all combinations of None/Shallow/Edit/Full,
completion/teardown flags, present/missing/non-electrical endpoint and mode-switch
scope; delayed receiver return versus permanent retirement; nested scope handling;
generation replacement; stale owner binding; GPM initialization; and no polling
while dormant. Retired or delivered disconnects stop retrying.

## Preserve the actual vanilla conversion contract

The November 9 user instruction supersedes the earlier HVAC-only restriction:
preserve existing vanilla connection/logic persistence wherever it applies.

Native ModeSwitchInternal calls Electrical.CleanUp(false) when the replacement
also has Electrical, then carries the same ID and GUI property maps into it. If
Electrical is lost, native cleanup deliberately disconnects. Whole-ship teardown
and a preserving replacement are therefore not permanent endpoint removal.

The model runs native cleanup and GPM reconstruction across the preservation
boundary and verifies gate, delay, override and wiring retention. The full Unity
placement/ModeSwitch method is source-traced rather than replayed in this harness.
The shipped assets contain 11 loose definitions retaining Electrical (PDCs and
decoy/missile launchers), and each installed/loose pair is checked. Loose signal
boxes and loose fusion laser arrays do not have Electrical: their vanilla conversion
disconnects. This model does not invent universal uninstall/reinstall preservation.

## Production follow-through and remaining limits

The production module now implements scoped ModeSwitch entry/finalizer,
reconstruction/GPM-completion wake hooks, definition-based relationship scope and
weak lifecycle state. Its phase also covers immediate resave of incoming messages,
idempotent queue initialization, reciprocal cleanup persistence and settled ghost
inputs. See the module notes for hook counts, cost and validation boundaries.

**Labels:** rejection is the tested conservative option. A lossless escaped format
would need compatibility behavior for vanilla readers and existing saves. Already
truncated/ambiguous labels cannot be reliably reconstructed from damaged text.

**Performance/lifetime:** temporary fixture membership/retry collections are cleared
in finally. Production uses weak state and pooled dispatch scratch, has no new
Update hook or missing-endpoint polling, and serializes outgoing state on mutations.
Weak-reference collection is checked with the actual helpers. Neither the prototype
nor this test suite is a measured allocation/performance verdict for live gameplay.
