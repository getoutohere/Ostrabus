# Same-save reload memory investigation

Audit date: November 18, 2026. Native code and assemblies: Ostranauts 1.1.0.2,
Unity 6000.3.27f2. This investigation added no game behavior or memory cleanup.

## Assessment

Repeated loading demonstrably expands Mono's reserved heap. The native loader
also creates substantial, avoidable overlap between the old world, archived
input, old saved-item records and replacement deserialized data. These are
concrete explanations for memory pressure and a rising allocation high-water
mark, but do not establish a complete accounting of the reported increase.

No large accumulating object leak was demonstrated in the paths examined.
That is not proof that the game or mod has no other leak. A heap reference graph
was not obtained, and registry counts do not count every reachable old object.

The user chose to resume ordinary play without debugger overhead. The follow-up
audit and tests below ran offline and did not query, attach to, collect garbage
in, or modify the running game. Temporary live memory observers had already
been stopped.

## Reload observations

One debug-enabled process loaded the same save twice. These snapshots include
natural collections; no collection was forced in the game.

| Counter | First loaded scene | Reload completed | Later natural collection |
|---|---:|---:|---:|
| Process resident memory, GiB | 10.69 | 16.49 | 16.56 |
| Process private memory, GiB | 12.98 | 18.93 | 18.98 |
| Mono used memory estimate, GiB | 7.53 | 9.56 | 9.15 |
| Mono reserved heap, GiB | 7.77 | 13.41 | 13.41 |
| Registered condition owners | 75,690 | 75,690 | 75,690 |
| World ships | 382 | 382 | 382 |

No destroyed Unity wrappers remained in the condition-owner registry after
either load. Of 67 weak samples taken from the old scene, 54 were collected;
13 destroyed wrappers remained observable after multiple natural collections.
None retained a native Unity object or a matching registry entry. This small
sample does not establish either the total retained size or the owning roots.

Before old-world teardown, used memory had already reached 10.77 GiB and the
reserved heap 12.00 GiB. Teardown removed all registered condition owners but
left 548,060 saved-item records. After several natural collections, the empty
scene used 5.74 GiB. Do not attribute that entire number to saved-item records:
game definitions and other process-wide data remain loaded too.

A previous process without the debugger showed a natural decrease from about
17.5 to 12.5 GiB used while Mono reservations remained about 25 GiB. That is
additional evidence of garbage collection and retained allocator capacity,
rather than proof that every additional resident byte belongs to a live world.

The observed loaded plugin identified itself as 0.6.0, with MVID
`d3d768b8-9501-4826-a8ce-fccdbe3c8164`. It was not the prepared 0.7.0 binary.
The offline source audit describes the checked-out code; the saved-item lookup
implementation tested below has remained unchanged since the repository's
baseline commit. Avoid silently treating every source observation as evidence
about an identical deployed binary.

## Native loading lifetime

| Method | Verified behavior | Consequence |
|---|---|---|
| `Ostranauts.Core.LoadManager.OnLoadSelectedSave` | Extracts the entire archive into `Dictionary<string, byte[]>` before `CrewSim.Teardown`. | New raw input overlaps the fully populated old world. |
| `Ostranauts.Tools.DotNetZipCompressor.ExtractArchive(string)` | Extracts each entry into a growing `MemoryStream`, then copies it with `ToArray`. | The current entry temporarily has both an extraction buffer and its retained copy. |
| `DataHandler.JsonToData<TJson>(string, Dictionary<string,TJson>, Dictionary<string,byte[]>)` | Decodes an archived entry to a UTF-16 string, then deserializes it with LitJson. | Input bytes, decoded text and output records overlap. |
| `CrewSim.DoLoadGame` | Clears `DataHandler.dictCOSaves` only after reading the replacement world and all ship JSON. | Old saved-item records remain rooted while their replacement is being built. |
| `CrewSim.Start` | Invokes `OnSceneFinishedLoading`, then removes its runtime listeners. | The archive-capturing callback has an explicit release path. |

The latest existing `autosave_1_update` archive was examined read-only, using
the ZIP directory and PNG headers, without deserializing its world:

| Data | Size |
|---|---:|
| Compressed archive | 128,590,402 bytes, approximately 123 MiB |
| Expanded archive, 418 entries | 1,586,175,015 bytes, approximately 1.48 GiB |
| Main world JSON | 708,494,665 bytes, approximately 676 MiB |
| Largest individual ship JSON, `OKLG_UNK` | 174,016,005 bytes, approximately 166 MiB |
| Ship PNGs | 31 images; 59,520,000 bytes of base-level RGBA pixels |

