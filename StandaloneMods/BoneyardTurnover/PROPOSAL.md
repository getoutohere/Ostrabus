# Boneyard Turnover

**Draft proposal for feedback.** An optional standalone simulation mod for
Ostranauts.

Implementation status: standalone **0.1.0 is built and offline-tested**; live
acceptance is pending. See the [current README](README.md) and
[validation record](VALIDATION.md). The design discussion below is retained as
the original policy proposal, including its feedback questions.

## Purpose

The newer regional boneyards are largely fixed populations. An unlucky initial
selection can leave a field without the new and unusual ship designs or equipment that
make a long trip worthwhile, and salvaging the field does not ordinarily provide
another selection later.

This mod would represent gradual removal and arrival of wrecks while the player
is elsewhere. Returning after a substantial absence would reveal a mixture of
familiar wrecks and new salvage opportunities. It would model the result of
turnover without creating a delivery fleet or continuously simulating that work.

This is an intentional, opinionated game design alteration.

## Proposed player experience

When the player leaves a supported boneyard's ATC region, the mod records the
field's eligible wrecks and the departure time. When the player returns, it
replaces a random subset with newly generated wrecks from that field's normal
ship-template pool.

The replacement fraction follows a **seven-day half-life**:

`replacement fraction = 1 - 2^(-days away / 7)`

All durations are **in-game time spent outside the ATC region**.

| Time away | Expected fraction replaced |
| --- | ---: |
| No absence | 0% |
| 1 day | 9.4% |
| 3.5 days | 29.3% |
| 7 days | 50% |
| 14 days | 75% |
| 21 days | 87.5% |
| 28 days | 93.75% |

For example, returning to 20 eligible wrecks after seven days would replace 10
randomly selected wrecks. At one day, the expected count is about 1.89: the mod
would replace one or two, with probabilities that preserve that average.
Fractional counts are randomly rounded rather than always rounded down.

There is no minimum waiting period, guaranteed rare item, guaranteed unseen
design or scheduled complete-field reset. Random turnover can nevertheless
replace every eligible wreck, particularly in a small field after a long absence.
A particular old wreck might survive several visits. For an original wreck,
short repeated absences give the same expected survival probability as one
equivalent uninterrupted absence, assuming it stays eligible. More frequent
visits do let the player inspect intermediate replacements.

Time spent remaining inside the region does not refresh its field. Reloading a
save does not count as a departure, advance the timer or grant another roll.

### BepInEx configuration

The proposed curve settings would be shipped as normal BepInEx CFG entries:

| Setting | Default | Effect |
| --- | ---: | --- |
| `HalfLifeDays` | `7` | Accumulated days away at which an initially eligible wreck has a 50% replacement chance. Smaller values refresh faster. |
| `CurveShape` | `1` | `1` preserves the exponential curve above; below `1` gives more early turnover and a longer tail; above `1` delays early turnover and then rises more sharply. |

Both values must be finite and positive. Defaults retain the proposed seven-day
curve. Settings would be captured at departure so changing the CFG cannot
silently change an absence already recorded in a save.

For non-default shapes, surviving wrecks retain their accumulated time away.
Time spent inside the region pauses that age. This keeps frequent short visits
from changing the intended survival curve. It requires one age value per
tracked wreck, rather than a growing history of visits.

The [implementation study](IMPLEMENTATION-STUDY.md) includes the generalized
formula, example curves, native integration findings and test limits.

## Scope

The intended scope is the static, asset-defined fields around Mercury, Luna,
Phobos, Ganymede (JFTS), Titan (SVIR) and Europa, together with MSUZ, subject
to verification of each field's actual region, anchor and current environment.
Earth's derelict field is intentionally excluded to preserve its role as a no-go
zone.

Authored blockade or story variants must be handled as variants of their actual
fields, not as additional populations to create alongside them.

**Active boneyards with their own replenishing hauler operation are excluded.**
In particular, this mod would not add a second replenishment system to OKLG or
VORB. An operation does not become eligible simply because its haulers are
temporarily absent, disabled or destroyed.

