# Vanilla signal-box audit

November 5, 2026 original audit. At that point, signal behavior fixes were not
implemented. Diagnostic routing
is implemented separately in ElectricalAuditLogging.cs: faulty-connection reports
and endpoint context go to Player.log and `/logs`, without player-console spam.
Native audit decisions remain unchanged. No live save was changed. The installed
game's actual Electrical methods, diagnostic routing and offscreen persistence pass 67 offline checks,
with seven reproduced behavior findings.

November 9 follow-up: the current assembly still reproduces all seven findings.
[Offline candidate fixes and their limits](../Tests/SignalBoxAudit/CANDIDATES.md)
now pass 2,484 combined checks, including reconstruction authority and vanilla
conversion preservation. At that point, they remained test-only.

November 10: [production repairs](SignalBoxes/README.md) now address these findings.
The harness preserves the native reproductions, then tests independent prototypes
and actual production hooks: **4,941 assertions pass**. This document retains the
original evidence; implementation scope, save compatibility and current limits
are in the linked module notes.

Relevant definitions: `ItmElectricalBox01` and its Off/Damaged variants use
`Electrical,Electrical`; `GUIBreaker` wires its knob to `Electrical.SetGateMode`,
its main switch to `ToggleOverride`, and its output controls to `SetUpConnection`,
`ToggleConnection`, `RemoveConnection`, and `RenameConnection`.

## Scope decisions for follow-up

Focus fixes on signal-box relationships. Electrical is shared infrastructure;
do not apply all changes indiscriminately to every electrical device.

Updated November 9: preserve connection and signal-logic state through uninstall/
reinstall wherever vanilla already does so. This supersedes the earlier HVAC-only
scope instruction. Native Electrical-to-Electrical mode replacement preserves
identity and GPM state; a replacement without Electrical deliberately disconnects.
Do not invent universal preservation for device types whose vanilla loose form
has no Electrical component. The offline candidate asset/conversion checks record
which shipped loose forms retain it.

Temporary ship unloading is distinct from explicit wire removal, uninstallation,
and permanent destruction. Do not erase wiring simply because an endpoint is
temporarily unmaterialized. Review mode-switch identity and reload ordering when
designing lifecycle patches.

`Removed N faulty connections!` is emitted by Electrical.AuditConnections when
an endpoint is missing from the ship lookup or lacks Electrical. It does not
identify the affected IDs or establish a bug by itself. Successfully removing
an input can expose finding 5 (stale gate result); finding 2's ghost connection
can evade the audit because its source still exists. No particular player's
connection removal has yet been attributed to either case.

## Live offscreen removals: existing wiring survives save-back

November 5 follow-up: `Player.log` recorded 25 prune decisions over 10 distinct
source/direction/endpoint pairs, all O-GXD at load=None, base Ship.json absent,
endpoint absent from runtime lookup, and savedCO=True. Sources were a vent and
two signal boxes; targets included the fusion laser array and pellet feeder.
All three source IDs also exist in the previously extracted saved O-GXD layout
(`obj/BARTER-SOURCE-O-GXD.json`), establishing that they are existing parts.

The None/no-json signature is consistent with async barter/preview ships: they
hide the base Ship.json property and do not perform ordinary ship materialization.
The logger did not capture the CLR ship subtype, so the individual log entries
do not distinguish those two kinds of copy. Both persistence paths were tested.

`Tests/SignalBoxAudit/OffscreenPersistenceChecks.cs` executes native JsonShip.Clone,
Electrical.AuditConnections/InitGPMValues, BarterZoneShip.AddToLocalInstance,
AsyncShipLoader.SaveChangedCO -> BarterZoneShip.SaveChangedCOs, and preview save-back
under bundled Mono. Unity/lookup fixtures model an endpoint not yet loaded.

- The native clone deep-copies item/GPM arrays; runtime property dictionaries are
  reconstructed separately from serialized string arrays.
- Audit removes the clone's runtime connection and emits the same diagnostic
  None/no-json/savedCO=True signature. Original and cloned saved layouts retain it.
- AsyncShipLoader refuses save-back before the barter copy is fully loaded.
- After finalization, repeated native barter save-back still preserves an existing
  item's saved GPM instead of exporting its runtime property map. Existing saved
  CO records also remain unchanged. Save-back exports runtime state for newly
  added items only; this is not a blanket guarantee for arbitrary new cargo.
- Native electrical reconstruction from the preserved layout restores the wire.
- AsyncPreviewShip.SaveChangedCOs is empty; preview pruning cannot propagate
  through that interface either.

