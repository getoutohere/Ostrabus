# Temporary diagnostics

Diagnostic simulation hooks belong to a separate Harmony owner and are installed only when needed. A disabled flag inside a hot-path hook still incurs Harmony dispatch overhead; do not describe that as zero cost. Keep diagnostic code separate from gameplay patches, default new traces off, and unpatch only their owner. Do not remove gameplay fixes when stopping a trace.

## Work/suit trace

Launch default: BepInEx `[Diagnostics] WorkTrace = false`. Live commands:

- `worktrace on`: install fifteen native validation/work hooks.
- `worktrace off`: remove those hooks, retaining captured events for inspection.
- `worktrace clear`: clear captured events.
- `worktrace status`: report whether hooks are installed.

Read `GET /workorders/trace`. Captures player/crew validation failures, native work-related suit requests and their disposition, direct claims, and the `WearSuitQueue` follow-up order. Events include crew, target, interaction IDs, real/fake airtight state and airlock-blocked flag; suit requests also include positions and the character's room pressure. The endpoint exposes the player's currently stored suit follow-up even with tracing off. This stored value can outlive its pledge and must not by itself be treated as an active order.

Work timing events: `work-claimed` follows native `FinalizeTask`; `interaction-start` observes the native scheduler sample marker called when action time first starts decreasing, before `fEpochAdded` is assigned; `effects-begin`/`effects-returned` bracket non-cancelled `ApplyEffects`. For a feed-item action, the latter follows material delivery effects; it denotes normal return, not independently verified transfer success. `CompleteTask` is deliberately not used as the completion marker because native code calls it before transferring materials. Target ID falls back to the stored interaction ID reference when the target has been destroyed. Observation reads cached participant references, without invoking their lazy resolvers.

Use `crewEpoch` differences for the character's game-time waits and durations. `epoch` is the global world clock; those clocks can be offset by scheduler processing. `wallMsSinceTraceStart` is monotonic elapsed real time, includes pauses/trace overhead, and is not a CPU measurement. `nativeStartEpoch` and `durationHours` expose stored native timing fields. Correlate install and feed events by crew and placeholder target ID, and stages of each action by interaction ID. No per-frame polling is needed. Trace only work-related interactions (valid native duty, installation/uninstallation, feed/reload/haul and equipment actions), so unrelated social/combat menu tests cannot overwrite this sequence.

Reproduce with tracing on, pause when the character starts equipping the helmet, read the endpoint, then turn tracing off. Correlate the failed validation with the subsequent suit request using crew, interaction ID, target, and epoch. Fake-airtight preflight is distinguished from normal validation. A material failure and a movement failure require different fixes; safe air at the current position alone does not establish a safe route.

The trace observes existing results; it does not rerun triggers, pathfind, enable verbose trigger behavior, alter results, or write save data. While enabled it allocates iterator wrappers for watched crew's deferred checks, and detached event data on failures/requests. The 256-entry ring overwrites older events and contains no game objects. Disable tracing for ordinary play. A wrapper already returned to native code can finish after disable, but its recording is suppressed; there is no global list retaining it. Scene unload clears events and invalidates previous wrappers' recording sessions. Shutdown removes hooks and clears events.

When off, these fifteen simulation methods have no hooks belonging to WorkTrace. Only the console command recognizer remains; it runs when a console command is submitted, not on simulation ticks. This trace does not disable API infrastructure or other modules.

Existing load profiling is independently gated at patch installation by `[LoadProfiling] Enabled` (restart required). Disabled skips all profiling-only hooks. Its concrete ship-file routing is retained if `Optimizations.ExperimentalSaveJson` is enabled, because that replacement parser also depends on those call sites; those wrappers are then functional infrastructure. Neither gameplay optimizations nor their hooks are disabled with profiling.

## Torch burn diagnostics

Enable **Torch burn diagnostics** in the Mod Settings PDA before engaging a
course, then switch it off after arrival. Default off, session-only, no launch
config or saved-game state. It waits for the player's materialized reactor ship
and pins that ship's ID; later boarding does not silently change the subject.
Manual burns are observable too, allowing comparison with plotted autopilot.

The trace records paired states around three distinct paths:

- `reactor-run`: before reactor control/heat processing, the applied state at
  native Fusion entry, and the final result. Includes FLOW, CYCLE, bus/ratio,
  core temperature/pressure, pellet capacity/feed, cooling, power/load, wall
  wear, fuel-rate conditions and native reactor fuel caches.
- `course-playback`: native waypoint timing, CYCLE/fuel values, derived segment
  acceleration/fuel slope, stored plotting FLOW, plan identity/attachment,
  position/velocity, input acceleration, docking-group mass caches and anchor
  acceleration. Input acceleration is not an independent trajectory measurement;
  compare velocity changes across timestamped samples as well.
