# Boneyard Turnover: risk investigation

**Investigation record, followed by an implemented standalone 0.1.0.** Reviewed against
Ostranauts 1.1.0.2. The standalone study now passes **252 checks**, including 68
additional risk checks beyond the earlier 184-check implementation study.
No player save, installed asset or running game was modified.

The production implementation now wires these controls into actual field
resolution, native protection indexes, staged replacement and archive hooks.
See [validation](VALIDATION.md) for the additional executed evidence. Descriptions
of "prototype" or future implementation below record the investigation's state
at the time; full live-scene acceptance remains pending.

## Ordinary arrivals, including SFFWD

These risks are not all expected events on an ordinary arrival. Several controls
deliberately construct corrupt inventory, malformed metadata or supplied cleanup
exceptions. They define refusal/failure behavior, not observed incident rates.

Native `GUIFFWD.FFWD` calls `StarSystem.Update`; Ostrabus's existing adapter splits
that call into ordinary world updates. The native update processes collisions,
then `AIShipManager.Update`, which detects an ATC change, performs RegionCleanup
and loads the new environment through SpawnPOIs. The same pre-environment-load
boundary is available during normal flight and SFFWD. No new SFF simulation or
separate timeskip turnover engine is indicated by this feature.

The expected straightforward case is a deferred or genuinely shallow, saved-only
wreck. SFF does not guarantee quiescence: native AI and collisions still run,
so ownership, occupancy, attachments and targeting must be checked at application.
Queue ordering and one-time departure consumption are routine integration
requirements. Orphan inventory and injected destruction failure are defensive
exceptions handled by retaining uncertain hulls or stopping, not by adding a
general repair subsystem.

## Conclusions

The model is feasible without making subsequent saves dependent on the plugin.
The first runtime implementation should operate on deferred wrecks and genuinely
shallow, saved-only visited hulls. It should temporarily skip fully loaded or
partially loaded hulls, unresolved occupants, active targets and uncertain data.
That retains normal visited salvage as a candidate without treating every
instantiated Ship as safe to destroy.

The investigation does **not** justify automatic replacement of arbitrary live
ships, forced unloading, forced AI retargeting or repair of unrelated orphaned
inventory. Those are outside this feature's scope.

| Risk | Executed result | Implementation consequence |
| --- | --- | --- |
| Removing a normal visited shallow hull | Actual native destruction removes its hull, ownership, base CO and collision/loaded registrations. | Native removal is usable for the tested saved-only shape, with eligibility and postcondition checks. |
| Orphaned runtime inventory | Native destruction can finish while an item with an unresolved/external parent remains registered against the destroyed hull. | Skip nonempty runtime item maps and malformed base-object state; do not load or repair them just to refresh the field. |
| Unresolved occupants | A hull with a PersonSpec can report zero resolved people. | Check native person records and saved actual crew, not only GetPeople count. Native `aShallowPSpecs` contains NPC spawn markers, not occupants; 0.1.3 distinguishes these and retains unknown entries. |
| Incoming navigation/target references | Native deletion does not clear another ship's current references. Native CheckTargets can also leave a target ID/situation behind. | Preserve currently targeted wrecks; do not redirect controllers or assume deletion will make them choose another target safely. |
| Spawn queue ordering | A replacement made after the old ID was queued is not materialized by that queue. Repeated native loads can queue duplicate IDs. | Commit due turnover before native environment loading. Consume a departure once; do not use each load callback as a new visit. |
| Deferred placement overlap | An actual successful native safe-coordinate call can place a candidate on a deferred wreck. | Check deferred objects and other staged replacements in addition to native physical checks. |
| Save thread reads later state | A frozen-metadata prototype writes the original snapshot despite later source mutations, on the actual saving worker. | Capture immutable state with native save data; never read the later live world from the writer. |
| Failed/cancelled saves | Controlled pre-commit failure retains the old archive; native cancellation publishes no new metadata in the fixture. | Keep metadata in the native archive and share its commit path. No independent external-file publication. |
| Corrupt, wrong-world or future metadata | The bounded prototype rejects the tested malformed, duplicate, oversized, unsupported and mismatched inputs. | Clear/stage incoming state conservatively; absent/invalid metadata grants no catch-up pass. |
| Mid-destruction exception | A supplied base-CO cleanup failure occurs after the ship has already been deregistered. | Native destruction is not transactional. Halt the batch, verify what remains and report partial cleanup; do not promise rollback. |

## Native cleanup evidence

### Ordinary visited, saved-only shallow hull

The fixture has `fFirstVisit > 0`, `LoadState.Shallow`, native saved item data,
an empty runtime item map and an actual native base condition owner. The study
executes `Ship.Destroy(false)`, `CondOwner.Destroy`, `StarSystem.RemoveShip`,
collision-queue unregistration and loaded-ship removal. The old hull no longer
appears in the actual native `StarSystem.GetJSONSave` enumeration.

A separate projection runs the real `LoadManager.SaveGameData` through its
initial saved-CO trimming phase. Saved item records whose `strRegIDLast` owner
is absent are removed; records with a surviving owner are retained. This study
stops immediately after that phase, before Unity-dependent save capture. It is
not a full save-scene execution.

Saved item payloads are therefore different from live instantiated inventory.
A large `JsonShip.aItems` array does not by itself require destroying each item
as a Unity object before the old hull can stop being saved.

### Broken or partially loaded hull

The orphan counterexample supplies a runtime item whose parent lies outside the
ship's item map. Native destruction emits its orphan diagnostic and breaks out
of the item loop, then finishes removing the hull. The item remains in
`DataHandler.mapCOs`, is not destroyed and still points to the destroyed Ship.
This validates the conservative skip, not a new general-purpose cleanup fix.

