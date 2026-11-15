# New AI crew can reuse and regenerate an occupied character identity

November 14, 2026; native Ostranauts 1.0.1.10. Confirmed native defect with an
offline reproduction. The preventive repair is implemented and offline validated.
The evidence below describes the pre-intervention baseline; subsequent authorized
cargo recovery and hull pruning are recorded at the end. Tomiwa was not modified.

## Historical evidence: O-6F7 / Tomiwa Trejo

At the investigation baseline, O-6F7 (SVIR Hauler 75) holds 1,224 suits. It has no crew, controller or
navigation plan and is coasting without commanded acceleration. The 936.205 kg
RCS reserve is unchanged across the reviewed saves. It is not a successful
cargo arrival: the carrier market and complete load remain in free space.

- `docked`, `depart` and `wtf` store a living Tomiwa aboard O-6F7. In `wtf`,
  world epoch 65633872851.5786, his StatAge is 28.
- `arrive`, world epoch 65633959849.6322, contains no crew on O-6F7. Its actual
  saved CO named Tomiwa is instead aboard O-MYSS, age 46, retaining the O-6F7
  ownership claim and old personal messages, with added O-MYSS ownership and
  CCRE/OKLG affiliations. Searches inspect actual aCOs records, not merely
  copies of social profiles.
- O-MYSS's ship log records `Tomiwa Trejo transferred from BCRS.` at world
  epoch 65633930744.914. His personal log contains the older O-6F7 transfer and
  `Tomiwa Trejo reports for duty on O-MYSS`. That latter message is emitted by
  `AIShip.AddCrew` for `MoveReason.ADDNEWCREW`, distinguishing its newly-created
  fallback from ordinary `ADDCREW` reuse. Personal-message clocks are not assumed
  to equal the world epoch.
- Current live lookup finds that actor ID alive at BCRS. O-MYSS now has other
  crew. This is not evidence that the original freighter captain died.

The ship log's reported BCRS origin does not prove he travelled there first:
`PersonSpec.MakeCondOwner` moves an actor before the final AddCrew overload logs
its origin, and that overload then defaults to the requested station ID.

An additional stable-identity comparison strengthens the historical attribution:
human actors use their name as strID/strCondID, with no separate actor GUID in
the saved CO. However, all **71** body/inventory/attached-item IDs owned by Tomiwa
in `wtf` also occur under Tomiwa on O-MYSS in `arrive`, including identical arm,
forearm and hand GUIDs and their parent/slot relationships. All **10** old
personal-message GUIDs persist too. The later inventory has 93 attached entries,
retaining every old entry. These are the same persistent actor contents and
history, not merely a different NPC with the same display name. The separate
runtime stack/RNG qualification below concerns the exact caller execution, not
whether the old freighter pilot's identity was retained on the new ship.
The detached comparison is `Tomiwa-stable-identifiers.json` in the evidence
directory.

## Native mechanism

1. `Ostranauts.Ships.AIPilots.AIShip.AddCrew(string, Ship, string, bool)` builds
   `GetPilotForbids` and searches eligible existing station personnel. Serving
   pilots are supposed to be excluded.
2. Its no-volunteer/underpopulation fallback constructs
   `new PersonSpec(personSpec, bNew: false)`. A fresh random full name is chosen,
   but this flag skips the constructor's live-name uniqueness loop.
3. The newly constructed profile has no cached actor or strCO backlink, so
   `PersonSpec.MakeCondOwner` enters its new-character branch and calls
   `DataHandler.GetCondOwner("Crew01", FullName, ..., bLoot: true)`.
4. `DataHandler.GetCondOwner` intentionally resolves existing live actors by
   strName first, then saved actors by the same key. On a generated-name
   collision it returns the old character, including one aboard a remote ship.
5. `MakeCondOwner` does not distinguish that result from a newly created CO. Its
   subsequent body assigns the new profile, builds social/chargen components,
   rolls age/career, sets StatAge and moves the actor to the requested ship.
   Existing history and ownership survive while attributes and ship change.
6. The old ship is left without its pilot. Crew/controller safeguards are
   downstream of that theft; stopping/unregistering an empty hull does not cause
   the identity collision and does not deliver its cargo.

This explains the otherwise implausible age change, preserved ownership/history,
new-crew log and abandoned loaded freighter together. No historical runtime
stack/RNG recording exists for the exact event; its saved signature strongly
matches the independently reproduced native path.

## Assets versus code and Ostrabus attribution

`personspecs/personspecs.json`'s CombatPilotCCRE requests empty first/last names
(random generation), the BCRS homeworld and combat/LEO attributes. The first-name
pool contains Tomiwa and the last-name pool contains TREJO, capitalized by native
name generation. These are ordinary authored names, not instructions to recruit
the occupied freighter captain or overwrite his identity. No asset-only age,
faction or spawn-rate change can repair identity ownership at the factory.

The baseline reproduction loads the shipped Assembly-CSharp and uses **no Ostrabus
behavior patches**. Before this repair, production sources did not rewrite PersonSpec's
constructor, MakeCondOwner, name generation or the string AddCrew creation path.
The PDC GetCondOwner prefix only handles replacement PDC magazine generation and
does not apply to Crew01. BountyDepartureFuel wraps the separate existing-CO
AddCrew overload only inside bounty spawning and supplies servicing; it neither
generates names nor reruns character creation. Our simulation/spawn timing can
alter opportunities for the native bug, but is not required for it to occur.

