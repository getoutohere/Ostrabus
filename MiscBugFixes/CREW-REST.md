# Autonomous sleep locations and idle sitting

Always-on MiscBugFixes corrections, independent of altwork.

## Sleep location selection

Native `TIsBed`/`TIsBedFree` identify beds through `IsCushion` and `IsSheet`.
Backpacks and EVA suits carry those labels, so `CondOwner.GetMove2` considers
them alongside actual beds. Learned interaction scoring and bounded candidate
sampling can select those objects before a bunk or medical bed.

`CrewSleepPatch` scopes the player's autonomous crew choice coroutine, then
filters its inventory and deferred ship-search results for bed triggers.
Candidates with `IsContainer` or `IsEVASuit` are excluded before the native
selection/sampling step. This avoids patching the frequently called global
condition-trigger evaluator or changing item labels. Other native constraints,
including occupancy, access and reachability, still apply. Other potential
sleeping surfaces remain available; this is not a whitelist of known beds.

Scope is restored before each coroutine yield, including nested calls, and
rechecked on resumption. Manual control, other NPCs and unconscious/subdued
actors and non-sleep searches are excluded. Manual sleep choices remain native.
Drafted flags do not disable this target filter: native GetMove2 can choose
activities autonomously while IsDrafted remains set. The original shared sitting
eligibility predicate incorrectly excluded those searches, allowing container
sleep choices. Sleep eligibility is now separate; drafted sitting remains protected.
Existing sleep chains are not forcibly interrupted; the policy applies when
choosing the next sleeping place. Collapse and medical sleep logic are unchanged.

## Autonomous idle sitting and restore work

`ACTChairSitAllow` can select itself repeatedly through `GetReply`. As long as
it renews, the actor does not return to `GetWork`, whose fallback discovers
automatic restoration (`ProcessAutoTasks`). Shift-change cancellation does not
help when new damaged items arrive during the same work shift.

`CrewIdleSeatPatch` checks only the autonomous, unsecured chair renewal. The
actor must be a conscious player/crew member, on a work shift, permitted to
restore, aboard a ship they own, and outside combat/emergency/drafted control.
This covers ordinary chairs and stools using the same native interaction.
Secured seating, nav-console use and sleep chains are not interrupted.

At most once per 30 simulation seconds per eligible actor, the patch gathers
native `TIsAIRestoreItem` candidates on that ship. It includes patching (bCanPatch,
excluding scrap patch actions), repair (bCanRepair), and restoration (bCanUndamage).
Native InsertUndamage applies category-specific like-new/recently-tried exclusions
and distance/damage ranking. Crew duty weights are native `10^Patch`,
`10^Repair + 1`, and `10^Restore + 2`, with the same category order and tie behavior
as ProcessAutoTasks. It validates at most two candidates with native tools,
conditions and path checks. Material fetching is disabled, while native required
item availability is still checked. The probe does not claim tasks,
queue work or execute effects. It restores the active pathfinder destination,
target and range even if validation throws. Probe failures retain vanilla sitting
behavior and log a warning. Native path-search scratch/cache may still be updated
by its normal reachability queries.

Failed probes advance a numeric cursor through the freshly ranked list. An
unusable leading pair can no longer monopolize every chair renewal and hide a
usable third candidate. Each pass still considers at most two entries; later
passes wrap around and retry previously blocked work. Finding usable work or an
empty list resets the cursor. Changing ships or reversing simulation time resets
both progress and the throttle. Candidate changes are handled by fresh ranking
and a bounds check; no candidate references are saved between passes.

The cursor belongs only to this discovery probe. It does not populate the native
recently-tried dictionary, reserve jobs or change actual duty selection. Native
`ProcessAutoTasks` still selects work and records its own attempts after the
actor gets up. It may first reject its original top two, then use their native
cooldowns to reach the later usable job on its next work pass. The original
probe lacked both native failure cooldowns and independent progress, so its
first two entries could keep an eligible crew seated until personal needs
happened to interrupt the loop.

When usable automatic maintenance exists, the reply becomes native `ACTChairSitEnd`,
which clears `Sitting`, releases `IsOccupied`, and retains the native continuation
and work selection. Actual work is chosen and revalidated by the existing
`GetWork`/`ProcessAutoTasks` path after getting up. Items or permissions can change
between the probe and dispatch; no work is guaranteed or reserved by the probe.
No available or usable work means the sitting loop is left unchanged. The earlier
version checked only Undamage/restoration, missing repair-only and patch-only
targets. This correction concerns automatic maintenance, not explicit
construction/hauling orders or stockpile hauling.

