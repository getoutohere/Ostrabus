# Native content availability: 1.0.1.10 continuation in 1.1.0.0

## Verdict

The dedicated content audit finds no demonstrated save-origin restriction that
requires a separate content-unlock migration for the inspected continuation.
Native loading already supplies the new regional boneyards and Earth evacuation
platforms. Current restocking and NPC creation supply the new equipment. No new
fixed station kiosk, named station resident, plot, plot beat or dialogue branch
was found that requires starting over.

This does **not** mean every new ship design is guaranteed to exist in every
save, or that routine derelict haulers draw every new design. The distinction
between current template versions and eligible design pools is material:

- The 17 added mobile ship templates are selected by regional boneyard tables.
  They are absent from the ordinary `RandomShip` hauler pool and the Venus pool.
- Fifteen of those 17 have already been selected in the inspected `patch` save.
  Two were not selected by its finite random populations. A fresh reference also
  lacks one of those two. Neither a normal region revisit nor a routine hauler
  delivery guarantees a new roll for them.
- Later authored story outcomes can generate another eligible regional
  population. This is conditional on reaching the corresponding outcome; it is
  not an always-running replenishment mechanism.

The developers' motive for their blanket fresh-save statement is not established
by this audit. Do not turn that uncertainty into a missing-content claim, a
guarantee of every randomized design, or a reason to regenerate the player's
world. The previously proposed body-map assignments do not resolve any identified
content-access defect in this already-upgraded save.

## Inputs and scope

Compared the preserved 1.0.1.10 installation and decompiled C# against 1.1.0.0.
Current native assembly SHA-256:
`a79a5081d9c94307a45fe1d0b55ff1aa1d0af6ea8dfce1f9acc5961cf260ef07`.
This is a content-availability audit, separate from Ostrabus hook compatibility.

The JSON inventory covers 504 old and 532 current distributed asset files,
including the generated `tsv/output/stakes` assets that `DataHandler.LoadMod`
actually loads. Unused TSV source/output elsewhere and `archived_content` are
excluded. Of the union, 387 files are byte-identical and 145 are added, removed
or changed; a raw file change does not necessarily change a named definition.
All selected files parsed successfully. Reports retain file hashes and named
definition changes, rather than inferring new content from filenames alone.

The player's named `patch` save was read without executing game code or writing
the archive. It is a 1.0-origin continuation saved in 1.1 with normal Ostrabus,
without the optional migration prototype. Its archived origin version remains
1.0.1.7; this is not the version of the executable that last wrote it.

| Input | Physical hulls | Deferred environment objects | Archive SHA-256 |
| --- | ---: | ---: | --- |
| `patch` | 307 | 254 | `95635C61DCEB0BFBADCB244C5FB515D3AA4694DC07BA2AD08DF3AAF90F072849` |
| `migration-reference` | 129 | 250 | `F32419B6DC1BF8660DBAA8865B39E7C79831602C7BEF0A5470D307589669B118` |

The reference was created fresh in 1.1. These are different random worlds, not
a same-seed experiment. Presence is direct evidence; absence alone does not prove
unreachability. Their original archives passed before/after hash checks.

## Initialization and saved-state boundaries

`DataHandler.LoadMod` loads current ships, loot, items, overlays, person
specifications, plots, beats, interactions, jobs and generated stake assets.
Definition loading has no save-origin filter selecting a 1.0 content catalogue.
However, knowing a definition exists is insufficient to prove a source reaches it.

The important consumers and persistence boundaries are:

