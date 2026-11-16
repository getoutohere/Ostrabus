# PASS and shared time advancement

Status: implemented and offline validated on 1.1.0.0. PASS and GUI SFF now share
the existing motion/economy engines and decision-aware world stepping. Native
`GUIFFWD.Active` remains truthful. No JSON policy, ship velocity repair, saved
journal or replacement AI controller is introduced.

## Implementation and validation (2026-11-16)

`SffDecisionStepsPatch` replaces the one world-update call in each native
`GUIFFWD.FFWD` and `CrewSim._TeleportCO` iterator. `SffAdvance` tags only the
synchronous world advance, splits at impact decision opportunities and native
course completion, and limits ordinary processing windows to six hours. A
completion uses the next representable epoch after arrival because native
playback requires strict `epoch > arrival`. It never overshoots the requested
end. An inactive course is offered completion once, preventing retry storms.

`PassPendingEndTurnsPatch` wraps positive-time passenger iterators with the
native `Condowner.Utils.Scheduler.CompletePendingEndTurns()` yield. The original
iterator owns loading, passenger/follower transfer, catch-up and final region
refresh. Its disposal is forwarded. No jump scope spans a yield. Zero-time
teleports are unchanged; disabled orbital/SFF features retain native advancement.

The world observer admits tagged short windows into `SffSimulation` and
`SffEconomy`. Existing control/route/combat/hidden exclusions remain. Actual
target arrival checks refresh native target coordinates, and the existing
destination-preservation fix now also refreshes the target at
`Dock.ResolveInstantly`'s native mooring placement. This closes the non-GUI
coordinate gap without assigning a new destination or changing docking policy.

Native hourly role callbacks retain the original interval's eligibility rather
than each window's duration. Each eligible current controller can run once at
the final window; newly observed controller identities do not inherit earlier
time. The dictionaries last only for this jump. Existing motion, freight,
withdrawal, recovery-Hold and station-return guards remain authoritative.

`JumpBoundary` begins/ends a collision-history generation. Weak hull tracks
invalidate lazily even if first observed after arrival. During a jump physical
collision handling stays native; subsequent normal-frame sweeps still work.
Collision-only custom builds share the tag hook without enabling orbital motion.
SFF custom builds require the completion and destination-integrity fixes.

`GET /orbital` -> `lastSffAdvance` reports source, requested/end epochs, world/
decision/course-completion steps, role callback admissions and budget fallback.
The existing PDA time-skip toggle controls both sources. No new runtime poll is
added. After 512 adaptive steps, six-hour progression continues without further
forecasts; this grants no collision immunity or guaranteed rescue.

| Offline evidence | Result |
| --- | --- |
| `OrbitalCheck` full suite | 5,121 checks; native patrol phases, PASS durations from 0.5 s through 172,801 s, route expiry/exact-end boundaries, hourly callback/new-controller controls, target refresh, coroutine drain/cancellation, nesting and exception cleanup |
| `PassHandoffStudy` actual production adapter | The same 1,200 s coast tail below ends 8.94e-8 km from the existing conic solver, versus 49.969623 km before the bridge |
| `OrbitalCheck economy` | 1,494 checks; actual 30 s/1,500 s/6 h PASS windows preserve production, dispatch clock, freight position, delivery and cargo accounting |
| `AiRouteCompletionCheck` | 689 checks including native instant mooring with stale coordinates, both GUI flag values and no preceding course completion |
| `HullOutlinesCheck` | Short PASS scans and first observation after arrival cannot bridge old history; following ordinary motion still produces a swept hit; weapon/body exclusions remain |
| `TorchTowCheck`; native reactor `sff-clock` | 343,871 group/planner/brace checks; 256 independent-clock fuel-settlement checks |
| Custom builder | 580 self-checks; SFF, economy, collision-only and mixed builds compile, preserve bootstrap order and bind 10/26/11/36 patch classes with zero failures/warnings |

These tests execute the bundled game Mono and production helpers. Scene loading,
Unity's coroutine dispatcher, transfer presentation and downstream collision VFX
remain engine boundaries; no full in-game shuttle run is claimed. No installation
or save was changed to perform them.

## Pre-implementation investigation and negative controls

Reuse the existing engines. Add a shared, explicitly scoped world-jump boundary
for GUI SFF and positive PASS teleport jumps; retain the native GUI flag's actual
meaning. The following pre-bridge probes execute
the assembly and production helpers offline, without modifying a save
or installing hooks in a running game.