The structural prototype rejects any nonempty runtime item map, non-shallow load
state, saved or unresolved occupants, active AI, native player ownership,
dock/tether group or supplied plot/bounty obligation. Temporary protections do
not permanently exclude a visited wreck: after removing them, the saved-only
control becomes structurally eligible again.

This is not yet the complete production eligibility reader. Field membership,
native claim resolution and plot/bounty indexing still need to be wired to the
actual field catalog and exercised together. The existing prototype receives
obligation IDs explicitly rather than pretending to execute plot logic.

## References and region-entry ordering

Native removal clears the destroyed hull's own targets. It does not walk other
ships and clear their references. The study executes that behavior and confirms
that `CheckTargets` alone is not a complete repair.

The structural controls preserve wrecks named by live scan/combat/undock/station-
keeping pointers, situation pointers, `TargetData`, current target IDs and the
corresponding saved target IDs. Before each destructive pair, revalidate those
references and other protections. This lets existing agents keep their goals
instead of adding a parallel retargeting policy.

The queue study executes native `LoadEnvironmentForRegion`,
`ProcessDerelictSpawnQueue` and `UnloadEnvironmentForRegion`. It supplies the
final ship-construction callback, so no full interior or scene is created:

1. Publishing the new deferred record before loading queues the new ID.
2. Loading first, then removing the old record and publishing the new one causes
   the pending old-ID lookup to fail; the new record remains unmaterialized.
3. Calling native loading twice can enqueue the same ID twice.
4. Native environment unloading clears pending IDs even on its null-region exit.

The recommended entry boundary remains immediately before native environment
loading, after successful world initialization. A departure must be committed
once and consumed once. A save load, repeated callback or same-region UI action
must not become an additional absence or replacement roll.

## Placement

Two distinct native hazards are reproduced:

- `SetupDerelictPosition` continues after a supplied failed safe-coordinate
  search, returns a radius and applies the body attachment anyway.
- `SetSituToRandomSafeCoords` itself checks physical ships and the nearest body,
  but not other deferred stellar wrecks. With the study host's deterministic
  random input, its repeated successful candidate overlaps an existing deferred
  wreck exactly.

A replacement planner must obtain a successful native search, verify the actual
position against the authored field bounds and reference, then check existing
deferred wrecks and other staged candidates as well. All those checks precede
destruction. Failure keeps the predecessor; it does not select a different hull
to delete. This is spawn validation, not a change to collision physics.

## Persistence and rollback

The archive prototype captures bytes on the main thread, then lets the real
native saving worker add those immutable bytes to its ZIP. Changing the source
trip, curve setting and member ID after capture does not change the saved data.

The bookkeeping prototype verifies:

- A completed trip cannot be consumed again after loading its after-return state.
- Restoring an earlier snapshot restores its earlier pending trip, rather than
  inheriting completed work from the later session. The corresponding native
  world must likewise be restored; a prototype bookkeeping test alone does not
  demonstrate the full world transaction.
- Wrong-world and wrong-epoch data, unknown schema, future departure times,
  duplicate members, negative ages, invalid shape, missing trip/fields,
  malformed member types, truncated data and oversized payloads are rejected.
- A supplied I/O failure immediately before native ZIP commit leaves the previous
  committed archive and metadata byte-identical. This tests the selected failure
  boundary, not every possible disk fault or operating-system crash.
- Native cancellation publishes no optional metadata and does not alter an
  unrelated committed fixture. The fixture's resolved temporary path is checked
  before invoking native cancellation's recursive cleanup.
- A supplied optional-metadata failure is contained: native saving still succeeds
  and the resulting fresh archive has no old metadata entry to resurrect.

Native parsing and plugin-free native resave were already exercised in the
earlier study. Full scene loading, cancellation timing races and transition/save
interleaving remain integration acceptance cases. There is no claim that the
standalone runtime hooks already implement this bookkeeping.

## Non-transactional deletion: residual risk

A fault-injection control throws from the base condition owner's cleanup while
running the rest of native `Ship.Destroy`. At that point the hull has already
been deregistered and marked destroyed, while its base CO and situation remain.
Blindly retrying destruction is ineffective because the native method exits for
an already-destroyed hull. Re-registering that partially destroyed object would
not be a rollback.

The implementation should therefore:

1. Finish every template, identity, placement and protection check before removal.
2. Admit only the supported shallow saved-only shape, retaining uncertain cases.
3. Treat native removal as the irreversible boundary; do not yield or permit a
   save between removal and a verified one-for-one publication.
4. Verify registries, base CO, runtime items, references and queue state afterward.
5. On exception or failed postconditions, stop further turnover for that operation
   and log the affected IDs and incomplete stage. Do not blindly spawn, reroll,
   re-register the broken object or continue deleting other candidates.

Unexpected native or third-party callback exceptions cannot be eliminated by a
wrapper. The bounded guard reduces exposure, but this residual risk must remain
explicit during live acceptance. No new catch-and-continue cleanup policy has
been installed into the game.

## Investigation conclusion and implementation follow-through

The production field/obligation reader, guarded pair application, private
reproducible randomness and archive/load handling are now implemented and tested
together. The separate installer preserves other plugins and CFG files; full
and custom deployment scripts do not install or remove this standalone plugin.
One eventual live acceptance pass remains. Destructive turnover is not enabled
by running the investigation or its offline harness.

The native lifecycle checks supply Unity destruction, transform enumeration,
editor-only parent-validation calls and logging. The base-CO destructor retains
its native managed cleanup; its terminal Unity object lookup is replaced only
inside the offline fixture. These boundaries do not prove rendering, complete
Unity scene disposal or arbitrary loaded-item destruction.