Only wrecks belonging to a supported field are candidates. Nearby combat losses,
ordinary NPC ships, stations, asteroids and incidental derelicts are not swept
into the system merely because they are nearby.

## What can be replaced?

The proposed default is that an **abandoned wreck remains replaceable after it
has been boarded or stripped**. Salvaging a wreck should not permanently freeze
it into the world's population. This default is open for feedback.

The mod would protect:

- Player-owned ships and native claims that can be reliably identified.
- Ships in docking, towing or tether groups, regardless of who owns the other
  member.
- Ships with occupants or an active AI controller.
- Ships currently targeted by other ships, or still fully/partially loaded for
  active use. These are temporary protections, not a permanent ban on visited wrecks.
- Ships referenced by active plots, bounty records or other identified native
  persistent obligations.
- Wrecks moved out of the field or no longer identifiable as members of it.

Protection is checked again on return and immediately before removal. A wreck
that becomes protected while the player is away is skipped. Uncertain cases
are retained rather than deleted.

The proposed definition of absence also requires that no player crew member
remains in the region. An empty player-owned ship parked there protects itself
and its connected group, rather than freezing the entire field. The crew rule
is another point for feedback.

## Replacement rules

Replacement is **one-for-one**, preserving the count of surviving eligible
wrecks rather than increasing the field's population on every visit.

New wrecks use the current field's authored template weights, faction/ownership,
damage-generation and placement rules. They appear within its allowed field
bounds and retain its intended attachment to a natural body or manufactured
orbital reference. The mod does not move a station-relative field to the nearest
planet.

These new selections provide fresh chances at the field's distinctive designs
and parts. They do not increase the authored odds or promise that a particular
item will be intact or present.

If the game changes the field through a story event, the mod discards an obsolete
turnover plan rather than restoring the previous environment.

**Vacancy replenishment is not included in this initial proposal.** If a wreck
is destroyed, removed or towed away, this model does not automatically add a
replacement for the missing slot. A completely emptied field would remain
empty. Refilling such vacancies would need a separate population rule and is
an important feedback question.

## Installation and removal

The mod would be a separate BepInEx plugin, independent of `Ostrabus.dll`.
Existing saves could begin tracking supported fields without starting a new
game. Installing it would not immediately reroll every field or invent a prior
absence duration.

**The plugin must not become a requirement for loading subsequent saves.**
Replacement wrecks would be ordinary native game objects using vanilla ship
templates. Removing the plugin would stop future turnover; existing replacement
wrecks and other completed world changes would remain.

Departure and eligibility bookkeeping would live in an optional metadata file
inside the native save ZIP, alongside the game's own files. This refines the
original external-sidecar proposal: the metadata shares the native save commit
and travels with a copied or cloud-synced archive, while the native loader ignores
it. A later native save without the plugin drops the optional metadata.
If metadata is missing on reinstall, tracking starts afresh at a later qualifying
departure. No guessed catch-up replacements occur. Full in-game uninstall/load
acceptance remains to be tested; the offline native writer/reader checks pass.

## Questions for feedback

1. Is a seven-day half-life appropriate for the travel times and salvage economy?
2. Should boarded or partially stripped, abandoned wrecks remain eligible, as
   proposed, or should a visit permanently protect them?
3. Should the first version also refill vacancies? If so, should its population
   target be the field's original count or a value from its authored range?
4. Should leaving player crew in the region suspend turnover for the whole field?
5. Would a single return summary, such as "4 wrecks replaced; 3 protected," be
   useful, or should turnover remain invisible except through exploration?

## Technical implementation appendix

### Native mechanisms already available

The reviewed 1.1.0.2 code provides a suitable lightweight representation:

- `SolarSystem.StellarDerelict` stores a wreck's selected template, identity,
  ownership, field map and situation without constructing its full interior.
- `JsonStellarDerelictSave` serializes that native representation, including its
  parent reference and fixed/circular attachment data.
