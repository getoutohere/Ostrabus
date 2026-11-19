# Native navigation ownership inventory

Read-only development scanner using Mono.Cecil on the installed game assembly.
The current reviewed 1.1.0.0 image has 32 direct withdrawal calls in 27 methods.
Its added RCS-loss abort has a native registered-Auto regression. The 1.0.1.10
counts below describe the original audit; `--check-review` now requires the exact
new reviewed assembly. Unknown binaries receive no inferred game-version label.
Run from the repository root:

~~~powershell
dotnet run --project Tools/NavigationOwnershipAudit -c Release
dotnet run --project Tools/NavigationOwnershipAudit -c Release -- --check-review
dotnet run --project Tools/NavigationOwnershipAudit -c Release -- method Ostranauts.ShipGUIs.NavStation.NavModCoursePlot EngageTrip
dotnet run --project Tools/NavigationOwnershipAudit -c Release -- destruction
dotnet run --project Tools/NavigationOwnershipAudit -c Release -- personnel
~~~

Output goes to ignored obj/NavigationOwnershipAudit, or the first argument's
directory. It records the assembly SHA-256, selected lifecycle/control call sites,
direct control-field stores and field-address accesses. The sorted reset caller
list contains 28 methods in the inspected 1.0.1.10 assembly. The withdrawal
inventory contains 31 direct calls in 27 methods. The separate tracked
withdrawal-review.json assigns each caller a disposition and evidence level.

The --check-review command validates caller coverage, per-method call counts and
the exact reviewed assembly SHA-256, and fails when revalidation is required.
It does not automatically accept changed game code. Even an unrelated assembly
change invalidates the review; the existing behavior-graph tools provide finer
dependency validation for their respective graph slices. The method command
prints native IL without loading Unity or changing the installed assembly.

The `personnel` mode inventories direct alive-field stores and calls to
`CondOwner.Kill`, `CondOwner.Destroy`, `RemoveFromCurrentHome` and `Ship.Destroy`
in both the native assembly and the current Debug plugin. It writes assembly
identities and surrounding IL to `obj/ObituaryStudy-20261119`. This distinguishes
native versus mod call sites without executing game code. Restoration writes and
runtime-object disposal require separate interpretation; they are not automatically
deaths or permanent deletions.

## Obituary recorder feasibility review (1.1.0.3)

Investigated against native assembly MVID
`5b1ee244-539d-4902-8d62-97490deee9d7`. The resulting implementation and its
limits are documented in [obituary evidence](../../Diagnostics/Obituaries/README.md).
The `personnel` command reproduces the direct-call inventory; the design below
records the original feasibility review.

| Observed mechanism | Evidence and recording consequence |
| --- | --- |
| Normal death | The four native callers of `CondOwner.set_Kill(bool)` are `Wound.Run`, `CondOwner.AddCondAmount`, the `_FallAway` coroutine and `Ship.Destroy`. The current plugin also calls it from `ShipboardActionSimulation.KillAndPrune`. Observe the actual `bAlive` transition, including partially completed calls; the misleading `Kill` getter returns `bAlive`. |
| Fatal conditions | `CondOwner.AddCondAmount` executes assets' `bFatal` flag. Read the existing condition IDs and values rather than reimplementing thresholds. Examples in `conditions/conditions.json` include `DcOxygen04`, `DcCO2PoisoningFatal`, `PulmonaryHemorrhage` and `CardiacArrest2`. |
| Living NPC removal | `AIShipManager.SpawnPassShip` can prune station population with `RemoveFromCurrentHome(true)` followed by `Destroy()`, without setting `Kill`. `CrewSim.DockAndDespawn` can destroy rejected arrivals based on `TIsValidStationDropOff` or reputation. Record the removal mechanism, not an invented medical death. |
| Ship destruction | `Ship.Destroy(bool)` clears its situation and removes people from the ship before killing them on the non-despawn path. Capture vessel evidence at entry and link crew events to it. The boolean alone cannot distinguish routine disposal from permanent removal. |
| Loading and replacement | `CrewSim.DestroyAndReload` destroys an old instance and reconstructs the same logical vessel. `CondOwner.SetData` restores saved `bAlive`; `PersonSpec.MakeCondOwner` copies it. Reconstructing a corpse is not a new death. Catch-up during loading must remain observable. |

