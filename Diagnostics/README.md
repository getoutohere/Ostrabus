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

Offline verification: `Tests/WorkTraceCheck` installs/removes the actual hooks in the game's embedded Mono, checks repeated toggles, preserves another Harmony owner's patch, and verifies in-flight iterator yield/result/disposal behavior. It also checks all twelve profiling installation gates, including the experimental parser dependency. This validates hook lifecycle, not the helmet bug's cause.
