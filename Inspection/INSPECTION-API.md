# Live inspection API

Default: `http://127.0.0.1:18764`. Enabled when this plugin starts. Configure
`[InspectionApi] Enabled` and `Port` in the plugin's BepInEx config; restart to
apply. Install the rebuilt DLL and restart the game to load this version.

Inspection reads do not change game state. Explicit maintenance routes can prune
eligible ships or derelicts; they require `POST` and confirmation. Personnel
trace controls also require `POST`; they enable diagnostics without changing
combat behavior. `POST /micrometeoroids/strike` deliberately requests a real
BeatManager MM event, with possible ammunition use and damage. The API has no
authentication; local processes can query it.
It binds IPv4 loopback only and requires the matching numeric Host header.
Browser Origin headers are rejected. No arbitrary code, reflection queries,
request bodies, or persistent connections are exposed.

| Route | Result |
| --- | --- |
| `/health` | Whether the game is ready and the current simulation generation. |
| `/cos/{id}/combat` | Raw combat state, queued targets/attack modes, armor/damage and direct equipment slots; also accepts `/npcs/{id}/combat`. |
| `/combat/personnel/trace?since=0&limit=25` | Read the current/last personnel ranged-combat capture, forward-paged by `nextSince`. |
| `POST /combat/personnel/trace/start?coId={id}&seconds=120` | Start tracing one shooter; omit `coId` to use selected crew. Duration is 1–300 wall-clock seconds, default 120. |
| `POST /combat/personnel/trace/stop` | Remove trace hooks immediately and retain captured records for inspection. |
| `/console?offset=0` | Actual GUI console text, including typed commands and responses. Available before loading a save. |
| `/logs?since=0&limit=25` | Recent console/Unity/BepInEx logs; subsequent polls use `nextSince`. Works without Unity Update. |
| `/profiling/load` | Last three bounded startup/save load profiles, including current progress, stage wall/work time, yields, inclusive method timings, slow calls and GC counts. Served off the main thread and available while loading; see [profiling notes](../Optimizations/README.md). |
| `/profiling/transit` | Detached temporary transit/barter method timings and recent slow frame gaps, readable while loading. |
| `POST /profiling/transit/start` | Install the temporary transit profiler; preserves loading behavior and records GC counts. |
| `POST /profiling/transit/stop` | Finish capture and remove all transit profiling hooks. See [transit profiling](../Optimizations/TRANSIT-PROFILING.md). |
| `/profiling/socials` | Detached inclusive/exclusive timings, allocations, portrait fallback counts and deferred UI work from a temporary Socials capture. Readable while the main thread is busy. |
| `POST /profiling/socials/start` | Arm temporary hooks for the next Socials opening. Automatically unhooks after that opening and three deferred UI frames, or after 180 seconds. |
| `POST /profiling/socials/open` | Explicit diagnostic UI action: arm capture and open the native Socials app. Rejects an unavailable PDA or an already-open Socials app. Does not evict portrait caches or rerun save loading. |
| `POST /profiling/socials/stop` | Cancel capture and remove its hooks while retaining captured detached results. Also stops on scene/system changes and API shutdown. |
| `/events/docking?since=0&limit=25` | Recent successful dock/moor/undock/unmoor transitions observed since plugin startup. |
| `/events/ai?since=0&limit=25&shipId=O-TXXW` | Bounded in-memory history of sampled AI command, target, combat, and docking-state changes. |
| `/events/combat?since=0&limit=25&shipId=O-TXXW` | Successful weapon-fire and ship-damage events; filters by involved ship ID. |
| `/shipboard` | Current shallow boarding encounters, including start epoch and per-ship start crew readiness (`alive/active/ranged/other weapon/unarmed`) alongside current crew/load state, next casualty timing, ships ordered to return home, and the latest resolution's prize-capture checks. Weapon counts include detected carried weapons; read-only, captured on the Unity thread. See the [shipboard action module notes](../ShipboardAction/README.md) for the encounter and resolution rules. |
| `/world` | Ship/CO counts, AI-region count, pause state. |
| `/ui/scrollbacks` | Current map and docking/comms display bindings, visibility, scroll bounds/positions, follow state and pointer mapping. Read on demand; no recorder or additional hooks. |
| `/ui/hulls` | Exterior-perimeter worker/queue state, capture progress, published/applied versions, component/vertex/rendered-point counts, zoom/selection/scale details, collision-cover pieces/errors and optional physical hit/sweep/fallback counters. Read on demand; no diagnostic hooks. |
| `/orbital` | Orbital/SFF reports, including `lastEconomy` dispatch, production, arrival, failure and timing counts with bounded shipment cargo summaries. |
| `/economy` | Economic clocks, market count/settings and last SFF economic report; see [economy inspection](ECONOMY.md). |
| `/economy/stations` or `/economy/markets` | Paged station markets or all registered markets, including lifecycle and recipe/category counts. |
| `/economy/markets/{id}` | Market detail; append `/stock`, `/production`, `/actors`, `/manifest` or `/trade/{category}` for paged diagnostics. Station and freight detail aliases support the same views. |
| `/economy/freight` | Cargo-pod markets with raw AI/home/destination/course/fuel state; player cargo and parked hulls are identified, not assumed to be NPC deliveries. |
| `/economy/stations/{id}/freight` | Related freight with explicit incoming, pilot-home and hull-origin matches. |
| `/economy/hubs` | Native stored trade hub memberships, without executing redirection or stochastic route selection. |
| `/economy/audit` | Paged, request-only market/hull/controller consistency and cached-versus-backing stock checks. Missing hulls are observations, not inferred losses or pruning candidates. |
| `/economy/markets/{id}/audit` | Direct consistency check; also accepts station/freight aliases. Explicit completeness flags distinguish unknown results from matching stock. |
| `GET/POST /economy/freight/{id}/approach[/{stationId}]` | Stage a live freighter 10 km from its existing target and resume its saved native docking step. POST requires `confirm=APPROACH`; see [freight recovery](FREIGHT-RECOVERY.md). |
| `GET/POST /economy/freight/{id}/recover/{stationId}` | Credit an absent hull's surviving cargo once and clean orphaned records. Explicit destination and `confirm=RECOVER` required for POST. No hull or NPC is deleted. |
| `/ai/controllers` | Paged raw regional AI entries, including duplicates, null entries and stale hull references; no controller is repaired or selected. |
| `/ships/{id}/controllers` | All registrations for a call sign with duplicate/distinct-controller counts and exact hull identity checks. Works when the hull is absent. |
| `/flight` | Detached flight-journal status, file path, written/dropped counts, errors and recent event metadata. Available during loading. |
| `POST /flight/start` | Start a bounded, event-driven journal with a baseline, SFF and ATC transition captures and warning/error logs. |
| `POST /flight/mark` | Capture a flight checkpoint, e.g. after engaging the course. |
| `POST /flight/stop` | Capture final state, remove all temporary observers and asynchronously drain the file writer. See [flight journal](FLIGHT-JOURNAL.md). |
| `/memory?since=0&limit=25` | Detached bounded memory/lifecycle records; works during loading without Unity calls. |
| `POST /memory/start` | Install temporary memory/cleanup observers and capture a baseline; survives transit and save reload. |
| `POST /memory/snapshot` | Explicit managed/native/process memory and CO ownership census; no forced GC. |
| `POST /memory/assets` | Explicit expensive Unity object/texture/mesh/material scan; can hitch. |
| `POST /memory/stop` | Capture final state and uninstall all memory hooks/listeners. See [memory diagnostics](MEMORY-DIAGNOSTICS.md). |
| `POST /micrometeoroids/strike` | Request one MM event through BeatManager, retaining station/docking/torch/atmosphere exclusions and area-weighted scene target selection. Returns whether it triggered, target ID, detection probability/result, interception result, bursts and rounds. This causes real firing/ammunition use and possible damage. |
| `/player` | Player character, selected crew, their respective ships, nav device/ship, and crosshair target ID. Also included in `/world`. |
| `/player/nearby?radiusKm=500&offset=0&limit=25` | Ships near the selected crew's ship, with approximate distance, bearing and relative velocity. |
| `/ships?offset=0&limit=25` | Ship IDs, names, load state, destroyed and AI flags. |
| `/ships/O-U2V` | Ship summary, raw shallow fuel/capacity/mass, propulsion fields, AI timers, scan/combat targets, navigation target and stored nav plan, saved direct docking links, and read-only `stationDockedCollisionExempt`. |
| `/ships/O-U2V/ai` | AI registration, region, home station, active command chain/state, navigation target and stored nav plan, affiliations, captain, combat target and combat blackboard. |
| `GET /ships/O-U2V/refuel` | Preview replenishing this AI ship's RCS fuel to capacity. Optional `targetKg` specifies desired total kilograms, clamped to capacity; lower targets never drain fuel. |
| `POST /ships/O-U2V/refuel` | Apply that explicit refill on the Unity thread, using corrected shallow/native materialized filling. Preserve remaining route consumption by rebasing stored fuel levels once. See [refuel contract](REFUEL.md). |
| `/ships/O-U2V/factions` | Ship owner, ship affiliations and ship CO affiliations, including unregistered ships. |
| `POST /ships/O-U2V/prune?force=true&dryRun=true` | Preview named-hull pruning with native detachment; apply with `confirm=PRUNE`. Ownership/plot/crew protections remain. See [ship pruning](SHIP-PRUNING.md). |
| `/ships/O-U2V/comms?offset=0&limit=25` | Raw crew/profile references and both sides' recent conversation entries. Does not send messages or evaluate interactions. |
| `/ships/O-U2V/comms/O-QYX?offset=0&limit=25` | Stored conversation between any two ships, read from both ship logs. |
| `/npcs?offset=0&limit=25` | COs with a PersonSpec; the scan cursor counts all COs, so pages may be empty. |
| `/npcs/{coId}` or `/cos/{coId}` | Runtime CO summary, up to 32 conditions, and current interaction name. Does not search saved PersonSpecs. |
| `/cos/{coId}/conditions?offset=0&limit=25` | All raw active conditions and values, paged. |
| `/cos/{coId}/activity?offset=0&limit=25` | Current interaction, queued interactions, and active pledges without evaluating them. |
| `/derelicts?stationId=VORB&minDistanceKm=200&offset=0&limit=25` | Preview unanchored derelicts at least the requested distance from a station. Threshold can be any finite nonnegative value. |
| `POST /derelicts/prune?stationId=VORB&minDistanceKm=1000000&confirm=PRUNE&maxShips=5` | Remove up to `maxShips` eligible derelicts per call (default 5, maximum 10). |

