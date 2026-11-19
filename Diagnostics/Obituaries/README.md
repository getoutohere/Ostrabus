# NPC obituary evidence

Automatically records future NPC deaths, living removals and crewed-vessel
destruction attempts without changing death, cleanup, navigation or asset rules.
Supported native version: Ostranauts 1.1.0.3.

The full build enables this by default. In the custom builder, select the
independent developer feature `obituaries`; `inspection` is optional and supplies
the HTTP interface. There is no per-frame polling or trace to start and stop.

```ini
[Diagnostics]
Obituaries = true
```

Restart after changing the flag. Disabled startup installs none of this module's
hooks. The module has eight observer/context patch classes covering twelve
native methods, with no transpilers or behavior replacement.
Crewless hulls, including ordinary projectiles, exit before vessel snapshot
allocation or controller enumeration; individual NPC deaths remain observed.

## Storage and inspection

Files are written to `BepInEx/Obituaries/obituaries-0.jsonl` through
`obituaries-3.jsonl`; zero is newest. Four rotating 16 MiB files bound retention
to 64 MiB across sessions. Old evidence eventually expires. Saving/reverting
does not delete the log or put any additional fields into a save.

- `GET /obituaries`: current session/load branch, capture failures, suppressed
  restoration/disposal counts and writer state, including dropped events/errors.
- `GET /obituaries/events?coId=NPC_ID&limit=25`: latest matching retained events.
  Optional exact filters are `shipId`, `kind`, `session`, `afterEpoch` and
  `beforeEpoch`. URL-encode IDs. `limit` is 1-50.
- `since` is a process-local sequence cursor and requires a `session` filter.
  Load branches distinguish repeating the same save within one process.
  Responses select the latest matches in file order, not a complete unbounded
  history. A query reports corrupt/incomplete lines it encountered.

Both endpoints read on the HTTP worker and remain available while Unity is
paused or loading. They do not scan people, materialize actors or execute AI.
No console death/removal spam is added. The first capture exception produces a
single BepInEx warning; status counters retain later failures.

## Meaning of events

| Kind | Observation, not an inferred story |
| --- | --- |
| `death` | A real `bAlive: true -> false` transition through the native death setter, including a setter that throws after changing the flag. Repeated kills and reconstruction of an already-dead saved person do not count as new deaths. |
| `living-removal` | A previously living runtime actor completed `CondOwner.Destroy`. This is not proof the persistent person identity is dead. Caller context distinguishes population pruning, rejected station arrivals and other removals. |
| `removal-after-death` | A destruction operation started with a living actor and finished dead/removed; an inner death event may already describe the transition, so this is not another counted death. |
| `vessel-destruction-begin` / `vessel-removal-begin` | Pre-operation crew and ship evidence. The boolean despawn argument alone does not prove permanent loss or harmless unloading. An entry record does not imply the operation completed. |
| `operation-failed` / `vessel-operation-failed` | An observed operation threw; its native exception remains unchanged. |
| `load-start` / `load-complete` | A new load branch and its completion. Real catch-up deaths remain observable. |

Normal `CrewSim.EmptyScene` disposal and `CrewSim.DestroyAndReload` replacement
do not generate living-removal obituaries. Their synchronous scopes still allow
real death transitions to be observed. Per-step coroutine scopes never survive
a yield. Temporary detachment context is discarded when its operation returns;
there is no accumulating NPC registry.

Evidence includes stable person/ship IDs, runtime identity, faction IDs,
world/actor clocks, UTC, native/plugin MVIDs, active conditions with fatal
conditions first, available cached room pressure/temperature/gas composition,
current action and caller stack. Crew events link to a shared vessel event.
Before ship destruction, capture preserves motion, acceleration, reference and
lock flags, docking membership, retirement timestamp/last player visit/despawn
station, cached RCS, bounded controller matches and up to
eight stored route samples. Snapshot code does not call mutable ship getters,
triggers, RNG, catch-up, atmosphere updates or navigation/ephemeris playback.
Reference types and station classification are retained; a `BodyOrbit` is not
assumed to be a natural collision surface.

Collection sizes and strings are capped and sampled/truncated data is marked.
Only detached primitives/collections reach the writer. Its nonblocking queue
holds at most 64 records; UTF-8 output is capped at 32 KiB per record. Oversized
records retain event identity and an explicit truncation marker. Queue pressure
or disk failure drops diagnostic evidence, never blocks or changes gameplay;
drops appear in status and subsequent records. Shutdown requests draining but
does not wait on disk from Unity. An abrupt exit can lose queued records or the
last line. Restart separates an unfinished tail from the next event.

A fatal condition identifies the immediate mechanism, not whether an earlier
navigation, combat or environmental bug caused it. Already-past deaths cannot
be reconstructed. Other mods writing alive fields directly bypass the death
setter; the read-only [personnel IL audit](../../Tools/NavigationOwnershipAudit/README.md)
documents direct stores/callers for the reviewed native assembly.

## Validation

[ObituaryCheck](../../Tests/ObituaryCheck) runs under the game's bundled Mono
with the actual native death setter and fatal-condition evaluator, plus native
docking rejection. Rendering and destructive teardown use explicit fixtures.
Checks cover A/B death results, immunity, repeated kills/resurrection, saved
corpses versus real initialization deaths, unrelated actors during restoration,
population removal context, replacement/world disposal, exceptions, pre-loss
motion, HTTP filters and the production JSON wire encoder, bounded rotation across restart, crash tails, oversized
evidence, queue pressure and disk failure. These are offline checks; ordinary
in-game population bursts have not yet been profiled.