## Native automatic-work scratch-list corruption

CondOwner.ProcessAutoTasks maintains aATsPatch, aATsRepair and aATsRestore fields.
After building categories it sometimes assigns aATsRestore to another category's
list to choose the combined validation order. That alias survives the call. On
the next scan, different categories can be inserted into the same list and then
concatenated with themselves or in the wrong order.

The shipped method reproduces a priority error with all duty levels 0: the first
scan chooses patch work, but a repeated scan with that patch unavailable chooses
restoration before repair. Fresh-list order is patch, repair, restoration. Mixed
categories can exhaust the two-candidate limit while eligible work goes unchecked.

AutoTaskScratchPatch preserves distinct buffers at entry and restores them in a
Harmony finalizer on success or failure. It clears scratch contents after native
selection so they retain no candidate references. Buffers are reused after warmup;
there is no additional weak table or periodic scan. Native dispatch, recently-tried
bookkeeping, fetching and the two-candidate limit remain native. This fixes native
automatic maintenance selection whether or not the actor is seated.

## Manual intent and emergencies

Manual sitting is excluded by both the actor's native `IsAIManual` state and
the interaction's `bManual` marker. Vanilla reply assignment does not propagate
`bManual`; the patch carries it across same-actor chair/nav seating replies.
The existing native interaction save field persists that marker without custom
save data. It is not copied into unrelated action families.

The patch adds no routine work-related interruption to manually ordered sitting.
Native emergency detection, emergency pledges and cancellation remain untouched;
the marker does not disable them. Existing manual chains also retain the native
actor-level manual-control protection.

## Cost and lifetime

No per-frame polling or whole-world scan. Bed filtering occurs only during
autonomous choice. Maintenance probes occur only on eligible sitting renewals with
the simulation-time throttle. Category/rank scratch lists live in a weak-key table
and are cleared in `finally`, so values do not retain item/ship references between
probes. Coroutine wrappers dispose inner iterators and release their own retained
lists/actors on completion or disposal. Thread-local scope is cleared before
yielding. Native searches and ranking allocate their normal temporary results.
Probe progress adds only numeric fields and a ship ID string to the existing
weak-key state; it introduces no additional hook, scan, collection or save field.

## Validation

`Tests/CrewRestCheck` passes 696 offline checks using the bundled Mono. It installs
the production hooks and exercises yielded search scopes, inventory/world result
filtering, manual/NPC exclusions, switching to manual control during a search,
cancelled iterators, eligibility and safety gates, probe throttling, native ranking
and recent-attempt exclusions, unusable work, path restoration on failure,
manual seating reply/save markers and actual asset labels/stand-up cleanup rules.
Repair-only and patch-only failures are reproduced against the prior seat policy.
The native scratch alias and resulting repeated-scan priority error are reproduced
before installing AutoTaskScratchPatch. Corrected native ProcessAutoTasks then
supplies the first/second-choice oracle for 27 duty-priority combinations. Tests
also cover reused/distinct buffers and candidate cleanup after native exceptions.
Sleep regressions reproduce the drafted-actor bypass before correction and cover
all three drafted flags, a flag change during a yielded search, and unchanged
direct/manual searches outside autonomous choice.

The progress regression fails against the old probe: two blocked native-ranked
candidates repeatedly hide a usable third. Corrected probes advance within the
two-entry budget, keep crew cursors independent, revisit failed work, reset on an
empty list/context change/time reversal, and tolerate removal of candidates.
The shipped native ProcessAutoTasks then rejects its own leading pair, records
its native cooldowns and queues the discovered third target on its following
pass. Tests verify that discovery does not write those cooldowns, restore-path
state is retained across failed cycles, and scratch lists release their targets.

Native ProcessAutoTasks, InsertUndamage, reply-role assignment and
JsonInteractionSave execute.
Unity searches, full `GetMove2`/`GetReply` bodies, condition storage and restoration
interaction validation use controlled fixtures. Asset checks verify native
stand-up cleanup definitions and oxygen discomfort exit conditions; this is not
a live emergency simulation. No live game/save was modified by the tests.

```powershell
dotnet build Tests/CrewRestCheck/CrewRestCheck.csproj -c Release --nologo
dotnet run --project Tests/SaveJsonCompatibility/Host -c Release -- Tests/CrewRestCheck/bin/Release/netstandard2.1/CrewRestCheck.dll
```
