# Tile helper CO retention during transit

Always-on fixes in `TileHelperCleanupPatch.cs` and `AsyncShipCleanupPatch.cs`.
These repair destruction boundaries, not a periodic registry sweep.

## Confirmed defect

`TileUtils.PadTilemap` creates dedicated `TIL` grid helper condition owners.
They enter `DataHandler.mapCOs` and point at their owning Ship, but are not normal
entries in its item map. `Tile.Destroy` clears Tile references without managed
CO destruction. Native `CondOwner.OnDestroy` disposes its clock and updates a
debug count but does not unregister the CO or clear its collections. Unity
deletion therefore leaves globally rooted dead wrappers, data and old hulls.

Barter/preview/rating destruction unregisters mapped items and destroys the
hierarchy, also leaving helper IDs behind. Their helper-to-owner references can
retain the old copy's uncleared item map.

The live OKLG transit reproduction returned to the same apartment scene and
83,131 live CO components, but the registry grew by 78,081 destroyed wrappers:
exactly 64,753 ordinary-hull tiles plus 13,328 tiles from seven barter copies.
Managed used memory grew from 6.68 to 8.70 GiB across four natural collections.
Async Unload did run. See [the diagnostic report](../Inspection/MEMORY-DIAGNOSTICS.md).

## Cleanup and ordering

The Tile patch captures only a live, not-yet-destroyed CO named exactly `TIL`,
attached to a `bShipTile` Tile on the same GameObject. Native `Tile.Destroy`
clears room/power/path references first; its postfix cleans the helper CO.

Physical deletion stays with the native caller. `TileUtils.TrimTiles` accesses
`tile.gameObject` after `tile.Destroy`, so ordinary `CondOwner.Destroy` would
delete the object too early. A Harmony reverse patch copies native CO destruction
into a private entry point, replacing **only** its single `DestroyImmediate`
call with a stack pop. Native scheduler cancellation, index/registry removal,
condition/collection cleanup, owner clearing and destroyed flag are preserved.
The caller deletes the Unity object at its original point, invoking native
OnDestroy and clock disposal. Changed physical-deletion IL fails installation.

There is no hook on the general `CondOwner.Destroy` entry point and no rewrite
of `Ship.Destroy`. Equipment, crew, rooms, foreign GameObject references and
non-ship tiles are excluded. The destroyed flag makes cleanup idempotent.

The three async destruction methods preserve native control flow, including
the preview early return. A wrapper cleans helpers owned by that exact hull
immediately before hierarchy deletion. Mapped-item removals are identity checked:
the registry entry must be the object in that copy's map, still owned by the
copy. Newer same-ID objects and objects transferred to a live hull survive.
Other async mapped-item destruction remains native.

This does not repair the separate stale-count orphan loop in `Ship.Destroy`,
or sweep already-destroyed objects in the current process. Restart with the
new library for a clean baseline. No save conversion is required.

## Cost and validation

Work occurs only at actual destruction boundaries. No polling, tracking,
retained game references, diagnostic hooks, forced GC or asset unloading is
added. Proper managed cleanup incurs the native lifecycle's normal work and
allocations. Its private reverse entry point does not run temporary observer
prefixes on public `CondOwner.Destroy`; diagnostic registry/Unity state remains
the meaningful comparison rather than that entry-call counter alone.

`Tests/TileHelperCleanupCheck` executes native Tile destruction, copied native
CO cleanup, native OnDestroy and all three patched async methods with fixture
Unity object access/deletion. Its 1,036 checks cover managed cleanup, caller
deletion order, clock disposal, exclusions, idempotence, same-ID replacements,
transferred/foreign ownership, preview policy and 1,000 cycles without registry
growth. The startup audit checks reverse-patch and transpiler binding. The
offline fixture does not host a Unity scene.

## Live validation after deployment

The deployed DLL hash matched the built cleanup DLL. The user repeated the
original loop and returned to the same apartment scene. Both tests destroyed
exactly 78,081 helpers: 64,753 ordinary-hull and 13,328 barter-copy helpers.

With the fix, registered COs, live CO components and native debug CO count all
returned to 83,131. There were **zero** destroyed Unity wrappers left in the
registry, zero entries marked destroyed, no detached/async owner entries and
zero dead animator entries. The async-loader dictionary was empty. All sampled
retained wrappers shown had completed managed cleanup: no conditions, queued
interactions or owner reference remained. Diagnostics reported zero errors.
This confirms removal of the reproduced helper retention, not just suppression
of its warning. All diagnostic hooks/listeners were stopped after capture.

Process resident memory still rose from 9.73 to 12.61 GiB, private memory from
11.89 to 14.81 GiB, and managed used estimate from 6.33 to 7.49 GiB; natural GC
count advanced from 261 to 266. This does not establish that all memory growth
is fixed or attribute the remainder to a particular cause. Allocator reservations,
collection timing, caches and other retention need separate evidence. Artifacts
are retained under ignored `obj/MEMORY-FIXED-*` JSON files.
