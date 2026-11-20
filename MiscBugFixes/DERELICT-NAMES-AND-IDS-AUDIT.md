# Derelict names, deferred registration IDs and captain-mismatch report

Investigated and preventive repairs implemented November 20, 2026 against native
Ostranauts 1.1.0.4. No installed asset change or live/save mutation.

Native Assembly-CSharp SHA-256:
`904c72870f55124bbe3f9ee924a4a650279c362541b5795174d0cf940b97c474`.

## Original report status

The report is not closed by these two preventive repairs. The naming symptom has
a confirmed cause and fix. Deferred-ID overwriting is an independently reproduced
defect, not an established cause of the reporter's docking stall or wrong-target
text. The reporter's O-JVT/Lily Robertson state has not been supplied, and the
maintainer's separate saved world cannot attribute that incident.

A focused follow-up reviewed native navigation staffing, crew assignment,
message delivery and gig actor resolution. Dock.RunCommand returns Running when
NavAIManned is false; the comms captain fallback is compatible with there being
no eligible person on board, but can also occur normally after disembarkation.
BaseCharacter.AssignCaptain retains GetCaptain's out parameter without requiring
its true/success result. Existing investigations establish independent pilot-loss
mechanisms: fresh NPC identity collisions and attached-crew collection during an
instant station return. Neither is proven to have happened to O-JVT.

Gig descriptions can use the current ship of a name-keyed target actor through
JsonJobSave.COThem and GrammarUtils.ShipFriendly; a changed location therefore
does not alone identify a corrupt ship registration. Conversely a genuine
wrong-target incident must not be dismissed as cosmetic without its saved target
IDs and actor placement. The affected save, command/controller state, actual crew,
message participants and docking ports are needed to distinguish those paths.

## Confirmed name loss

`Ship.InitShip` generates `publicName` through `DataHandler.GetShipName` when a
template's name is empty or `$TEMPLATE`. The random name is separate from
`strRegID` and is not used to identify docking targets.

`SolarSystem.EnvironmentEntities.SpawnShipFromStellarObject(IStellarObject,
string)` subsequently assigns `ship.publicName = ship.strRegID` in its
`StellarDerelict` branch. This discards the generated name. Its asteroid branch
also uses the registration as a name, but asteroid naming is a separate policy.
The wreck branch contains no make, model, hull-size or docking-vector test.
The assignment exists in every inspected 1.1 source version, 1.1.0.0 through
1.1.0.4.

Assets already request ordinary names. For example:

- `ships/SalvagePodSmall.json`: Ryokka TU-70a, 17x13, `$TEMPLATE`.
- `ships/Mesa.json`: Testudo Mesa Mk. I, 65x46, `$TEMPLATE`.
- `loot/loot.json`: `RandomDerelictSmall` includes SalvagePodSmall;
  `RandomDerelictBig` includes Mesa. `RandomDerelict` draws from both.
- `blueprints/environments/environments.json`: map 801 draws RandomDerelict
  around Ganymed; `AddDerelictRing` creates deferred StellarDerelict objects.

By contrast, `HaulerDeployingPilot.SpawnDerelict` calls
`StarSystem.AddDerelict("RandomShip", ...)`, which spawns the ship directly
without the deferred converter's name overwrite. Existing saved hulls and
different spawn routes can therefore retain names. This explains a route-based
inconsistency; it does not establish the route of each ship in the report.
The claimed small-Ryokka/large-Testudo boundary is not a native naming rule.

The implemented correction preserves the name already assigned by native ship
initialization in the wreck branch. Editing ship templates or generating a
second name in a UI hook would not repair the owning mechanism.

## Separate, confirmed registration-allocation hole

`Ship.GenerateID` rejects a random candidate only when it is in
`CrewSim.system.dictShips`. It does not check `dictStellarObjects`.

`SolarSystem.StellarDerelict` calls that allocator when no explicit ID is given.
`EnvironmentEntities.CreateStellarObject` stores the result using the deferred
dictionary's indexer, which replaces an existing entry rather than rejecting
duplicate keys. Consequently a repeated random ID can silently replace an
earlier deferred wreck with a different wreck. StellarShip uses the same
allocator too. This is an identity defect, independent of display-name loss.

The normalized GenerateID source is identical across the inspected
1.0.1.10 and 1.1.0.0--1.1.0.4 decompilations. The new deferred wreck population
exposes another consumer of the old allocator; this is not evidence that the
name generator started producing invalid IDs in 1.1.

