# Conditional candidate investigation — 2026-11-09

The [continued scope audit](AI-REGIONAL-SCOPE-AUDIT.md) extends this review. The
independent duties below do not justify VORB-only scope: native cross-role tests
now demonstrate wider accounting gaps and a duplicate empty-home shortcut.
Those follow-ups are now [implemented and regression-tested](../Navigation/RCS-REPLENISHMENT.md);
the analysis below is retained as the evidence leading to that decision.

## Sleep eligibility: retain the scoped hooks

Native assets share target predicates between autonomous and manually invoked
sleep: SeekSleepSimple uses TIsBedFree, SeekSleepSimpleLieDown uses TIsBed, and
comfortable/medical continuations use their derived bed triggers. The opener is
not human-only. CondTrigger.Triggered receives the target CO; its bed-label
requirements do not include the requesting actor or manual invocation provenance.

The new CrewRestCheck experiment clones the actual vanilla TIsBedFree asset,
adds IsContainer/IsEVASuit forbids in memory and runs the shipped CondTrigger
evaluator against controlled cushion/sheet targets. Vanilla accepts them and
the stricter asset rejects them. That result is the same for a manual request:
the trigger has no caller argument. Five additional assertions bring the rest
suite to 604 passing checks. Parent validation is a fixture boundary; target
condition evaluation is native. No installed assets are changed.

A global bed-asset edit therefore cannot replace the current hooks while
preserving their player-autonomous-only policy, manual sleep and other NPCs.
An asset split would need separate autonomous actions, eligibility, continuation
chains and provisioning to learned interaction histories in existing saves.
Native GetMove2 chooses from learned Condition history and rejects human-only
actions, so simply adding another interaction definition does not guarantee its
availability or suppress the old shared autonomous option.

Verdict: **no behavior-equivalent drop-in asset replacement established**. Keep
the current scoped filters. An intentional redesign of who may sleep on suits
and packs is a separate policy decision, not a hook-count optimization.

## VORB route containment: retain pending evidence of redundancy

Native TargetData.ApplyToShip assigns four target fields; it does not validate
NavData, rebase remaining fuel, clear manual fallback or repair old route data.
The general target replacement patch fixes references. The general retarget
patch invalidates TripFuelPrediction. Neither owns every duty of VorbNavPlanGuard.

Fresh offline results:

- The four retained OKLG runaway captures still reproduce native unreferenced
  target convergence of approximately 7.6e18 to 1.0e21 seconds. Referenced moving
  target controls produce 2,241 to 58,057 seconds. These are the retained general
  target-defect fixtures, not original VORB planner rejection captures.
- Lifecycle tests execute native station replacement with production reference
  rebinding. Healthy VORB routes survive equivalent target reconstruction, while
  real station/offset changes clear their old route/fallback appropriately.
- Four **injected** plan defects (negative RCS, expired route, non-finite position,
  reversed chronology) are rejected and latch manual fallback. These checks prove
  guard behavior, not that today's native planner naturally generates each input.
- Adding 20 kg to the route budget shifts 100/50 kg endpoints to 120/70 kg,
  preserving the 50 kg planned expenditure. General target-reference repair
  does not provide this accounting.

The native lifecycle suite now passes 143 assertions (nine new guard/rebasing
checks). Expected guard warnings are suppressed only for the named injected
fixture; unexpected production warnings still fail the harness. Unity engine
internal-call warnings from offline presentation stubs are not live game errors.

We lack retained pre-fix VORB rejected-plan payloads sufficient to replay every
old reason under all later general fixes. It would be unsound to conclude that
the fallback/guards are now redundant from the successful OKLG controls alone.

Verdict: **keep the VORB route guard and fuel rebasing**. No safe unconditional
removal follows from this investigation. If future normal-play diagnostics
capture a rejection, retain its full planner inputs and route and trace its
producer before adding more containment. Do not ask for repeated reloads just
to manufacture a historical failure.

## Other decisions

VORB anchoring remains the existing hook, per the user's explicit decision;
asset migration is deferred. The separately reproduced StationReturn scheduling
defect is repaired in [RETURN-PRIORITY.md](../Navigation/RETURN-PRIORITY.md).
