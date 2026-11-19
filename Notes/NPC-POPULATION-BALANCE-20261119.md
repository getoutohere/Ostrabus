# NPC population balance and administrative removals

November 19, 2026. Read-only gameplay investigation against Ostranauts 1.1.0.3,
native assembly MVID `5b1ee244-539d-4902-8d62-97490deee9d7`.
The initial investigation was read-only. Following user approval, the shared
source-list repair and [retirement handoff policy](../Navigation/CREW-RETIREMENT.md)
were implemented. Recruitment assets and native population pruning remain
unchanged. Offline probes and save-inspection tooling were extended.

## Observed flow

The retained native Player.log contains one `CrewSim.objInstance.LoadGame` call,
62 `#NPC# Reusing NPC` messages and eight `#NPC# Creating new NPC` messages.
These are recruiter branch attempts, not a census of all world births or proof
that every attempt successfully allocated an actor. All eight fresh-creation
messages report `bNotEnough: False`: their selected source was not classified as
underpopulated, but the search did not produce a usable volunteer.

The same log records a population transport of four people from EJDR_GEO to VLA01
and removal of seven people at MTRS. Obituaries independently captured the seven
living MTRS removals through station population management. Reuse predominates in
this session; the evidence does not establish runaway global population growth.
No complete before/after world census was collected.

The first three captured living-pilot deaths after the six-hour SFF were separate
retired-hull cleanup events: O-DWL / Addison Jocelyn Calhoun, O-ZLHA / Miracle
Strong, and O-5ZH / Ivan Singh. Native `StarSystem.Update` attempted crew recycling
but destroyed each retired hull even when recycling failed. Matching Earth
stations were full or over capacity. These were not recorded planetary crashes.
The detached first-event evidence is [OBITUARY-FIRST-LIVE-20261119.json](OBITUARY-FIRST-LIVE-20261119.json).

Other observed person-materialization deaths ran through derelict loot generation
and a fatal `DcPoison05` effect. Those are authored victim-generation effects,
not evidence that an existing traffic pilot died during navigation. Recorder
events require lifecycle and call-stack interpretation.

## Creation and recruitment

`Ostranauts.Ships.AIPilots.AIShip.AddCrew(string, Ship, string, bool)` first searches
eligible existing people. It excludes serving pilots/player roster members and
uses the selected `JsonPersonSpec` and native condition trigger. If no candidate
is found, or the selected source has too few residents, it creates a new person.
There is no maximum-station-population admission check in this fresh branch.

`AIShip.NotEnoughNPCsOnStation` protects a minimum resident population: an unresolved
station is unavailable, `_UNK` is exempt, and a station with mass above 10,000 and
at most three residents is underpopulated. It does not mean the station has room
for another arrival.

`AIShipManager.SpawnFactionShips` fills asset-defined civilian/navy fleet quotas,
not station resident deficits. It subtracts registered active ships, schedules
another successful dispatch six game seconds later, and otherwise waits roughly
200-400 seconds. Controller removal can free a fleet slot for a replacement.

### Authored recruitment restrictions

`data/personspecs/personspecs.json` defines `CivilianPilotEJDR` with homeworld
`EJDR`, current career `Shipbreaker`, and trigger `TIsTrafficAIScav`. The same
homeworld/career restriction exists in 1.0.1.10. The generic `CivilianPilot`
profile does not require a particular homeworld.

The `burn` save contains several Earth-station shipbreakers from other regions.
Jay Farmer, Adriana Nash, Madelyn Mayo, Devine Rojas and Elias Ryan pass the
native traffic trigger in the offline probe but fail the Earth pilot profile.
Isabella Sheba Salazar, then at EJDR_GEO, does too. Addison is an Earth-born
shipbreaker who matches the profile, but is already a serving pilot and therefore
should not be recruited away from his ship.

The actual native matcher was executed with saved chargen identity and condition
presence. Changing only the test Jay's homeworld to EJDR makes him eligible;
adding `IsNotTrafficAI` rejects him again. These controls establish the rejection
mechanism, not that regional homeworld policy should be removed. Broadening this
asset would be a behavior-policy change with faction/recruitment consequences.

### Confirmed C# defect: filtering mutates the shared source registry

