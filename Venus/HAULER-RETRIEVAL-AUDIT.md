# Derelict depot retrieval audit

Source/data audit of 1.0.1.10, with live Venus observations. The scoped correction
is implemented in VorbRetrievalDepotPatch.cs and verified offline. After deployment,
live inspection confirmed four previously idle VORB retrievers selecting owned
wrecks and following native approach plans. Completed retrievals were not captured
as part of that activation check.

## Configured depots

The shipped main-world star_system.json configures exactly two stations with
StationIsDerelictSpawner/StationIsDerelictCollector. Additional entries in
test_environments.json are test fixtures, not more depots in the normal world.

| Depot | Owner | Regional ATC | ATC owner | Deploy below | Collect above |
| --- | --- | --- | --- | --- | --- |
| OKLG | OKLGCorp | OKLG | OKLGCorp | 60 wrecks | 40 wrecks |
| VORB | VENCCiv | VNCA | VNCACiv | 16 wrecks | 9 wrecks |

These values are thresholds, not guaranteed populations or simultaneous-hauler
limits. AIShipManager.SpawnHauler checks at randomized 2,000–3,600 game-second
intervals. It counts Derelict-status hulls within ScavPilot.MaxTargetRange (about
5,000 km) of each depot. The count does not filter by owner. If both deployment
and collection are eligible, the native code randomly chooses between them.
This method does not impose a cap based on already-active collecting haulers.

## Deployment

HaulerDeployingPilot.SpawnDerelict resolves the dispatch station's owner and passes
it into StarSystem.AddDerelict. Therefore OKLG creates OKLGCorp wrecks and VORB
creates VENCCiv wrecks. The existing Venus loot selection patch changes the wreck
pool, not ownership. Deployment's return selection uses origin/HomeStation;
the existing general Haulers recovery addresses the abandoned-return handoff.

AnchorDockedShip checks StationAllowsAnchoring on the origin station. OKLG has
that condition in shipped world data; the existing Venus patch enables it for
VORB. The existing VORB undock patch restores the placeholder orbit lookup. These
are distinct from collection ownership and should not be replaced by a retrieval
fix.

## Retrieval mismatch

HaulerRetrievingPilot.GetTarget has three important paths:

1. When towing a derelict, return to the hull's origin station when available.
2. When empty, find the nearest regional station, then inspect recent messages
   from that station for an explicit third-party target (120-second window).
3. Otherwise call FindDerelictTarget with that regional station's ID. It obtains
   the station owner and searches that owner's wrecks, not the home depot's.

OKLG's normal home/regional identity makes this work without exposing the
assumption. VORB is not a region, so its haulers resolve VNCA and search VNCACiv
instead of VENCCiv. The same substitution also selects the wrong station's
assignment-message history for a VORB dispatch.

The live census examined all 224 registered hulls and found 11 derelicts anchored
to VORB|Aux. Every one was owned by VENCCiv; none by VNCACiv. Four active VORB
retrievers had living pilots, full RCS, no tow, no target and no RCS acceleration.
The repeated spawn checks see enough nearby wrecks to justify more collectors,
while collection target selection excludes the VORB wreck pool.

This is a demonstrated selection mismatch, not proof that every momentary idle
retriever is stuck for this reason: planning rejection, explicit assignments and
other native command transitions are separate considerations.

Other native selection details retained by the fix: candidate wrecks must
be visible, Derelict-status, at most Shallow, outside active plot lists and not
tutorial wrecks. Visit history influences their selection probability. Candidate
enumeration itself has no matching 5,000 km proximity filter.

## Implemented scope

A Venus-scoped correction covers the shipped world's only affected depot.
In HaulerRetrievingPilot.GetTarget's empty-hull branch, use a valid VORB
dispatch/HomeStation instead of the nearest regional station when the controller
was dispatched by VORB. Preserve the existing selection routine and its explicit
assignment-message precedence; do not impose wreck ownership on an explicitly
requested rescue target. Leave OKLG and other rescue dispatches unchanged.

25 offline checks cover VORB ordinary collection, OKLG unchanged behavior, VORB
assignment messages including foreign-owned rescue targets, existing tow return,
invalid/missing home fallback, native candidate exclusions and save-restored
controllers. The production transpiler patches the native lookup in place.
It does not change faction ownership, suppress retriever spawning, force a
destination or periodically repair ships after bad selection. It retains no new
state; the only added work is a bounded-by-existing-registry controller lookup on
native target selection, and it avoids the normal regional-station scan for VORB.