Derelict pruning runs on Unity's main thread. It rechecks every target at the
time of the POST and excludes docked ships, body-locked ships, plot ships,
player-owned or player-docked ships, stations, and any derelict with crew that
does not pass the game's `TIsNPCPrunable` trigger. Empty derelicts are eligible.
Removed hulls use the game's `Ship.Destroy(isDespawning: false)` cleanup path.
The preview does not evaluate crew pruneability; the POST response lists ships
skipped for crew safety. Pruning checks a small per-frame work budget; repeat
the POST if `scanIncomplete` is true or preview pages still show candidates.

IDs are URL-escaped path segments. Responses wrap data with `epoch`, `generation`,
`capturedUtc`, and main-thread `inspectionMs`. `/health`, `/console`, and `/logs` have smaller responses.
Nulls are explicit. Finite numbers use invariant round-trip precision; non-finite
values are strings (`NaN`, `Infinity`, `-Infinity`) to keep JSON valid.

Example query from this workspace:

```powershell
Invoke-RestMethod 'http://127.0.0.1:18764/ships/O-U2V' | ConvertTo-Json -Depth 8
```

## Performance and lifecycle

Detailed `/cos/{id}` includes `autoVent` for sampled Kang vent COs: the stored
two-side O2 readings, low-pressure durations, effective QoL settings, last sample
epoch and trip/queued-close state. It is null without current runtime history.
Reading it does not create state or run sensors. See [auto-vent notes](../QoL/AutoVents/README.md).

