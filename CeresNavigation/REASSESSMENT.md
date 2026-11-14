# Ceres navigation reassessment: 2026-11-13

Status: pre-refactor offline audit and reproduced findings. The subsequent
[implemented refactor](REFACTOR.md) addresses these findings, adds a native retreat
failure-progression repair, and records current regression coverage. The body
below preserves the original diagnosis and recommendations as historical evidence.
Boarding resolution is outside this review. No live ship orders, pruning, fuel
changes or other interventions were issued.

The objective is affordable patrols, opportunistic fighting and credible retreats,
without suppressing encounters to conceal broken navigation. Keep execution
defects separate from intentional fuel-reserve policy. The old distant cluster
cannot now be attributed retrospectively to one defect.

## Verdict

Keep the general integrity repairs. The original Ceres module has already had
its broken return progression, persistence and priority scheduling repaired.
However, the current combination is both too conservative at patrol admission
and insufficient to enforce a home reserve during combat. Another general native
fuel-budget inconsistency contributes to premature rejection of useful travel.

Do not tighten a distance leash or increase fuel margins. Address the shared
arithmetic, let native selection try affordable alternatives, and express any
combat reserve policy through native retreat decisions rather than overriding
the pilot tree. Complete the separately diagnosed PASS route-completion repair;
Ceres policy cannot make a double-integrated trajectory safe.

## Native behavior and assets

Inspected the installed 1.0.1.10 C# and the matching asset JSON:

- `star_systems/star_system.json`: BCER/GalileanConfederacy and BCRS/CCRE each
  specify `StationMaxFactionNavy=20` and `StationInfluenceRange=11500` km.
  COHO/BeltPirates specifies 10 and 3000 km. These describe home-centered random
  patrol candidate generation, not a universal pursuit boundary.
- `loot/loot.json`: `RandomNavyShipCCRE` and
  `RandomNavyShipGalileanConfederacy` each weight Vector3 refits 65%, Pequod
  refits 25%, and their larger faction design 10%. Belt Pirates weight Vector3
  and Babak refits 45% each, Pequod 10%.
- `personspecs/personspecs.json`: `CombatPilotCCRE`,
  `CombatPilotGalileanConfederacy`, and `CombatPilotBeltPirates` supply the
  corresponding combat-crew affiliations, equipment/condition loot and native
  personal behavior. A Belt Pirates faction vessel is not necessarily a
  `PiratePilot`: the station's faction-navy population uses `NavyPilot` too.
- `NavyPilot` owns patrol selection and ordered priority/combat/threat/support/
  ordinary-mission branches in C#. Pirate and Police trees have distinct ordinary
  missions. No examined JSON entry defines the Ceres mod's 20% homeward policy,
  its rejected-plan latch, or the missing skill factor in delta-v arithmetic.

This audit supports C# repairs for the identified execution/arithmetic defects.
The reserve remains an explicitly added simulation policy. Preserve the authored
factions, ship mixes, crew generation and encounter evaluation.

## Chain review