| Probe | Observed result |
| --- | --- |
| Current non-GUI 1,500-second entry | No bulk motion/economy context starts. |
| Explicitly enter the existing economic engine with the GUI flag false | Stock, freight flight progress and dispatch timing match the corrected 1,500-second GUI-SFF reference; zero native role FFWD callbacks. |
| Explicitly enter the existing motion engine with the GUI flag false | A circular Ceres orbit remains circular over 1,500 seconds, within 1 m radius tolerance. |
| Route finishes at +300 s during a 1,500 s jump, with the existing completion repair | Its 1,200 s constant-gravity coast ends 49.969623 km from the existing conic engine's result. Entering a bulk context alone produces the same error: the plotted route was excluded from that context. |
| Split at the route's successful-completion boundary, then reuse the existing motion engine for the remainder | Position discrepancy falls to 8.94e-8 km in this controlled case. No new physical integrator is needed. |
| Native manager FFWD: 3,600 s versus 300 + 3,300 s | One role callback versus zero. Subdividing a jump changes native callback eligibility unless the outer interval is accounted for. |

These are fixture results, not live measurements of current traffic. The route
study supplies an ideal initial gravity sample and executes native playback,
the production completion repair and existing conic propagation. Engine/UI stop
services are a boundary. It demonstrates a remaining coast-handoff error, not a
return of the repaired residual-engine/double-integration bug.

### Integration design implemented above

1. Extend the existing GUI-SFF call-site adapter to also intercept the world
   update in `CrewSim._TeleportCO`'s iterator. A small outer context records the
   jump source, original interval and current window. It exists only during
   synchronous world advancement, with nested/exceptional restoration. It must
   not span passenger loading, coroutine yields or crew transfer.
2. Before a positive PASS jump, yield the native
   `Condowner.Utils.Scheduler.CompletePendingEndTurns()` helper, as GUI SFF does.
   This requires a coroutine-boundary adaptation in addition to the synchronous
   world-call replacement. Preserve native loading, passenger/follower transfer,
   catch-up and final `CollisionManager.RefreshCurrentRegion` ordering. Zero-time
   teleports remain unchanged. The native helper also cancels work claims, so
   coroutine cancellation, reentrancy and unfinished work need explicit tests.
3. Generalize `SffAdvance` to recognize that context. Reuse its impact forecasting
   and native-priority scheduling. Also split at native plotted-course completion
   before advancing a long coast tail. Native playback completes only when
   `epoch > arrival`; the prototype uses the next representable epoch after
   arrival, clamped to the requested end. The next window can capture the newly
   unplotted ship/group with the existing motion engine. Do not round away time,
   replay a course twice or extrapolate a previously expired route from a stale
   saved endpoint.
4. Enter `SffSimulation` and `SffEconomy` through the existing
   `OrbitalStepObservationPatch`, using the shared context. The motion engine
   already accepts an explicit bulk-mode argument; the economy manager prefix
   runs before native FFWD's sub-hour return. Permit positive sub-minute windows
   explicitly and bound long transfers to manageable windows rather than dropping
   the economy's existing 24-hour/work-budget safeguards.
5. Preserve native role-callback timing across windows. For sub-hour original
   trips, do not enable hourly instant-arrival callbacks. For longer trips, account
   for the outer interval and invoke each eligible current controller's native
   callback once, retaining `SffCatchupPatch`, recovery-Hold, station-return and
   withdrawn-controller guards. Intermediate native AI decisions remain allowed.
   Actors created/replaced during a jump require controller-identity/lifetime
   accounting; they must not inherit time from before their creation. This can
   be transient bookkeeping for one jump, not a persistent journal or replacement
   AI registry. Existing instant-docking target-refresh behavior also needs a
   non-GUI long-jump control; its current completion helper sits in the GUI branch.
6. Teach the alternative physical-collision path about the discontinuity. Its
   three GUI gates and 120-second history cutoff are insufficient for shorter
   PASS trips. A begin/end generation stamp can invalidate existing weak-cache
   history lazily, including hulls first observed after arrival, without scanning
   every hull or treating the teleport as a swept collision. Preserve native
   combat/projectile behavior.
