# Earth/Luna to OKLG: repeated SFF comparison

Read-only captures on game 1.1.0.3, compared with the
[departure baseline](EJDR-DEPARTURE-BASELINE-20261119.md) and
[first six-hour comparison](EJDR-SFF6H-COMPARISON-20261119.md).
No ship pruning, cargo credits, controller commands or live deployment occurred.

## Capture boundaries

| Capture | Epoch | Physical hulls | Controllers | Cargo-backed freighters |
| --- | ---: | ---: | ---: | ---: |
| Before departure, EJDR | 65635072061.963318 | 381 | 22 | 20 |
| First SFF, still EJDR | 65635093822.671295 | 383 | 31 | 22 |
| Later, TRANSIT-ZONE | 65635224058.976494 | 311 | 31 | 29 |
| Arrived at OKLG | 65635269140.753738 | 334 | 41 | 31 |

The last capture spans one paused epoch, 54.74 game hours after baseline.
Ayden is alive aboard OKLG_FLOT. The 41 controllers comprise 31 freighters,
one PASS shuttle, seven scavengers, one pirate and one police ship. The shuttle
O-PTUV legitimately uses the HaulerCargo controller class without a freight
market; native AIShipManager explicitly constructs PASS transports this way.
All 32 cargo-role captains are alive aboard their corresponding hulls.

The baseline loaded Ostrabus MVID was
`508d0588-d4fb-426c-9df6-4e0f1d567593`; subsequent captures loaded
`74a76bb0-f603-4926-a981-10197e2a82f8`. Game MVID is
`5b1ee244-539d-4902-8d62-97490deee9d7`; generation remained 7. This is an
operational comparison, not a same-binary A/B experiment.

Raw evidence is under ignored `obj/Survey-20261119-pre-OKLG`,
`obj/Survey-20261119-post-SFF6h`, `obj/Survey-20261119-transit-SFF` and
`obj/Survey-20261119-OKLG-arrival`. These are private captures, not package assets.

## Freight and markets

All 31 current freighters carry positive stock, totaling 15,115 units; individual
loads range from 72 to 3,020. Direct controller and stock captures establish the
count: the general freight pagination endpoint returned a 503 on a later page
and must not be treated as a complete fleet census. The PASS stock query's
404 is expected. No duplicate or stale controller registration was observed.

All 126 current registered-market comparisons completed and matched backing
inventories. O-YTG6 initially reached the inspection budget; a successful direct
retry confirmed 83 cached and backing units. The earlier transit capture had
123 complete matching comparisons and one incomplete comparison, not 124
proven matches.

The same 57 shipless market IDs retain exactly the same observed quantities as
the departure baseline: 29,798 units in total. These are historical records,
not 57 new losses. No redistribution was performed.

O-S5FM's first-six-hour arrival remains the strongest complete delivery receipt:
2,647 medical units appeared at SVIR, its cargo market was removed, and captain
Xiaoyan Jarvis was alive at SVIR. Further successful captain observations:

| Previous freighter | Captain | Observed location |
| --- | --- | --- |
| O-X8FA | Erica Castillo | VORB |
| O-1YUU | Eric Jesus Bond | VORB |
| O-JWRM | Liang Damian Barr | MHNG |
| O-LBZP | Clinton Tanner | New freighter O-ZMQG |
| O-157 | Grant Cardenas | VORB |
| O-EJXS, old mission | Hadassah Angela Owens | VORB, alive |

Captain locations are evidence of handoffs, not proof of exact delivered
quantities. O-EJXS now has a different captain and mission; its new voyage must
not be compared as a continuation of the original luxury shipment. Cooper Mann
from O-44NC is absent from live mapCOs; this does not establish death or delivery.
A filtered obituary read failed with a closed connection. Later timed-out
captain retries are retained separately and do not override successful earlier
observations or prove that someone disappeared.

## Navigation watches at OKLG arrival

Negative range rate means closing. Values are stored snapshot observations,
not newly calculated flight plans.

| Ship | Destination | Range, km | Range rate, km/s | Remaining route, hours | RCS, kg |
| --- | --- | ---: | ---: | ---: | ---: |
| O-TWV | VORB | 146,196,153 | -1,143.14 | 38.36 | 1,030.83 |
| O-2DM | VORB | 422,578,661 | +340.47 | 90.89 | 1,197.98 |
| O-GJ3C | SVIR | 1,338,641,154 | -3,687.85 | 112.93 | 2,248.09 |
| O-KYR | VORB | 367,432,386 | -587.51 | 87.49 | 431.74 |
| O-Y8H | MTRS | 1,278,762,998 | -4,038.71 | 107.07 | 0 |
| O-X8V3 | MTAM | 1,256,229,469 | -4,121.57 | 106.11 | 0 |

