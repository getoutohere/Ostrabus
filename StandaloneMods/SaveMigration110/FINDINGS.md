# What needs migration into 1.1.0.0

The later [dedicated content-availability audit](../../Build/CONTENT-AVAILABILITY-1.1.0.0.md)
supersedes broad availability assumptions in these earlier findings. It includes
plots, generated stakes, fixed residents and the player's named `patch` save.
In particular, current template versions do not imply that routine hauler pools
contain all 17 newly added mobile templates: those are regional-boneyard draws.

## Future ship and wreck generation

The player's concern is future generation, not retrofitting saved hulls. Native
`DataHandler` loads the current installation's `ships/` and `loot/` definitions.
`AIShip` selects a current loot entry and calls `StarSystem.SpawnShip`.
`StarSystem.AddDerelict` likewise selects from current loot and calls that method;
the deploying hauler still uses it. New deferred wrecks also materialize through
the same template spawn path. `SpawnShip` resolves `DataHandler.GetShip`, clones
that current definition and calls `_SpawnShip(bTemplate: true)`.

There is no save-creation-version gate in that generation pipeline that restricts
the continuation to 1.0 designs, equipment or loot. Newly generated hulls use the
current authored 1.1 templates and loot rules. No migration patch is needed to
make 1.1 ship/wreck content available to future generation in this continuation.

## Existing station layouts, kiosks and static NPCs

Future template generation alone does not establish that a saved station receives
new fixtures or authored residents. `StarSystem.Init` restores physical hulls
with `_SpawnShip(bTemplate: false, ...)`; `Ship.InitShip`/`SpawnItems` restore
saved items and crew rather than merging additions from the current blueprint.
An added kiosk or one-time resident in a changed station template would therefore
need a separate migration decision.

The direct 1.0.1.10 versus 1.1.0.0 comparison covers all 60 `NewGame` station
entries (56 distinct templates), plus environment-map entity templates and other
`Station*.json` files: 70 distinct templates in total.

- Of the 56 primary station templates, 55 are byte-for-byte identical, including
  San Diego Mall (`Station_VNCA_Mall`), COHO and the other populated stations.
  The remaining template is `Nav Buoy`: its optical/IR sensor was replaced with
  an EM sensor. It adds no kiosk or resident.
- The broader set additionally finds a changed `Station Tug` (new thrusters,
  loot/equipment changes) and the two new `Station_EVAC`/`Station_EVAC_BreakIn`
  templates. The four native Earth evacuation objects already exist in the
  inspected continuation. No existing populated station gains a kiosk in this
  asset comparison.
- Across all current `personspecs/*.json`, no new named station resident is
  defined. Seventeen existing specifications change only `strLoot`: sixteen
  generic pilot loadouts and Merga's racer loadout. `CivilianPilotAtlantis` is
  replaced by `CivilianPilotJATL`, a generic pilot specification rather than a
  new static resident. Existing station NPC spawner entries are unchanged with
  their station templates. Plot and plot-beat asset files are unchanged as well.

The read-only comparison and report are retained under ignored
`obj/Migration-1.1.0/StationDiff/` and `station-template-diff.json`. This establishes
the absence of these particular missing-content cases; it does not infer the
developers' motivation for their fresh-save recommendation.

## Correction: the two assignments do not enable content

The complete native field-use search shows that `BodyOrbit.EnvironmentMapID` is
read for initial `EnvironmentEntities.SpawnEnvironments` selection and the
separate Earth upgrade. Ordinary region entry instead materializes existing
stellar objects through `LoadEnvironmentForRegion`; it does not read these body
map IDs. `StarSystem.Init` skips `SpawnEnvironments` once either saved stellar
array exists. The continuation already has both arrays and its new content.

Consequently assigning Ganymede 801 and Venus 802 after loading changes saved
metadata without adding content or changing existing wreck handling. It does not
retag legacy hulls, change their orbits/layouts, enable equipment or alter AI
cleanup. No gameplay defect was demonstrated from these zero fields. The
optional plugin is unnecessary for this goal and is not recommended. Its tests
prove its narrow writes/preservation, not that those writes are useful.

The initial findings below correctly identify a schema difference but previously
misclassified it as migration work that needed doing.

Reviewed native assembly SHA-256:
`a79a5081d9c94307a45fe1d0b55ff1aa1d0af6ea8dfce1f9acc5961cf260ef07`.
The comparison uses preserved 1.0.1.10 assets/C#, current 1.1 assets/C#, the
existing continuation's 1.1 quicksave and a newly created `migration-reference`
save. Reads preserve each source ZIP's SHA-256. Large global CO tables are
streamed past, not loaded into memory or copied wholesale.

## Native migration already supplies most content

`StarSystem.Init` restores `aStellarShips`/`aStellarDerelicts` when either array
exists. When both are absent, it calls `EnvironmentEntities.SpawnEnvironments`.
That processes saved regional stations' current map conditions. The 1.1 maps add
the new regional derelict rings, using native loot, count, owner, placement and
orbit rules. Random rings have no population deduplication check: invoking this
again would replenish or duplicate content.

