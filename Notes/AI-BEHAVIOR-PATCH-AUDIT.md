# AI behavior patch audit — 2026-11-09

Follow-up: A1 is now repaired by [native-priority return scheduling](../Navigation/RETURN-PRIORITY.md).
The [conditional-candidate investigation](AI-CONDITIONAL-CANDIDATES.md) supports
retaining the scoped sleep and VORB route protections. VORB asset migration is
deferred by user decision; retain its anchoring hook. The report below records
the original audit findings and evidence before this follow-up.
The subsequent [shared refill repair and simplifications](../Navigation/RCS-REPLENISHMENT.md)
also removed the duplicate target observer and regional empty-home shortcut.
Counts and candidate descriptions below refer to the original audit snapshot.

## Verdict

Keep the proven C# integrity repairs. There is no evidence that most of these
patches can simply be replaced by trigger/pledge JSON. Several important pilot
trees, target-selection algorithms, scheduling boundaries and numerical routines
are authored directly in C#.

The highest-priority finding is **our shared return controller bypassing native
decision-making**, not excessive Harmony dispatch overhead. A clear asset-owned
correction is VORB's missing anchoring permission. Other opportunities need scope
and migration design before hooks can be removed safely.

This is an audit and recommendation set. No production patch was changed,
removed, deployed or toggled. No live simulation requests were made for this
audit. Prior live results are identified separately from fresh offline checks.

## Scope and inventory

Reviewed ship navigation, crew/lifecycle safeguards, deploying/retrieving
haulers, Ceres patrol policy, scav SOS, boarding, orbital/SFF travel and economy,
crew rest, autonomous disembark filtering, alternative work and work reach.
UI, sensor presentation, PDC mechanics, general mass/electrical fixes and pure
serialization/performance patches are outside the behavioral review except at
shared boundaries. This is not a line-by-line certification of every mod method.

The compiled Debug DLL has **165 HarmonyPatch-decorated types**:

| Namespace/group | Patch classes |
| --- | ---: |
| MiscBugFixes | 54 |
| Optimizations | 22 |
| Orbital, including economy | 17 |
| AlternativeWork | 13 |
| Venus | 13 |
| ShipNavigation | 8 |
| Inspection | 8 |
| QoL | 5 |
| CeresNavigation | 4 |
| Haulers | 4 |
| Micrometeoroids | 4 |
| NavConsole | 4 |
| PdaSettings | 2 |
| Other root/nested patches, diagnostics and ShipboardAction | 7 |

These are metadata declarations, **not installed-hook or native-method counts**.
Some classes have multiple callbacks/targets; some are conditionally installed;
temporary manually installed diagnostics are a separate population. Runtime call
frequency and body cost were not measured here. Inventory is reproducible with
[PatchInventory](../Tools/PatchInventory/README.md), which does not load the plugin.

## Findings requiring action

### A1 — Return intent bypasses combat and queued decisions

**Priority: first. Confidence: reproduced control-flow defect. Keep the return
intent/persistence repair, refactor how it participates in scheduling.**

[StationReturn.Run](../Navigation/StationReturn.cs) directly executes FlyTo/Dock
and its RunAI prefix skips the original method while the return owns execution.
YieldToOtherControl checks manual control, connected player ships and
`_activeReaction`. That field is not the native combat/priority selector.

Native `Ostranauts.Ships.AIPilots/AIShip.RunAI` executes `_activeReaction` **or**
`_behaviorTree.RunCommand`. `NavyPilot` puts PriorityQueue, CombatResponse,
ThreatResponse and SupportResponse before its ordinary Main behavior.
`AmIInCombat.RunCommand` tests Blackboard `InCombat`.

The new `audit-priority` mode in
[CeresAuditChecks](../Tests/AiPlannerDiagnosis/CeresAuditChecks.cs) reproduces:
pending return + `InCombat=true` + executable queued work + no active reaction
still runs the return flight and never enters the behavior-tree probe. Native
combat eligibility runs for real; tree and physical flight outcomes are fixture
boundaries. This proves scheduling bypass, not the outcome of an actual attack.

This matters to both Ceres returns and boarding survivors/prizes. Merely retaining
queued commands is not equivalent to allowing them to execute. Existing tests
checked retention and explicit reactions, but did not reject this starvation.