- `StarSystem.GetJSONSave` writes deferred wrecks into `aStellarDerelicts`;
  `StarSystem.Init` reconstructs them without a plugin-defined runtime type.
- `SolarSystem.EnvironmentEntities.LoadEnvironmentForRegion` queues deferred
  wrecks, and `ProcessDerelictSpawnQueue` materializes them gradually through
  native ship creation.
- `AIShipManager.RegionCleanup` and the environment load/unload methods expose
  candidate boundaries for observing actual region departures and returns.

The field definitions in
`data/blueprints/environments/environments.json` contain the relevant
`JsonSpawnDerelict` records. These specify template loot tables, field radius
bounds, alternative centers, ownership, factions and attachment behavior.
Selection should use these existing assets instead of copying ship lists into
the plugin.

The [risk review](RISK-REVIEW.md) documents the native cleanup, target-reference,
spawn-queue and save-failure checks. Initial runtime eligibility would be
conservative about uncertain or partially loaded hulls. Native destruction is
not transactional; an unexpected failure after removal starts cannot be
promised to roll back, so the implementation must stop rather than continue
deleting candidates after a cleanup failure.

The feature needs C# integration for absence tracking and turnover. The inspected
environment JSON defines initial populations; it does not provide an existing
elapsed-absence replacement evaluator. No installed vanilla asset would be
edited. The broader content audit is recorded in
[the 1.1 content investigation](../../Build/CONTENT-AVAILABILITY-1.1.0.0.md).

### Field identity and eligibility

A field key needs its current environment definition, actual anchor and ring
identity. A region ID or environment-map number alone may not distinguish every
field. Earth is explicitly excluded even though its environment defines a
derelict ring. Region ownership and active-hauler exclusion must come from authored
capabilities and reviewed field definitions, not the instantaneous number of
haulers alive.

The departure snapshot must handle both deferred wrecks and instantiated ships.
Native `UnloadEnvironmentForRegion` excludes previously visited hulls from its
normal conversion pass, so examining only `dictStellarObjects` would omit much
of the salvage the player has touched.

Use stable ship IDs for the snapshot. Revalidate membership, ownership,
occupants, controllers, attachments and persistent references before deleting
anything. A player-owned ship elsewhere must not be treated as a missing wreck
to replenish. Pending native spawn-queue entries must not retain references to
removed IDs.

### Turnover calculation

On return, revalidate the departure roster. With the default exponential curve,
let `N` be the number of candidates still eligible and `t` the nonnegative elapsed
absence in days:

```text
p = 1 - 2^(-t / halfLifeDays)
expectedCount = N * p
count = floor(expectedCount)
with probability fractionalPart(expectedCount): count += 1
select count distinct candidates uniformly
```

This preserves the expected fraction while keeping each visit's count close to
the curve. It does not guarantee the survival time of any particular wreck.
The default half-life is seven days; any exposed configuration must reject
nonpositive or nonfinite values.

For other `CurveShape` values, calculate each survivor's conditional probability
using its accumulated absent age. Different wrecks can then have different
probabilities. The study tests a randomized selection that retains each wreck's
probability while keeping the total count at the floor or ceiling of their
summed expectation.

Selection and replacement planning must have stable randomness for the same
saved departure/return state. The bookkeeping must distinguish completed work
from pending work so failure, reload or repeated entry callbacks cannot create
additional draws. It should not consume unrelated gameplay random rolls merely
by inspecting a field.

### Creating and removing wrecks

Prefer deferred `StellarDerelict` replacements, retaining native template
materialization and damage generation. Do not spawn complete ships merely to
populate an off-screen field.

Reuse the semantics of `EnvironmentEntities.AddDerelictRing`,
`SetupDerelictPosition` and `CalculateBreakInMultiplier`, including alternative
field centers and fixed/circular attachment modes. The implementation must
check placement success: the native position helper can log a failed search
and continue, which is not sufficient assurance before deleting an old wreck.
Do not invoke `ChangeEnvironment` to refresh a yard; it can also change authored
infrastructure and other surrounding entities.

