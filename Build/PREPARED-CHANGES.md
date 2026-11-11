# Historical preparation: changes after 0.2.2

This preparation was subsequently committed as `191d8d9` ("Improve AI integrity,
crew controls and navigation UI"). It retained version 0.2.2 and excluded the
pending torch changes. The current preparation is [0.3.1](RELEASE-0.3.1.md).
The scope and validation below describe that earlier source snapshot.

## Included

- Repair shallow AI flight/fuel handoff, native-priority station returns,
  escape-target invalidation and shared RCS refill accounting. Simplify regional
  observers/shortcuts while retaining authored AI behavior and documented scope.
- Restore native scavenger emergency-assistance eligibility and expose capped
  per-ship refueling. Add protected force-pruning for explicit named dock groups.
- Add machine-readable retriever/scavenger/deployer transition graphs, derived
  diagrams, patch audits and deterministic assembly dependency revalidation.
  Compressed baselines contain hashes and dependency metadata, not game DLLs or
  decompiled method bodies. Their documented acceptance history is retained.
- Add configurable helmet/EVA-battery crew shortcuts and unsafe-air checks;
  expose the station-docked hull policy in the PDA settings app.
- Repair comms history selection/retention and rendered log scrolling; improve
  Socials portraits and filter construction drones. Profiling remains opt-in.
- Replace stale/incomplete nav silhouettes with cached parallel exterior traces,
  size-based visibility and correct map-coordinate viewport culling.
- Add optional hull-shaped free-ship physical contacts with bounded cached
  geometry, swept checks and native impact handling. Weapon hit tests, native
  sizing, planetary/station handling and AI avoidance remain unchanged.
- Include inspection extensions, selected fixtures, offline harnesses and the
  documented assets-first/native-mechanism implementation policy.

## Explicitly excluded again

The two torch gameplay changes still await in-game testing:

1. Reactor fuel-cache synchronization: `MiscBugFixes/TorchFuelCachePatch.cs`.
2. Torch autopilot fuel/control refactor: `MiscBugFixes/TorchCourseFuel.cs` and
   `MiscBugFixes/TorchCourseFuelPatches.cs`.

`Tools/TorchNativeOracle/` is excluded with them. Existing torch documentation
remains marked pending playtesting and explains that its implementation/harness
may be absent. The previously committed standalone torch calculator is retained.

## Preparation checks

The torch-excluded source snapshot builds independently in Debug without
warnings. Its native full-plugin binding audit binds **179** Harmony classes
without failures or warnings; the full local Debug build binds **184**, including
the five excluded torch patch classes.

Module checks cover navigation/scav/refill/retargeting, hotkeys, crew rest,
pruning, comms, Socials/portraits, nav rendering/collisions and PDA controls.
Graph validation passes 49/79/63 model checks for the three slices. Selected
native fixtures and all compressed baseline metadata are scanned for personal
absolute paths. Binaries, complete saves, raw captures and build output remain
ignored.

The outline changes have user-confirmed live rendering/zoom feedback. New physical
collision geometry has extensive offline/native-boundary coverage; live impact
feel and complete Unity damage/crew effects remain to be observed. Existing
simulation gaps and investigations remain documented rather than presented as
completed fixes.