Recommended direction: retain serialized return intent but integrate it below
native urgent/combat/threat decisions, at an ordinary mission boundary. Verify
each pilot's real tree; do not globally prepend a return or blindly replace a
Main node. Preserve resume-after-interruption and save/load stage semantics.
Pilot tree construction is C#, so this repair cannot be expressed by changing
the existing dialogue/pledge assets alone. Whether a fuel-critical ship chooses
to disengage is policy; disabling all of its defensive decisions is not required.

Required cases: incoming attack with return pending; queued clearance/surrender
commands; quiet return; interruption/resume; save/reload; player takeover; towing;
failed docking. No immediate hook deletion is recommended.

Also inspect request-time side effects: Request calls Bind immediately, before
Run's YieldToOtherControl check. A scheduling fix must not leave an early target
overwrite that interferes with an active reaction. This is a source-identified
composition concern, not an additional live failure attributed here.

### A2 — VORB anchoring permission belongs in authored data

**Priority: next asset migration candidate. Confidence: direct code/asset match.**

Native `AnchorDockedShip.RunCommand` already evaluates `TIsAllowsAnchoring` on
the origin station and unlocks the dropped hull when it fails. The trigger
requires `StationAllowsAnchoring`. In `star_systems/star_system.json`,
`NewGame.aSpawnStations[OKLG].aStartingConds` includes that condition; VORB's
corresponding list does not. Our
[VorbAllowsAnchoringPatch](../Venus/VenusDerelictFixes.cs) adds it from a command
prefix. This is a data omission, with a working C# evaluator.

Prefer a native asset-mod definition for that permission. **Do not remove the
runtime correction on that basis alone:** startup station conditions and saved
station COs have different lifetimes. Establish how existing saves acquire the
condition, then narrow any necessary compatibility repair to initialization or
an explicit migration, rather than every dropoff.

There is also a mod-conflict cost: `DataHandler.LoadModJsons` loads the keyed
`JsonStarSystemSave` definition, and `JsonToData` replaces matching `strName`
entries. A file containing a partial `NewGame` is not a demonstrated nested-array
patch to VORB alone. Do not ship a truncated system or casually override the full
system definition. Prove a suitably narrow native asset route first. The PDA app
adds its own keyed records; changing an existing nested station record differs.

### A3 — Sleep exclusion is asset-shaped, but a global edit changes scope

**Priority: design candidate, not an approved removal. Confidence: direct assets
plus existing scoped behavior tests.**

`TIsBed`/`TIsBedFree` require `IsCushion` and `IsSheet`, permitting containers and
suits with those labels. Adding `IsContainer`/`IsEVASuit` to their forbids would
exclude those targets through the native evaluator. Comfortable/medical bed
triggers also inherit `TIsBed`.

But [CrewSleepPatch](../MiscBugFixes/CrewSleepPatch.cs) intentionally changes
**player-crew autonomous choice only**, preserving manual sleep and other NPCs.
The target-only trigger cannot distinguish who chose the action or whether it
was manual. A global forbid is not equivalent and can remove intentional
improvised sleeping surfaces for other characters.

Potential native asset design: separate autonomous sleep interactions/eligibility
from manual actions. First audit interaction provisioning, learned interaction
history and old-save availability. This is a larger migration than adding two
forbids; do not replace three scoped hooks with broader gameplay by accident.
Keep current behavior until that equivalence is established or broader policy
is explicitly desired.

### A4 — Ceres guardrails are an enhancement layered on general repairs

**Priority: after A1. Confidence: source-level policy identification; no fresh
live Ceres validation.**

[CeresNavigationGuardrails](../CeresNavigation/CeresNavigationGuardrails.cs) adds
20% outbound/home reserve, faction-home choice, planned-route reserve rejection
and periodic return decisions. Its 30-game-second monitor covers short-range
chasing as well as navigation. Those are deliberate risk-aversion policies, not
merely restoration of vanilla fuel checks. Native LowFuel/LowFuelRetreat use
different thresholds. General Navy candidate validation, target identity and
accounting repairs should remain independent of this policy.

