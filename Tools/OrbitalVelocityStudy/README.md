# Offline orbital velocity and handoff study

The current scoped candidate and its evidence are in
[POSITION-PRESERVING-VERDICT.md](POSITION-PRESERVING-VERDICT.md).

This is an offline executable. Its study hooks exist only inside a separate
process running the game's bundled Mono. The `production` mode links and tests
the maintained [plugin implementation](../../Orbital/Velocity/README.md);
the earlier prototype variants remain available for comparison.
It does not connect to the game, write saves, build Ostrabus, deploy a DLL, or
install runtime diagnostics in the player's session.

Build the workspace plugin first with `dotnet build Ostrabus.csproj`; preservation
checks load its actual `NativeEphemeris` helper for comparison. This creates the
normal build artifact only and does not install it into the game.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Tools/OrbitalVelocityStudy/Run.ps1 -Preserve
# Shorter repeat, omitting the full reactor oracle:
powershell -NoProfile -ExecutionPolicy Bypass -File Tools/OrbitalVelocityStudy/Run.ps1 -Preserve -Quick
# Validate the actual production sources and their composed regression suites:
powershell -NoProfile -ExecutionPolicy Bypass -File Tools/OrbitalVelocityStudy/Run.ps1 -Production
```

The driver builds and executes each host sequentially. Existing suites locate
some JSON fixtures relative to their entry assembly; composition copies those
fixtures into this tool's build output. The six composed suites retain their
existing assertions. One existing test fixture now explicitly restores its
intended fixed-body ephemeris boundary; see the findings.

## Experiments

`Fixtures/Venus.json` contains fourteen observed free-flight/orbit handoffs and
selected coordinated snapshots from the 2026-11-17 Venus recording. Capture
epochs are observation times; the separately captured body calculation epochs
must not be treated as simultaneous. The fixture is a partial state replay,
not a complete save or Unity scene.

The executable compares:

1. Native `BodyOrbit.UpdateTime`, `CreateBOFromShip`, `ShipSitu.LockToOrbit` and
   subsequent `TimeAdvance` behavior.
2. Instantaneous velocity using the native orbital phase solver.
3. A common-epoch conic fit, retaining native velocity sampling.
4. Both changes together.
5. Both changes plus a separate, tighter Kepler phase-solver experiment.

The first four characterize why fixing only the readout or only the handoff
is insufficient. The last variant also requires detached ephemerides to use
the same phase precision. The offline adapter changes only the cloned test
process's `NativeEphemeris.Evaluate` IL constants; it does not edit that source.
An explicit negative composition omits this adaptation and reproduces a
normal-time/SFF position mismatch.

The separate `preserve`/`compose-preserve` mode retains native phase precision,
requires exact native coordinate preservation, compares against the actual
unmodified production SFF helper, and declines unsuitable coasting-orbit fits
through the existing native failure path. It also tests the 116 saved body
references in `Fixtures/LegacyBodies.json`. Omit `-Preserve` from the driver to
repeat the earlier precision experiments and their negative controls.

The native authored system JSON provides Venus/station orbital elements and
atmosphere definitions. The velocity cache timing, backward differences and
conic conversion are C# mechanisms, not asset-defined behavior policies. No
asset values or AI decisions are replaced by this experiment.

## Coverage and boundaries

- Native handoffs and subsequent native updates, repeated lock/unlock cycles,
  native body JSON round trips, bound versus escape trajectories.
- Nested natural/manufactured references, stationary placeholders, both orbital
  directions, eccentricity and phase boundaries, large/negative clocks,
  position-only future queries and restoration of the original epoch.
- Composition with existing orbital/SFF/PASS, AI rendezvous, route completion,
  planetary navigation, docking/attachment/towing, and torch reactor/fuel suites.
- Bundled-Mono solver timings, warmup, repeated queries, and thread allocation
  counters where the runtime provides them. These are not whole-game FPS or
  SFF-duration measurements.

Fixture boundaries include a known cached ship mass, suppressed Unity log
presentation, and the existing suites' documented UI/CO/physics boundaries.
Existing test totals describe their existing coverage; they do not mean every
assertion directly exercises the new velocity path. Invalid/cyclic inputs are
checked through the prototype's validation function; no claim is made that
falling back to malformed native recursion is safe.

## Verdict

See [POSITION-PRESERVING-VERDICT.md](POSITION-PRESERVING-VERDICT.md) for the selected
offline candidate. The broader precision experiment in [FINDINGS.md](FINDINGS.md)
is not selected because it changes derived body positions. The scoped candidate
does not require that change or a world migration. Unity playtesting
remains separate from these offline results. Production
integration now exists in `Orbital/Velocity`; use `-Production` to test it rather
than infer its behavior from the earlier prototype run.
