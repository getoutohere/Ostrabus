# Opt-in flight journal

Capture an ordinary trip without prescribed maneuvers or repeated save loads.
This is a diagnostic recorder, not a gameplay feature or simulation correction.
It never activates automatically.

## API

| Route | Behavior |
| --- | --- |
| `POST /flight/start` | Install temporary observers and capture a baseline. Repeated start is idempotent. |
| `POST /flight/mark` | Capture a checkpoint, useful after engaging a plotted course or arriving. Returns 409 if not recording. |
| `POST /flight/stop` | Capture a final snapshot, remove observers immediately and let the writer drain. Also works before the world is ready. |
| `GET /flight` | Detached status, file location, written/dropped counts, errors and the latest 64 event summaries. Works while Unity is busy. |

Controls require POST. They run through the existing main-thread inspection
queue; GET reads detached metadata on the HTTP thread. The API does not accept
an arbitrary output filename. A previous writer must finish draining before a
new session can start. Check writerFinished and writerError after stopping.
GET status and POST stop return metadata directly; start/mark use the normal
main-thread response wrapper's `data` field.

Files are uniquely named JSONL under `BepInEx/FlightJournals`. The first line
identifies format/version and units; normal closure adds a `journal-end` footer.
Event records use the public DTO field names (`Kind`, `Epoch`, `Ships`, etc.).
Each line can be streamed through a JSON parser for offline analysis; no need
to paste the file into chat. A crashed process or writer failure can leave a
partial final line and no footer. Treat that session as incomplete and retain
the preceding complete records.

## Automatic events

- `baseline`, `checkpoint`, `final`: explicit session boundaries/checkpoints.
- `sff-world-before` and `sff-world-after`: an actual StarSystem.Update during
  Super Fast Forward with a step of at least 60 simulation seconds. The after
  observer runs after Ostrabus's orbital finalizer and preserves the existing
  SFF result before another step can overwrite it. NewSffReport explicitly
  distinguishes a newly generated report from a stale/disabled report.
- `atc-before`, `atc-after`, `atc-failed`: immediately around native
  AIShipManager.RegionCleanup. Before runs at first prefix priority.
- `atc-populated`: the end of the AI manager update that entered the new region,
  after its initial population pass. This is not a claim that asynchronous
  loading has finished.
- `atc-follow-up`: one later AI manager update at least 60 simulation seconds
  after that transition. No forced time advancement or wall-clock polling.
- `log`: Unity warnings/errors/exceptions/assertions and BepInEx warning/error/
  fatal messages. The Unity Log bridge is excluded from BepInEx capture to
  avoid duplicating the same Unity event.

Before/after and region follow-up records share a TransitionId. UTC and game
epoch are included. Background-thread log callbacks use the last observed game
epoch and explicitly set EpochApproximate; main-thread logs use current epoch.
Logs can precede or follow related snapshots, so use transition/epoch/UTC
rather than assuming every nearby line describes the same simulation phase.

SFF capture surrounds the orbital world step, not the later character fatigue/
sleep report UI. The recorder does not change Orbital toggles, time acceleration,
targets, ship state, fuel, courses, pruning or spawning.

## Snapshot content

Snapshots include bounded world-wide ship rows, so distant contacts are not
excluded merely by a player-relative radius:

- Ship identity and object identity, load/classification/damage state, AI flag.
- Stored position/velocity, thrust/acceleration vectors, reference body, lock
  flags and docking links.
- AI registration region, home station and raw command/reaction/pilot types.
  AIShip.Ship is deliberately not called: its getter can repair a stale ship
  reference. Stale registration references are counted separately.
- Target IDs, target motion and reference consistency against the world map.
- Cached RCS fuel/capacity, RCS count, fusion fuel/thrust and torch indicators.
  FuelCachesCurrent identifies the shallow-state fuel cache; materialized ships'
  cached values are not advertised as current tank measurements.
- Stored plan point count, final destination motion and arrival epoch. The
  player's course additionally includes bounded waypoint motion, times, raw
  fuel values, torch-cycle state and deceleration flags.
- Analytical body positions/velocities, cache epochs and orbital elements.
  The player's reference-body ancestry and real planets/moons take priority
  over placeholder orbits when reaching the cap.
- World/AI/CO counts and the existing detached Orbital/SFF status report.

Numeric load/classification/damage values are the game's enums. Motion units
are AU and seconds. Body cache epochs are preserved because those cached values
are not necessarily contemporaneous with the snapshot. Analysis must account
for that before drawing orbital/relative-velocity conclusions. The recorder
does not call UpdateTime, replan routes, evaluate triggers or enumerate tanks.

## Cost, bounds and cleanup

Only while active, a separate Harmony owner observes three native methods:
StarSystem.Update, AIShipManager.RegionCleanup and AIShipManager.Update.
Normal world updates only reject the non-SFF step; full surveys occur at the
events above. There is no additional permanent Update check or startup patch
attribute. Stop/API disposal removes all three hooks and both log listeners.

Unity-thread capture copies detached scalars/strings into pooled ship-row
arrays. The temporary AI index is cleared in finally. The writer returns each
array only after consuming it, clearing every row before pooling; at most four
idle arrays are retained. No Ship, CondOwner, BodyOrbit, AI controller or Unity
object is retained by a queued record. Course/body arrays and JSON serialization
still allocate, so active recording is not allocation-free.

The writer owns a background task and streams JSON directly to its file,
avoiding a large intermediate JSON string for each world survey. File creation,
serialization, flushing and disk I/O occur there. The Unity producer never waits
for disk or queue capacity; queue/contention drops are counted. Status retains
only event metadata, not historical full surveys in memory. CaptureMs records
main-thread survey work so its actual live cost is visible.

Bounds:

- 1,024 ship rows, 128 bodies, 128 player course points per snapshot.
- 16 direct docking links per ship; at most 4,096 AI registrations examined.
- A 16-record writer queue, 8,192 accepted records and 128 MiB per file.
- Messages/stacks limited to 4,096 characters; truncation is marked.

Truncation, dropped records, capture errors and writer errors are explicit.
File/record limits stop further acceptance; the next observed native callback
removes observers on the Unity thread. Stop also works while paused. A normal
stop drains asynchronously: wait for writerFinished before exiting if the final
records matter. Failure/size-limit termination may lack a footer and have a
partial trailing record. Queue primitives are disposed when the writer ends.

## Validation and normal use

`Tests/FlightJournalCheck` runs against the real game method signatures with
fixture method bodies and a simulated orbital finalizer. It checks before/after
ordering, native exception identity, deferred population sampling, frozen
snapshots, truncation/player/body priority, warning capture, background writing,
queue backpressure, disk/size errors, file framing, array/AI-index release,
restart/idempotence and complete hook/listener removal. It does not claim a live
capture cost or validate orbital outcomes by itself.

For the OKLG-to-Venus trip: start before departure, mark after course engagement,
then let automatic SFF/ATC events capture normal play. Mark/stop at arrival or
after the intended prospecting interval. Compare departure pathologies with
newly developed ones; use ATC before/after populations to distinguish native
region cleanup from navigation failures. No special maneuver, artificial
refueling, pruning, repeated transit or reload is required.