Do not infer that correcting fuel corruption makes reserve policy redundant:
an honest fuel budget can still fund an outbound pursuit without enough fuel to
come home. Conversely, do not claim every reserve failure is a corrupt route.
The existing policy should be documented/tested as patrol decision-making, with
combat interruption preserved. No existing reviewed JsonShipSpec field supplies
this predicted route-plus-home budget, so moving a few constants to a trigger
would not transfer ownership of the calculation to native assets.

### A5 — VORB route guard is containment, not proof the cause is gone

**Priority: reduce only after source-level route replay. Confidence: source-level
scope/overlap; no new failing planner trajectory reproduced here.**

[VorbNavPlanGuard](../Venus/VorbNavPlanGuard.cs) rejects bad plans, pins manual
fallback until destination changes, guards TimeAdvance and rebases refills. Some
historical reasons overlap newer general fixes: stale target references,
departure waypoint identity and mixed-mode fuel accounting. The current code
still has finite-value, chronology, expired-route and predicted-fuel checks.

Recommendation: replay old rejected routes with the general fixes and record
which guard reasons still occur. Remove only redundant branches demonstrated
unnecessary; preserve needed refill accounting and independent numerical checks.
Do not turn its broad 100 AU / 1e9-second envelopes into universal AI travel
limits. Manual fallback itself changes navigation strategy and deserves its own
regression cases. JSON does not repair invalid NavData arithmetic or references.

## Disposition of the remaining behavioral families

“Keep” means the implementation layer is appropriate, not that every edge case
has been newly proven in this audit. Referenced module tests/docs supply prior
evidence; fresh runs are listed separately below.

