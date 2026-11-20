# Native save, arrival and handback integration

Study disposition: **abandoned on 2026-11-19**. See the [postmortem](../POSTMORTEM.md).
This document preserves historical qualification, not an active adoption plan.

Status: offline integration prototype, requalified for 1.1.0.0. No installed patch, game
asset or descriptive behavior graph is changed. This milestone executes more
of the native boundary; it does not certify a production replacement executive.

## Coherent native snapshots

Native `LoadManager.SaveGame` calls `SaveGameData` synchronously, then passes
its `SaveDto` to `SavingJob`. The worker serializes those objects later. A
separate executive sidecar written at a different time could describe a different
world, even if both files look individually valid.

`NativeSavePort` instead captures one bounded actor record with the native DTO,
under a main-thread operation fence. Its prototype embedding is one namespaced
`JsonGameSave.aCustomInfos` entry, `Ostrabus.MissionExecutive=...`. Native
`CrewSim.SetCustomInfos` ignores unknown keys; the native parser is exercised in
the tests. Other custom-info entries are preserved. The record contains reducer
state, the explicit Navy profile and pending arrival evidence, not a second
copy of ships, markets, inventories or people.

The record binds the native snapshot digest, epoch, native/plugin binding and
existing model/reducer/profile versions. Tests reject missing ships, different
save generations and altered same-epoch hull data. The native collector catches
per-ship serialization exceptions and continues; the prototype therefore checks
that its expected hull set was actually captured before accepting the result.

The DTO is detached before a worker receives it. Tests retain a shallow crew
save-object alias, advance the live simulation through arrival, mutate that old
object, then run the actual threaded writer. Loading restores the earlier
snapshot and finishes the route once with the expected fuel settlement.

This implementation prioritizes an inspectable correctness experiment. It
serializes/copies snapshot data more than once to detach and fingerprint it.
**No save-time or allocation improvement is claimed.** A production version
needs a measured immutable-snapshot/streaming-digest integration; copying the
research implementation wholesale would be inappropriate for large saves.

## Native wire format matters

The tests run native `LoadManager.SaveGameData`, `SavingJob` (both synchronous
and threaded), native ZIP creation/extraction and LitJson DTO parsing. Native
LitJson reads some empty arrays as `null`. Hashing reconstructed DTOs would thus
reject a genuine archive. `NativeArchiveImage` preserves the source JSON strings
for snapshot validation before that normalization, and constructs fresh native
DTOs from those same strings for restoration. Object-property order is
canonicalized for hashing; array contents and order are retained.

Ship geometry/inventory serialization, portraits, unrelated subsystem snapshots
and physical scene reconstruction remain explicit data providers. This is not
an end-to-end Unity load, and does not exercise the outer save UI, cancellation,
Steam Cloud or power-loss archive replacement behavior. Tests write only ignored
workspace files under `obj/ShipExecutiveNativeSaves`.

## Operation fence and arrival ordering

The fence rejects foreign-thread world access and reentrant simulation during
capture. A save requested during an actor operation or native crew transfer is
refused before collection; the runtime caller must defer it to the completed
operation boundary. This is coordination on the simulation's owner thread, not
parallel Unity simulation or a worker-held world mutex.

The model names this acknowledgement `InvocationBoundaryReady`. The integrated
port checks that its coordinator is active and the actor is inside an owner-thread
operation; it does not claim that an empty callback committed a disk checkpoint.
The standalone fixture satisfies the same boundary after its explicit flush.

Raw native command receipt is fenced before its constructor, through admission.
This prevents `TakeFuel.SyncFuel` from running inside a captured snapshot. Typed
callers have already executed constructors before `AddCommand`: arbitrary
third-party reentrant constructors are not made safe by an admission hook.
Production adoption needs a verified producer boundary, not a rollback promise.

The integrated arrival port retains one intent in memory and excludes saves
from the synchronous native effect. It does not write a file for each arrival.
The original standalone barrier/file fixture remains as a reference negative
control and is not the proposed runtime persistence mechanism.

A standalone saved `ArrivalPrepared` label remains ambiguous. A record captured
by this coherent port can additionally prove it was prepared before native
invocation, because capture cannot interleave with the effect. That marker must
agree with the original saved phase, not merely the reducer's post-restore
`ArrivalUncertain` phase. Tests reject a false pre-invocation marker on a partial
transfer. A genuine coherent prepared record returns to `ArrivalReady`; a saved
partial/unknown effect stays uncertain and is not replayed.

## Whole-participant readback

