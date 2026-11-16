# Native nav module placement checks

From the repository root:

```powershell
dotnet build Tests/NavModulePlacementCheck -c Release
dotnet Tests/SaveJsonCompatibility/Host/bin/Release/net8.0/MonoHost.dll Tests/NavModulePlacementCheck/bin/Release/netstandard2.1/NavModulePlacementCheck.dll
```

The harness executes the shipped private
`Ostranauts.ShipGUIs.NavStation.Draggable.MoveRectTransformUsingAnchors` method,
first unpatched and then with the production transpiler. Unity object equality,
transform access, corner projection and rectangle property reads/writes are
engine boundaries. Normalization, size preservation, rounding and native early
returns execute from the game assembly.

With the live panel dimensions/scale and a bottom-aligned parent, two native
conversions reproduce Y anchors `1.02` to `1.42`. A centered-parent negative
control already works. Repaired cases cover six pivots, three scales, four
rectangles, repeated conversions, native property resets, absent/zero-size
parents, intentionally out-of-bounds placements and rejected IL drift.

727 checks pass. This tests the coordinate defect and preservation of native
math; it does not simulate Unity event routing or claim full scene acceptance.