| Family / implementation | Disposition | Basis and next action |
| --- | --- | --- |
| [Shallow RefuelRCS](../RefuelRCSPatch.cs) | Keep C# defect repair | Native shallow branch writes mass instead of stored gas; JSON cannot repair the setter or overflow result. Preserve TakeFuel's return contract. |
| [Mixed flight accounting](../MiscBugFixes/AIFlightFuelHandoff.cs) | Keep C# defect repair | Native planned absolute fuel and manual progress ownership cross incorrectly. Five hooks cover ownership, physical spending and persistence; count alone does not justify removing any. |
| [Target replacement](../MiscBugFixes/ShipReplacementTargetsPatch.cs) | Keep C# defect repair | DestroyAndReload replaces objects while observers retain old references. Changing target-selection assets cannot make references live again. |
| [Invalid delta-v and target prediction](../Navigation/NavigationFuel.cs) | Keep C# integrity | Normalize unusable NaN budgets and invalidate obsolete TripFuelPrediction on semantic retargeting. Keep distinct from “provide free fuel.” |
| [Departure waypoint](../Navigation/DepartureWaypointPatch.cs) | Keep C# defect repair | Native short departure waypoint chooses a body reference in code; station-relative construction belongs at that calculation, not faction policy. |
| [Navy candidate filter](../Navigation/NavyCandidateFuelPatch.cs) | Keep general core; separate policy | Native single-candidate and all-rejected fallbacks can return unaffordable candidates. General preselection filtering repairs that; Ceres round-trip margin is additional policy. |
| [Instant docking / reserve hull](../MiscBugFixes/AIShipLifecyclePatches.cs) | Keep C# defect repair | Native instant docking lacks normal attached-state guard; hidden-hull reuse must not steal connected ships. These are topology/lifecycle invariants. |
| CrewedAIControl in same file | Keep containment; verify role exceptions | Native continued thrust after pilot loss is invalid, but the police inspection regression showed that “not aboard” can be legitimate. Keep inspection exception and role-specific tests; do not broaden into deleting ships. |
| [Deployer release/idle recovery](../Haulers/HaulerReturnPatches.cs) | Keep event repair; candidate to simplify idle fallback | Empty-haul cleanup discards target and can leave abandoned maneuver. Event handoff asks native pilot for its home target. Idle RunAI postfix covers legacy bad states; retire only after proving release/failure paths fully cover new states and deciding compatibility for existing saves. |
| [Retriever SFF handoff](../Haulers/HaulerRetrieverSffPatch.cs) | Keep C# defect repair | Out-of-band pickup queues one-shot HaulShip but leaves Main on outbound FlyTo/Dock. Patch corrects the exact native stage, not new behavior policy. |
| [VORB retriever depot](../Venus/VorbRetrievalDepotPatch.cs) | Keep C# regional identity correction | GetTarget hardcodes nearest regional desk, whereas VORB's wreck owner/depot differs from VNCA. Preserve native recent assignments and candidate filtering. Changing wreck ownership to fit the wrong desk would mask the mismatch. |
| [VORB undock relocking](../Venus/VorbUndockAnchorPatch.cs) | Keep C# lookup repair | Native Undock excludes placeholder orbital references. The patch includes VORB references for eligible derelicts while retaining native anchoring thresholds. This is separate from A2's permission. |
| Venus wreck mix in VenusDerelictFixes | Keep explicit enhancement | SpawnDerelict hardcodes RandomShip in C#. Station-specific configurable selection needs that seam; editing global RandomShip would also affect other depots. Tables may define pool contents, but do not assume a station-specific selector already exists. |
| [Scav emergency recovery](../MiscBugFixes/ScavEmergencyRecovery.cs) | Keep C# reachability/lifecycle repair; minimize copied policy later | Main/Hold, one-leg fallback and RequestHelp lifecycle are C#; native JSON still owns SOS gates, replies and dispatch. Prefix repeats native routing, which creates update-maintenance risk; prefer a narrower seam if equivalent behavior can be demonstrated. No new rescue FSM. |
| [StationReturn and legacy queue migration](../Navigation/StationReturn.cs) | Refactor scheduling, retain persistence repair | Native queue treats a successful child as command completion; custom queued trees also cannot reload natively. Returning needs stage/intent persistence, but does not require bypassing urgent native decisions (A1). Legacy payload migration is compatibility, not patrol policy. |
| [Shallow boarding](../ShipboardAction/README.md) | Keep as explicit simulation enhancement | Casualties, mutual faction hostility, prize crews and forced post-fight separation are authored mod policy. Native pilot trees do not supply an equivalent shallow boarding resolver. Asset-driven parameters are possible; replacing the simulation with existing pledges is not established. Uses A1's return scheduler. |
| [Orbital parent gravity/coasting](../Orbital/README.md) | Keep numerical fix/integrator distinction | Parent-frame acceleration omission is a C# force-law defect; coasting integration adds a numerical method. Neither is a trigger/pledge issue. Continue separating gravity ownership from AI steering. |
| Orbital SFF motion in same module | Keep explicit simulation enhancement plus guards | Bounded partial travel and arrival gating replace a coarse native skip. User-approved fuel abstraction is intentional, not a workaround to move into SOS JSON. Must preserve combat/player torch/loaded-course exclusions. |
| [Economic SFF timeline](../Orbital/Economy/README.md) | Keep scheduling repair/enhancement | C# advances epoch once, losing dispatch opportunities and interleaving production incorrectly. Recipes/trade definitions remain asset-owned; JSON cannot schedule intermediate events on its own. Historical planner scope and arrival ownership need composition checks. |
| CargoLoadingPatch in economy module | Keep small C# conservation fix | Successful loading retains a remainder that is restored at origin. An asset recipe adjustment would hide duplication. |
| [AlternativeWork](../AlternativeWork/README.md) | Keep optional scheduling enhancement | Native task collection/claims/path/material execution are reused; ranking, incremental preparation and rolling supply window intentionally change scheduling. Preserve 1.0.1.10's background jobs and urgent AI scheduling. A duty/pledge tweak cannot implement incremental path/material planning. |
| [WorkRangePathPatch](../MiscBugFixes/WorkRangePathPatch.cs) | Keep C# geometry repair | In-range result should describe the valid standing tile, not walking into the work target wall. Do not compensate by changing every work interaction's reach. |
| [AutonomousDisembarkPatch](../Optimizations/AutonomousDisembarkPatch.cs) | Keep C# search optimization | Native permission checks can reject a final action but only after broad target discovery. Early local filtering avoids pointless searches; globally forbidding interactions in JSON would also affect manual/remote/emergency uses. |
| [Idle seat renewal](../MiscBugFixes/CrewIdleSeatPatch.cs) | Keep bridge, investigate narrower availability signal | Assets explicitly include ACTChairSitAllow as its own reply. GetReply can renew without returning to GetWork/ProcessAutoTasks. Removing that reply or ending every work-shift seat changes idle behavior; JSON labels alone do not establish reachable, tool-eligible restore work. Current probe duplicates native ranking and must track native updates. |
| Manual seating provenance in same class | Keep C# provenance repair | Native reply assignment does not propagate bManual; JSON cannot reconstruct that invocation-specific provenance. Needed to honor explicit “stay seated” orders. |