## Offline reproduction and limits

The maintained [NpcIdentityCheck harness](../Tests/NpcIdentityCheck/README.md)
runs native code under bundled Mono. Eight baseline checks
cover occupied-name creation, native factory returning the same actor, legitimate
existing-profile early reuse, the live-name-disambiguating constructor contrast,
actual AddCrew forbidding the serving pilot yet reusing it through fresh
generation, and saved-only identity collision despite bNew=true.

The name draw is deterministic so the collision is reproducible. Volunteer
availability and homeworld/logging are boundary fixtures. Native AddCrew,
GetPilotForbids, PersonSpec construction/GetCO/MakeCondOwner entry, GetPeople and
GetCondOwner lookup execute. A controlled exception after the factory returns
the occupied actor stops before Unity component/chargen mutation. Later age
overwrite and relocation are source-verified, not claimed as executed by that
probe. The saved evidence independently shows those effects.

Evidence and probe output are under the directory named by
`obj/economy-fleet-survey-folder.txt`; `obj/FreightPilotIdentity` searches the
historical hull archives read-only. No live/save intervention was performed.

## Repair boundary

Fresh AI crew creation needs an identity distinct from both live and saved
actors, preserving intentional reuse through the existing station-selection
branch. Merely flipping bNew to true is incomplete: its native check consults
only mapCOs, ignores dictCOSaves and stops after five collisions. Do not globally
disable GetCondOwner reuse or globally rename authored plot characters. A repair
must maintain existing-character transfers, restored saved actors and authored
NPC references while making the fresh-creation fallback unambiguous.

## Implemented correction and validation

`AiCrewIdentityPatch.cs` replaces exactly the fresh fallback's
`PersonSpec(JsonPersonSpec, false)` constructor call in `AIShip.AddCrew` with a
helper returning that same profile type. It first constructs the ordinary native
profile, then checks its full name against both `DataHandler.mapCOs` and
`DataHandler.dictCOSaves`. If the name is occupied, it calls native
`DataHandler.GetFullName` again using the profile's existing gender, repeating
until both dictionaries report an unused ID. Keys with null values still count
as occupied. Names come from the game's ordinary human naming mechanism; no
numeric suffix or identifier is appended to a person's name.

Each candidate uses average O(1) dictionary membership checks. Additional native
name draws occur only for occupied names; collision-free creation keeps its
original draw. The native generator retains its name pools, gender rules and
full-name selection behavior. There is no population scan, per-frame work,
persistent registry or save schema change. Native
fresh recruitment runs synchronously through allocation; this does not add a
second identity reservation mechanism. All 21 shipped civilian, combat, scavenger
and police role templates covered by the harness use human/random-name profiles.

Native station selection, serving-pilot exclusion, factory lookup, existing-profile
reuse, saved restoration and character creation remain authoritative. The
transpiler refuses a changed constructor call shape rather than rewriting an
unrecognized native path. Custom builds expose the independent bugfix feature
`ai-crew-identities`.

Validation passes 8 unpatched baseline checks and 122 repair checks. These execute
native recruitment with the actual production patch, verify the old pilot's
identity, age, ship and ownership survive a collision, cover live/saved and 32
consecutive occupied name draws, and exercise intentional station recruitment and
saved lookup. Collision-free creation and intentional recruitment do not draw extra
names; retries retain the profile's gender input.
They also verify the 21 native templates retain authored age, career, loot and
factions and that exactly one IL instruction is replaced. Fresh Unity scene-object
allocation is a controlled boundary; full chargen and scene rendering require
live testing. Native existing-person boarding executes with scene positioning
substituted explicitly.

Broader validation passes 52 native lifecycle checks, all 283 full-plugin patch
bindings, 547 custom-builder self-tests, and an isolated one-feature custom build
with one bound patch and no frame calls. Debug/Release builds and binding audits
report no errors or warnings. This prevents future collisions; it does not restore
Tomiwa's historical attributes or return him to O-6F7.

## Authorized live recovery

After explicit user authorization, O-6F7 was pruned with zero crew removed. Its
old route pointed to VORB in `docked`, `depart` and `wtf`; VORB accepted the four
suit items it could store. The original remaining 1,220 items stay in the native
cargo market. The subsequent user-authorized systemwide redistribution found no
receiver advertising native demand for suits, so those items remain warehoused.
Neither intervention changed Tomiwa's body, age, location or history.

The redistribution credited 47,125 other warehoused items to 20 stations and
removed 52 emptied cargo-market records, conserving the initial 75,587 items as
47,125 credited plus 28,462 retained. Live incoming cargo was reserved before
credits; 660 medical items were redirected from JFTS to SVIR after identifying
O-Q6J's 1,320-item inbound medical shipment. Native source and receiver stock audits
and global conservation passed. The external receipt and exact transfer ledger are
under `obj/FreightRedistributionExecution-20261114-220258/`; its combined summary
also links the medical-redirection receipt. These are explicit maintenance
operations, separate from the preventive identity patch and ordinary simulation.