The saved continuation contains the same nine map-bearing regional stations as
the fresh reference: HQCH 100, EJDR 1000, MTRS 1002, MHNG 101, MSUZ 102, MVOL
1001, JFTS 1005, JPTN 103 and SVIR 1006. Blockade variants remain controlled by
their authored current station maps; the migration does not reset plot choices.

| New regional wreck map | Existing save deferred wrecks | Fresh reference deferred wrecks |
| --- | ---: | ---: |
| HQCH / Mercury, 100 | 12 | 12 |
| EJDR / Luna, 1000 | 13 | 10 |
| MTRS / Phobos, 1002 | 14 | 15 |
| MSUZ, 102 | 19 | 15 |
| JFTS / Titan, 1005 | 16 | 14 |
| JPTN / Europa, 103 | 24 | 24 |
| SVIR, 1006 | 15 | 18 |
| Earth, 800 | 53 | 47 |

Different counts are normal random generation/depletion, not migration deficits.
Physical and deferred objects must both be counted: all 52 fresh map-801 wrecks
are already physical near the starting region, so their absence from the deferred
array does not mean Ganymede failed to spawn.

`CrewSim.DoLoadGame` also has `1.0.1.13_Earth_Upgrade`. It refreshes Earth's
atmospheric data and, when missing, assigns body map 800 and invokes native
`SpawnBOEnvironment`. Both inspected saves have Earth map 800 and all four
`Earth-EVAC01` through `Earth-EVAC04` platform identities. The plugin does not
repeat that upgrade or respawn its content.

## The two remaining native body fields

1.0 new games generated Ganymede/Venus fields directly from
`NewGame.aSpawnDerelictRings`. In 1.1 those populations move into maps 801/802,
referenced by new body `nEnvironmentMapID` fields. Loading a saved body retains
its omitted zero rather than importing the current NewGame body definition.

| Body | Existing continuation map | Fresh reference map | Operation |
| --- | ---: | ---: | --- |
| Earth | 800 | 800 | Preserve native upgrade. |
| 1036 Ganymed | 0 | 801 | Assign 801; preserve legacy field. |
| Venus | 0 | 802 | Assign 802; preserve legacy field. |

The continuation has 41 physical derelicts locked to Ganymede and 12 locked to
VORB|Aux; these lack the new environment tags. Many have been visited. The fresh
reference instead has 49 map-801 Ganymede wrecks, three map-801 prospecting wrecks
and 12 deferred map-802 Venus wrecks. The migrator must not create these fresh
populations on top of the old ones, replenish removed prospecting wrecks, retag
visited hulls or move the VORB field to the new default distances.

This is a metadata completion, not the mechanism granting access to the new
regional wrecks: the native loader has already granted that access in this save.
The optional plugin's scope is deliberately limited to the demonstrated omission.

## Equipment, designs and work rates

`DataHandler` loads current item, overlay, interaction and loot definitions
independently of a save's creation version. New ship variants are present in
current regional ship-loot pools, e.g. `RandomDerelictHQCH` includes `LB-77b`
and `Light Tug 2`. New equipment appears through current native ship/loot/shop
paths. For example `ItmOKLGFixer` now offers RTGs. These are ordinary loot draws,
not a guarantee a particular shop currently has one.

The existing continuation already has new-design deferred wrecks: seven Light
Tug 2, five Li Bai Gen III, ten Bulk Lifter Mk IV, four Rouncy Mk II, three
Ocelot Mk II and five Baleen Mk II. This is direct saved-state evidence that
new design availability is not limited to the fresh reference.

`Trader.UpdateGPMs` retains a loot-table name; `Restock` resolves it through
`DataHandler.GetLoot` and generates the next stock from current definitions.
Existing physical inventory is not retroactively replaced. New work orders also
resolve current generated installable/repair/undamage interaction definitions.
Existing active interactions or legacy equipment variants are not reset by this
plugin. None of these availability mechanisms requires world regeneration.

No new named primary stations, bodies or asteroid fields were added to
`NewGame` in the reviewed asset diff. Newly authored environment identities are
the four Earth evacuation platforms, already present in the continuation.

## Evidence artifacts

`Tools/SaveContentAudit` reads both physical ZIP ship entries and deferred saved
objects. The native saved situation field is `boPORShip`; the runtime field is
`strBOPORShip`. The tool explicitly uses the saved name. Output includes source
hashes, body IDs, map populations and selected physical-hull metadata.

Private receipts under ignored `obj/Migration-1.1.0/` include `current-content.json`,
`fresh-content.json`, native saved-system slices and native regression output.
These are observations of two different worlds, not a same-seed A/B. Deterministic
planner/mutation/native-serialization controls separately establish the migration
effect. No game assembly, whole save or copied vanilla asset table is distributed.
