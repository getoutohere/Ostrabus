# Course-plot fine controls

Always-on QoL in `Ostrabus.QoL.CoursePlot`, scoped to **ACCEL LIMIT** and
**DRIFT PHASE** in native `NavModCoursePlot`, not the Torch Drive module.

- Hover anywhere inside the existing slider rectangle and scroll upward/downward.
- ACCEL LIMIT advances the displayed acceleration on a **0.01 G grid**. The
  underlying CYCLE value is found by inverting the actual native
  `Ship.GetMaxTorchThrust` function with a bounded allocation-free binary search.
  The lower bracket avoids exceeding the requested G ceiling. A valid running
  fusion reactor is required; zero/nonfinite thrust leaves the setting unchanged.
- DRIFT PHASE advances its underlying control value on a **0.01 grid**. This
  remains the native cruise-speed fraction, not a percentage of travel time.
- One vertical scroll event gives one step, regardless of device wheel magnitude.
  Horizontal wheel motion does nothing. Existing minimums, safety maximums,
  disabled controls and raycast bounds are respected.

Handlers are attached idempotently during native module initialization. A
transparent Image fills only the slider's existing rectangle when necessary,
making its full bounds available to Unity event routing. There is no new layout,
tooltip, keyboard binding, module setting or saved key. Existing dragging works.

Assigning `Slider.value` invokes the native callbacks: GUI property-map saving and
the native delayed replan remain intact. No replacement planner, parallel task or
extra replan coroutine is added. The native `UpdateUI` callback also refreshes the
G readout from the selected setting, including before a course has been plotted,
so a fine adjustment has visible feedback.

## Wheel sound

Native `GUIOrbitDraw.MouseHandler` reads wheel input independently of Unity UI
scroll-event consumption. Although zoom is gated by its map-hover flag, it also
calls `ScrollLog` for wheel motion over unrelated UI. `ScrollLog` plays
`ShipUINSMapPan02` unconditionally, explaining the apparent zoom sound.

A prefix skips that log-scroll action only while a valid fine-control handler
owns the pointer on the same console and the mouse remains inside its rectangle.
Other scrolling, map controls and audio remain native. The handler also consumes
its UI event. Hover ownership uses a weak reference; disable/exit/destruction
clears it, and destruction clears component references. There is no new Update,
coroutine, global UI scan or per-frame pointer polling. The native log-scroll
callback checks hover state only when it is already being called.

## Validation

`Tests/CoursePlotCheck` executes the native thrust law under bundled Mono and
checks 0.01 G progression/reversal, float drift, native slider limits, zero thrust,
multiple masses and reactor configurations, drift-control endpoints, no-hover
behavior and binding all three patches. **12,243 checks pass**. Fixtures replace
ship mass/reactor discovery and Unity object equality, retaining the shipped
thrust function. Actual Unity pointer routing, event timing and visual layout
remain live-game checks.

```powershell
dotnet build Tests/CoursePlotCheck -c Release
dotnet run --project Tests/SaveJsonCompatibility/Host -c Release --no-build -- Tests/CoursePlotCheck/bin/Release/netstandard2.1/CoursePlotCheck.dll
```