- Socket acceptance, bounded HTTP parsing, serialization, and responses run on
  background tasks. Only detached primitives/collections leave the main thread.
- Unity `Update()` executes at most one query per frame. Collection scans check
  a 1 ms budget and return bounded/truncated results. This is a cooperative scan
  budget, not a hard upper bound on JIT, allocation/GC, or an individual getter.
- Four simultaneous connections, a backlog of 16, and at most 16 pending
  inspections. Saturation returns 429; clients should back off rather than retry
  tightly. Each header-read, inspection-wait, and response-write phase has a
  two-second timeout. Incomplete headers are disconnected; inspection expiry
  returns 504 and its queued work is skipped.
- HTTP headers are limited to 4096 bytes, targets to 512 characters, list size
  to 50, offset to 10000 (1048576 for economy pages and console), docking/condition lists to 32, and mapped strings to
  1024 characters. Requests carry no body and connections close after one reply.
- Scene switches/unloads and a replaced StarSystem invalidate queued requests.
  Game-not-ready queries return 503. Plugin destruction stops the socket listener,
  closes clients, and cancels waiting requests. Paused games can be inspected as
  long as Unity continues calling Update.

## Interpretation limits

Fuel fields are copied **without calling GetRCSRemain/GetRCSMax**: those can scan
canisters or change fallback capacity. They are current for shallow ships and
may be stale for Full/Edit ships. A zero stored capacity remains zero in the
response; the API does not invent a fallback. Mass is the raw shallow mass field,
not current mass including market cargo.