The IL scan found four native direct stores to `CondOwner.bAlive`: `Awake`,
`SetData`, the death setter and `PersonSpec.MakeCondOwner`. `PersonSpec.bAlive`
stores occur in constructors. This supports transition-based recording for the
reviewed binary; reflection, other plugins and future binaries are not covered
by that claim. The 97 native direct `CondOwner.Destroy` sites include items,
UI previews, physical damage and ordinary disposal, so hooking every call and
calling it a death would be misleading.

### Recommended design

- A separate optional diagnostic feature, enabled once in CFG and automatic
  thereafter. No save fields, per-frame census, actor materialization or AI
  policy changes; no asset overrides are needed for observation.
- Three core observations: the death setter, person destruction and ship
  destruction. Add narrowly scoped context for population pruning, arrival,
  initialization and world loading/replacement. This is not a claim that three
  hooks alone provide adequate coverage. In particular, removal can erase the
  current ship before destruction; its caller must supply that context.
- Record stable NPC and ship IDs, name, faction, game/world/actor times, a
  process session and load-branch identifier, native/plugin identities, caller
  stack, alive-before/after, fatal conditions, relevant wound/environment data
  when already available, and bounded pre-destruction navigation context.
  Distinguish cached state from event-time state. Preserve reference type and
  ID: manufactured orbital/ground stations are not natural collision surfaces.
- Separate confirmed deaths, living removals, vessel losses, restoration and
  ordinary runtime disposal. Include the native or mod origin and uncertainty;
  an alive object being removed is not proof its persistent identity is dead.
  A shared vessel record avoids repeating the same evidence for every crewman.
- Capture detached data on the game thread; serialize and write on a worker.
  Reuse the bounded asynchronous writer pattern, not the fleet snapshot payload.
  Proposed limits: four rotating 16 MiB JSONL files, 64 queued records of at most
  32 KiB each. Mark truncation, dropped events and disk errors explicitly.
  Rotation must cover sessions too, not allow four extra files per restart.
- Query by NPC ID, ship callsign, time range and mechanism through the inspection
  API, reading the files rather than retaining an expanding in-game registry.
  Console summaries can remain optional to avoid noise.

There is no measured overhead yet. These are event-driven observations with
fast rejection of non-person objects, but context copying and stack capture
still run on the game thread. Benchmark ordinary play and population-removal
bursts before making a performance claim. No new broad navigation trace should
be necessary just to keep the recorder running.

### Required implementation checks

Exercise actual native deaths from fatal conditions, wounds, falling and ship
loss; living removal through population and docking rejection; mod boarding and
explicit debug pruning; already-dead save loading, real catch-up death, shallow
replacement, repeat kills, resurrection followed by death, nested destruction,
and reverting to an earlier save. Verify NPC identity, removal context and event
linkage without false death counts. A recorder fault must leave native return
values, exceptions, RNG consumption, navigation and death behavior unchanged.
Also test queue overflow, rotation, failed disk writes and interrupted shutdown.

Existing `CondOwner.DebugReportCauseOfDeath` only prints fatal conditions or
messages. `Inspection/ShipDestructionEvidence.cs` records some ship evidence
during time skips, but its history is transient and does not cover general NPC
death/removal. Neither is a durable obituary record.

The recorder can establish what operation removed someone and preserve evidence
before it disappears. A fatal condition or collision still does not prove the
upstream circumstances were legitimate; earlier navigation, environment or
combat defects may require follow-up investigation. It cannot reconstruct old
deaths retroactively, and bounded recording must report gaps honestly.

This is an IL inventory, not a reachability proof, defect counter, hook counter
or complete alias analysis. Field addresses can be reads. Use the
[collateral audit](../../Navigation/CONTROL-OWNERSHIP-AUDIT.md) and native
[reproduction suite](../../Tests/UnregisteredCourseCheck/README.md) for dispositions
and evidence. No game process, assembly, asset or save is modified.