| Mechanism | Actual behavior | Availability consequence |
| --- | --- | --- |
| `StarSystem.SpawnShip(string, string, ...)` | Resolves `DataHandler.GetShip`, clones the current template and calls `_SpawnShip(bTemplate: true)` for a new identity. | Newly created hulls use current layouts and spawn rules, subject to the caller's design pool. |
| `StarSystem.Init` | Restores physical saved hulls with `bTemplate: false`. Restores saved stellar arrays, or calls `EnvironmentEntities.SpawnEnvironments` when both arrays are absent. | Existing objects are not generally merged with changed templates; an old save can nevertheless receive new environment populations automatically. |
| 1.0 `JsonStarSystemSave` | Has no `aStellarShips` or `aStellarDerelicts` members. | An ordinary 1.0 save enters the new loader's environment initialization fallback. This is a concrete native bridge, not a custom migration. |
| `EnvironmentEntities.AddDerelictRing` | Chooses a concrete template from the current regional table while creating each deferred wreck. | A saved deferred wreck already has its design selected; visiting it does not reroll that design. |
| `EnvironmentEntities.SpawnShipFromStellarObject` | Materializes a deferred entry using current template data, then assigns that entry's environment tags. | Deferred objects retain their chosen identity/design but receive current template content on materialization. |
| `Trader.Restock` / `AddNewItems` | Retains an inventory-table identifier and resolves current `DataHandler.GetLoot` contents when generating stock. | Existing kiosks can acquire new stock without adding another kiosk or replacing the station. |
| `PlotManager.Init` / `CheckPlots` / `CheckPlot` | Restores plot phases, tokens and history; checks current `DataHandler.dictPlots` and current beats. | The saved game does not freeze a separate 1.0 plot catalogue. Completed story history remains completed. |

## World and station content

| Content | Evidence in `patch` / native source | Classification |
| --- | --- | --- |
| Mercury/HQCH boneyard | Map 100 has 12 deferred wrecks. | Supplied by native environment initialization. |
| Luna/EJDR boneyard | Map 1000 has 19 deferred wrecks. | Supplied by native environment initialization. |
| Phobos/MTRS boneyard | Map 1002 has 12 deferred wrecks. | Supplied by native environment initialization. |
| MSUZ boneyard | Map 102 has 14 deferred wrecks. | Supplied by native environment initialization. |
| Titan/JFTS boneyard | Map 1005 has 15 deferred wrecks. | Supplied by native environment initialization. |
| Europa/JPTN boneyard | Map 103 has 27 deferred wrecks. | Supplied by native environment initialization. |
| SVIR boneyard | Map 1006 has 13 deferred wrecks. | Supplied by native environment initialization. |
| Earth field and evacuation platforms | Map 800 has 59 deferred wrecks plus `Earth-EVAC01` through `Earth-EVAC04`. `CrewSim.DoLoadGame` contains the explicit `1.0.1.13_Earth_Upgrade`. | Supplied by native migration; no repeat spawn required. |
| Existing populated stations and their kiosks | All 60 `NewGame` station entries compared: 55 of 56 distinct templates byte-identical. The exception is `Nav Buoy`, with a sensor replacement. No kiosk addition. | No missing new fixture demonstrated. |
| Other station/environment templates | Broader comparison covers 70 templates. Only the buoy, `Station Tug`, and two new EVAC templates differ or are new. | Tug changes concern ship equipment/loot; new EVAC objects already present. |
| Named static station residents | Station spawn entries unchanged; all person-spec files compared. No new named resident specification. | No missing new resident demonstrated. |
| Ganymede 801 / Venus 802 body-map fields | Those fields remain zero in the continuation, with legacy physical fields retained. The new body IDs are inputs to initial generation, not ongoing content unlocks. | Assigning them after initialization has no demonstrated gameplay benefit. |

`Station_EVAC_BreakIn` is the actual map-800 platform template.
`Station_EVAC` is an additional authored blueprint, not a separately promised
fifth location. `_1.1 Showcase` and `_Placeholder` are development/support
templates, not missing ordinary world destinations.

A final direct comparison also confirms that **the OKLG and VORB station objects
themselves have explicit environment-map values of zero in both `patch` and the
fresh 1.1 reference**, with `IsEnvironmentEntity` false. They are not missing the
station map assignments used by the Ceres transitions. The fresh-only 801/802
assignments are on the celestial bodies Ganymede/Venus, not on these stations.
This does not justify assigning those body-map numbers to OKLG/VORB either.

## Added mobile ship templates

