# Earth/Luna traffic and system economy baseline

Captured November 19, 2026 at approximately 00:02 UTC (November 18 local time),
Ostranauts 1.1.0.2. Baseline game epoch: `65635038076.148506`.
Read-only survey; no cargo transfers, pruning, refueling, recovery commands or
diagnostic traces were activated.

## Location and coverage

The player is in Stellar Magnificence (`O-GXD`) near Earth. The reported active
ATC registry is still **EJDR**. Its ten active civilian ships are clustered near
Port Shajiang and the Mare Imbrium Space Elevator at Luna, approximately
384,500-388,000 km from the player. Do not describe those ships as nearby low
Earth traffic merely because they share the active registry.

The complete ship-registry scan contained 380 hulls. A separate 100,000 km
radius search around the player found 59 derelicts and four Earth-EVAC navigation
markers, and no active AI ships. All 63 objects had an Earth body lock and zero
recorded RCS/input acceleration. No local drifting or phantom-thrust case was
identified in this snapshot. This was not a projected planetary-intersection
calculation.

## AI controllers

The controller scan completed with 30 registrations: ten EJDR civilians and
twenty INTERREGIONAL cargo haulers. Each matched its live registry hull, had one
controller, and had a living captain aboard. All were running `FlyTo`, had a
current route with a future endpoint, and had negative target range rate
(closing). No active combat targets or reactions were reported.

| EJDR callsign | Name | Target | Recorded RCS fuel remaining |
|---|---|---|---:|
| O-X02 | Plasma Opera | EJDR-FD2 | 90.7% |
| O-4ALM | Just Passing Through | EJDR-FRD1 | 83.2% |
| O-DWL | The Star | EJDR_GEO | 93.2% |
| O-1U4 | Colloquial Metaphor | EJDR-500B | 89.2% |
| O-B8WS | Bleak Pop | EJDR-FD3 | 83.9% |
| O-474 | Martyred Rabbit | EJDR-500B | 90.4% |
| O-HAZV | When It's Done | EJDR-FD3 | 90.7% |
| O-AWMR | Invisible Reliquary | EJDR-3500P | 92.2% |
| O-YYNS | Carbon Semicolon | EJDR-FD1 | 91.4% |
| O-Y04V2 | Frequency Illusion | EJDR-3500M | 89.0% |

Targets here are station infrastructure/reference IDs, not mobile salvage hulls.
Stored route data is a baseline, not proof of future successful docking.

## Live freight

All twenty live freighters had cargo backed by cargo-pod actors and consistent
market totals. None was empty or captainless. All had an active torch route and
were closing on their current destination in the captured data. The large
interplanetary ranges are not by themselves evidence of runaway motion.

Useful arrival watches:

| Callsign | Destination | Payload | Hours to current route endpoint |
|---|---|---|---:|
| O-5QGU | OKLG | 45 media | 0.50 |
| O-S38 | SVIR | 1,074 science | 1.42 |
| O-QBG | MTRS | 129 electronics | 3.09 |
| O-0E65 | VORB | 163 control systems, 68 industrial products | 3.52 |

These endpoint times are not guaranteed delivery ETAs; local approach and
docking may follow. O-KYR had the lowest recorded RCS reserve, 431.74/2,997.46 kg
(14.4%), while still following its torch route to VORB. This makes it a useful
arrival/propulsion-handoff watch, not an established fuel-stranded case.

Nine SVIR freighters are carrying **4,212 luxury goods to VORB**: O-Q8J, O-KYR,
O-TWV, O-2DM, O-X8FA, O-EJXS, O-GQN, O-PW28 and O-1YUU. Each carries 468.
VORB's current luxury inventory is 1,842/3,300, leaving 1,458 spaces. Thus the
committed loads exceed current headroom by 2,754 units before intervening
consumption. This is a supply-coordination concern, not a demonstrated delivery
failure or authority to reroute cargo. Native overflow disposal remains the
chosen policy.

## Markets and production

All **129** registered markets were audited to completion. Cached stock totals
matched backing actor inventories; no negative/identity mismatch was identified
by these audits. Across 27 station markets, 276 recipes were examined; no recipe
was overdue or invalid at capture. The next system dispatch check was about
55 minutes away, rather than stuck overdue.

There are substantial supply shortages:

- MTRS and MTAM have zero metal, plastics and water. MTRS has nine productive
  recipes missing inputs, MTAM eight. Two live shipments carry 790 electronics
  to MTRS, which will address only some of its shortages.
- VNCA has zero metal, blocking consumer goods and industrial products, with
  downstream plastics/textiles shortages. VCBR is missing sensors, industrial
  products, plastics and electronics for several science/HVAC recipes.
- SVIR has seven productive recipes missing inputs, including metal, science,
  textiles, tools, HVAC and control systems. Incoming science, textiles and
  medical shipments provide some relief; no live metal shipment was found.
- BCRS has 660/660 metal and 532 ores; its metal production is output-full and
  its metal stock is eligible for native export. HQCH's trash-to-metal recipe
  has input and is waiting for its next cycle. This is not a system-wide absence
  of metal production.
- EJDR itself has no productive recipe currently blocked by missing inputs;
  industrial-products stock is low at 57/880. Its trash stock is full.

Shortages and full output stores are observations of logistics imbalance, not
automatically defects in recipe execution.

## Shipless cargo records

There are **80** shipless markets with cargo-pod configuration. Seventy-one
contain stock, totaling **41,249 units**; nine are empty. These are not eighty
live freighters, and a market surviving its hull is not sufficient evidence to
recreate a ship or credit the same cargo a second time.

Of the 80 records, 68 were already shipless in the November 16 market baseline.
Twelve IDs were absent from that baseline and now contain **9,885 units**:

| Former callsign | Remaining stock |
|---|---|
| O-6HJ6 | 468 luxury goods |
| O-WDN | 468 luxury goods |
| O-ZNU | 1,472 media |
| O-AL0 | 536 textiles |
| O-F3X | 202 medical |
| O-2P3 | 3,300 food |
| O-QKJ | 468 luxury goods |
| O-N2V | 160 industrial products |
| O-J3FE0 | 1,469 He3 |
| O-KKWJ | 692 media, 58 life support |
| O-5JQ | 94 sensors, 220 science |
| O-DRCB | 220 HVAC, 58 life support |

The two snapshots are about 173.8 game hours apart. These records warrant a
save/lifecycle investigation, with O-2P3 the largest single remaining load.
The comparison does not establish destruction time, original destination,
whether a particular repair was loaded then, or whether time skipping caused
the loss. Nothing was transferred or deleted during this survey.

## Evidence for later comparison

Detached raw API captures, complete paginated stock/recipe/actor data, route
summaries, pilot identities, nearby motion and historical comparisons are in
the ignored directory `obj/EJDR-Survey-20261118`. The deployed plugin MVID was
`11a15884-f885-4625-aaed-bc58b93fdbb9`; game assembly MVID was
`158470fc-8494-4bfe-b750-ad9d18dbf8d0`.

No delivery completion was observed during this snapshot survey. Compare these
IDs, payloads and backing market totals after a later time advance rather than
inferring success from a controller disappearing.