`NativeArrivalObservation` captures the incoming root, docked group members,
affected crew IDs, destination and correlated effect ID before native docking.
The qualification bounds are 32 hulls and 256 crew; these are explicit refusal
bounds for this prototype, not new game rules.

Native `CrewSim.DockAndDespawn` now executes. The shipped
`TIsValidStationDropOff` forbids `IsDead`, `IsDerelictVictim` and `IsOKLGPirate`;
native code also rejects a faction reputation classified as `Dislikes`. Native
trigger evaluation, reputation classification, transfer/destruction branching,
claim/unclaim calls, WanderSoon scheduling, hull hiding and AI unregister execute.
Physical movement/destruction, visual presentation, reputation inputs and the
scheduling implementation are providers; company/roster policy is not qualified.

The observer requires native retirement of the root and accounting for every
captured participant. Present crew must be at the destination. Destroyed crew
need an actual destruction witness from their captured object, retained with
the correlated intent; absence alone is never that proof. Group hulls must
remain present. It does not force survival, prune hulls, transfer remaining crew,
retire every attached controller or infer success from `Dock` returning success.

One newly qualified outcome is `actor-retired-with-all-crew-accounted`. It can
include legitimate native destruction, so the older all-crew-at-destination
description is insufficient. A partial native exception preserves the already
transferred crew and unresolved intent. Tests save/load that state without
invoking docking again. Completed mixed outcomes and a destroyed captain also
survive native archive reload without resurrecting anyone.

Native unregister removes the arriving controller before the observer necessarily
has complete evidence. Pending readback is therefore permitted against the same
canonical retired hull without requiring its old AI registration. It cannot
authorize a new flight or second arrival. Restoring a missing canonical crew
reference can finish observation without another docking call.

## Handback

In addition to the existing ordinary funded return projection, the adapter can
export a native main-patrol approach to a non-docking waypoint. It copies the
canonical goal and native flight payload into the real `FlyToPatrol` and main
sequence cursor 2, verifies native serialization, then releases authority.
The test saves the resulting native controller, reloads it through native tree/
blackboard APIs and resumes the real dispatcher. No executive metadata remains
for the released actor, and the old executive cannot advance it.

A nonempty executive inbox, active reaction, combat lock, suspended mission or
pending effect defers handback. It does not discard a command, erase combat or
manufacture a stationary ship to make export convenient. The new inbox guard
closes a gap in the earlier return-only helper, which checked the native queue
but did not know about the separate executive inbox.

This is still a qualified subset, not safe arbitrary uninstall/downgrade. While
vanilla ignores the custom-info key, that alone does not prove it can continue
every executive mission. Native-only saves are not silently adopted on restore.

## Remaining gates

- Route-completion integration is now qualified with the shared GUI/PASS
  coordinator; its native AI negative control remains in the suite.
- Qualify materialized navigation/tow groups, actual gas containers and
  weapon/decoy eligibility; active Ceres policy remains unqualified.
- Exercise actual Unity crew/scene lifetimes and the final native load-to-dispatch
  handoff before runtime adoption. Multi-actor activation/rebinding and general
  disable/removal remain outside this single-actor save port.

No production hook is removable based on these tests. The combined evidence is
[test-results.json](test-results.json); reproduce it with the existing
`Tools/ShipExecutiveStudy/Prototype/Run.ps1` runner.

## 1.1 world schema and checkpoint version

The current binding is `native-navy-1.1.0.0-prototype-v2`; the embedded executive
record uses schema 2. Standalone and native-archive restore reject older bindings
before reconstructing the world. The 1.0 receipts are historical evidence, not
checkpoint migration promises.

The fixture collector/restorer now includes physical and deferred hull identity,
raw/weighted RCS counts, exhaust metadata, nonzero route capability snapshots and
circular-orbit phase. Duplicate physical/deferred IDs or a deferred population
changed during capture are refused. Representation loss alone is not arrival
evidence, and a deferred actor is not silently adopted.

Native `LitJson.JsonMapper.WriteValue` omits empty collection properties. That
loses a distinction used by 1.1 `StarSystem.Init`: two absent stellar arrays can
generate environments, while explicit empty arrays do not. The prototype's
existing bounded record carries two empty-array bits, included in its snapshot
digest. Its archive reader reconstructs shape only from explicit native wire
arrays or the same archive's record. Unknown native-only archives get no invented
shape metadata. Tampering with those bits fails validation before world mutation.
This qualifies the executive codec, not a general repair to native serialization
or proof of safe mod removal. Full native load/scene initialization is still a
production gate.