The asset comparison finds 21 added ship-template identifiers: the 17 mobile
templates below, two EVAC templates and two development/support templates.
This inventory does not depend on interpreting the announcement's terminology
for "designs" versus "variants". All 17 have positive-probability entries in
current regional tables; their native derelict/broker override probabilities
are zero. No ordinary `RandomShip` or `RandomDerelictVenus` ancestry reaches them.

| Added template | Example eligible regional pool | Deferred count in `patch` |
| --- | --- | ---: |
| Baleen Mk II | Earth, Titan, Europa, SVIR | 4 |
| Bulk Lifter Mk IV | Earth, Luna, Phobos, MSUZ, Titan, SVIR | 8 |
| Heavy Tug 03 | Earth, Luna, Phobos, MSUZ, Titan, SVIR | 5 |
| LB-77b | Mercury, Luna | 1 |
| Li Bai Gen III | Earth, Luna, Phobos | 5 |
| Light Tug 2 | Earth, Mercury, Luna, Phobos, MSUZ, Titan, SVIR | 6 |
| Ocelot Mk II | Earth, Luna, Europa | 5 |
| Rouncy Mk II | Earth, Luna, Titan, Europa, SVIR | 2 |
| SalvagePodEndurance 2 | Luna, Phobos, Titan, SVIR | 0 |
| Sat_Miura | Earth | 3 |
| ShuttleRed | Luna, Phobos, MSUZ | 0 |
| TankerHauler N2 | Mercury, Luna, Phobos, MSUZ, Titan, SVIR | 1 |
| TankerHauler N2 Indy | Earth, Luna, Phobos, MSUZ, Titan, SVIR | 1 |
| TankerPod N2 Mk II | Luna, Phobos, MSUZ, Titan, SVIR | 5 |
| Tombolo 3 | Earth, Luna | 5 |
| UtilityPod Mk II | Earth, Luna, Phobos, MSUZ, Titan, SVIR | 4 |
| Volatile Blue | Earth | 2 |

There are no physical instances of those 17 in this snapshot; their positive
counts are deferred objects waiting to materialize. The fresh reference contains
two `SalvagePodEndurance 2` wrecks and no `ShuttleRed` either. This is a population
roll distinction, not evidence that a fresh game guarantees all designs.

### Phobos visit follow-up (2026-11-16)

Read-only inspection of `autosave_4_adventure_1` after the player's visit finds
all 12 previously recorded Phobos wrecks materialized. Ten selected older
templates; O-6CZK and J-WUX selected the newly added `TankerPod N2 Mk II`.
This variant and the old `TankerPod N2` both have a 15-by-24 template grid,
167 floor pieces and 50 walls. Their similar appearance does not establish
that the old template was selected.

J-WUX is unvisited (`fFirstVisit = 0`). Its actual saved physical item array
contains four `ItmRCSCluster03` (Ryokka CL-03) and one `ItmCanisterN2Big`.
Live read-only inspection also confirms four stored thrusters with weighted
RCS count seven. This establishes actual new equipment in this continuation,
in addition to earlier deferred-template evidence.

O-6CZK has been visited, and its saved items no longer contain those five
components. Native `Ship.InitShip` calls `Ship.BreakIn` on first full/edit
initialization of a prefilled derelict; that procedure randomly deletes parts
and subsequently damages survivors. `TIsDerelictSafe` includes ordinary
thrusters and tanks, and no `TIsDerelictForbidMTRS` definition was found in the
distributed trigger table. These mechanisms can explain the absence, but
the inspection does not prove the historical removal caller for each item.
Preboarding stock is not a guarantee that every part survives first boarding.

Private report: `obj/Migration-1.1.0/phobos-visited-save.json`; source archive
SHA-256 `975A94BC4492E5CD2991AF3F3A55557AF471CE571E46CBE005FD51308C3E1BCC`,
unchanged before/after reading. No ship was loaded, regenerated or modified
by this inspection.