Dock links come from saved JSON and may be stale for Full/Edit ships. No recursive
dock traversal occurs. AI registration scans stop after 512 entries or the scan
budget; `registered: null, incomplete: true` means unknown, not unregistered.
Missing CO entries and Unity-destroyed/null entries are reported separately.

Each response is sampled within one Update call. Separate requests/pages need
not share an epoch. Offset pagination uses live dictionary order; insertions and
removals can skip/repeat entries. `nextOffset` can yield an additional empty page.
An offset too expensive to reach returns 503; prefer direct-ID queries.

This first version does not expose pledges, queued interactions, historical
snapshots, or expensive full-ship fuel calculations.

The `/ai` response's `combat` section includes AI type, cached AI captain and
their raw affiliations, target affiliations, and active command/reaction types.
The `commands.chain` section follows the current queued command and recognized
sequence steps, including FlyTo's path/manual command types and bounded saved
command state. `/cos/{id}` also includes raw faction IDs.

The `navigation` section reports `shipSituTarget`, `TargetData`, scan and
station-keeping targets, current velocity/acceleration, and the current stored
`ShipSitu.NavData` points. `currentNavPlan` still includes the first 16 points,
and now also includes `activeSegmentState`, the previous/next waypoint pair
selected using the game's stored arrival-epoch search, and the plan's final
`destination` waypoint. These fields expose the stored path only; the API does
not advance or recalculate it. `lastAdvanceEpoch` is the game's `fEpochLast`
field; the legacy `lastPlanEpoch` name is an alias and does not mean the route
was recalculated. Use `secondsUntilArrival` for the returned segment and
destination to interpret its schedule.

`navigation.vorbNavigationGuard` reports existing VORB hauler guard state: manual fallback request, last rejection reason, rejection count for the target, target ship ID, and rebased RCS fuel in kg. It is null before runtime state exists and reading it does not evaluate navigation or create state.

