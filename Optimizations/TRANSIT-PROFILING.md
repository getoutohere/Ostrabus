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

- Individual `GUITransit._Go` and `BarterZoneShip.Init` iterator MoveNext calls.
- Old-ship serialization (`SaveToShallow`) and normal hull destruction.
- Item creation, post-load setup, container insertion and slot assignment while
  inside the observed loading iterators.
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

`Tests/TransitProfilingCheck` verifies default dormancy, explicit method binding,
root/subphase scoping, frame/hull correlation, preserved native calls/results
and exception identity, frame-gap reporting, repeated start/stop and complete
unpatching. Its 17 checks use fixture Unity frame counts and engine-heavy
bodies, not a live transit performance claim.