Native `HaulerDeployingPilot.SpawnDerelict` calls `AddDerelict("RandomShip", ...)`.
Ostrabus can substitute the current Venus-specific pool for a configured share of
VORB deliveries. Both paths use current template definitions, but neither pool
contains these 17 additions. This corrects any earlier implication that ongoing
hauler deliveries would naturally sample every newly added design.

All normal callers of environment generation were reviewed. Besides initial
loading and the Earth upgrade, `Endgame` uses `ChangeEnvironment` for authored
regional outcomes. Relevant changed maps include 10020/1020 (Phobos/MSUZ) and
1007/1008 (SVIR/Titan); these contain eligible pools for the two unsampled
templates. The player's Ceres plot is active rather than recorded completed.
Further rolls therefore have a future authored route conditional on its outcome.
Do not invoke those outcomes or regenerate their fields merely to fill a random
design checklist. Ordinary region entry only loads existing environment entries.

## Ceres-related environment transitions: observable conditions in space

This section records the two faction configurations as observable changes to
stations and surrounding objects. It does not recount scenes, dialogue or the
events that select a configuration. It describes native code and authored maps;
it is not a claim that both transitions were observed in live gameplay.

### Xinhua configuration

| Location | Observable change |
| --- | --- |
| JPTN / Europa | The three GalCon blockade signal beacons are absent from the new environment. The ordinary buoy/checkpoint infrastructure remains. |
| SVIR | A fuel depot and one navigation buoy are assigned to Xinhua. A Xinhua blockade signal beacon is added. The region's target for new routine civilian traffic is reduced to zero. The replacement map requests 10-20 additional wrecks from the SVIR pool. |
| JFTS / Titan | A fuel depot and freight dock are assigned to Xinhua. A Xinhua blockade signal beacon is added. The region's target for new routine civilian traffic is reduced to zero and naval traffic remains enabled. The replacement map requests 10-20 additional wrecks from the JFTS pool. |

From a vessel passing through these regions, the difference is a changed set of
contacts and facility affiliations: blockade beacons disappear near JPTN and
appear near SVIR/JFTS, and some facilities identify with a different faction.
The blockade entities carry native naval-spawning conditions; the actual number
and timing of naval contacts still depend on normal AI population rules.

### Titan configuration

| Location | Observable change |
| --- | --- |
| MTRS / Phobos | The station uses its damaged replacement layout. A security checkpoint and a navigation buoy are assigned to Titan and configured as blockade-spawning entities. The replacement map requests 10-20 additional wrecks from the Phobos pool. |
| MVOL | Two navigation buoys are assigned to Titan and configured as blockade-spawning entities. No additional wreck field is specified by this replacement map. |
| MHNG | A navigation buoy and maintenance dock are assigned to Titan and configured as blockade-spawning entities. No additional wreck field is specified by this replacement map. |
| MSUZ | A navigation buoy and freight dock are assigned to Titan. The station's naval population target is enabled. The replacement map requests 10-20 additional wrecks from the MSUZ pool. See the unresolved condition reference below. |

From a passing vessel, the difference is Titan affiliation on specific local
facilities, associated military traffic, a visibly different MTRS station, and
additional salvage contacts around Phobos/MSUZ.

The broader native configuration also changes ownership/layouts of selected
Ceres stations. Those station replacements are separate calls in `Endgame`,
not work performed by `ChangeEnvironment`. They already existed in the prior
ending implementation; the material 1.1 change reviewed here is the actual
execution of the surrounding-environment transitions.

### Timing and retained objects

The transition does not destroy and replace every nearby ship in front of the
observer. New infrastructure can initially be a lightweight environment record;
existing physical infrastructure is flagged for later replacement/removal when
the native environment unload process permits it. Materialized wrecks are
retained and retagged, and deferred wreck records are retained as well. New
wreck populations are additional draws, not a wipe of all old salvage.

These are consequences of applying the corresponding configuration, not a
periodic world refresh. Calling the same transition again can add another wreck
population. Existing unrelated traffic is not selected merely because it is
nearby; the transition matches old environment tags.