`GET /ships/{id}/ai` also includes `haulerRecovery`: home-target restorations,
abandoned-maneuver stops, pending derelict-release state, last epoch and reason.
It is null before a recovery event. This is read-only weakly owned diagnostic state;
querying it does not evaluate commands, create recovery state or restore targets.

`motionRelativeToScanTarget` compares stored ship and scan-target positions and
velocities. Positive `rangeRateKmPerSecond` means separation is increasing.
This read does not call `UpdateTime`; if either object has not advanced to the
current epoch, the comparison may be stale. Compare with a second time sample
when direction matters. `propulsion` contains raw RCS and fusion/torch fields,
including whether the torch is currently thrusting. The API does not project a
position, calculate fuel consumption, inspect item inventories, or invoke AI
commands. The ship's `shipSituTarget` can describe an orbit/coordinate
destination even when `scanTarget` is null. Position and velocity are in AU
and AU per simulation second; acceleration fields are raw game values. NavData
fuel fields are kept raw because their planner units depend on propulsion mode.

Combat blackboard keys are allowlisted: InCombat, Enemies, Combatants,
ClosestEnemy, SurrenderedTo, SurrenderedToUs, EnagementProfile, EnagementRange,
DodgeTarget, TripFuelPrediction, NeedsRepairs, RejectedPaths, shipboard-action
return flags, and the Ceres navigation return flag. The game's misspellings are preserved.
Each key reports `present` separately from its value; absent is not false/empty.
Ship lists and faction lists are capped at 32, with count/truncation metadata,
and respect the cooperative scan budget. Unexpected types are named without
enumeration, reflection traversal or ToString. No blackboard writes, faction
score calculations, trigger evaluation, AI execution, or firing are performed.
AIShip.Blackboard is preferred, with AICharacter.Blackboard used only if null.
These fields are stored state and may be stale; they do not prove weapons fire.

Nearby queries default to 500 km; `radiusKm` accepts finite values from 0 to
100000. They scan at most `limit` registry entries per page, include background
and hidden objects, and exclude the reference ship and destroyed ships. Follow
`nextOffset` even after an empty page. Distances are stored center-to-center
coordinates, not projected positions or MFD collision-surface ranges. The player
character, selected crew, and active nav device can be on different ships; the
response exposes each separately. Nearby uses the selected crew's ship.

Comms inspection reads up to 32 crew profiles without calling PersonSpec.GetCO,
GetCaptain, or any eligibility checks. Message pages scan raw log entries in
reverse storage order, returning matches for the other ship. Follow each log's
own `nextOffset`; an empty page can mean no match or no remaining scan budget.
Text, interaction IDs/titles, up to 16 reply IDs, availability time and read state
are copied without marking messages read. No diagnosis of empty status replies
is attempted by this endpoint.

## Logs

`/console` reads ConsoleToGUI's actual `myLog` buffer on the main thread, even
before a save loads. It returns at most 1024 characters per request, starting at
the newest end; `offset` counts characters backward from that end and accepts
0..1048576. Follow `nextOffset` for older text. `limit` does not change the fixed
chunk size. Text includes GUI rich-text tags and command echoes/results that
bypass logging events. This is the game's own rolling buffer, not persistent
history; clearing/trimming/appending can move offsets between requests.

Single-ship pruning by exact registry call sign:

```powershell
# Eligibility check only; no removals.
Invoke-RestMethod -Method Post 'http://127.0.0.1:18764/ships/O-X5GYK/prune?dryRun=true'
# Explicit removal, with all checks repeated against current state.
Invoke-RestMethod -Method Post 'http://127.0.0.1:18764/ships/O-X5GYK/prune?confirm=PRUNE'
```

The path accepts one exact registry ID/call sign, URL-encoded, up to 64 characters.
There is no display-name, transponder-alias, prefix or wildcard matching. This
route requires POST, including for dry runs. It accepts only `dryRun`, `confirm`
and optional boolean `force`.
Actual deletion requires `confirm=PRUNE`; `dryRun=true` checks eligibility only.
The existing loopback, browser-Origin rejection, queued main-thread execution,
generation and request-expiration protections apply.

