# Ship destruction can abandon valid COs

Status: diagnosed; no runtime fix implemented.

## Apartment-to-OKLG transition, November 5, 2026

The same fault occurred while `CrewSim.SaveToShallow` converted O-GXD (Dream)
from materialized state after transit out of the apartment. The log named two
remaining parentless roots:

- `ItmFusionPelletFeeder01Off`, `da02ca08-4d23-4ab3-a17f-1252d751cf72`
- `ItmFusionLaserArray01Off`, `6e0ec168-a6cb-40a8-ab95-95bfe87c763c`

Immediately following the orphan warning, two exceptions had this stack:

```text
Ship.GetCOByID
Electrical.CleanUp
Electrical.OnDestroy
UnityEngine.Object.DestroyImmediate
Ship.Destroy
CrewSim.DestroyAndReload
CrewSim.SaveToShallow
Transit _Go.MoveNext
```

The secondary exception is explained precisely by native ordering:
`Ship.Destroy` abandons CO cleanup, later sets `mapICOs = null`, and then calls
`DestroyImmediate(gameObject)`. Remaining electrical components receive Unity
`OnDestroy` without their CO's ordinary managed destruction having cleaned them
up first. `Electrical.CleanUp(true)` queries connected IDs through cached
`coShip.GetCOByID`; that method checks the ID but not the now-null registry.
Its `mapICOs.TryGetValue` consequently throws. A lookup null guard alone would
hide this symptom while leaving the skipped CO destruction defect unresolved.

`SaveToShallow` serializes the ship and COs before calling `DestroyAndReload`;
the log therefore does not establish loss of those parts from the saved state.
Incomplete managed cleanup and retained references remain the demonstrated
concerns. The preceding massfix run examined 720 containers on five materialized
ships and corrected zero objects.

Captured evidence: `obj/TRANSIT-PLAYER-TAIL.txt`,
`obj/TRANSIT-API-LOGS.json`, `obj/TRANSIT-CONSOLE.txt`.

## Observed case

During save-reload teardown, the console reported:

    WARNING: Unable to destroy 67 orphaned COs while destroying ship VNCA

The detailed Player.log listed Caleb Tanner Barton's body parts, inventory,
and other attached COs. Crucially, the final entry was Caleb himself, with no
parent. These were not all genuinely orphaned children.

The log places this after `Loading save via Zip Extraction to Memory`, rather
than at the earlier departure from the Venus ATC region. The separate
`Destroying leftover CO: ItmDrinkPouch01` message comes from later global
cleanup, not the VNCA warning's own cleanup loop.

Captured evidence: `obj/VNCA-TEARDOWN-LOG.txt` and
`obj/ORPHAN-CO-REPRODUCTION.json` (local diagnostic artifacts).

## Root cause

In vanilla `Ship.Destroy(bool isDespawning = true)` (`Ship.cs`, approximately
lines 1326–1365 in the local decompilation), the loop:

1. Counts COs skipped because they have a parent.
2. Finds a root, removes it from the ship, and destroys it. Removing/destroying
   that root can also remove its descendants from the ship's CO collection.
3. Compares the earlier skipped count against the collection's new count.
4. If equal and nonzero, reports orphaned COs and abandons the loop.

The comparison mixes two different collection states. Equality does not prove
that the remaining collection contains only parented objects.

Minimal reproduction, preserving iteration order:

    [childB -> B, childA -> A, B -> null, A -> null]

The loop skips two children, then removes B and childB. Two objects remain:
childA and its valid root A. The stale skipped count is also two, so vanilla
incorrectly aborts. The local reproduction demonstrates this bookkeeping
failure; it is not a full Unity destruction test.

## Memory implications and limits

After abandoning CO cleanup, `Ship.Destroy()` continues and calls Unity's
`DestroyImmediate(gameObject)`. COs still in that Unity hierarchy can therefore
lose their native objects without receiving the game's normal
`CondOwner.Destroy()` cleanup.

`CondOwner.Destroy()` removes global registry/index entries and animator
registrations and clears managed state. `CondOwner.OnDestroy()` only disposes
the clock and decrements the debug CO count. In particular,
`CrewSim.COAnimators` is a static list whose removal happens in
`CondOwner.Destroy()`, so bypassing it can retain destroyed animator wrappers.

This confirms a cleanup defect and a reference-retention risk, not that all 67
CO graphs remain allocated forever. `DataHandler.Init()` later destroys live
leftovers and clears `mapCOs`; Unity-destroyed entries can compare equal to
null and skip that explicit destruction. Other scene-owned references may
also become collectible during reload. Exact retained memory has not been
measured.

## Proposed fix and validation

Replace the stale-count termination test with progress detection. Continue
normal root removal/destruction while the mapped CO count decreases. Warn and
stop only when a pass cannot make progress; distinguish no available root
from a root whose removal failed. Preserve native destruction methods and
their ordering rather than force-deleting every remaining child.

Validate child-before-parent ordering, cascading subtree removal, multiple
roots, empty collections, genuine orphan/cycle cases, and failed removal
without an infinite loop. Confirm normal `CondOwner.Destroy()` runs for the
remaining root and descendants. For live validation, observe reload teardown
and animator/index cleanup; disappearance of the warning alone is insufficient.

Implementation caution: a previous docking investigation found output changes
when this offline host copied a large native grid builder through an identity
Harmony transpiler. Validate any patch to the large `Ship.Destroy()` method
against native behavior rather than assuming a successful patch installation
preserves it.
