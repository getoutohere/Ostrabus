# Field frames, wreck motion and legacy displacement

Reviewed November 19 against the player's bounty save, Ostranauts 1.1.0.3
native code and installed JSON. At the initial review the installed standalone
was 0.1.3. The later tested 0.1.4 reads saved offset centres; its subsequent
deployment and load verification are recorded below. The
rejected proposal to derive a yard anchor from wrecks' current parents is absent.

## Applied save repair

The installed bounty archive contains:

- EJDR: 19 wreck IDs, with 47.6939 hours of reconstructed physical absence.
- MTRS: ten in-bounds candidate IDs with the requested eight turnover days
  (192 hours). O-8MCY is outside the inner bound and was retained.
- No HQCH age record. The requested five days have not been applied to Mercury.

The production reader accepted both records. Existing Luna state was preserved
when Phobos was added. All 355 native archive payloads have identical SHA-256
hashes; only the optional turnover entry changed. Each application retained an
original archive backup. Current game installation and save identities were
checked before application.

## Actual saved populations

These 0.1.3 baseline counts are production **spatial admission** checks against the full saved
body hierarchy, not promises that every matching hull passes ownership,
occupancy, target, plot and bounty protections at a later return.

| Region / map | Resolved field reference | Wrecks | Pass spatial checks | Finding |
| --- | --- | ---: | ---: | --- |
| HQCH / 100 | HQCH_DET | 12 | 0 | All twelve now orbit Mercury rather than retaining their original field attachment. |
| EJDR / 1000 | EJDR_GEO | 19 | 19 | Saved and calculated centres agree; the repaired spawner checks are relevant here. |
| MTRS / 1002 | Phobos | 11 | 10 | O-8MCY is about 0.856 km inside the inner boundary. Two members have nonzero orbital rates; orbital motion alone does not exclude them. |
| JFTS / 1005 | Ganymede | 15 | 0 | Current ground-station bearing produces a centre 2,070 km from the saved field centre. |
| SVIR / 1006 | SVIR_Shipyard | 13 | 13 | Manufactured reference; do not replace it with Titan. |
| MSUZ / 102 | MSUZ_RB | 14 | 14 | Existing reference retained. |
| JPTN / 103 | JPTN | 27 | 27 | Existing reference retained. |

JFTS is at Ganymede; SVIR is at Titan. Earlier turnover documentation incorrectly
called JFTS the Titan field. The saved hierarchy resolves those identities.

The private evidence is under ignored `obj/TurnoverRepair-20261119/`:
`bounty-content.json`, `bounty-world.json`, `all-field-study.json`, its log and
the applied repair receipts. The snapshots preceded the Phobos metadata addition;
their native payloads are identical to the installed repaired save.

## Mercury is a displaced legacy cohort

An older player-save content audit, `obj/Migration-1.1.0/patch-content.json`,
contains the same twelve IDs. Every one was attached to HQCH_DET. Every one is
now attached to Mercury, with a nonzero orbital rate at roughly 246,000 km from
the planet centre. A separate fresh 1.1 reference also has twelve fixed
HQCH_DET-attached wrecks.