The native crew condition is `TIsNPCPrunable`; there is no invented hull flag.
Every resolved crew member must pass before anyone is removed. Unresolved crew,
non-pruneable crew, player-owned/current-player ships, active plot ships, stations,
body objects are refused. Docked/tethered ships are refused unless `force=true`
can safely detach the shallow group through native methods. Connected player,
station/body and plot ships stay protected; only the named hull is pruned.
Empty traffic AI and derelicts
are eligible under these protections; other empty hulls are refused. Explicit
pruning has no distance/faction restriction and permits anchored derelicts.

Dry runs return HTTP 200 with `eligible`, `removed=false`, and either a refusal
`reason` or `pruneableCrew` count. Deletion returns `removed` and `removedCrew`;
HTTP 409 means refused, 404 means the exact registry entry is absent, and 503
means protective world/player/plot state is unavailable. If crew cleanup leaves
occupants, the hull is kept and the response reports partial crew cleanup.
Native hull destruction unregisters AI and cleans the registry/COs. This endpoint
has no background worker, retained ship references or persistent state.

Offline `Tests/ShipPruneCheck` covers parsing and refusal/commit ordering with
native types and explicit trigger/destruction test doubles (156 assertions).
It also executes native pair/chain undocking and group teardown, while gravity,
mass and presentation are fixture boundaries. It does not execute Unity scene
teardown offline. No live pruning is performed by those checks. Full force and
partial-detachment contract: [ship pruning](SHIP-PRUNING.md).

`/logs` is served on the HTTP thread and stays available when the simulation
cannot pump requests. It combines Unity's threaded log event, BepInEx events,
and a Harmony hook for ConsoleToGUI.LogInfo (which bypasses Unity logging).
BepInEx's Unity Log source is skipped to reduce duplicates. Entries carry source,
level, UTC, sequence, text, and stack where provided; independent sources can
still report the same underlying problem.

The ring holds 512 entries; text and stack are each capped at 1024 characters,
with truncation flags. Capture never waits on the reader: contention drops an
entry and increments `droppedOnContention`. Formatting occurs on the HTTP thread.
`since=0` returns recent entries; poll with `nextSince` for incremental history.
Overwrites and cursor overruns are explicit. No history from before plugin startup
is backfilled. Unexpected inspection errors also enter this buffer. BepInEx's
existing `LogOutput.log` remains directly readable from the workspace tools.

`/events/docking` records successful dock, moor, undock, and unmoor transitions
observed after plugin startup. Poll with `since=<nextSince>`. Each event includes
UTC/game time and actor/target IDs, names, owner factions, AI type, active command,
combat target, and port IDs where available. For a new connection, `actor` is the
ship instance whose game method initiated the transition; reciprocal updates are
collapsed into that one event. The ring holds 256 events and resets when the
simulation/scene changes or the plugin restarts.
Transitions from before plugin startup are not available.

`/events/ai` samples registered AI ships at most once per real-time second while
the simulation is running and records changes
to command, scan/combat target, combat flag/lists, and dock links. The first sample
seeds comparison state without emitting a baseline event. It observes at most
512 AI ships per sample. This is bounded memory only; it writes no history to disk
and may miss state changes shorter than the sampling interval.

`/events/combat` records the game's successful weapon-fire event (shooter,
weapon, and current combat target) and ship-damage event (damaged ship). The game
damage callback does not identify the attacker, and a fire event does not prove
the projectile hit. Both event rings are bounded in memory and reset on a scene
or simulation change.

`/cos/{id}/conditions` pages the runtime condition map without evaluating
triggers or calculating derived health. `/cos/{id}/activity` reads the current
interaction queue and pledges through stored IDs/definitions; it does not call
pledge completion checks or lazily resolve CO references.