O-TWV changed from opening at 2,381 km/s to closing. O-2DM's opening rate fell
from 3,865 to 340 km/s; it still has a future route, not an expired approach.
The zero/low-RCS ships remain watches until their final approaches are tested;
these snapshots do not establish a new delivery failure. No nonplayer hull
without a controller had residual input or RCS acceleration in the last capture.
The nine local OKLG controllers have living captains, no active ship-combat
targets, and ordinary FlyTo or Lurk commands; no intervention was indicated.

The last six-hour GUI SFF used 23 world steps, including 20 course-completion
boundaries and two decision boundaries, taking 1,837.8 ms in our simulation pass
without a budget fallback. An earlier six-hour transit skip used one world step
and took 359.6 ms. These are retained simulation timings, not total wall-clock
loading times; the detailed economic/physical reports cover the last subwindow
of a split skip, not necessarily all six hours.

## Luna turnover failure and repair

The `coast` save at epoch 65635224058.976494 records TRANSIT-ZONE and optional
turnover metadata with an empty Fields array. All 19 visited Luna wrecks remain
native shallow hulls. The archive hash was unchanged by the read-only audit.
The player company contains only Ayden and Shan; both were resolved aboard
Yarder at departure, so an extra unresolved roster entry was not the cause.

Two erroneous guards excluded these wrecks:

1. Native Ship.SaveCOs stores NPC loot-spawner items in aShallowPSpecs, not
   occupants. Every Luna hull has two or three SysLootSpawner records. Our saved
   array-length guard rejected them as occupied even though actual crew was empty.
2. Shallow loading instantiates these markers. The blanket nonempty loaded-item
   guard rejected them too, although native root-owned spawners are not people
   or orphaned interior objects.

The suspected geometry error was a false lead: the first offline fixture
incorrectly assumed a natural parent. Native GetNearestBO actually selects
EJDR_GEO itself. The corrected fixture executes native selection and placement,
which agree with the saved centre within a metre. No geometry change is included.

Boneyard Turnover 0.1.3 repairs these checks without widening the annulus,
changing authored pools or adding native save fields. It distinguishes
root-owned native spawn markers from people, ordinary loaded items and orphan
markers. Existing ownership, target, plot, bounty, docking, navigation and actual occupancy
protections remain. Departure counts/refusal reasons are logged at transitions.

The saved-geometry reproduction captures and commits all 19 wrecks as a cohort;
10,439 production checks pass on both supported game assemblies. At this stage
the repair was built and packaged, not installed. This departure was never captured, so the
update does not invent missing absence history; tracking resumes on a future
qualifying departure. See the standalone
[validation](../StandaloneMods/BoneyardTurnover/VALIDATION.md) for test limits.

The subsequent explicit repair prepares a separate save copy using the observed
coast-to-OKLG absence, starting at the first proven outside timestamp. This
external evidence permits a conservative backfill even though the runtime
plugin cannot infer missing history automatically. The original flot repair
credits 12.52 hours and preserves all 353 native payload hashes; it was
superseded by a repair of the player's newer bounty save. That copy credits
12.68 hours and preserves all 355 native payload hashes. Both original archives
remain untouched; application waits for the game to close.
See the [repair tool](../Tools/BoneyardTurnoverRepair/README.md).

Both first-outside fallbacks were subsequently rejected as inappropriate: coast
was already late in the trip. Replaying the full saved 54.532-hour course and
saved body hierarchy through native code reproduces the six-hour live positions
(EJDR error zero; Yarder error below a millimetre). The 10,000,000-km native
region range is crossed at epoch 65635098013.32843, 7.2049 hours after engagement.
This gives 47.6939 hours of reconstructed physical absence at bounty, rather
than 12.6812. The exact historical RegionCleanup callback was not retained;
this is a validated geometric reconstruction. Earlier prepared copies are
marked superseded so the apply helper will reject them.

## Final applied state

With the game closed, the corrected standalone 0.1.3 DLL was verified against
the installed plugin and the repaired bounty archive was applied atomically.
Luna has 19 captured wrecks and 47.6939 hours of reconstructed absence. Phobos
has ten in-bounds candidates and the requested eight turnover days; O-8MCY
remains excluded. All 355 native payload hashes are unchanged. Only the optional
turnover entry changed, and each application retained an original archive backup.
The player was told the save was ready to load.

Mercury's requested five-day age was not applied: its original twelve wrecks
have changed attachment from HQCH_DET to Mercury, and none passes the current
spatial checks. The separate seven-field
[model review](../StandaloneMods/BoneyardTurnover/FIELD-MODEL-REVIEW.md) documents
that legacy displacement and a confirmed Ganymede offset-centre defect. No
speculative frame selection, relocation or orbital change was installed.
