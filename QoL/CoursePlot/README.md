# Course-plot fine controls

Hover over ACCEL LIMIT and scroll for 0.01 G torch-acceleration steps, or over
DRIFT PHASE for 0.01 steps. Acceleration uses the native nonlinear thrust law
and its slider limits. Assignment runs the native value-change callback and
delayed replanning. RCS-only acceleration retains the game's controls; drift
wheel adjustment remains available.

## Native Shift-drag compatibility

Ostranauts 1.1's `PrecisionSlider` retains pointer-down, drag and release handling.
Ostrabus's component handles scroll and hover. During an active native precision
drag, a wheel assignment also synchronizes its private unsnapped accumulator to
the actual clamped slider value. Otherwise the next native drag would jump back
to the pre-wheel baseline. Ordinary wheel use does not enter precision mode.

The existing three hooks attach handlers, refresh the pre-plot torch G readout,
and suppress the unrelated map/log wheel sound only while a valid slider owns
hover. The entire slider rectangle is a transparent raycast target. Exit,
disable and destruction clear weak hover ownership. No new Update loop,
coroutine, scene scan or saved state is added.

The `course-controls` custom feature is retained. The separate
[module-placement repair](../../NavConsole/MODULE-PLACEMENT.md) fixes the native
parent-pivot conversion; placement still failed when wheel support was removed.
The layout inspector confirms that both controls are native `PrecisionSlider`s.

## Validation

`Tests/CoursePlotCheck` passes 12,255 checks using the shipped thrust law and
native precision-drag method. Cases include the stale-accumulator negative
control, wheel/drag progression in both directions, clamped endpoints, native
pointer-down suppression and ordinary sliders. Unity presentation/callbacks,
slider storage/clamping and native rounding calls are fixture boundaries in
the mixed-input tests. Actual pointer routing remains a live-game check.

```powershell
dotnet build Tests/CoursePlotCheck -c Release
dotnet Tests/SaveJsonCompatibility/Host/bin/Release/net8.0/MonoHost.dll Tests/CoursePlotCheck/bin/Release/netstandard2.1/CoursePlotCheck.dll
```