`/ships/{id}/comms/{peerId}` returns matching messages from both ships' logs,
including stored interaction/title/text/reply IDs. It does not request a new
response or evaluate whether a message can currently be sent.

## Personnel combat

Use this for personal ranged weapons, distinct from `/events/combat` ship weapons.
Start capture through the API, let the chosen actor fire, stop capture, then read
`/combat/personnel/trace`. IDs in query strings and paths must be URL-encoded
(for example `Ayden%20Kelly`). No request body is needed. GET cannot start or
stop diagnostics. A second start returns 409 while the first trace is active;
after stopping, starting a new trace clears its predecessor.

Each ray records the native origin, direction, range, attack definition,
shooter, intended target from the executing interaction and target state at
firing. Rays fired outside an observed interaction can have a null intent.
Pellets share an `attackSequence` within their enclosing attack interaction.
`physicsCalled` distinguishes an early native return from an empty raycast.

The trace observes the **original** `Physics.RaycastAll` result, including
distances, points, normals, collider type/name/trigger/layer/bounds and resolved
CO IDs. Candidate hits retain Unity's unsorted array order; distances identify
their relative positions. Processing events follow the native traversal order,
showing destructible/character component visits, actual damage rolls, object
durability checks, requested `StatDamage` additions with immediate before/after
values, and actual wound calls with their native return values. The three damage
rolls are environment, blunt and cut, in that order. Multiply them by the attack
definition's respective damage and the ray multiplier for initial budgets.
Native wound return values include native wound scaling/armor; they are not
unscaled projectile damage or a complete health report.

Object durability checks report the effective value used by ray processing.
`ignoredFloorMeat=true` identifies the always-on loose-meat cover fix: zero
effective cover durability lets the ray continue without changing the scrap's
actual durability or spending projectile damage.

A candidate collider, attack reply, or normally returned ray is **not** proof
that the intended target took damage. Follow processing events to distinguish
geometry misses, intervening destructibles consuming damage, skipped shooter
colliders and actual target damage. No additional raycasts, random draws,
attacks, trigger evaluation or damage calls are made. Native exceptions remain
native exceptions; exceptional rays record their exception type.

Recording defaults off. The two Harmony hooks (attack context and ray
observation) are installed only on explicit start and removed on stop, expiry,
scene/system change or plugin disposal. While idle, only a constant-time check
in the API pump remains. Active recording allocates diagnostic snapshots and
adds collider/property reads, so use short captures rather than leave it on.
Capture stops at 64 rays or the wall-clock deadline, even while game time is
paused. Limit/timeout removes hooks at the next Unity pump, after native calls
have unwound. Each ray stores at most 32 candidate hits and 64 processing events;
truncation and dropped events are explicit. Stored history contains only
bounded primitive data, no Unity objects, and never enters saves. Stop retains
history; a new trace or scene/system transition clears it. Combat snapshots
read at most eight queued interactions and 32 direct equipment entries; they
do not search inventories recursively.

`Tests/RangedSelfHitCheck` exercises the real native ray damage method with
engine fixtures, including production hook installation/removal, intended
target context, shooter filtering, ordinary/wound damage, unchanged random-draw
count, exception propagation, capacity/timeout cleanup, detached serialization,
HTTP query parsing and compatibility with the permanent self-hit fix. Fixtures
provide collider hits; they do not establish live Unity collision geometry.

On 1.0.1.10, `/workorders/scheduler` also includes bounded native `workClaims`,
their reservation/spawn counts and iterator states, total reserved-task count,
and the native work-manager frame budget. It reads these without advancing jobs.

## Validation

The mod builds with zero warnings/errors against the installed game DLL. A
standalone harness links the actual transport, dispatcher, HTTP parser, and JSON
code with game/scene doubles. It checks real loopback HTTP, main-thread dispatch,
pagination validation, method/origin rejection, scene invalidation, not-ready
state, client/queue saturation, expiry, recovery, numeric precision, and rebind.
Unity/Mono integration and live per-frame costs still require an in-game run.
