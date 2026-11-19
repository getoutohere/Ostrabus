# Planetary route clearance and freight lifetime

Implemented against Ostranauts 1.1.0.2, native assembly MVID
`158470fc-8494-4bfe-b750-ad9d18dbf8d0`.

## Defects and repair

`Ostranauts.Ships.Commands.FlyToPath.PlanTrip4` uses planetary detour waypoints,
but does not validate the resulting trajectory. Its initial velocity-matching
and subsequent transfer segments can pass through a planet while ending at a
valid station rendezvous. The previous Ostrabus rendezvous validator checked
the final arrival, which does not establish clearance along the route.

`CelestialRoutes` checks the piecewise path actually played by `NavData`,
including body motion and body-bound waypoint offsets. It subdivides uncertain
intervals using orbital acceleration bounds, includes the vessel or connected
hull envelope, and rejects unresolved geometry after a fixed work budget. The
existing native planner tries detached body-relative detours. It keeps the
original mission destination, acceleration limits and propulsion accounting.

`FlyTo.RunCommand` previously returned the failed planned command's running
status without using its existing `FlyToManual` branch. After a rejected freight
plot, the existing flight handoff permits native manual steering while retaining
the buyer. An available pilot and working propulsion are required; player control,
absent pilots and fully loaded fuel exhaustion retain their own handling. Manual
steering does not accumulate failed-plot retries while it is the active alternative.
Direct `FlyToPath` callers retain their native retry counter and terminal failure.

`FlightCPU.GetDockingNavPoint(ShipSitu, BodyOrbit, ref double, ref double)`
returns the same `true` result for unobstructed flight and exhaustion of its
three side candidates. In AI manual flight and the existing skip adapter, the
latter produces the missing near-side radial exit, using the same native
two-radius waypoint scale. Clear paths and manufactured placeholders retain
native handling. Grounded ships retain their native departure mechanism.

Previously saved planet-crossing courses are revalidated before owned physics playback,
at the next native planner turn, and before corrected SFF/PASS begins. Discarding
a course releases commanded thrust, not momentum. A ship already too close and
too fast for its engines can still collide. This is prevention, not invulnerability,
teleportation or an automatic historical cargo recovery.

Freight catch-up previously switched from native plotted travel to numerical
manual approach within 5,000 km of the station. This could put low-thrust carriers
into a planet even when normal shallow navigation completed safely. Catch-up now
continues native planned RCS legs and uses `BaseCommand.GetHandOffDistance` for
arrival. Manual numerical approach remains the bounded alternative when close
planning fails. Both GUI SFF and PASS use this same freight propagation code.

Native `BodyOrbit` permits signed orbital periods. Detached position and
acceleration prediction accept finite nonzero periods of either sign.
`bIsBO` situations retain native `vBOOffset`, including station stand-off points;
they are not assumed to be natural planets.

## Cargo destruction

Native `Ship.Destroy(false)` removes the hull from `StarSystem.dictShips`
immediately, without unregistering its market. The later destroyed-hull scan
cannot clean a hull already removed from that dictionary. A genuine physical
loss could therefore leave its cargo in a shipless market indefinitely.

The existing logical hull replacement/destruction observer unregisters the
captured cargo market after actual destruction of its registered mobile hull.
It requires the same world and market identities and absence of a replacement
hull. Despawn, save/shallow replacement, stellar conversion, stations, projectiles
and unregistered preview copies keep their existing contracts. The native default
argument is `isDespawning = true`; it must not be mistaken for physical loss.
No destination is credited for destroyed cargo, and no sweep deletes old shipless
records whose provenance is unknown. The explicit destruction callers are
available through the ownership audit's `destruction` command.

## Layer and performance

The relevant star-system JSON distinguishes ground ports, orbital stations and
manufactured regional references. Cargo/market JSON defines stock, cargo pods,
capacities and production. None supplies a route-clearance or physical-destruction
market callback. These defects belong to the C# mechanisms; authored target
selection, triggers, threats, docking and overflow policy remain authoritative.

Clearance receipts use weak route keys and weak world references. A warmed check
compares waypoint geometry and interval/envelope identity without rerunning
ephemerides or allocating. It detects middle-waypoint edits, earlier playback,
world replacement, body-count changes and larger hull envelopes. Native orbital
elements are treated as static within a world. No saved fields, persistent actor
journal, frame-level fleet scan or new cargo cleanup hook is added. Restored-course
preflight visits the existing AI registry only at corrected skip entry.

## Evidence and limits

Run the complete regression entrypoint from the repository root:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Tests/AiPlannerDiagnosis/Run-Freight.ps1
```

The runner builds before execution, rejects nonzero exits and missing completion
receipts, and records per-case logs, results and assembly identities. Unknown
diagnosis modes fail rather than silently running the old default fixture.

The retained inputs contain six historical carriers and all twenty carriers
from the `roll` snapshot. Historical preventive replays start at their saved
course departure; current-fleet replays start at the actual snapshot. Each is
required to reach its original native station handoff in normal course playback,
two-hour SFF windows and eight-hour PASS windows: **78 complete voyage cases**.
Normal replays execute native planning, steering, shallow propulsion and playback.
They install the built plugin's navigation, avoidance, control-handoff and gravity
patches together. Saved burn acceleration and steady fuel loss calibrate effective
loaded mass and fuel efficiency after stripping item inventories; this is not an
independent cargo-mass accounting test.
Their independent clearance oracle uses actual native `BodyOrbit.UpdateTime` and
`NavData.GetShipSituAtTime`, with a separate minimum-distance search. It does not
call the production clearance predicate or detached ephemeris implementation.

Separate native controls cover between-waypoint crossings, moving retrograde
bodies, tangent/enlarged hulls, cache edits, clipped playback intervals, placeholder
exclusions, offset preservation and an unrecoverable late Mars checkpoint whose
position and velocity must remain unchanged when its course is released. Native
destruction/replacement tests distinguish real loss, despawn and same-ID previews.
The broader runner includes existing economy/transfer/overflow tests, fractional
SFF timing, PASS ownership, route completion and planetary avoidance composition.

The November 18 final run passed **97 cases**, including all 78 voyages. Supporting
suites passed 1,474 rendezvous checks, 1,497 economic checks, 521 delivery checks,
5,848 orbital checks, 28 destruction/replacement checks and the existing lifecycle,
route-completion and avoidance regressions. Counts overlap across suites and
must not be added as independent coverage. All 313 full-plugin patch classes bound
without failures or warnings. Eight custom profiles compiled, verified their
selection/order and bound natively: avoidance, rendezvous, combined navigation,
cargo loading, target lifetime, SFF, economy and the full preset.

Fixture boundaries are scene pilot availability, Unity object comparison, frame
clocks and redraw presentation. Full-Unity combat, random traffic interaction,
physical final docking and crew retirement are not executed by the voyage
matrix; separate lifecycle/accounting tests cover their relevant handoffs. PASS
voyage cases exercise the shared freight adapter under the jump scope; existing
PASS integration regressions cover invocation through native time-skip hooks.
Passing these cases is not a proof that every possible voyage is physically
recoverable or that every historical warehouse record had this cause.

The [saved evidence](../Notes/FREIGHT-STRANDED-CARGO-20261118.md) establishes four
deep planetary route penetrations followed by disappearance and unchanged cargo.
It does not recover a historical runtime destruction stack. No live save, market
or ship was modified during this implementation and offline validation.
