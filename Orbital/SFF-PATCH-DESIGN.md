# SFF patch investigation — 2026-11-04

Historical investigation and prototype notes. The subsequent runtime implementation
is now in `SffSimulation.cs`/`SffDynamics.cs`; see [SFF-IMPLEMENTATION.md](SFF-IMPLEMENTATION.md)
for the implemented behavior, limitations and verification. The design discussion
below records the findings before implementation.

## Recommended interception boundary

Keep exactly one native `StarSystem.Update(skipSeconds)` call. Capture the start
epoch and relevant physical states before that call advances `fEpoch` or any body.
Change only numerical trajectory propagation for selected real world objects.
Do not subdivide the whole world update: its ledger, encounters, market, messages,
crew effects and AI FFWD have their own jump semantics and must not run repeatedly.

Eligibility must be captured at the start, not inferred after motion has run:

1. Objects entering with any `NavData` are excluded for the entire jump. Preserve
   the native call exactly once, its fuel/reactor accounting and its endpoint logic.
2. Body objects, body locks and analytical orbit locks keep vanilla propagation.
3. Register only actual ship situations and free docking-group anchors; exclude
   UI/route-prediction temporary situations. Treat group members through the anchor.
4. Initially target unpowered, atmosphere-free coasting in a single gravity domain.
   Never remove a live RCS/thrust command or force an escaping trajectory to be bound.

The trajectory hook must preserve native rotation and other work. A possible narrow
IL boundary is after the NavData/locked-object branches in `ShipSitu.TimeAdvance`,
around the free translational integration. Before implementing that hook, resolve
the group-member and gravity-query integration below. A broad TimeAdvance prefix
based only on its *current* NavData field is insufficient.

## Player torch-course protection established from source

`ShipSitu.TimeAdvance` first invokes `NavData.TimeAdvance`. Successful playback
directly assigns position/velocity, accounts for fuel and returns before numerical
gravity. The replacement must be unreachable on this success path.

More subtly, `NavData.TimeAdvance` has SFF-specific behavior after the planned arrival
time: it creates `shipSituTarget`, extrapolates that temporary situation for the
overflow interval, copies it back and clears NavData. Some failure paths also return
false after touching route/fuel state. Thus a late `NavData == null` test can accidentally
overwrite a completed plotted torch course, and a global hook could modify its
temporary prediction object. Capturing initial ownership and restricting hooks to
registered physical situations avoids both errors. Do not call route playback a
second time as a "validation" step.

Required regression cases: during a torch burn, a coast segment, arrival exactly at
the skip endpoint, skipping past arrival, route exhaustion/failure, and an AI plotted
route. Compare native position, velocity, fuel, reactor state and target behavior
with the SFF correction off/on. Source analysis establishes the boundary; these
integration tests still need to be implemented with the runtime patch.

## Coast propagation

Transform the captured ship position and velocity into the selected reference body's
frame at the start epoch. Propagate its two-body orbit for the skip interval. Add
that body's analytical position and velocity at the end epoch. Use the gravity
constant and units from the actual numerical simulation. Derive consistent body
velocity from the underlying trajectory, rather than mixing cached velocity epochs.

`Tools/OrbitDrift/SffInvestigation.cs` prototypes universal-variable Kepler propagation
for elliptical, parabolic and hyperbolic cases. It creates no persistent BodyOrbit
or lock, changes no saved state, and does not require n-body simulation. Escaping
states remain escaping.

Do not simply call native `Ship.LockToOrbit` to implement a temporary coast. Its
`BodyOrbit.CreateBOFromShip` path uses nominal SI G (~6.67408e-11), whereas numerical
gravity's single-precision AU constant corresponds to ~6.56803e-11. It also rejects
hyperbolic trajectories, fits phase approximately and installs persistent orbital
state/changes velocity. Those are unwanted changes for a temporary SFF calculation.

## Group and environmental handling still required

- `StarSystem.Update` updates every ship, then updates each docking group once.
  `DockGroup.TimeAdvance` advances its anchor and copies the formation's transformed
  positions and velocities to the members. The correction must follow that anchor
  once and retain the group's rotation, offsets and tow-load behavior.
- Individual member updates occur before the final group update. Avoid giving those
  members a bad temporary jump or independently solving them as unrelated objects;
  their post-update atmosphere/condition logic also needs a consistent state.
- The native pre-integration gravity selection uses end-epoch body positions and
  old ship coordinates. Fixing the final position alone leaves selected-body and
  atmosphere bookkeeping inconsistent. The SFF path needs appropriate final reference
  information without running the native selector against mismatched epochs.
- A valid endpoint is not proof of a safe path. Check periapsis/arc crossings against
  the body's surface/atmosphere and preserve genuine collision consequences.
- An orbit wholly inside one dominance region can be propagated in one solve.
  A trajectory crossing gravity domains needs bounded event detection and continuous
  position/velocity at each switch; do not clamp it or silently keep the initial body.
- Non-plotted powered AI needs a separate bounded numerical trajectory branch or an
  explicitly justified native catch-up policy. Do not reinterpret powered flight as
  an unpowered coast or erase fuel/thrust state. This is not covered by the prototype.
- Retain `AIShipManager.FFWD` after the single world update. Its pilot-specific instant
  docking/work resolution differs from ordinary numerical flight. Do not repeat it
  for trajectory substeps.
- Keep bounded per-jump state and clear it in a Harmony finalizer, including exception
  paths. No persistent ship references, extra updates, or custom save fields are needed.

## Offline evidence

Run:

```powershell
dotnet run --project Tools/OrbitDrift -c Release -- --sff-study .
```

Results: `VORB-DRIFT-ANALYSIS/sff-study.json`.

Using the captured M-5N4 state, the prototype retains periapsis altitude ~332 km and
apoapsis altitude ~11,529 km for 1h, 2h, 6h, 24h and 168h skips. The vanilla six-hour
step becomes unbound. The universal solution agrees with independent 1s RK4 to less
than one millimetre at seven days in this test. There are 2,021 checks, including
randomized energy/angular-momentum invariance, reverse-time checks and circular,
eccentric, near-parabolic and escaping cases.

One solver-only .NET 8 timing was ~0.67 microseconds per coast over 10,000 calls.
This excludes Unity/Mono, body selection, ephemerides, collision tests, bookkeeping
and any numerical fallback; it is not an in-game performance guarantee.

The cheap and well-supported first target is therefore stable, unpowered wrecks
and other coasters. A general SFF fix must address the integration cases above
before claiming protection for all AI ships or player-docked formations.