This explains repeated pruning in fresh copies and the user's working controls.
For these existing-item offscreen removals, the tested save-back paths do not
damage saved wiring. They are safe to leave as diagnostic-only reports. Do not
turn this into a global audit suppression patch: a fully materialized ship's
normal serialization can export its runtime GPM, and finding 7 remains a separate
real failure mode. No gameplay patch or manual live reproduction was needed.

Evidence: `obj/ELECTRICAL-AUDIT-LIVE.txt`, `obj/ELECTRICAL-AUDIT-SHIP.json`,
`obj/ELECTRICAL-OFFSCREEN-PERSISTENCE.txt`. This was a logging-only investigation;
the later functional repair defers audits on related temporary copies.

Suggested priorities: explicit disconnect delivery; correct initial/re-enabled
effective output (gate and main override); reevaluation after configuration/input
changes; outgoing-queue persistence; compatible connection-label handling.
Keep pending power/delay behavior intact. These priorities were implemented in
the November 10 repair, alongside reconstruction and native conversion handling.

## 1. Gate knob changes do not reevaluate output

`Electrical.SetGateMode`, local decompiled Electrical.cs around 570, changes the
enum and saved gate field but does not set `bNeedsCheck` or evaluate the gate.
The `GateMode` property setter also omits reevaluation. `Update` only resolves
the signal queue when `bNeedsCheck` is true and the deadline has arrived.

Reproduction: two active inputs, one On and one Off. OR resolves On. Turn the
knob to AND. Native `UpdateManual` leaves status On with no pending reevaluation;
an explicit subsequent native resolve correctly changes it to Off.

Gate serialization is **not** faulty: `gate.ToString()` in `SetGateMode` uses
the method's integer parameter, not the enum field. It writes `1`, and native
`InitGPMValues` parses it successfully. An initial source-reading suspicion of
an enum-text/number mismatch was ruled out by executing the method.

Fix boundary: schedule a normal reevaluation when gate configuration changes,
preserving signal delays and power-dependent delivery.

## 2. Disconnect messages are dropped, leaving ghost inputs

`Electrical.RemoveConnection`, around 775, first queues Disconnect, then removes
the destination from `outputConnections`. Later, `ResolveSignalQueue` calls
`SendSignal`, around 457, which refuses to send unless that destination is still
present in `outputConnections`. The queued Disconnect is cleared without reaching
the receiver.

Reproduction uses native connection handshake and delivery. Receiver OR has an
On input from source A and Off from source B. Remove A's output wire. A no longer
lists the receiver; the receiver still lists A's On input and stays On. A subsequent
native `AuditConnections` does not fix it: it checks that A exists and has an
Electrical component, not that the reciprocal output still exists.

Fix boundary: reliably deliver disconnection while retaining enough validated
endpoint information, and reconcile both sides. Do not remove the existence
check globally or allow arbitrary queued messages to recreate disconnected links.

## 3. Processed outgoing signals survive in saved state

`QueueSignal`, around 484, serializes the outgoing queue into GPM `sendQueue`.
`ResolveSignalQueue`, around 408, delivers it and clears the runtime list.
`SetGPM`, around 597, updates status, connections, input queue and gate, but does
not refresh `sendQueue`. Its old serialized entries remain.

Reproduction: queue Off; native delivery consumes it and the receiver processes
it. Runtime sendQueue is empty while GPM sendQueue still contains Off. Native
`InitGPMValues` loads that already-delivered command back into the runtime queue.
It can be replayed on the next reload. This is duplicate delivery, not proof that
every reload will resend it forever: initialization also clears the stored field.

Fix boundary: serialize the actual outgoing queue after processing, including
an empty value. Retain genuinely deferred signals when the sender lacks power.

## 4. New and re-enabled outputs disregard the source's current state

`SetUpConnection`, around 755, unconditionally queues Connect and On. Likewise,
`ToggleConnection`, around 526, queues On whenever a branch is enabled, regardless
of source gate status or master override.

Reproduction: a powered source with an Off OR result connects to a receiver.
Native delivery sends On, and receiver records an On input while source status
remains Off. Disable/re-enable that branch: On is sent again despite the source
remaining Off.

The reproduction intentionally controls sender power. Native dispatch requires
IsPowered; this audit does not claim unpowered hardware can transmit. Powered.Run
separately responds to IsSignalOff/IsOverrideOff, so exact live timing depends on
the device power transition. The incorrect queued value is directly established.