| Stage | Native mechanism and current integration | Assessment |
| --- | --- | --- |
| Population and home | Station caps/ship loot; `NavyPilot.CreateAIShip` records origin/home. Ceres `FindHome` prefers its available same-owner assigned home, then a same-owner station. | Preserve. No evidence for reducing population, patrol influence or hostile encounter frequency. |
| Patrol candidates | `NavyPilot.GetTarget` merges POIs and random influence points. Native single-candidate and all-rejected branches can bypass affordability. `NavyCandidateFuelPatch` filters that merge. | General fuel-bypass repair remains justified. Ceres adds a separate heuristic round-trip filter, which can reject a funded plan; see R3. |
| Candidate planning | `TargetNewIfNone.FindTarget` makes up to nine attempts. Locked targets get native generated trajectories; other targets get a round-trip heuristic via `GetNeededFuel`. | Preserve native planning/retry machinery. Our rejected-plan latch currently redirects the next draw home before alternatives can be planned; see R1. |
| Planned and manual flight | Native `NavData` fuel/trajectory playback versus `FlyTo` progress accounting. Shared target replacement/clearing, refill reconciliation and departure-frame patches handle specific corruption boundaries. | Keep cause-level repairs. Ceres should not reinterpret corrupt trajectories as evidence of reckless intentional travel. |
| Threat and combat entry | `AreThreatsNearby` uses visible signatures, faction relationships and a 2500 km relative-distance ceiling; `InitiateAgainstBiggestThreat` sets combat targets. Native Navy also responds to friendly emergency signals. | Preserve encounters and reinforcements. EM propagation can affect visibility, but the existing relative encounter ceiling is still present; no new faction/home leash is warranted. |
| Pursuit and tactical retreat | Short range uses `KeepWithinFiringDistance.KeepRange`; long range uses `KeepAtDistance` with FlyTo/Dock. Both consult native `LowFuelRetreat` first. | Pending ordinary-mission return is not a tactical retreat. Both combat branches can defer it; see R2. |
| Fuel budget | `Ship.Maneuver`, `CalculateRCSFuelConsumption`, native planned flight and `DeltaVRemainingRCS`. | General remaining-delta-v calculation omits the skill efficiency factor used for consumption; see R4. Shallow noncombat movement intentionally skips direct maneuver fuel charges and uses route/progress accounting. Do not replace that abstraction indiscriminately. |
| Return and docking | Shared `StationReturn` dispatches native FlyTo/Dock only at the ordinary Main branch, after native priorities. Intent/stage survive Blackboard save/load. | Keep progression/persistence repairs and correct priority ordering. Do not restore the former RunAI override to force a combat retreat. |
| Connected groups | Ceres guard does not steer docked groups; shared return waits while connected. Native mission/boarding/undocking behavior remains responsible. | Preserve. This audit does not certify boarding resolution or substitute an automatic undock policy. |
| SFF and PASS | Existing orbital bulk propagation excludes active native routes; native NavData remains responsible for those. SFF destination identity is repaired separately. PASS does not enter the GUI SFF context. | Shared time-advance bugs remain relevant regardless of Ceres guardrails. See the independently reproduced PASS launch below. |
| Region/load lifecycle | General target restoration, crew/controller integrity and stale-timer repairs preserve identity and ownership at specific boundaries. Native retirement/crew transfer remain. | Keep. No new Ceres-specific cleanup controller is justified by this review. |
| Historical cleanup | `pruneceres` explicitly destroys eligible stranded or >200000 km-from-home ships, subject to crew/player protections. | User-invoked recovery policy, not navigation prevention or proof of an impossible journey. Never make this automatic as a substitute for simulation repairs. |

## Fresh reproductions

`Tests/AiPlannerDiagnosis` now builds against current source again. Its wildcard
had pulled unrelated newer ScavengerReturn/OrbitalWaiting integration into a host
without those integrations' dependencies. The project now excludes those two
files; no production module or runtime hook was removed.

Run the new mode through the bundled Mono host:

```powershell
dotnet build Tests/AiPlannerDiagnosis -c Release --nologo
dotnet run --project Tests/SaveJsonCompatibility/Host -c Release --no-build -- Tests/AiPlannerDiagnosis/bin/Release/netstandard2.1/AiPlannerDiagnosis.dll ceres-chain
```

104 assertions pass. This includes reproduction assertions retaining current
undesirable outcomes, not 104 claims that gameplay is fixed. The native candidate
retry loop, production reserve hooks, AI/selector dispatch, native low-fuel test,
Blackboard/queue persistence and fuel arithmetic execute. Candidate order,
generated trajectory points, combat action bodies, world lookups and physical
flight/docking outcomes are declared fixture boundaries.

### R1: one rejected plan prematurely ends alternative selection

`PlannedReservePatch` rejects a generated plan and writes
`CeresNavigation.ReturnAfterRejectedPlan`. `PatrolHomePatch` then replaces the
next candidate with home. Replaying native `FindTarget` with an expensive first
visit and an affordable second visit produces exactly one expensive plan and one
home plan; the affordable second visit is never planned. The same production
reserve check independently accepts the affordable trajectory.

This can reasonably reduce activity without helping navigation correctness.
Recommendation: use native bounded retries for rejected candidates. Home should
be the fallback when alternatives are exhausted or the native low-fuel decision
requires it, not the mandatory next candidate after one bad plan. The current
postfix cannot implement an exhaustion-aware fallback by itself; repair at the
selection/retry boundary instead of adding another recurring controller.

### R2: a pending reserve return cannot end an active pursuit

The reproduced vessel has 75% tank fill and delta-v above native `4 * maxSpeed`,
but fails the native homeward BingoFuelCheck because of relative motion.
Native `LowFuelRetreat.RunCommand` returns Failure. The Ceres guard records a
return; 100 subsequent AI dispatches, 30 seconds apart, run the higher-priority
combat branch without executing a return flight. Queued/threat/support branches
can also legitimately defer ordinary return; the existing priority regression
checks confirm their ordering.