7. Preserve feature composition: common jump tagging can be shared by SFF and
   hull-collision builds, while motion/economy activation still honors their own
   existing flags. SFF's PASS path needs the shared route-completion protection;
   collision-only consumers must not acquire an unsolicited orbital simulation.
   Update the custom source generator, catalog ownership/dependencies and help
   text together. Add jump source to existing bounded inspection reports;
   optional diagnostics may consume it without adding an always-on journal.

### Reuse and adaptation inventory

| Existing implementation | Treatment |
| --- | --- |
| `SffDynamics`, ephemerides and body-relative offset prediction | Reuse; no new orbit solver. |
| `SffSimulation` guidance, crew/lock/combat/hidden exclusions and impact handling | Reuse through the explicit context; allow tagged short windows. |
| `SffAdvance` / `SffDecisionStepsPatch` | Adapt source recognition, route-end boundaries and bounded windows. |
| `OrbitalStepObservationPatch` | Adapt its entry predicate to actual tagged jumps. |
| `CoastIntegration` | Retain ordinary-frame behavior; its current bulk-context exclusion already prevents duplicate integration. |
| `RouteCompletionPatch` | Retain its successful-completion/abort distinction and drive release. Use completion-boundary scheduling for a correctly integrated bulk coast remainder. |
| `SffEconomy`, `EconomicTimeline`, `FreightRoute` and planning-clock isolation | Reuse production/dispatch/arrival ordering and intermediate-leg continuation; adapt duration admission/window ownership. |
| Cargo loading, transfer, stock/writeback and retirement fixes | Reuse unchanged; these do not depend on the GUI flag. No new delivery/refund policy. |
| `SffBeforeCatchupPatch` / `SffCatchupPatch` and existing native role guards | Retain guard semantics; add outer-interval callback accounting. |
| Torch fuel settlement and docked-course movement | Retain their existing clock and movement owners; verify split-window fuel settlement and one movement per group. They are already independent of the GUI flag. |
| `PhysicalCollisions` | Adapt discontinuity gates/history, including first observation after arrival. |
| Custom builder and inspection/optional trace labels | Keep generated predicates and feature ownership synchronized; distinguish PASS from GUI SFF in reports. |

The authored `ENCFerryPickupAllowTeleport` interaction still correctly requests
`strTeleportRegID = FERRY`. The travel definition needs no JSON policy change.
The missing boundary is C# integration, not an authored decision to disable
orbital physics, freight progression or safe AI scheduling during a shuttle ride.

### Investigation fixtures

- `Tests/OrbitalCheck/PassReuseChecks.cs`: explicit-context economic equivalence,
  unchanged sub-hour role exclusion and the native split-interval threshold control.
- `Tests/OrbitalCheck/SffDecisionChecks.cs`: non-GUI motion-engine reuse and the
  existing danger/queue/short-window regressions.
- `Tests/OrbitalCheck/PassHandoffStudy.cs`, CLI `pass-handoff-study`: negative
  control and completion-boundary prototype using the same captured Ceres geometry.
- `Tests/OrbitalCheck/PassAdapterChecks.cs`: actual shared adapter, role-budget,
  coroutine-wrapper and scope tests. `AiRouteCompletionCheck` separately covers
  native playback/instant placement; neither substitutes for Unity scene loading.

The audit also found and corrected a separate builder regression: an SFF-only
composition regenerated the old `fTimeDelta < 60` guard and dropped sub-minute
decision-step physics. The new source-generation check fails before that fix;
the custom native harness executes the generated prefix and verifies the short
step. Both that guard and tagged PASS entry now exist in generated builds.

The closure above joins these isolated controls at the production bridge. The
scheduler drain's worker queue and Unity scene services remain explicit fixture
boundaries, rather than asserted live execution.

## Reproduced failure

Two PASS trips produced two large world updates, the largest about 1,500 seconds.
`/orbital` recorded those updates but zero GUI SFF updates. Ten local Mercury
civilians subsequently appeared 15,000-89,000 km from HQCH while still targeting
HQCH or its nav/security buoys. They had previously been within the local ring.

Replaying O-RRK's saved RCS route through the shipped assembly, without any
Ostrabus production patches, produced an extra **42.673509 km/s**. Its observed
first post-ferry route began with **42.685178 km/s** of excess velocity relative
to the old endpoint. The replay excludes external gravity and the intervening
normal-frame interval. Its acceleration components match the old route's final
braking acceleration multiplied by the entire 1,500-second step.