`AIShipManager.GetAllATCRecycleStations` returns the stored regional
`dictPilotSrcRegIDs` list directly for a matching regional key. `AIShip.AddCrew`
then adds `OKLG_UNK` and removes temporarily underpopulated source stations from
that very list. Later recruitment therefore sees changed shared configuration,
even if a filtered station subsequently recovers its population.

Both native versions contain this mechanism. Two offline checks establish that
the accessor aliases the registry and that executing native AddCrew actually
removes an underpopulated source from it. This is a definitive native defect,
but the audit did not inspect the current live registry and cannot attribute a
specific live creation attempt to a previously removed source.

The narrow proposed repair is to give AddCrew a local copy before adding/filtering
sources. Preserve the existing source assets, minimum-resident protection,
candidate matching, fallback and fresh-person identity repair. Do not globally
change the accessor contract without auditing its other callers. The approved
repair is now implemented as independent bugfix `ai-crew-recruitment`, with native
before/after execution proving source recovery and volunteer reuse.

## Population limits and cleanup

`CrewSim.DockAndDespawn` admits acceptable returning crew without testing
`MaxPopulation`; its native drop-off trigger/faction rules can instead reject and
destroy an actor. Some native transit admission tests also use `Population <=
MaxPopulation`, permitting an arrival when a station is already exactly full.
Consequently capacity is a soft population-management target, not a hard docking
limit. Station ownership alone does not establish matching faction membership
or nonzero population capacity for crew recycling.

`AIShipManager.SpawnPassShip` runs every 24-36 game hours and selects one most
overpopulated shallow station. If more than four suitable destination stations
have spare capacity, it attempts to transport up to four eligible residents.
Otherwise it removes prunable residents until the selected station is below half
capacity or has no more eligible candidates. Initialization only schedules the
first run. Native `TIsNPCPrunable` in `data/condtrigs/condtrigs.json` protects player,
player crew, social-combat and `IsNotTrafficAI` actors.

Separate stale-hull cleanup checks a three-game-hour retirement age during a
sweep paced every 120 real-time seconds. A fast-forward can advance retirement
ages before that next sweep, so administrative deaths shortly after SFF do not
by themselves prove a physics error during the skip.

Blocking all such removals would remove an actual population-control mechanism.
Preserving everyone without accounting for retirement, reuse and replacement
would be an unsupported policy change.

## Effects of existing Ostrabus patches

- The [crew identity repair](../MiscBugFixes/NPC-IDENTITY-COLLISION-AUDIT.md) changes
  the fresh-construction path after recruitment fails, not the recruitment
  decision. It can increase actual actor count relative to broken vanilla:
  instead of hijacking an existing identity on a random name collision, it creates
  the genuinely distinct person the native branch requested.
- SFF subdivision gives native `StarSystem.Update` and therefore native faction
  spawning more opportunities to execute than one large jump. Native fleet
  quotas/deadlines still apply; this is not proof of identical creation counts
  to vanilla. Role-callback admission limits do not gate these manager updates.
- Active-ship stale-timer repair prevents inherited retirement from killing a
  successfully reactivated worker. It does not suppress later deliberate native
  retirement. Current bounty-target preservation also extends some actor/hull
  lifetimes; it must not be treated as having zero population effect.

No full net-population attribution to these patches has been established. The
measured reuse count, native quota mechanism and observed culling do not support
a claim that the current session is simply manufacturing new pilots indefinitely.

## Verification and retained tools

The [identity harness](../Tests/NpcIdentityCheck/README.md) passed eight native
baseline checks and 133 repair/recruitment checks on 1.1.0.3, including actual
native AddCrew shared-list mutation and repaired recovery. Its separate saved-population mode passed
four native matcher/trigger/source-alias controls. Unity scene allocation and
services are explicit fixtures; neither run is a complete live-world simulation.

The [save study](../Tools/FreightLifecycleStudy/README.md) now supports
`root --station-lifecycle` to extract saved station factions, ship conditions and
crew counts alongside motion without loading complete physical inventories.
Saved condition records are not automatically expanded through DEFAULT assets;
runtime population/capacity conclusions require native definitions or live data.

Ignored evidence is under `obj/ObituaryLive-20261119/`: native recruitment output,
identity/source-list reproduction, selected save extracts, and filtered native
personnel-flow log. The durable findings above do not depend on retaining large
release artifacts or modifying a save.
