# Nav module placement

`Ostranauts.ShipGUIs.NavStation.Draggable.MoveRectTransformUsingAnchors()` converts
the container's world corners into local coordinates, divides by its parent's
size, and adds a hardcoded `0.5` to both axes. That assumes a centered parent
pivot. A bottom-aligned parent instead requires adding zero on the Y axis.

In the 1.1 live reproduction, the course module becomes active but moves above
the console: Y anchors reach approximately `1.02` to `1.42`. The palette removes
its entry because activation succeeded. Other modules' centered parents happen
to satisfy the native assumption. Removing Ostrabus's wheel controls does not
resolve this failure; both course sliders are native `PrecisionSlider` instances.

`ModulePlacementPatch` replaces only the four center offsets with the actual
parent pivot's X/Y values. Native projection, two-decimal snapping, module size,
overlap rejection, enable/disable behavior and saved placement remain native.
There is no clamping or forced acceptance of an invalid placement. The patch
checks the parent-rectangle local and all four X/Y/X/Y arithmetic sites before
installation, rejecting an unexpected native implementation.

The module JSON's `NavModCoursePlot` record selects the prefab and default
anchors; it cannot express a correction to this coordinate conversion. The C#
mechanism owns the defect. The repair supports non-centered parents generally,
rather than changing one prefab or moving an offscreen panel after the fact.

The `nav-module-placement` custom feature is a bugfix. Its one hook executes only
when native layout conversion is requested, with four pivot reads per call. It
adds no Update callback, scene scan, retained state or simulation work.

The [native harness](../Tests/NavModulePlacementCheck/README.md) reproduces the
two half-height errors and passes 727 preservation, repeat, scale and IL-guard
checks. Unity projection/property access are fixture boundaries. Live pointer
placement is confirmed: the course module's parent pivot is `(0.5,0)` and its
accepted bounds are approximately `(0,0)` to `(0.25,0.4)`. `/ui/nav-layout` exposes
these bounds, pivots, scales and active state without modifying them.