This establishes the attachment change; it does not identify the individual
call that changed every hull. This is not a newly discovered failure: the
[earlier undocking audit](../../Navigation/DOCKED-ORBIT-LOCK.md#undocking-reference-overwrite-november-16-follow-up)
already records E-XMH changing from HQCH_DET while docked to O-S04 in `arrive`
to Mercury in `pathcheck`, and identifies V-6BM, J-ZEK and H-QS10 with Mercury
references. The initial passive-docking fix missed undocking because its
fixture supplied the same original and candidate reference. The subsequent
repair deliberately tests distinct references and shipped in Ostrabus 0.7.0
(commit a148ec2). Native Ship.Undock can call
ShipSitu.TryRelockToBO with the nearest non-placeholder body. The known passive
attachment failure can convert a station-attached wreck to a circular planet
orbit while preserving its immediate position. The existing Ostrabus passive
attachment repair prevents that transition when the original lock survives.
It does not reconstruct already changed ships; that exclusion was explicitly
documented in the earlier audit. The earlier audit establishes historical
damage for the named hulls, but does not date all twelve transitions or prove
that no other path can still cause attachment loss.

The original HQCH_DET field lookup is therefore appropriate. An attempted
change to select Mercury from the wrecks' current parents was rejected: it
would test a 2,840-3,140 km planet-centred annulus against wrecks roughly 246,000
km away. A wreck's present parent is not sufficient evidence of the yard's
original frame. That proposed runtime change was removed and never installed.

Only two of the twelve are currently inside the original local annulus by
position alone. Automatically expanding the yard, ignoring bounds or adopting
all displaced hulls would change the proposal's treatment of moved-away wrecks.
It must not be smuggled into an ordinary metadata repair.

### Original placements recovered from patch

Following the player's suggestion, the actual `patch/patch.zip` was inspected
read-only. Its SHA-256 matches the earlier content audit
(`95635C61DCEB0BFBADCB244C5FB515D3AA4694DC07BA2AD08DF3AAF90F072849`).
All twelve matching IDs are deferred `aStellarDerelicts` entries, each with
`strParent=HQCH_DET`, original `vBOOffsetx`/`vBOOffsety` and `fOrbitW=0`.
Their original radii are 401.0036-653.6600 km; all twelve pass the existing
401-701 km field bounds. No randomized placement or inferred original offset
is necessary for this cohort. Exact records and provenance are retained in
the private `mercury-original-placements.json` report beside the field study.

This establishes original navigation state only. A repair must retain current
ship contents and actor/claim protections, reconstruct coherent current-epoch
position/velocity from the restored attachment, and avoid overwriting intentional
player relocation. No ship or installed save was changed by this extraction.

## Ganymede has a separate field-centre defect

Map 1005 places JFTS-FD01 at exactly the same authored distance and angle as
the wreck ring: 750 km at -5 degrees from the ground-station placement reference.
The saved facility is fixed to Ganymede. All fifteen saved wrecks remain
20.318-59.962 km from that facility, matching the native 20-60 km annulus.

The current turnover reader recomputes the placement from the ground station's
present bearing. The station has rotated since generation, so this is no longer
the saved field centre. A detached probe using the existing native facility as
the centre admits all fifteen, with other guards unchanged. The equivalent Luna
probe changes its centre by zero and continues to admit all nineteen.

This is a confirmed defect in our reader of an existing field, not an asset
defect or a reason to change native station/wreck motion. The relevant native
methods are SolarSystem.EnvironmentEntities.AddDerelictRing,
CalculateEntityPosition, SpawnEnvironmentEntity and SetupDerelictPosition.

## Proposed model

Keep three separate concepts:

1. **The yard's authored placement frame.** An explicit spawn reference stays
   authoritative. Otherwise resolve the native, map-matched static infrastructure
   belonging to the authored environment, with agreement checks. For offset
   yards, use the unique existing facility authored at the ring centre, in its
   physical or deferred representation. Do not derive a centre from wreck
   averages or select a planet merely because it is massive or nearby. Missing
   or conflicting evidence retains the field rather than guessing.
2. **Each wreck's actual native motion.** Fixed attachment and circular
   body-locked motion are distinct. Read the native state at the boundary;
   preserve it for survivors. Body-centred rings such as Phobos can already
   contain rotating members without changing their radial membership. Do not
   freeze orbiting wrecks or reparent them merely to simplify turnover.
3. **The captured, protected cohort.** Keep current claim, crew, docking,
   navigation, plot, bounty and target protections. A hull intentionally moved
   away must not become a replacement vacancy. Any admission of known legacy
   displacement must be an explicit repair policy, separate from normal field
   membership.

The first implementation step should correct field-frame reading and test all
seven real saved cases, including both physical and deferred forms. That needs
no new physics controller or frame-by-frame work. Existing departure/return
hooks are sufficient; template pools, damage and native placement remain
authoritative. Departure age and sampling policy do not need changing.

For Mercury, a five-day **metadata-only** backfill is insufficient. Two possible
explicit follow-ups are:

- Repair the documented twelve-member legacy population back into its authored
  yard attachment and valid placement, preserving hull identities and salvaged
  inventory state. This changes motion/positions and needs a reviewed dry run
  and explicit approval before modifying the world.
- Add an explicitly scoped legacy-cohort policy that can retain and replace
  naturally drifting original members while preserving every actor/claim
  safeguard. This avoids immediate relocation but broadens eligibility beyond
  the original strict annulus. It needs a clear distinction between passive
  drift and player towing; an unbounded map-ID or faction test is insufficient.

Neither option has been implemented or applied. Normal field-frame correction
should not quietly choose between them. No Mercury wreck, orbit or age was
changed during this review.

## Regression acceptance before a runtime update

- Preserve all nineteen Luna admissions and their centre.
- Preserve the ten Phobos admissions and the intentional outlier refusal.
- Admit all fifteen Ganymede wrecks using the saved native centre, without
  widening its annulus or changing angular velocities.
- Preserve SVIR, MSUZ and JPTN frames and current spatial results.
- Reject missing/moved/ambiguous centre facilities; docking visitors must not
  redefine a stationary facility's placement frame.
- Keep explicit native references authoritative across story variants.
- Exercise any chosen Mercury legacy policy separately, with negative controls
  for claimed, occupied, docked, targeted, navigated and intentionally moved hulls.
- Continue native save round-trip, one-roll return, unloaded-plugin and RNG
  isolation controls. The earlier small catalog fixtures proved definition
  resolution, not full membership of real saved worlds.

## Model-test results before any Mercury save repair

The player's instruction to test the model first superseded the proposed
sequence of preparing a repaired archive immediately. No Mercury repair copy
has been generated and no Mercury save state has been changed.

`Tools/BoneyardTurnoverRepair test-field-model` now runs the seven saved fields
through the compiled production spatial predicate and actual native body/ship
motion and LitJson serialization. It passes **5,691 assertions**, including:

- All twelve historical Mercury placements fit the unchanged HQCH_DET field;
  their current Mercury-orbiting counterparts still fail it.
- Luna retains 19 admissions, Phobos retains ten and its one rejected outlier,
  and SVIR/MSUZ/JPTN retain 13/14/27 respectively.
- The Ganymede saved-facility centre probe admits fifteen rather than zero.
  This is an in-memory proposed centre, not an installed reader change.
- Every specimen retains its membership through native one-, two- and six-hour,
  five-, eight- and fourteen-day advances. Direct and hourly stepping agree
  within one metre; the largest measured difference is about 0.99 mm. Existing
  circular members keep their angular rate. Native physical situation and
  deferred Mercury serialization round trips preserve motion.
- Free, docked and moved-away controls remain outside spatial admission.

The optional `Tools/DockingOrbitStudy saved-mercury` regression adds **578
assertions** using the twelve original placements and the real saved Mercury
hierarchy. Both docking directions, both departure actors and applied-movement
release are exercised in 96 combinations. Native docking, two-hour group
following, undocking and native situation serialization run with the production
attachment patches. Passive visits retain HQCH_DET and exact original offsets;
deliberate release is not reversed on undocking. The complete suite passes
**1,412 assertions**, including the earlier 834 composition checks for native
hauling, towing, GUI/PASS continuation and collateral behavior. The tested main
DLL is byte-identical to the installed DLL at this check.

The standalone turnover suite also passes its existing **10,439 assertions**,
covering actor/claim guards, native saving, transition state, one-roll return,
replacement staging/commit and RNG isolation. Ten thousand are a sampling sweep,
not distinct gameplay incidents. This is separate from the actual saved-world
motion experiment; it does not prove every current Mercury hull is unprotected.

Private reports are `saved-field-model-checks.json`,
`saved-mercury-docking-checks.log` and `model-protection-regressions.log` under
the existing private evidence directory. The source audit inputs, actual patch
and bounty ZIP hashes, and installed standalone DLL hash remain unchanged.

At this checkpoint the centre-facility guards and a strictly scoped Mercury
repair were still outstanding. The following readiness section records their
completion. These model experiments neither rewrite a
world nor execute a complete Unity GUI/PASS scene. The docking fixture supplies
nearest-body choice (Mercury), crew/scene, fees and wear observations; native
body ephemerides, attachment motion, docking and undocking execute.

## Ready for a later save application

The standalone 0.1.4 candidate now resolves offset centres from their unique
native authored facility. Both physical and deferred representations are
supported; physical native saves can use the registry ID as their template
name. It refuses absent/ambiguous identities, duplicate representations,
changed owner/map/classification/template, free or circular motion, wrong
parent and radial displacement from the authored placement. A stationary
facility's docking visitor does not define its centre. There is no reliable
historical azimuth recovery for an otherwise valid fixed facility arbitrarily
edited around the same radius; that is not claimed by these guards.

The native reference resolver, template pools, bounds, damage, RNG isolation,
curve and all eight existing standalone bindings remain unchanged. No shared
Ostrabus navigation, player controls, anchoring, AI or timeskip hook was changed.

Final evidence:

- 10,457 standalone assertions pass against both reviewed native assemblies.
- 5,775 actual saved-world model assertions pass. This now includes the full
  production ForRegion reader through fourteen days, not just a manually assembled spatial predicate:
  Luna admits 19, Phobos 10, Ganymede 15, SVIR 13, MSUZ 14 and JPTN 27. Mercury
  still correctly refuses the displaced population; its twelve original
  placements pass in memory. One metre is the pose tolerance; the largest
  direct/chunked difference is about 0.99 mm.
- The 1,412-check production docking composition remains applicable; the
  standalone reader change does not modify that binary or those hooks.
- Eight exact-JSON repair tests cover preservation of unknown numeric precision
  and UTF-8, long token boundaries, unapproved-byte mutation, wrong/absent/
  duplicate properties, truncation and inventory-edit refusal.
- Thirteen installer checks and thirteen apply-helper checks use disposable
  workspace fixtures, including required DLL identity and native-verification
  refusal. They do not install anything into the actual game.

A separate `bounty-Mercury-ready.zip` has been prepared in the private evidence
directory. All twelve historical IDs pass current owner, occupancy, docking,
environment, controller, navigation, incoming-target, active plot/bounty/job
and subscription checks. The repair only accepts the audited passive circular
Mercury-relock signature. These checks protect current actor obligations; they
do not reconstruct a complete historical record of every player interaction.

The prepared copy changes 76 approved native properties in five ZIP entries:
eight deferred records in the root and four physical ship situations. It
restores original HQCH_DET offsets, updates coherent current-epoch positions
and velocities for physical hulls, clears their obsolete recent-path arrays,
and retains current rotations, damage, inventories and all other data. All 350
other native entries are byte-identical by SHA-256; changed native entries
are byte-identical outside the listed UTF-8 value-token ranges. No global CO
table or interior was deserialized and rewritten. The optional metadata adds
five assigned days for Mercury and preserves existing Luna/Phobos records.

Native loading, production field admission and LitJson situation round trips
pass for all twelve prepared records; the production metadata reader accepts
the result. The repair report records exact source/copy/plan/DLL hashes and
the full change manifest. The native save format remains native and does not
require the standalone to load later.

Neither the candidate DLL nor this archive has been installed. The actual
`patch` and `bounty` hashes and installed DLL hashes are unchanged. Application
requires the game to be closed and the source archive to still match the
recorded hash. A newer save requires a new plan/copy; the helper refuses to
overwrite newer progress. Work stops at this readiness boundary as requested.

The optional previous-plan input also preserves the original assignment epoch
when preparing against a newer save. A clock control verifies that five assigned
days followed by one additional game hour produces 121 hours, rather than
resetting to 120. This clock fixture writes a plan only and is not an alternative
prepared save to apply.

## Subsequent authorized application to undock

The player subsequently selected the newer `undock` slot and explicitly asked
for its archive to be patched before exiting and deploying the candidate DLL.
The repair was rebased against that archive, retaining the original assignment
epoch. Mercury therefore has 121.4122 hours, not a reset 120. Existing Luna and
Phobos records are unchanged; their elapsed absence is now 49.1061 and 193.4122
hours respectively.

All twelve Mercury candidates passed. The new copy passed exact native-byte
verification and native field/serialization/metadata validation. It was applied
atomically to the named slot with an exact original backup; both final hashes
were verified. No DLL was installed and the running session was not altered.
The player will quit, install standalone 0.1.4 and explicitly load `undock`.
This overrides the earlier readiness-only stopping point at the player's request.

Native CrewSim.QueueSaveAndDesktop uses LoadManager.AutoSave, whose
ConstructAutoSaveName writes a separate autosave-prefixed slot. The explicit
archive-only/running-game application mode was tested in a disposable fixture;
it defers all installation and retains the source/hash/backup checks. It does
not protect against manually saving over the named slot before reloading.
The private `undock-Mercury-verified.zip.applied.json` receipt records application,
backup, archive and required DLL identities. The prior prepared bounty copy was
not applied.

## Reload verification

After the player's reload, the installed standalone hash matches the tested
0.1.4 candidate, and BepInEx logs both loading and readiness for 0.1.4. The
native game module is the reviewed 1.1.0.3 assembly. The main Ostrabus DLL hash
remains unchanged. Live epoch 65635274795.2553 matches the repaired undock save.

Read-only live queries of E-XMH, H-QS10, O-267Z and J-ZEK confirm all four
materialized wrecks remain body-locked to HQCH_DET, have no active route and
exactly match the prepared current-epoch positions (zero measured difference).
There is no turnover metadata-rejection/startup-error message in the retained
BepInEx log. The other eight remain deferred save objects; their repaired native
records were verified before application. The current API does not expose the
standalone's in-memory pending turnover state, so live counts/ages are not
independently asserted from that state. Region-return replacement remains a
future gameplay acceptance check. No live simulation mutation was requested.
