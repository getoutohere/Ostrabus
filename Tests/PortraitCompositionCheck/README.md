# Portrait composition checks

```powershell
dotnet build Tests/PortraitCompositionCheck -c Release
dotnet run --project Tests/SaveJsonCompatibility/Host -c Release --no-build -- Tests/PortraitCompositionCheck/bin/Release/netstandard2.1/PortraitCompositionCheck.dll
```

Uses the installed game's bundled Mono runtime and shipped FaceAnim2.GetPNG.
The production helper/transpiler are linked directly from Optimizations. First
execute the native cold path, then install the actual production transpiler and
compare the resulting float pixels. Native key construction, cache checks,
generation/upload ordering and final cache registration execute in both runs.

Unity texture storage, creation, metadata, reads/writes, Apply, object equality
and DataHandler's image cache are controlled boundaries. Raw views borrow pinned
fixture bytes. The production pointer lookup's safety check is replaced only in
the harness because Unity's native safety subsystem is not running; the actual
Unity GetPixelData/PNG decoding/upload implementation is not claimed as tested.
The native managed Color.Lerp and Color32 operators provide the unchanged
reference oracle. Revised production arithmetic is compared using exact float
values, including RGB/ARGB layouts, transparent/opaque samples and rounding.

Coverage includes:

- Cold/warm native GetPNG, invalid-size cache regeneration, null and crewless
  branches; unchanged keys, cache replacement and one native Apply.
- Exact float pixel comparison for varied seeds, alpha values, RGBA32, ARGB32,
  RGB24 and mixed layers at 1x1, 3x7, 17x9 and full 166x258 portrait dimensions.
- Native fallback for unsupported formats, mixed dimensions and unreadable data;
  error cleanup and reentrant composition without corrupting the outer buffer.
- Reused output storage, bounds on pixel/layer counts, unsupported compressed
  layouts and output-buffer reuse. Counter calibration detects that this Mono
  returns zero even for deliberate allocations; zero is not allocation proof.
- Actual shipped IL binding and rejection of unknown method shapes.

49 checks pass. Pixel-call counts are measured at the boundary: native full-size
composition reads 471,108 individual pixels and writes 42,828; patched composition
borrows 11 input buffers and performs one bulk write. Printed stopwatch values
include controlled fixture overhead and are not live Unity UI measurements.

The ten-round warmed kernel comparison retains the previous bulk algorithm and
compares it with the revision on dense and mostly transparent fixtures, excluding
texture reads/writes. It verifies float equivalence and prints CPU timings without
a fragile performance threshold.

No game saves or portrait assets are bundled or modified. Fixture pins, image
registry and cache are released in finally; production keeps only a bounded
managed float-color scratch buffer.
