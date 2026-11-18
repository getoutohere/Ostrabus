# Temporary transit hitch profiling

The indexed barter finalization removed one measured quadratic operation, but
the user still observes a noticeable pause after station transit. No additional
scheduling change is made until the remaining work is measured.

`TransitProfiling` is explicitly installed through the inspection API:

- `POST /profiling/transit/start`: begin a short session and install observers.
- `GET /profiling/transit`: read detached method timings and recent frame gaps,
  including while Unity is blocked. `/profiling/load` also contains the session.
- `POST /profiling/transit/stop`: finish the session and remove all observers, including the optimized barter iterator when present.

No startup Harmony attribute, scene listener, permanent Update polling or
disabled game callback is added. A temporary `CrewSim.Update` observer exists
only between start and stop. Plugin/API disposal stops the profiler. The prior
`LoadProfiling.Enabled` value is restored on stop; its launch config is not
changed. Starting an already active transit profile is idempotent. A different
active load profile must finish first.

The observations cover:

- Individual `GUITransit._Go`, PASS `CrewSim._TeleportCO`, and
  `BarterZoneShip.Init` iterator MoveNext calls, including the optimized barter
  iterator when included in the build.
- Destination `StarSystem.SpawnShip`/`Ship.InitShip`, old-ship serialization
  (`SaveToShallow`, ship/owner JSON creation), replacement, and hull destruction.
  Load, unload, and destruction entry points establish their own scopes even
  when called outside a transit coroutine.
- Crew movement, owner catch-up, station inventory catch-up visits and visibility.
- Teardown subphases: item removal/destruction, tile destruction, scheduler
  cancellation and owner-index removal. When present, the mod's tile-helper
  cleanup caller is observed separately; its reverse patch is not modified.
- Item creation, post-load setup, container insertion and slot assignment while
  inside an observed loading/lifecycle scope.
- Zone assignment, local inventory indexing and final hull movement.
- CrewSim Update duration and the most recent 16 Update-to-Update gaps of at
  least 50 ms, with UTC/frame numbers and intervening GC counts.

Coroutine waits are not included in a MoveNext's work time. A large final
MoveNext can be compared with its child phases to distinguish contained-object
creation, PostGameLoad, indexing or other uninstrumented work. Frame labels
associate those calls with subsequent frame gaps. Update-to-Update intervals
include rendering, OS scheduling, focus changes and GC, not solely simulation.
GC counts indicate correlation, not that GC accounts for the whole interval.

Measurements use Stopwatch wall time, not CPU time. Nested method durations
overlap and must not be summed. The existing bounded load profiler retains at
most three sessions, 64 metric groups and 32 slow calls per session. Observers
have overhead; the per-item probes share outer detail strings and record only
aggregate counters/times rather than allocating an event for every item.
Only slow frame gaps allocate their own bounded records. There is no forced GC,
changed iterator yield, cache flushing or native result/exception replacement.

After deployment, start profiling, perform one representative transit, wait for
the visible hitch to finish, then stop and export the result. Return trips can
be included in the same short session. Stopping actually unpatches the methods;
the code may remain dormant without continuing to observe gameplay.

An eight-hour PASS capture on 2026-11-17 exposed a coverage defect in the earlier
profiler: it observed the boarding GUI but not the separate teleport coroutine.
That capture measured a 107.879-second frame gap, 4.470 seconds of world advance
and 31.900 seconds of VENC unloading (including 29.364 seconds in destruction).
The remaining 71.508 seconds were outside those measurements. They cannot be
retrospectively attributed to destination loading, NPC catch-up or mod hooks.
There was no GC during that frame gap. The expanded scopes above address this
diagnostic gap; they are not a performance fix or an attribution of that stall.

`Tests/TransitProfilingCheck` verifies default dormancy, explicit method binding,
root/subphase scoping, frame/hull correlation, preserved native calls/results
and exception identity, frame-gap reporting, repeated start/stop and complete
unpatching. Its 26 checks additionally exercise the PASS coroutine, destination
initialization/catch-up and independent hull teardown, and verify per-owner
probes stay silent outside loading. They use fixture Unity frame counts and
engine-heavy bodies, not a live transit performance claim.