Fix boundary: initialize/re-enable a branch with the sender's effective permitted
output, accounting for gate result and manual override; preserve branch enable
as distinct from forcing On. Check main override and power transitions as part
of any eventual implementation rather than changing status rules by name alone.

## 5. Connection audit changes gate inputs without reevaluation

`AuditConnections`, around 651, removes missing/non-electrical endpoints and saves
the maps. It does not mark the gate for reevaluation. This is separate from the
reciprocity issue above: even an endpoint that the audit successfully removes
can leave the old gate result indefinitely.

Reproduction: AND with one On and one Off input resolves Off. Remove the Off
source from the ship lookup fixture. Native audit removes that input, leaving only
the On input. Native UpdateManual leaves output Off and bNeedsCheck false; an
explicit native resolve then gives the correct On result.

Fix boundary: schedule normal reevaluation after an input set actually changes.

## 6. User connection labels are not escaped in save serialization

`ElectricalConnection.ToString/FromString` use `#` between fields, while GPM
connection lists use commas between records. GUIOutput.ChangeName accepts the
label and forwards it to RenameConnection without escaping these delimiters.

Native round trips:

- `Fuel#backup` becomes `Fuel`.
- `Fuel, backup` becomes `Fuel` and creates an extra malformed connection with
  an empty origin ID during InitGPMValues.

Fix boundary: compatible escaping/decoding or UI validation, with explicit
handling for existing unescaped saves. Do not silently switch the entire GPM
format or assume old labels can be reconstructed without ambiguity.

## 7. Audit equates runtime absence with endpoint deletion

Additional reproduction prompted by the player-visible `Removed N faulty
connections!` messages: retain a linked endpoint's JsonItem in the ship layout,
but remove it from the fixture representing materialized `GetCOByID` lookup.
Native AuditConnections removes the input and SetGPM saves an empty input list.
Restore the lookup entry and audit again: the relationship does not return.

The audit checks neither the saved layout nor ship loading/materialization state.
Native Ship.InitShip supplies all json.aItems to SpawnItems only at Edit/Full;
shallow loading supplies crew/shallow specs and force-loaded items. A missing
runtime CO therefore is not, by itself, proof of a permanently removed endpoint.
This component-level reproduction does not establish which endpoints caused the
player's recent aggregate messages or prove every shallow electrical audit runs
in a particular live transition. The existing message omits origin/destination
IDs and removal reason, so those individual events cannot be attributed afterward.

The message also accompanies legitimate cleanup of permanently removed devices
or endpoints that no longer have Electrical. Its text is insufficient to decide
which kind occurred. A successful input removal additionally exposes finding 5's
missing reevaluation.

Fix boundary: distinguish explicit disconnection/permanent removal from temporary
unavailability before pruning; preserve the native conversion contract described
above (the earlier HVAC-only restriction was superseded).
The aggregate player-console message can be moved to diagnostic logging while
preserving endpoint IDs, ship, load state and reason. Suppressing text alone is
not a correctness fix, and globally disabling connection audits would retain
genuinely stale relationships.

## Baseline and relationship to the transit exceptions

All sixteen combinations of OR/AND/NOR/NAND with two active boolean inputs
produce the expected truth-table output when native resolution actually runs.
The audit does not classify the no-input fallback as a bug; native code
intentionally sets it On regardless of gate mode.

The apartment-to-OKLG exceptions remain the separate Ship.Destroy orphan-loop
defect documented in ORPHAN-CO-CLEANUP.md: electrical OnDestroy queries a ship
whose mapICOs has already been set to null. None of the seven findings is needed
to explain that stack trace, and this audit does not establish which, if any,
has affected the player's current wiring.

## Reproduction harness

```powershell
dotnet build Tests/SignalBoxAudit -c Release --nologo
dotnet run --project Tests/SaveJsonCompatibility/Host -c Release -- Tests/SignalBoxAudit/bin/Release/netstandard2.1/SignalBoxAudit.dll
```

Electrical gate, queue, serialization, initialization, connect/disconnect and
audit methods execute from the actual installed assembly. Engine object identity,
ship lookup, condition storage, clocks and logging are controlled fixtures.
Native SendSignal/AddSignal run between fixture components. No signal algorithm
is reimplemented as the oracle; expectations assert truth tables or the resulting
native maps/queues/status. This is component logic testing, not a Unity power-grid
or GUI interaction replay. A skipped Unity-native call warning from JIT compilation
does not establish a gameplay exception; check the process result and final count.

Captured run: `obj/SIGNAL-BOX-AUDIT-OUTPUT.txt`.
