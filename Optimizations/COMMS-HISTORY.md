# Comms display and history retention

The local ATC channel opens the station's stored comms log. Vanilla
`GUIMessageDisplay.ShowPanel` sorts by delivery time ascending and then takes
100 messages: the oldest 100, permanently hiding newer stored traffic on reopen.
In the November 9 `option` fixture, OKLG has 773 entries. The native displayed
window ends at **2079-10-17 17:18:33**, exactly matching the reported symptom;
the stored log continues to October 28. Bundled-Mono replay reproduces it.

`MiscBugFixes/CommsDisplayPatch.cs` replaces that single `Take(100)` call with a
bounded tail selection. The native ascending sort, formatting, message objects,
live arrivals and conversation options remain in place. The
newest 100 appear in chronological order. Selection never edits the stored log.
The full-history ShowLog page keeps its full selection. Separate
[scrollback controls](../NavConsole/README.md#comms-and-diagnostic-scrollbacks)
repair pointer routing and comms auto-follow without changing message selection.

## Retention policy

Vanilla `Comms.TrimMessageLog` retains 20 messages **per peer**, with no age or
global limit. Fifty peers can therefore leave 1,000 messages after native
trimming. `CommsHistory.cs` additionally removes entries older than seven
24-hour periods of simulation time, with these exceptions:

- Retain unread **incoming** messages. An outgoing message's Read flag is not
  the native inbox predicate and does not independently exempt that entry.
- Retain the last stored entry per conversation, regardless of age. Native
  MFDComms uses that entry for reply context; insertion order is preserved even
  if delivery timestamps arrive out of order.
- Retain records present in the native in-flight queue, including overdue
  records. Queue and restored log entries need not be the same object: matching
  uses sender, receiver, delivery time and interaction name, the components of
  native ShipMessage.ID. No message is removed from the delivery queue.
- Retain malformed/unknown-owner entries and invalid timestamps. Skip sweeping
  if the delivery queue is unavailable or contains unresolved identities.

The policy intentionally shortens historical transcripts. It does not impose a
hard global cap: unread/pending/context exceptions may exceed any nominal limit.
No interactions, effects, replies, read flags or NPC destruction are executed.

Native surrender consumers and police/retrieval target selection inspect only
the preceding 120 seconds; hail suppression uses 600 seconds. Both windows are
wholly retained. Docking clearance, request timers, AI blackboards, queued
commands, plots and transaction state are separate from historical comms text.
Latest conversation context and unread messages protect the longer-lived UI
paths. Other mods may introduce additional history dependencies; this audit
covers the shipped 1.0.1.10 assembly and Ostrabus.

## Lifecycle and save safety

- Subscribe to `CrewSim.OnGameFinishedLoading` through the plugin lifecycle.
  On saved-game loading this fires before FinishedLoading becomes true. A short
  coroutine waits for that flag on the same CrewSim instance, then sweeps once.
  World replacement or plugin shutdown cancels the pending callback. Scene
  transits do not install per-frame checks or trigger another game-load sweep;
  ship editing is excluded. New games have no old history to trim.
- A prefix on `LoadManager.SaveGameData` sweeps before the native snapshot is
  built. Its caller has already cancelled/joined the previous save worker. The
  new background SavingJob starts only after the snapshot returns.
- Native `Ship.GetJSON` writes `Comms.GetJson()` for both shallow and materialized
  hulls, so both receive the trimmed log. No custom disk/archive rewrite or
  modification of a snapshot owned by a background writer occurs.

Cleanup runs synchronously on the main thread against one consistent queue
snapshot. It uses linear list compaction and reusable per-peer/pending-key
scratch collections, cleared in finally. No message/NPC/ship references remain
in those collections after a sweep. There is no idle polling or periodic sweep.
One BepInEx info line records the removal count and lifecycle phase; it does not
write to the player-facing command console.

The existing save archive is untouched until the game normally saves. If that
save fails, the runtime history has still been shortened, but Ostrabus has not
edited the previous archive. Retained entries use the original vanilla JSON
schema and load without the mod. Removed historical text is not recoverable from
the new save; an older save retains its original history.

## Offline validation

`Tests/CommsHistoryCheck` runs on bundled Mono and exercises the actual patched
ShowPanel selection, the save snapshot prefix, native Comms serialization and
restoration, and native unread/conversation accessors. It checks exact age
boundaries, overdue in-flight copies, outgoing/incoming Read semantics, stable
ordering and object identity, malformed records, idempotence, cancellation and
clearance metadata. Cecil verifies the native save worker/snapshot call order.
Unity panel preparation/rendering and restored interaction construction are
explicit fixture boundaries; no live NPC effects are evaluated.

An optional read-only save-archive replay streams commData using the game's
managed ZIP reader without extracting ship inventories. It verifies recent
AI/hail windows, unread inboxes, every peer's last reply context, queue counts and
clearance/request metadata, plus the source archive's unchanged SHA-256.

The read-only `option` replay passes 1,841 checks and removes 3,101 of 5,620
records at the save's own epoch with delivery protection applied. The source ZIP
hash is unchanged. The full production binding audit passes 171 patch classes
with no failures or warnings. Live scene rendering and a real new save write are
not exercised by this offline replay.

```powershell
dotnet build Tests/CommsHistoryCheck -c Release
dotnet run --project Tests/SaveJsonCompatibility/Host -c Release --no-build -- Tests/CommsHistoryCheck/bin/Release/netstandard2.1/CommsHistoryCheck.dll
```

Append a save ZIP path for the archive replay. Do not commit personal saves or
machine-specific paths. The earlier read-only fixture inventory contained 5,620
messages across 228 logs, about 2.45 MiB of compact JSON before compression.
Potential savings are modest compared with total save size; history deletion is
not presented as a major loading-performance fix.