The preventive correction checks both live registries in shared allocation.
Explicit-ID restoration and conversion intentionally retain an existing identity
and are not rerolled. No automatic repair of extant ambiguous references has
been designed or performed.

## Captain warning and docking claim

The exact warning is emitted by
`Ostranauts.ShipGUIs.MFD.MFDComms(string, ShipMessage)`. It compares the current
comms representative with the actor referenced by a stored message. On mismatch
it logs, substitutes the current representative for reply-option construction,
and displays the message. The warning branch is not an AI navigation retry loop.

`Ostranauts.Ships.Comms.Comms.GetCaptain` initially returns the vessel's ShipCO
through its out parameter. Crewless ships and derelicts return false while
retaining that synthetic representative. Thus `CO-O-JVT` is an ordinary ShipCO
identity, not proof that a generated human captain received a broken name.
The quoted warning establishes that the message-side actor is Lily Robertson on
OKLG while the currently resolved representative belongs to O-JVT. It does not
establish why Lily is there or what the vessel's AI is attempting.

`Ostranauts.Ships.Commands.Dock` resolves targets by reference/registration ID,
port availability, navigation staffing and range. It does not use publicName.
For a non-ATC target, `Comms.AIGetClearance` returns true immediately; displaying
this MFD warning is not a prerequisite for docking with a wreck.

`FlyTo.DescriptionFriendly` deliberately renders `Flying to ` followed by
`shipScanTarget.strRegID`. `GUIJobs.GetShipName` and
`GrammarUtils.ShipFriendly` render publicName, so name loss can appear in gig
descriptions without changing a gig's actual registered destination. These
presentation effects do not prove a wrong navigation target. The reported
spinning/docking loop and its relationship to any collision need the affected
vessel's saved state; they have not been reproduced here.

## Verification and limits

An isolated bundled-Mono probe executed the unpatched native converter for a
small Ryokka template and a large Testudo template with an already named Ship
supplied at the scene-allocation boundary. Both names were overwritten, their
registration IDs remained unchanged, and their deferred entries were retired.
Actual GetCaptain returned their ShipCO, and changing publicName did not change
actual FlyTo.DescriptionFriendly output.

A deterministic repeated RNG draw also executed actual GenerateID and actual
CreateStellarObject. Two different wreck templates received the same ID and
only the second remained in the deferred dictionary. Eighteen checks passed.
Unity object comparison, scene allocation, people enumeration, visibility and
condition writes were explicit fixtures; no full docking scene was simulated.
The repeated RNG proves collision handling, not the probability or occurrence
of a collision in the reporter's save. No Ostrabus behavior patches were loaded.

The asset metadata scan covered all 237 shipped ship templates. Temporary
probe sources, results, metadata and source-version comparisons are under
`obj/DerelictIdentity-20261120/`. The host emits a known missing Unity Debug.Break
internal-call diagnostic; the probe nevertheless reaches its successful final
assertions and exits with code zero. No conclusion is based on that diagnostic.

## Save-based regression comparison

Read-only `Tools/SaveContentAudit` inventories were collected for 12 archives:
the last 1.0.1.10 continuation, a separate fresh 1.1.0.0 reference world, and ten
continuation snapshots spanning 1.1.0.0 through 1.1.0.4. Each archive's SHA-256
was verified unchanged after extraction. SaveInfo's last-save version was used,
not a user-chosen folder name or the character's original creation version.

For physical hulls with default/mobile classification and Derelict damage state:

| Snapshot | Saved game version | Mobile derelicts | Name equals registration ID |
| --- | --- | ---: | ---: |
| option | 1.0.1.10 | 59 | 0 |
| migration-reference, separate fresh world | 1.1.0.0 | 52 | 52 |
| patch, immediately upgraded continuation | 1.1.0.0 | 59 | 0 |
| next | 1.1.0.0 | 68 | 10 |
| 1.1.0.1 | 1.1.0.1 | 68 | 10 |
| steal | 1.1.0.2 | 62 | 10 |
| 1.0.1.3, despite that folder name | 1.1.0.3 | 146 | 88 |
| 1.1.0.3 | 1.1.0.3 | 94 | 33 |
| apartment | 1.1.0.4 | 93 | 33 |
| bounty | 1.1.0.4 | 93 | 33 |
| home | 1.1.0.4 | 94 | 33 |
| fine | 1.1.0.4 | 95 | 33 |