In 1.0, the relevant ending code only assigned the destination map IDs. In 1.1,
it calls `SolarSystem.EnvironmentEntities.ChangeEnvironment`, which performs
the object removal, retagging, creation and later-replacement setup described
above. The affected station IDs are independent of the unused Venus/Ganymede
body-map assignments considered by the abandoned migration prototype.

### Unresolved authored reference at MSUZ

Map 1020 references `CONDMSUZBlockadeSpawner` for its two Titan-affiliated
entities. An exact search finds those references but no definition in the current
distributed JSON. Therefore this audit does not claim those two objects actually
receive the intended extra blockade-spawning conditions. Their assigned faction,
the station's separate naval target, and the new wreck-ring definition are
independently present. The effect of the missing condition table is a separate
native asset issue to investigate, not evidence of a fresh-save requirement.

Sources: `Endgame.XinhuaCeresVictory`, `Endgame.TitanCeresVictory`, environment
maps 103/104, 1006/1007, 1005/1008, 1002/10020, 1001/10010, 101/1010 and
102/1020; `EnvironmentEntities.ChangeEnvironment`, `SpawnATCEnvironment`,
`UnloadEnvironmentForRegion` and `ConvertShipToStellarObject`.

## Added equipment and recurring sources

These are sources in current authored tables, not promises about current shop
stock. The existing kiosk/station templates are unchanged, and native restocking
resolves the current tables using their existing identifiers.

| Addition | Concrete current source | Availability classification |
| --- | --- | --- |
| Large N2 tank | `ItmOKLGFurnishingsKioskInv`, plus regional furnishings and supply inventories. | Recurring shop source. |
| RC-5 RTG | `ItmOKLGFixer`, damaged stock in `ItmOKLGSupplyKioskInv`, and MVOL/SVIR/VCBR supply inventories. | Recurring shop source. |
| Havlorson Washburne RCS | `ItmTraderSanDiegoHalvorsonInv`, plus MTRS/MHNG/MSUZ/MVOL/SVIR furnishings inventories. | Recurring shop source. |
| Ryokka CL-03 RCS | BCER/BCRS/EJDR/JFTS/JPTN and other regional furnishings inventories. | Recurring shop source. |
| Miura Chimera RCS | `ItmTraderSanDiegoMiuraInv`; also installed in current ship templates such as `Station Tug`, `Tombolo 3` and `Volatile Blue`. | Recurring shop source and current wreck equipment. |
| Bingham-12 EVA | `ItmTraderSanDiegoBinghamInv`, supply inventories and current NPC loadouts. | Recurring shop/NPC source. |
| CDA EVA | Regional supply inventories through `ItmRandomSpacesuitKioskCiv...` tables; current faction NPC loadouts. | Recurring shop/NPC source. |
| Respirator suit (`OutfitPS03`) | Supply inventories, fixer stock and current NPC loadouts. | Recurring shop/NPC source. |
| Guanghan respirator suit (`OutfitPS05`) | Current CCRE/Xinhua combat-pilot and several civilian/pirate loadout tables. | New NPC equipment source; no ordinary shop source established by the table trace. |

The Guanghan source is not a claim that an old resident changes clothing on load.
`PersonSpec` creation resolves its current starting-loot table for a new person.
That is sufficient to establish a future source independent of save origin.

The new pressure-suit oxygen behavior, work rates, long-range RCS plotting and
native precision slider input are current code/asset mechanisms, not content
unlocked by generating another star system. Legacy suit aliases and already
active order snapshots are not retroactively replaced by this audit.

## Narrative, plots, encounters and jobs

The comparison includes narrative content rather than assuming that all content
is physical loot. The following asset groups are byte-identical between versions:

| Group | Files | Named entries |
| --- | ---: | ---: |
| Plots | 2 | 46 |
| Plot beats | 2 | 291 |
| Plot-beat overrides | 2 | 52 |
| Plot-manager settings | 1 | 1 |
| Racing leagues/tracks | 2 | 13 |
| Transit definitions | 1 | 56 |
| Headlines | 1 | 43 |
| Info nodes | 1 | 68 |
| Bounty definitions | 1 | 4 |
| Job-item definitions | 1 | 12 |

