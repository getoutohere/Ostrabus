# PhysX Quad convex-mesh errors

Status: investigation stopped at user's request. No runtime fix implemented.

## Observed errors

The console and Player.log repeat this pair:

    [Physics.PhysX] QuickHullConvexHullLib::findSimplex: Simplex input points appers to be coplanar.
    Failed to create Convex Mesh from source mesh. An internal unspecified error has occurred that could mean the PhysX's implementation of the Quickhull algorithm found the input mesh topologically challenging. Source mesh name: Quad

The inspected Player.log contained eight occurrences of the pair. Recent API
entries showed bursts around 2026-11-05 04:45:10–04:45:45 UTC, including several
within one second. Neither error supplied a stack trace or object identity.
This does not establish an every-frame failure or quantify performance impact.

Local evidence artifacts:

- `obj/PHYSX-CONSOLE.json`
- `obj/PHYSX-ERRORS.txt`
- `obj/PHYSX-RECENT-LOG.json`
- `obj/PHYSX-PREFAB-SCAN.txt`

## Serialized asset findings

A read-only diagnostic parsed serialized object tables and MeshCollider records
from the installed `Ostranauts_Data/resources.assets` (Unity 6000.3.23f1).
The following prefabs have enabled convex MeshColliders:

| GameObject | GameObject path ID | MeshCollider path ID |
| --- | ---: | ---: |
| prefabQuadGUI | 1949 | 13313 |
| prefabBubble | 1950 | 13314 |
| prefabPowerOverlay | 1952 | 13316 |
| prefabQuadGUI 2 | 1953 | 13317 |
| prefabQuadLightSprite | 1900 | 13318 |

These records all reference mesh path ID 10210 through external file ID 4,
resolved by the asset's external-file table to
`Library/unity default resources`. This is the built-in Quad mesh reference.
Several embedded light/UI objects have the same setup. For comparison,
`prefabQuadPlaceholder` has convex enabled but its collider disabled; some
progress-bar and background objects have non-convex colliders.

The diagnostic is a small purpose-built binary reader, not a general Unity
asset library. It retains raw collider bytes in the scan output. Mesh vertices
were not decoded, and no runtime collider instance was inspected.

Reader source: `obj/PrefabInspect/Program.cs`. No game assets were modified.

## Relevant creation paths

`DataHandler.GetMesh(string, Transform)` immediately instantiates a resource
prefab, falling back to `prefabQuad` if necessary.

- `Pathfinder.ShowFootprints()` requests `prefabQuadGUI`, then disables the
  clone's MeshCollider. A failed cooking attempt during instantiation could
  therefore occur before that disabling step.
- `WorkManager` also requests `prefabQuadGUI` for construction signs, so the
  GUI prefab is not exclusive to pathfinding.
- `Crew` and `Item` request `prefabQuadLightSprite` for light graphics. These
  provide additional candidate sources for the same mesh error.

The initial suggestion that footprints were responsible was a hypothesis.
The asset inspection strengthens the general prefab/collider explanation but
does not identify which creation path produced the observed messages.

## Limits and possible next step

Confirmed: repeated convex-cooking errors name Quad; multiple shipped prefabs
have enabled convex colliders referencing that mesh. Exact runtime attribution
remains unconfirmed. No causal link to the rotation or docking-fit patches was
established.

If resumed, correlate prefab creation with the error on the Unity thread using
bounded diagnostics, and inspect the implicated collider's intended role.
Do not globally disable MeshColliders or suppress all PhysX errors: some
colliders may serve picking or other gameplay functions. Any eventual fix
should target the confirmed malformed prefab configuration before cloning,
with its interaction behavior validated.