The main JSON alone needs roughly another 1.32 GiB when decoded as UTF-16 if
predominantly ASCII. This is an estimate from the encoded length, not a measured
string allocation. Expanded entries, extraction buffers, decoded strings and
deserialized records must not be confused with the much smaller ZIP file.

This archive footprint supports the overlap explanation; it does not measure
which objects remain reachable after loading has finished. PNG pixel counts
exclude mipmaps, CPU/GPU duplication, allocator overhead and other assets.

## Offline exclusions and remaining limits

Two tests ran in an isolated process using the game's unmodified Mono runtime
and Unity managed assembly. Forced collections occurred only in that test
process, to test reference lifetime.

- **Unity event callback retention:** after `UnityEvent.Invoke` followed by
  `RemoveAllListeners`, all 16 callback-owner samples were collected while the
  event itself remained strongly rooted. IL inspection confirmed that this
  installed Unity version replaces `InvokableCallList.m_ExecutingCalls` during
  `Clear`; it does not leave the previous runtime invocation list there.
- **Saved-item lookup cache:** the actual production
  `SavedCoShipLookupPatch.AnySavedCoShip` implementation performed 16 indexed
  lookups. All 48 sampled ship arrays, saved records and predicate closures were
  subsequently collected while its static caches remained. Its type/field
  metadata cache therefore did not retain those individual closures in this
  test. This is a targeted exclusion, not an exhaustive mod heap test.

Other source paths checked:

- `CrewSimTut.OnDestroy` removes tutorial listeners, clears tutorial beats and
  nulls its cached ship/equipment references. Tutorial fields are not sufficient
  evidence of a leak merely because they are static.
- `LoadManager.NotifyListenersWhenDone` clears `SavingJob.SaveDto` after normal
  completion, cancellation and handled failure. A completed save job is not
  automatically a permanently retained world snapshot. Unexpected interruption
  or exceptions outside those normal paths have not been exhaustively tested.
- Alternative work resets its manager and workers on scene unload; inventory
  and trigger scratch buffers clear object keys/references; portrait composition
  retains at most a bounded color buffer rather than source textures.
- Hull display and sensor tracking have scene/reset paths; hull worker inputs
  are detached geometry. The checked source does not establish an accumulating
  retained world in these caches. Their entire engine lifecycle was not
  reproduced by these two small tests.
- Async ship-job ownership is weakly keyed by loader, stores coroutine/ID pairs,
  and removes entries during unload. The previous live collection was empty.
- Ship-image cache replacement has no explicit texture destruction at the
  assignment site, but `MainMenu.Start` calls `Resources.UnloadUnusedAssets`.
  Replacement alone therefore does not prove indefinite native-texture
  accumulation. Image cleanup under every path was not reproduced offline.

## Changes worth considering separately

No periodic collection, global cache sweep or forced asset unload is justified
by this evidence. Those interventions can hitch and mask an ownership defect.

If lower reload peaks become a priority, the first bounded options are:

1. Evaluate the game's existing LeftShift load path, which extracts the archive
   to its temporary disk folder instead of retaining all expanded entries in
   RAM. Source supports its purpose; no in-game memory or timing A/B was done.
2. Release the previous saved-item index specifically after destructive
   whole-world teardown and before replacement parsing, rather than at the
   later native clear site. Preserve save failure semantics and check async
   saving and all entry points before changing this lifetime.

Do not clear `dictCOSaves` on general `DataHandler.Init`, station transit or
ordinary ship unloading: it is authoritative saved data for shallow/unloaded
ships. Do not prune records as a memory workaround.

Neither option is presented as a proven fix for all reported reload growth.
The first reduces input buffering, and the second reduces overlapping live
records. Further reachability evidence would be needed to claim an independent
retention leak has been identified and repaired.

## Reproducibility

Small captures, read-only ZIP footprint data, the IL inspector and the isolated
reference tests are retained in the ignored diagnostic directory
`obj/ReloadMemory-20261118`. No decompressed copy of the 1.48 GiB save was made.

The reference test links the production saved-item lookup source and uses
`Tests/SaveJsonCompatibility/Host` to execute it under bundled Mono. Diagnostic
artifacts are not required for users or deployment. No new feature inventory
entry is warranted because this pass changed no shipped behavior.
