# Target orbital extrema readout

Proposed enhancement; not implemented. Add periapsis and apoapsis to the existing
TARGET DATA module without increasing its footprint or changing navigation.

## Layout and meaning

Shorten `POINT OF REF:` to `POI: <body>` and use the freed line for a compact
readout, keeping the native font and styling:

```text
POI: VENUS
P: 420 km  A: 11,200 km
```

P and A are altitudes above the displayed reference body's surface, not distances
from its center. Confirm the native layout, reference semantics and available
width before implementation; abbreviate large distances consistently if needed.

For a free-flying target, derive the instantaneous unpowered two-body orbit from
its position and velocity relative to that body and the body's gravitational
parameter. This requires a few vector operations and square roots, not trajectory
simulation. These are current orbital estimates, not guaranteed future extrema:
thrust, drag, docking and other gravitational influences can change them.

Display `A: ESC` for an unbound escape trajectory and an em dash for unavailable
or undefined values. Explicitly handle body-locked, docked and thrusting targets;
do not silently present authored reference-orbit values as a free hull's own
calculated orbit. Authored BodyOrbit objects already store periapsis/apoapsis,
whereas free hulls' ShipSitu objects track position and velocity. Decide the
appropriate readout for each native target type after inspecting the UI bindings.

## Recalculation and lifetime

- Compute only for the selected target while TARGET DATA is visible.
- Refresh at most once per real-time second using the latest simulation state;
  normal 8x/16x acceleration does not multiply the calculation frequency.
- Refresh immediately when the selected target or reference body changes.
- Hold the result while paused unless selection/reference changes. Invalidate
  after SFF or a simulation-state replacement, so pre-skip data cannot linger.
- Reuse a small numeric cache and only rebuild text when rounded display values
  change. No world/contact scan, background thread or forward integration.
- Scope cache lifetime to the UI; clear it on teardown/reload and avoid retaining
  old scene objects. No saved fields or changes to native motion/planning.

## Validation before deployment

Offline cases should cover circular and eccentric bound orbits, escape and
near-parabolic trajectories, invalid/zero relative states, altitude versus center
distance and coordinate/unit conversions. Check reference/selection changes,
paused behavior, hidden UI, time acceleration and post-SFF invalidation. Inspect
the actual TARGET DATA layout for clipping and readable large values.