Preserving native priority was correct. The mistake is describing a pending Main
directive as enforcement of a tactical homeward reserve. Recommendation: if we
retain that policy, feed its affordability result into native `LowFuelRetreat`'s
entry decision for the intended faction/roles. Keep weapons, combat state and
native retreat execution authoritative. This is a policy refinement, not grounds
to suppress combat globally. Retreat destination selection also needs coverage:
native `TargetNearestStation` normally checks reputation/affordability but its
empty-list fallback chooses the nearest station without those filters. Do not
assume it necessarily chooses the Ceres guard's same-faction home.

### R3: early maximum-cruise heuristic can reject a funded short hop

`LacksRoundTripReserve` estimates two legs using maximum cruise speed. For
co-moving endpoints it requires `1.2 * 4 * maxSpeed`, independently of distance.
The later `PlanHasReserve` instead knows the outbound trajectory's remaining
fuel. A native fuel-arithmetic control accepts a supplied short-hop trajectory
plus the same homeward estimate and 20% margin, while the earlier candidate
filter rejects it.

This is a demonstrated disagreement, not a claim that a specific live patrol
was suppressed. Recommendation: retain the general native affordability repair
but avoid using the extra Ceres heuristic as an authoritative rejection before
an available native plan. Apply reserve once to that completed plan. Manual/
unlocked goals still need their native heuristic path; do not remove their budget
checks or run a second expensive planner for every patrol candidate.

### R4: remaining RCS delta-v ignores native pilot efficiency

`Ship.UpdateCrewSkills` chooses a fuel multiplier of 1 or 0.75 for
`SkillOpsSpaceship`. Consumption is:

`deltaV * thrusterCount * 0.728 / acceleration * efficiency`.

But the advertised remaining delta-v is:

`acceleration * fuel / 0.728 / thrusterCount`.

With 300 kg, native consumption of the entire advertised delta-v costs 300 kg
at multiplier 1 and only 225 kg at 0.75. Dividing advertised delta-v by the
efficiency multiplier produces a budget that actually costs 300 kg in both
native arithmetic controls. The skilled budget is understated by 25% of its
usable value (correcting it raises the displayed budget by one third).

This affects native LowFuel/BingoFuel checks and Ceres heuristics, and can also
affect other factions, recovery eligibility and the player's displayed delta-v.
It does not prove previous stranding was caused by this conservative error.
Recommendation: correct the shared getter as the inverse of native consumption,
retaining the existing invalid-capability normalization. Test normal/skilled,
full/shallow, empty/damaged capability and downstream threshold cases before
shipping. This belongs with general navigation bug repairs, not Ceres policy.

## Shared time-skip failure, independently reproduced

The [PASS investigation](../Orbital/PASS-TIME-SKIPS.md) replayed O-RRK's saved
route through native code without Ostrabus production patches. A completed route
fell through to a second full-interval integration with its last braking thrust:
42.6735 km/s unwanted velocity versus 42.6852 km/s observed after the shuttle.
The same mechanism can affect any pilot using that playback path. Fix the shared
completion handoff, not a Ceres distance/speed watchdog. The broader PASS/SFF
integration proposal remains separate from that minimum repair.

## Graph coverage and validation limits

The current traffic graph validates 410 model expectations, but its exported
constructor trees cover cargo/civilian roles. It is not an executable model of
all Ceres combat/retreat transitions. The existing Ceres host instantiates actual
Navy/Pirate/Police constructors and verifies return branch binding; that does not
make its synthetic combat action bodies a full combat simulation.

This review additionally reran 64 native traffic-role checks and 272 shared
target-lifecycle checks. All passed. The host retains its existing unassigned
settings-field build warning; the lifecycle host emits expected Unity internal-
call diagnostics at fixture boundaries. There was no Ceres live flight test.
These checks do not establish the frequency of premature returns or reconstruct
the original three-faction cluster.

## Recommended implementation order

1. Repair general RCS budget arithmetic and the independently diagnosed route
   completion handoff. Keep these usable without Ceres simulation policy.
2. Replace the first-failure home latch with native bounded alternative attempts;
   eliminate the extra pre-plan reserve veto where a native plan can decide.
3. Integrate the agreed home reserve with native tactical retreat eligibility,
   checking destination friendliness and avoiding a parallel combat controller.
4. Expand graph coverage around those actual decisions before claiming whole-
   chain behavioral validation. Preserve native engagement, surrender, emergency
   support, player control and legitimate long-distance torch flight.

Do not change the 20% margin, authored patrol influence, faction hostility or
population caps merely to compensate for these defects. There is also packaging
coupling to address: `NavyCandidateFuelPatch` currently ships only with the
`ceres-navigation` simulation feature even though its basic single/all-rejected
fuel bypass correction is general. Separate the bug repair from opt-in Ceres
reserve policy when refactoring that boundary.