`PlotManager.cs` and `JsonPlotSave.cs` are also byte-identical in the two
decompiled projects. `CheckPlots` enumerates current plot definitions;
`CheckPlot` uses the saved phase but reads current beats. There is no added
story-definition catalogue in 1.1 that an existing save fails to enroll in.

There are changes inside existing narrative and encounter mechanisms:

- Two AV Club dialogue actions change where `CONDPLOT_AVClub_Start` is applied.
  These are corrected effects of existing actions, not a new plot/start trigger.
- Two existing Ceres codeword consequences change condition targets. The
  Ceres-associated `PlotCeresGoldStack` definition changes from trigger-type to
  item-type loot and specifies six units of `ItmMineral09` (Gold Ore). A later
  direct caller search found its name only at its definition, with no reference
  in active assets or decompiled C#. Its use by an active quest is not
  established; do not describe this as a verified quest reward repair.
- Merga's creation specification changes only the starting-equipment table.
  The character and story already exist; this does not add a new resident.
- Seven relationship actions correct a continuation identifier. No new dialogue
  branches are added in those changes.
- All 35 generated stake files are compared, including stranger introductions,
  medical-heist conversations and the four surrender negotiations. No named
  definitions are added or removed there. Changes add explicit false/zero schema
  defaults and revise some existing surrender condition effects; they do not
  introduce a new story gated by new-game initialization.
- Twelve short-hop courier job definitions change payout ranges, not eligibility
  or story structure. `GigManager.MakeJob` reads current `DataHandler.GetJob` for
  new offers. Existing saved offers retain their already-generated terms.
- Two PAX 2020 chargen life events and their associated old interactions are
  removed from active data/moved into archives. They are not new inaccessible
  content; restarting does not restore them to the active catalogue.
- `Endgame` now calls `EnvironmentEntities.ChangeEnvironment` instead of simply
  assigning station map conditions. Future story-driven environment changes
  execute the new native mechanism; this does not require resetting plot history.

Completed actions are not replayed to award corrected rewards a second time.
Nothing in this audit warrants changing the player's plot phases, resurrecting
NPCs, resetting completion history, or forcing an outcome.

## Evidence, limitations and disposition

Read-only asset and loot-reference reports are retained under ignored
`obj/Migration-1.1.0/`:

- `content-asset-diff.json`: file hashes and named asset changes, including
  generated stakes.
- `content-loot-paths.json`: all 21 added template identifiers and nine equipment
  groups traced through loot references. Reference topology alone is not treated
  as proof of an active source: relevant callers and positive-probability table
  branches were reviewed separately.
- `station-template-diff.json`: station/environment template comparisons and all
  person-specification changes.
- `patch-content.json` and `fresh-content.json`: physical/deferred inventories.
  The updated save reader also retains active/historical plot state; private
  plot prose and actor tokens are not copied into this document.

Native evidence is concentrated in `DataHandler.LoadMod`, `StarSystem.Init`,
`StarSystem.SpawnShip`, `StarSystem.AddDerelict`, `Ship.InitShip`,
`Ship.GetLootRegion`, `LootSpawner.ShipMatch`, `Trader.Restock`, `PersonSpec`,
`PlotManager`, `GigManager.MakeJob`, `CrewSim.DoLoadGame`, `Endgame` and
`SolarSystem.EnvironmentEntities`. No live game mutation, Unity scene execution,
new DLL deployment or migration command was used for this audit. The normal
Ostrabus DLL remains SHA-256
`c6550e128ce67bca2fed6fa7c0dd62363efb72f1e775d7c4c3eb176b2a8c189d`.

This establishes availability mechanisms and the inspected saved populations.
It does not promise random drops, guarantee every existing quest remains
completable after arbitrary player actions, or prove the developers' intention.
It also does not audit future game builds or arbitrary third-party overrides.
No concrete content-access repair is proposed. Keep the optional
`SaveMigration110` body-field prototype out of the installation.