- `fuel-writeback`: the requested and applied SetReactants budget, real tank
  quantities before/after, and SFF state. This is separate from physical reactor
  consumption. Native skipped/error paths do not imply a successful fuel debit.

Ship.SetThrust observations also record the applied thrust argument in newtons.
Deep snapshots include each cached reactor module's ID, role, damage and off/
override flags, internal active-module counters and ignition/coil/cryo state,
core conditions and GUI properties, per-tank quantities/volume and each nav
console's saved controls (including course limits and torch safety).
Baseline/final samples and capacity changes include deep details; periodic deep
snapshots are limited to once per real second. Inspect their own epoch when
correlating them with faster scalar records.

Raw tank quantities retain native StatLiqD2O/StatSolidHe3 units. Their differences
are not automatically kilograms, and the scan does not rerun native availability
triggers. Incomplete scans and mass caches are flagged. Missing controls/derived
values use NaN, serialized as a string, rather than a misleading zero. Reads do
not initialize mass caches, repair stale dock-member references, call CatchUp,
replan, change controls/fuel or attach a corrected-course profile.

Files are JSONL under **BepInEx/TorchTraces**, with format
`ostrabus-torch-trace`, version 1. The existing bounded flight writer is shared;
torch payloads are in `Diagnostic.Observation` and `Diagnostic.Details`. A
normal close writes `journal-end`. They can be streamed through an offline JSON
parser; API polling is not required to retain a burn.

| API | Purpose |
| --- | --- |
| `GET /torch/trace` | Detached status, file path, writer/drop/error counts and latest scalar/deep snapshots; works while Unity is busy and after stopping. |
| `GET /torch/reactor?shipId=O-GXD` | Fresh deep snapshot on the main thread; defaults to the traced ship or current player ship. Does not enable tracing. |
| `POST /torch/trace/start` | Same session toggle as the PDA. No arbitrary filename accepted. |
| `POST /torch/trace/stop` | Remove observers and drain the file writer; allowed even while the world is not ready. |

Periodic observations are capped at four per real second **per path**; a new
plan, burn/coast state or game-epoch jump of at least 60 seconds forces a sample.
This retains SFF boundaries without recording every simulation tick. Scalar
records reuse 32 detached frames. Detailed arrays/dictionaries, timestamps and
writer serialization still allocate while tracing. The writer has a nonblocking
16-record queue, an 8,192-record limit and a 128 MiB file cap. Drops/errors make a
trace incomplete and are reported. After a limit/disk failure the next observed
call stops its hooks. Let the previous writer drain before starting another trace.

Five methods are observed only while enabled: FusionIC.Run, FusionIC.Fusion,
FusionIC.SetReactants, NavData.TimeAdvance and Ship.SetThrust. Stopping unpatches
only `ostrabus.diagnostics.torch`; gameplay fixes and other diagnostics remain.
No TorchTrace observer runs on simulation ticks when off. A stopped file writer
can briefly finish detached records in the background. Scene unload, simulation
replacement on the next observation, API teardown and plugin shutdown stop the
trace. Session state retains no ships, COs, reactors, courses or docking groups.

`Tests/TorchTraceCheck` runs the real reactor thermal/fusion and SetReactants
methods under bundled Mono, using fixture condition storage, power/scene and
thrust boundaries. Its 37 checks compare tracing on/off, distinguish controls
applied by another Harmony owner, preserve native exceptions/results, verify
SFF writeback labels, detached module/tank data, tow mass and stale-reference
non-mutation, record/file closure, repeated toggles and failure cleanup. Active,
stopped and deep responses also pass the actual restricted HTTP serializer. The
flight integrator is a fixture in this trace test; it is not a new validation of
autopilot physics. Tests/PdaSettingsCheck exercises the actual settings toggle;
Tests/FlightJournalCheck covers the shared writer's bounds/backpressure/failures.

```powershell
dotnet build Tests/TorchTraceCheck -c Release
dotnet run --project Tests/SaveJsonCompatibility/Host -c Release --no-build -- Tests/TorchTraceCheck/bin/Release/netstandard2.1/TorchTraceCheck.dll
```

Offline verification: `Tests/WorkTraceCheck` installs/removes the actual hooks in the game's embedded Mono, checks repeated toggles, preserves another Harmony owner's patch, and verifies in-flight iterator yield/result/disposal behavior. It also checks all twelve profiling installation gates, including the experimental parser dependency. This validates hook lifecycle, not the helmet bug's cause.