Prepare and validate a replacement before removing its predecessor. A failed
template selection or placement leaves the old wreck in place. Deletion of a
materialized hull must use reviewed native cleanup mechanisms rather than
merely removing a dictionary entry. Native destruction is not assumed to be
transactional; partial failures and remaining references need explicit tests.
Do not compensate for failure by deleting a different, unplanned wreck.

Use new native IDs for replacements. Do not reuse an old identity or redirect
quest/bounty references to a different ship. Preserve ordinary ship ownership
and faction behavior after creation.

### Save independence and optional archive metadata

The optional metadata entry contains bounded current bookkeeping: schema version,
save/world identity, field definition identity, departure time, eligible IDs,
accumulated absent ages, captured curve settings and stable selection state.
Replace completed rosters instead of accumulating an unlimited visit journal.
Do not repurpose the game's applied-patch history as an event database.

Freeze metadata alongside native save-data capture on the main thread. Add those
immutable bytes as a separate entry during the native ZIP-writing job, without
modifying native JSON. The game then commits or fails the combined archive using
its existing writer. Native loading ignores the extra entry; native saving
without the plugin omits it. External sidecars and full-archive fingerprinting
are no longer the preferred implementation.

On load, clear the previous session's state before staging metadata for the
incoming save. Missing, mismatched or corrupt metadata disables catch-up for
that snapshot; it must not prevent native loading or trigger guessed replacement.
Only activate the staged data after successful game loading. Copying the native
archive preserves its associated metadata without creating a plugin dependency.

The offline study executes the actual native ZIP writer, native save parser and
plugin-free resave. Full Unity scene loading and live travel remain acceptance
requirements, not claims of this investigation.

### Performance and integration boundaries

There is no simulated transport fleet, per-frame population scan or continuous
derelict AI. Work is driven by region transitions and ordinary saving/loading.
Bookkeeping size is bounded by the supported fields and their current rosters.

Deferred replacements should avoid most ship-construction cost. Removing visited
materialized hulls may still require inventory/reference cleanup, so transition
latency must be measured rather than described as free. If work needs spreading
over multiple frames, its ordering with native environment loading and spawn
queues must prevent visible disappearing wrecks or half-applied plans.

The standalone plugin must coexist with Ostrabus's attachment preservation,
environment transitions, lifecycle cleanup and bounty protection without
requiring those patches. Changes to gravity, navigation, combat, NPC cleanup,
market supplies and active hauler operations are outside this feature's scope.

### Acceptance tests

- Verify the curve at zero, one, seven, fourteen and long absences, fractional
  count rounding, distinct selection and repeated-short-trip expected behavior.
- Exercise actual native environment definitions, template pools and placement
  for supported fields, including manufactured orbital and ground references.
- Verify Earth stays excluded, and active hauler-serviced yards stay excluded
  even when their haulers are temporarily absent or destroyed.
- Protect player ownership, occupied hulls, every docking/tether connection,
  active AI and plot/bounty references; include previously visited stripped
  hulls as an explicit policy case.
- Cover deferred and materialized wrecks, native spawn queues, failed template
  selection, failed placement, deletion cleanup and story-driven map changes.
- Confirm reload, save overwrite, rollback, copying, branching, corrupt metadata
  and repeated region-entry callbacks do not double-apply turnover.
- Save with the mod, remove it, load and save under the native game, then
  reinstall. All replacement wrecks must remain native-loadable, and mismatched
  bookkeeping must be ignored safely and optional metadata dropped on native resave.
- Verify that the standalone package works without `Ostrabus.dll` and that
  ordinary full/custom Ostrabus deployment does not inadvertently install it.
- Measure transition time and metadata size for a large field and several
  accumulated fields; test normal travel, fast-forward and PASS region changes.

The implemented first version follows these rules; live acceptance remains pending.