## Shared-hook composition and simplification opportunities

1. **AIShip.RunAI:** Ceres reserve prefix, StationReturn prefix, crew-readiness
   prefix and hauler idle postfix share a native method. Review execution order
   and prefix-suppression behavior as a group. A1 has a reproduced scheduling
   bypass; other ordering outcomes require combined-hook tests, not guesses.
2. **TargetData.ApplyToShip:** both general prediction invalidation and VORB
   target-change handling construct semantic NavigationTargetKey values in
   prefixes/postfixes. A shared invalidation seam could reduce duplicate work,
   but VORB fallback reset and global fuel invalidation have different effects.
3. **Fuel additions:** shallow RefuelRCS directly notifies the VORB helper;
   materialized RefuelRCS and AIRefuel use separate observers; explicit refuel
   API snapshots/rebases route levels too. Existing tests guard double rebasing.
   Consolidate ownership before removing observers, not merely by sharing a name.
4. **AIShip.FFWD:** crew readiness, station return, SFF motion and economic freight
   ownership all participate. Preserve exactly one owner of movement/catch-up
   while still permitting legitimate native completion. Validate the combination.
5. **Scav GenerateNavData:** its null-target safety prefix and Ceres reserve
   postfix have distinct scope; a null home is not an unaffordable planned route.
6. **Compatibility hooks:** legacy Ceres/boarding queue migration protects old
   saves. Moving a current policy into JSON does not make saved intent/payloads
   disappear. Explicitly decide migration lifetime before deleting the hook.

No hook is recommended for immediate unconditional deletion from this review.
That is not a recommendation to retain the present architecture indefinitely.
The demonstrated priority conflict should be fixed before doing cosmetic hook
consolidation. Do not target a numeric reduction quota.

## Fresh validation and evidence limits

- Metadata inventory: 165 decorated patch types, no plugin execution.
- `Tests/AiPlannerDiagnosis`, bundled Mono, `audit-priority`: 79 existing repair
  regressions plus two assertions reproducing the current priority defect passed.
  The defect assertions intentionally describe bad current behavior; they must
  be replaced by desired-behavior regressions when A1 is repaired.
- `Tests/CrewRestCheck`: 599 checks passed.
- `Tests/AutonomousDisembarkCheck`: 67 checks passed.
- `Tests/AIShipLifecycleCheck`, `patched`: native defects reproduced, 52 corrected
  lifecycle checks passed.
- Behavior-graph validation: 49 retriever and 79 scav checks passed. These models
  cover declared slices, not the full return-policy/combat composition.
- Read-only native source review used 1.0.1.10. Exact method names above refer to
  decompiled sources; their line numbers are not original developer line numbers.
- Asset checks used the installed vanilla JSON: TIsBed family, chair reply chain,
  TIsAllowsAnchoring and NewGame station starting conditions. No assets edited.
- Prior live rescue/pilot survival is recorded in
  [LIVE-RECOVERY.md](../Tests/ScavFuelDiagnosis/LIVE-RECOVERY.md). Brody's later
  successful station return is useful evidence for that path, not proof of combat
  response while StationReturn owns execution.

This audit does not establish that guard rejection never happens now, that all
restoration availability can be represented in JSON, or that native starting
conditions retrofit saved stations. Those are explicit gates on later removals.

## Recommended sequence

1. Repair A1 with native-priority-first return scheduling and mixed-hook tests.
2. Prove a narrow native asset/migration path for A2, then relocate that policy.
3. Replay VORB rejection cases under the general fixes; prune demonstrated overlap.
4. Decide whether A3's player-only/manual-preserving scope should remain, then
   design an equivalent asset split if practical.
5. Extend the graph to Ceres/boarding returns and native priority interruptions;
   distinguish authored risk policy from engine defects in docs/settings.

Keep this report as the recommendation baseline. Gameplay changes require the
next implementation step; this audit did not silently start those refactors.