`NavData.TimeAdvance` writes the completed endpoint pose/velocity and leaves its
last acceleration, then clears NavData and returns false outside GUI SFF.
`ShipSitu.TimeAdvance` consequently performs ordinary integration for the full
step again. The existing SFF destination-preservation patch is inside the native
GUI-SFF completion branch and does not execute on this PASS path.

## Native entry points and authored intent

The native calls to `StarSystem.Update` are the ordinary `CrewSim.Update`,
`GUIFFWD.FFWD`, and `CrewSim._TeleportCO`. The latter handles PASS through:

`ENCFerryPickupAllowTeleport` JSON (`strTeleportRegID = FERRY`) ->
`Interaction` -> `Ferry.TimeChangeForCO` -> `CrewSim.TeleportCO` ->
`_TeleportCO` -> `system.Update(timeJump)` -> destination spawn and crew transfer.

The assets correctly express boarding/travel/arrival. The defect is in C# time
advancement, not the interaction definitions. Local PASS time is range / 0.5 km/s;
long-range PASS time is 80 hours per AU. Long trips can exceed a day.

Native GUI SFF sets its UI flag and drains `Scheduler.CompletePendingEndTurns`
before the world update. PASS does neither. Native AI role catch-up has its own
one-hour threshold (`GUIFFWD.FFWDTIMEUNIT`); a 25-minute ferry jump does not call
individual role callbacks. That threshold must not simply be removed.

## Pre-bridge coverage audit

| Mechanism | Current boundary | Required integration |
| --- | --- | --- |
| Orbital force correction | Applies to native gravity queries independently of UI | Retain unchanged. |
| Fine free coasting | Rejects steps over 60 seconds | Use the bulk propagation path for shuttle jumps. |
| SFF motion / partial AI travel | Started only when GUI SFF is active | Share a real discontinuous-time-advance context. Preserve combat, locks, hidden hulls and player-course exclusions. |
| Planned routes | Deliberately excluded from replacement motion | Fix successful native completion separately; enabling the motion context alone cannot fix the launch bug. |
| Economy and freight timeline | Same GUI-gated world prefix; accepts 60 s-24 h | Reuse for shuttle intervals; handle short and multi-day durations explicitly without duplicate production/dispatch/delivery. |
| Alternative collision history | GUI flag plus an existing 120 s history cutoff | Mark shuttle discontinuities explicitly; do not bridge sweeps across the jump. Preserve native combat/projectile behavior. |
| Torch fuel settlement and braced groups | Independent of GUI flag | Retain ownership, one-step group movement and paid-clock settlement; add ferry-boundary regressions. |
| Recovery Holds | Marked native queue plus SFF role guard | Retain; no automatic release or new target search. |
| Crew loading / transfers | Native teleport coroutine after world update | Preserve authored behavior; drain pending end-turn work before jumping, as native GUI SFF does. |

## Original repair plan (implemented above)

1. Introduce a scoped shared time-advance identity for actual GUI and shuttle
   world jumps. It must not fake `GUIFFWD.Active`, remain active during loading
   yields, or turn ordinary frames into world scans. Honor existing module flags.
2. **Implemented separately:** repair successful route completion at the shared playback/physics handoff:
   account for elapsed trajectory time once, stop the completed engine command,
   and propagate only the remaining coast interval. Retain the mission target
   and distinguish successful completion from abort/manual-control paths.
3. Start the existing motion/economy machinery and collision-history handling
   from that context. Keep native planning, role decisions and asset evaluators
   authoritative; do not repair velocities afterward or replace role policy.
4. Support long PASS transfers within bounded processing windows. Do not just
   repeat `StarSystem.Update` blindly: native FFWD can enqueue deferred commands
   that must receive a proper AI handoff between windows. Verify behavior against
   equivalent corrected SFF intervals, retaining economy event ordering.
5. Report jump source, duration and completion counts through existing bounded
   inspection reports. No always-on per-contact journal is needed.

Validation must cover short trips, the recorded 1,500-second case, hour-boundary
and multi-day trips, free/docked groups, route completion inside a jump, multiple
ship roles, recovery holds, freight accounting, exceptions/nesting and player
torch/braced courses. The numerical replay proves the defect. The completion
repair has its own native regression harness; that is not validation of the whole
PASS integration's engine-facing scene/transfer boundaries.
