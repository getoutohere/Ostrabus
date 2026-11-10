# Nav outline diagnosis

Reproduce native outline failures and evaluate a small boundary-extraction
reference without launching the game. This installs no Harmony patches and
writes no game assets or save data. The production plugin excludes Tests sources.

From the repository root:

```powershell
dotnet build Tests/NavOutlineDiagnosis -c Release
dotnet run --project Tests/SaveJsonCompatibility/Host -c Release -- Tests/NavOutlineDiagnosis/bin/Release/netstandard2.1/NavOutlineDiagnosis.dll
```

Game installation paths come from the shared build/development configuration.
Run native Mono harnesses sequentially; the host uses shared runtime scratch.

The native assertions intentionally characterize defects in the shipped
1.0.1.10 assembly. A future game fix can make them fail and require review.
They are not assertions that corrected production outlines should retain those
defects. The current run passes 145 native/reference checks.

Coverage includes detached-port and internal-hole seeds, input-order dependence,
X-only sizing, installation overrides, stale floor/silhouette/draw copies,
native IL access paths, socket agreement with port assets at four rotations,
reference boundary invariance and six actual hull assets. Engine-dependent
ship spawning, current-item snapshot selection and Vectrosity rendering are
outside this harness. No live framerate claim follows from its timings.

`ReferenceEdges` is a scratch-allocating, half-tile-grid reference algorithm. It
returns independent oriented edges, not the continuous point list expected by
the vanilla renderer. It is not a patch ready to activate.

See [investigation and proposed scope](../../NavConsole/OUTLINES-AUDIT.md).
