# EJDR departure / system baseline, November 19

Captured read-only before the player's planned Earth/Luna-to-OKLG torch trip.
All live captures were generation 7 at paused game epoch
`65635072061.963318`, beginning about 16:34 UTC. Game assembly MVID is
`5b1ee244-539d-4902-8d62-97490deee9d7` (Ostranauts 1.1.0.3); loaded Ostrabus
MVID is `508d0588-d4fb-426c-9df6-4e0f1d567593`.

Detached evidence is in ignored `obj/Survey-20261119-pre-OKLG`: raw ship,
controller, AI/navigation, freight, stock, production, market audit and orbital
snapshots, plus the compact `summary.json`. No traces, simulation commands,
inventory transfers, pruning, refueling, recovery or game-save writes were used.

## Traffic

- 381 registered hulls, 22 live AI registrations: 20 INTERREGIONAL cargo haulers
  and two EJDR civilians. No duplicate/stale/null controller entries were found.
- Every registered AI has a living captain aboard its matching hull. No active
  combat target or shipboard-action encounter was reported.
- All 20 freighters have positive cached cargo stock, active FlyTo commands and
  torch routes with future endpoints. These endpoints are not guaranteed delivery
  ETAs; approach and docking may follow.
- The full hull snapshot found no recorded input/RCS acceleration on a hull
  outside the controller list and the player's two ships. This is a stored-state
  acceleration check, not a projected celestial-intersection test.
- O-Y04V2 (Frequency Illusion), captain Kemi Rowland, is closing on EJDR-3500P at
  450 m/s; route end is about 14.2 minutes away, RCS reserve 85.4%.
- O-TYG (Mendicant Peaks), captain Molly Hammond, is holding while docked to
  O-DWL. RCS reserve is 31.7%; its saved visit Hold carries start epoch
  `65635067967.246513`, about 68 minutes earlier. A moored visit is not by itself
  proof of a navigation failure.

## Freight comparison watches

| Callsign | Destination | Payload | RCS reserve | Hours to current route end |
| --- | --- | --- | ---: | ---: |
| O-S5FM | SVIR | 2,647 medical | 66.7% | 2.51 |
| O-X8FA | VORB | 468 luxury goods | 41.5% | 6.85 |
| O-JWRM | MHNG | 73 media | 73.0% | 8.07 |
| O-44NC | MTAM | 3,200 volatiles, 90 D2O | 33.8% | 8.18 |
| O-KYR | VORB | 468 luxury goods | 14.4% | 142.23 |
| O-Y8H | MTRS | 324 D2O | 0% | 161.81 |
| O-X8V3 | MTAM | 324 D2O | 0% | 160.85 |

O-TWV and O-2DM currently open range from VORB by approximately 2,381 and
3,865 km/s, respectively; O-GJ3C opens range from SVIR by 3.69 km/s. Their
routes are still active/future. Preserve their exact route/phase data for later
comparison rather than calling instantaneous separation proof of runaway thrust.
Likewise zero stored RCS fuel on a shallow torch route does not establish that
the ship cannot complete its eventual native approach.

Nine SVIR freighters still carry 468 luxury goods each to VORB: O-Q8J, O-KYR,
O-TWV, O-2DM, O-X8FA, O-EJXS, O-GQN, O-PW28 and O-1YUU. Total inbound stock
is 4,212; VORB has 1,836/3,300 luxury stock, leaving 1,464 capacity. Existing
inbound exceeds current capacity by 2,748 before intervening consumption. Native
overflow disposal remains the chosen policy; no rerouting was performed.

## Economy

All 115 current registered market stock comparisons completed and matched
backing inventories. The first HQCH audit was budget-limited and was superseded
by a successful direct retry. The paged freight list returned one 503 response;
direct flight captures and complete stock pages for all 20 controller-confirmed
freighters were obtained afterward. Do not use the partial freight list as a
complete fleet count.

Across 27 station markets, all 276 recipes were captured: 157 waiting on their
cycle clock, 70 missing inputs and 49 output-full; none was overdue at capture.
MTRS has ten input-starved recipes, MTAM nine and SVIR eight. BCRS has the most
recorded metal, 660/660, followed by VORB at 293/3,300.

There are 66 unresolved/shipless market records. Fifty-seven retain stock,
totaling 29,798 units. Every one of those 57 IDs is present in the previous
November 18 market-audit set. This is a retained historical-cargo baseline,
not evidence of 57 new losses or permission to distribute those inventories.

## Luna boneyard turnover

- Boneyard Turnover 0.1.2 is loaded successfully on 1.1.0.3, enabled, with
  HalfLifeDays 7 and CurveShape 1. No turnover warning was found in the retained
  startup log. Runtime bookkeeping is not exposed by the main inspection API.
- Active ATC is still EJDR. The latest departure save also records EJDR and no
  pending field departures. Moving between Earth and Luna while remaining in
  EJDR does not start an absence.
- That save contains 19 Luna wrecks under environment map 1000, owned by EJDRCiv
  and anchored to EJDR_GEO. All 19 also resolve live as shallow, undocked,
  non-AI hulls with that same parent. They are departure candidates; actual
  eligibility also checks bounds, occupants, claims, targets and plot obligations.
- Candidate IDs: J-REG, M-XL4T, E-U8A4, O-HW2T, M-AJK, O-MUNQ, O-2KWR8,
  E-UL62, M-1UG, V-ZND, H-145, O-X45, B-XDJF, H-YPCU, H-1AM0, M-SQAB,
  H-DN8W, E-PGAH and H-7Y4.
- Ayden and Shan both resolve aboard AKS Yarder (M-TZ3T), docked to Stellar
  Magnificence (O-GXD). The group is no longer linked to a station in the live
  saved-docking representation. Shan is not being left behind at Luna.
- Qualifying departures should be recorded at the actual EJDR exit, and the
  resulting cohort can be checked in the next save's optional metadata. No
  departure/return turnover outcome is claimed from this pre-departure snapshot.

The read-only save audit used `depart.zip`, epoch `65635067968.346649`, SHA-256
`DB9A8DF5B6DA9E8040FD2A3F382DE21F56B3249175832C17AA742CA3E595661E`.
Its hash was unchanged. This save predates the live snapshot by about 68 minutes;
use the live snapshot for current motion and the saved data only where identified.
