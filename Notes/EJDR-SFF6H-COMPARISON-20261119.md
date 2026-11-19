# Six-hour departure SFF comparison, November 19

Compared against [the pre-departure baseline](EJDR-DEPARTURE-BASELINE-20261119.md).
Post-SFF captures are in ignored `obj/Survey-20261119-post-SFF6h`.
The complete repeated capture was paused at epoch `65635093822.671295`.
No recovery, refueling, cargo credit, pruning, trace activation or save write
was performed.

## Time advance and capture boundaries

The retained GUI-SFF report covers exactly 21,600 seconds, from
`65635072106.076935` to `65635093706.076935`. Its 12 world steps include 11
course-completion boundaries, with 31 admitted role callbacks and no budget
fallback. Reported processing time is approximately 1.85 wall-clock seconds.
The last economy record covers only the final subwindow; its zero deliveries
must not be interpreted as zero deliveries across the entire six hours.

The loaded main-plugin MVID is now `74a76bb0-f603-4926-a981-10197e2a82f8`,
different from the pre-departure capture. Game MVID remains the reviewed 1.1.0.3
identity. This is a world-state comparison, not an A/B run isolating one DLL.

An initial controller read was incomplete; the repeated complete capture has
31 registrations. The paged pod-market collection repeatedly returned 503 near
its tail. Fleet membership here uses the complete controller collection and
individual cargo-stock captures, not that incomplete pod-market list.

## Freight and economy

- Active freight rises from 20 to 22: 19 original freighters remain, O-S5FM
  delivers, and three new ships depart. All 22 current freight captains are
  alive aboard their matching ships; no controller identity conflict is found.
- O-S5FM delivered its complete 2,647 medical payload to SVIR. Medical stock
  rose exactly from 1,753 to 4,400. The cargo market is absent, its hull survives,
  its captain Xiaoyan Jarvis is alive aboard SVIR, and the cargo controller is
  removed. The separate `arrival-O-S5FM.json` preserves the comparison receipt.
- The other 19 original freighters retain their category/quantity payloads.
- New O-UCX carries 72 life support, O-LBZP 88 industrial products and O-157
  72 life support. The final subwindow explicitly records O-LBZP departing for
  MLAB and O-157 departing for VORB.
- All 117 market audits complete and match backing inventories. The count of
  positive historical shipless inventories remains 57, rather than increasing.
- VORB luxury stock changes from 1,836 to 1,830; its nine 468-unit inbound loads
  remain in flight. The existing inbound/storage imbalance remains a watch.

O-X8FA is about 447,599 km from VORB, closing at 295 km/s, with roughly 48.4
minutes to its current route endpoint. This is the next useful delivery watch.

O-GJ3C changes from separating by 3.69 km/s to closing by 403.4 km/s. O-TWV
and O-2DM are still separating, but their rates fall from 2,381 to 2,001 km/s
and 3,865 to 3,484 km/s respectively. Their route endpoints remain in the future;
this snapshot does not establish a route failure.

O-Y8H and O-X8V3 retain zero recorded RCS reserves while following their torch
routes. O-KYR retains its 14.4% reserve. These arrival/propulsion-handoff watches
remain unresolved, not demonstrated delivery failures.

## Other traffic

There are eight active EJDR civilians and the player's autopilot controller in
addition to freight. No active AI combat target, shipboard encounter or
controller-less non-player hull with recorded input/RCS acceleration was found.

O-Y04V2 remains active and has refilled RCS. O-TYG is alive and holding, now
undocked from its previous O-DWL host. B-23LS, O-DWL and O-ZLHA are absent from
the later hull registry; their removal causes are not established by this
comparison. None is one of the original 20 freighters or 19 Luna candidates.
The net hull count is 383, versus 381 before the burn.

## Luna turnover

The active ATC remains EJDR after the skip. All 19 Luna candidate wrecks remain
present with EJDR_GEO as parent. No turnover is expected before a qualifying
EJDR departure and later return.

The most recent `burn.zip` contains a pre-SFF epoch of `65635072067.879082`,
EJDR as region, and an empty pending-fields list. Its archive hash was checked
unchanged. It is not post-SFF bookkeeping evidence; confirm the cohort from a
save taken after the actual EJDR exit.