All callsign-only wrecks in those snapshots carry the environment marker.
Counts need not increase monotonically: hulls can unload to deferred objects,
be destroyed, leave the sampled classification or be replaced through turnover.
They are snapshot counts, not a cumulative defect counter.

In consecutive sampled continuation saves, 88 distinct deferred wreck IDs later
appear as physical hulls. All 88 have publicName equal to that same registration.
Fifty-six retain a usable template field on both sides, and all 56 match their
earlier deferred template. The other 32 physical records retain only their ID in
the template field, so no template-consistency conclusion is made for them.
No observed existing physical hull changes from a distinct human name to its ID.
Together with the native reproduction, this establishes the naming regression
in saved worlds and distinguishes new materialization from renaming old hulls.

No snapshot contains duplicate IDs across the inspected physical and deferred
ship/wreck collections, and no common ID changes between two known templates in
the sampled continuation comparisons. This does not clear the allocator defect:
an overwritten deferred entry leaves one key, and an overwrite occurring before
the earliest snapshot of that object is not observable retrospectively. Unknown
template fields and gaps between saves further limit exclusion. Disappearance
alone was not treated as proof of ID corruption, nor were different fresh worlds
compared as if they were successive states of the same world. Deferred asteroids
inside body fields were outside this particular ship/wreck comparison.

The latest `fine` snapshot's 33 affected hulls are associated with map 100
(4), 1002 (6), 1000 (19) and 800 (4), rather than the old named OKLG haul-in
population. No save repairs were attempted. The private catalog, extraction
plan, comparison script and results are in the same ignored evidence directory.
Full temporary inventories were reduced to the identity fields used by the
comparison to avoid retaining unnecessary plot, company and navigation content.

## Implemented prevention

`DerelictNamePatch.cs` replaces only the registration-as-name field write in the
StellarDerelict conversion branch with a guard that keeps the existing name.
There is no second random name draw. A warning names the vessel and preserved
name when they differ from the callsign; an already callsign-only hull does not
claim a repair. The asteroid and StellarShip name assignments remain native.

Native execution exposed a Harmony finally-emission issue in this converter:
even a no-op transpiler lets the asteroid foreach's finally continuation fall
through into unrelated type tests, returning null after constructing the rock.
The name patch therefore also emits an explicit branch immediately after that
finally to its original shared completion target. Existing non-asteroid branch
labels still skip this instruction. Both native leave targets, exception layout
and completion signature are checked before installation. Tests cover an empty
body dictionary, exhausted field searches and successful field removal. This is
preservation of the converter's authored control flow, not a new asteroid policy.

`ShipRegistrationPatch.cs` substitutes the native loaded-dictionary membership
check with loaded/deferred membership checks and passes the original prefix to
the existing recursive retry. Native character pools and RNG draws remain
unchanged for collision-free creation. Retries consume ordinary native draws;
each rejected candidate logs its ID and the registry that already owns it.
Null-valued keys still count as occupied. Explicit IDs never enter this allocator
and retain native restoration/conversion semantics.

Example native console warnings:

```text
[Ostrabus] Preserved derelict name 'Example Vessel' for M-H67; prevented replacement with its registration ID.
[Ostrabus] Prevented duplicate ship registration M-H67; already used by a deferred space object. Drawing another ID.
```

Both use Unity Debug.LogWarning, which ConsoleToGUI receives through the game's
normal log subscription. No persistent warning registry, frame callback, saved
marker, environment/loot override or navigation controller is added. Each
independent bugfix is selectable in custom builds and included in bugfix and
recommended presets. These guards prevent new damage; they do not rename old
callsign-only wrecks or reconstruct overwritten objects.

The maintained [native regression suite](../Tests/DerelictIdentityCheck/README.md)
executes unpatched controls and the actual production transpilers with explicit
scene/RNG/logging fixtures. Its optional no-op Harmony control independently
reproduces the finally issue without either production repair installed.

Validation passes 349 native regression assertions and the separate 24-assertion
finally control. Debug and Release builds have no compiler errors or warnings.
Full-plugin binding passes with logging off (325 bound, three intentionally
disabled) and on (328 bound, none disabled), both with zero failures/warnings.
Custom-builder self-tests pass 671 checks; the two independent feature profiles,
their combined profile and consumer Full all compile, verify and bind. These
are offline receipts; the rebuilt plugin has not been deployed into the running
game by this work.
