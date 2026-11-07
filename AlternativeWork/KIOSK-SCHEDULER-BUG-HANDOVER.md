# Kiosk interaction and station NPC stall — investigation hand-over

Date: 2026-11-04. Game: Ostranauts. Mod: Ostrabus, Debug configuration.

## Problem and scope

Ayden Kelly intermittently becomes stuck after a manual order to use a station
kiosk. He displays the Use animation, but the kiosk window does not open and
subsequent move orders do not execute. The issue has occurred at both the VORB
refueling kiosk and the adjacent ferry kiosk. Other NPC interactions on the
station also stall while world time continues advancing.

The problem was noticed after introducing AlternativeWork, the alternate
work-order selection and travel-cost planner. Causation has **not** been
established. Disabling AlternativeWork does not clear an existing stall, and
multiple kiosk attempts succeeded with it enabled.

The investigation was read-only: no source changes, rebuilds, deployment,
instrumentation, or game commands were performed by the investigating agent.
The user performed the toggles, selections, and movement tests listed below.
This document is the only new artifact requested for the hand-over. Another
agent is working on a separate module; its work must not be overwritten.

## Symptoms and direct observations

- Ayden can be in manual mode (`IsAIManual=1`) when the issue occurs.
- Refueling interaction: `GUIRefuelStation`, title `Use`, target CO
  `e2ba9017-1651-4f4a-bd98-3ec4cb593c39`.
- Ferry interaction: `GUIPDAFerry`, title `Use`, target CO
  `e33eb848-d1d3-4966-84a7-8321edd6d175`.
- Stuck interactions retain `epochAdded=0` and `duration=0.00027` hours
  (approximately 0.972 seconds). The queue exists, but the normal execution-start
  timestamp is not being assigned and the duration does not decrease.
- During the first refuel observation, the same interaction persisted across
  roughly 154 game seconds. The ferry interaction likewise remained unchanged
  across about 22 game seconds.
- `TaskBusy=1` was observed on Ayden during the initial refuel stall.
- A later manual move click was accepted: a new `Walk` interaction appeared
  behind the original refueling interaction. The click was not simply lost.
- World epoch advances and `/world` reports `paused=false` during controlled
  stalled samples. This is not a complete Unity or world-simulation freeze.
- Sampled NPC study, kiosk, and sleep interaction IDs and durations also remained
  unchanged. Some updates still occurred: Mariah Bell's `WanderSoon` duration
  changed and her queue later cleared.
- After the eventual recovery, NPC processing resumed too: Margaret Gardner's
  study duration decreased, Li Bernal's kiosk duration decreased, and other NPCs
  changed interactions.
- No relevant exception was found in the inspected retained API logs. A search
  of BepInEx `LogOutput.log` for exceptions, retry-limit messages, missing GUI
  keys, and planner failures also produced no matching explanation at that time.
  Absence of a log entry does not exclude an unlogged failure.

## Completed tests

| Test | Action / conditions | Result | Interpretation |
| --- | --- | --- | --- |
| Initial recovery | User switched to the other crew member and issued movement. | User reported regaining control. | Initially suggested a scheduler/cancellation reset; exact necessary action was not isolated. |
| Altwork disabled, fresh refuel use | After recovery, user ran `altwork off` and used the refuel kiosk. | Worked normally. | Initially implicated altwork, but recovery was a confounding change. |
| Altwork re-enabled | User ran `altwork on`, then used the refuel kiosk twice. | Both attempts worked. | Enabled state alone is insufficient to reproduce. |
| Other crew autotask | User selected crew on the player vessel who could not leave, enabled their autotask, then returned to Ayden and used the kiosk. | Worked normally. | Other crew autotasking is insufficient by itself. |
| Ayden autotask transition | User enabled Ayden's autotask for about one game minute, then manually ordered kiosk use. | Worked normally. | This transition alone is insufficient. |
| Adjacent ferry kiosk | User used the ferry next to the refuel kiosk. | Stall recurred; API captured `GUIPDAFerry`. | Not specific to the refuel window implementation. |
| Broader NPC check | User noticed station NPCs appeared stuck; agent sampled their activities. | Multiple unrelated interaction durations did not advance while world epoch did. | Shared scheduling/clock progress is implicated. |
| Disable altwork while stuck | During a later refuel stall, user ran `altwork off` without first recovering. | Ayden and five sampled NPCs retained identical IDs and durations across approximately 39 game seconds. Altwork was false, active selections zero, game unpaused. | Ongoing alternative selection is not required to sustain the stall. Does not exclude an earlier altwork trigger. |
| Switch selection only | User switched from Ayden on VORB to Shan Kaitlyn Luna on `O-GXD`, without a new order. | Ayden, Margaret, Milcah, Trevor, and Li remained stalled. Mariah's `WanderSoon` changed. | Selection alone did not immediately release the original stall. |
| Move Shan | User issued Shan one move order. | Sampled stalled interactions remained unchanged. Mariah's queue cleared. | Shan's movement alone did not release the sampled blockage. |
| Return to Ayden and move once | User selected Ayden and clicked to move. | No visible movement. API showed the same kiosk interaction first and a new `Walk` second. | Order delivery works; cancellation/queue advancement did not complete. Cancellation flag was not exposed. |
| Switch away a second time | User switched back to Shan after Ayden's move was queued. | User reported Ayden immediately cancelling kiosk use and walking. Subsequent inspection found his queue empty and NPC interaction progress resumed. | Recovery correlates with switching away after a pending move/cancellation. Does not prove selection itself is the root cause. |

No controlled reproduction from a clean starting save with altwork disabled
throughout has been completed. Successful attempts while off do not rule out
intermittent base-game behavior or state left by earlier activity.

## Representative controlled capture

All values below came from generation 7 of the live inspection API. These are
observations, not a persistent historical trace. No complete raw capture file was
written during this investigation.

While altwork was off and the game unpaused:

| Character | Interaction | Duration in both samples (hours) |
| --- | --- | ---: |
| Ayden Kelly | `GUIRefuelStation` | 0.00027 |
| Margaret Gardner | `ACTStudySkillCont` | 0.0052813478054470463 |
| Mariah Bell | `WanderSoon` | 0.000028 |
| Milcah Curtis | `ACTKioskUseNPC` | 0.00027 |
| Trevor Montoya | `SeekSleepSimpleLieDown` | 0.0024145737202962243 |
| Li Bernal | `ACTKioskUseNPCAllow` | 0.0018699168395996094 |

Ayden's samples were at epochs `65631765648.51757` and
`65631765687.773087`, with interaction ID
`94653c31-947a-487b-be6a-80bcfcb7d8ca` throughout. His queued Walk later had ID
`c0ba55ff-fccc-461b-9e30-312243283b00`.

After recovery, at epoch `65631765979.962318`:

- Ayden had no queued interaction.
- Margaret retained her study interaction but its duration had fallen to
  `0.0015208552686903189` hours.
- Li retained his kiosk interaction but its duration had fallen to
  `0.00089377183702256944` hours.
- Milcah and Trevor had changed interactions.

## Suspected mechanism and confidence limits

**Leading hypothesis: starvation in the shared native character scheduler,
possibly triggered or exposed by altwork timing/state changes.** The specific
runtime blocker has not been identified.

Source snapshot root: `<decompiled-source>`.

Relevant paths:

- `Condowner.Utils/Scheduler.cs`: `Run`, `DrainPriorityCredits`,
  `ProcessEndTurnTicks`, `ProcessUrgentInteractions`, and `UpdateClocks`.
- `Condowner.Utils/EndTurnDispatcher.cs`: `Tick`, active/pending requests, and
  request completion/cancellation.
- `Condowner.Utils/Clock.cs`: local epochs depend on `Scheduler.ProcessedTime`,
  plus any parent-clock relationship and local catch-up advancement.
- `CondOwner.cs`: `UpdateManualDeferred`, `EndTurnSteps`, `AICancelAll`, and
  `QueueInteraction`.
- `CrewSim.cs`: scheduler invocation and crew-switch lifecycle.

There is a concrete starvation path in the scheduler:

1. `Scheduler.Run` processes deferred end-turn work, urgent work, and priority
   clock catch-up before ordinary ticking.
2. When `DrainPriorityCredits()` returns false, `Run` records skipped priority
   time and returns before `UpdateTickerOrder` and normal shared-clock advancement.
3. `DrainPriorityCredits` can return false when debt remains after bounded work
   or when deferred/urgent processing consumes the available work slice.
4. `CrewSim` still proceeds to the world-system update after `Scheduler.Run`
   returns. World time can therefore advance while ordinary character clocks
   and interaction execution fail to progress.

This control flow matches the observed world/character split, but the live debt
dictionary, dispatcher owner, and exit branch were **not observed**. Other early
returns around deferred/urgent processing could produce similar symptoms. Do not
describe the priority-debt branch as the confirmed cause of this incident.

In `CondOwner`, a new manual order calls cancellation machinery and then queues
work; cancellation cleanup itself needs scheduling. A pending kiosk followed by
Walk is consistent with cleanup not running. The current API does not report
`bCancel`, so cancellation-request state is not confirmed directly.

The Use animation does not prove the interaction reached its execution/UI stage:
`RefreshAnim` can select the animation from the first queued interaction.
`EndTurnSteps` normally assigns `fEpochAdded` before opening its UI and reducing
duration. Ayden's zero timestamp is therefore meaningful evidence of missing
execution progress, rather than proof of a window-opening exception.

Crew switching is not a clean priority-only test: `CycleCrewPart2` may perform
ship loading, catch-up, and UI lifecycle work. Both VORB and `O-GXD` were Full in
a later inspection, but transient load states during each switch were not captured.
The earlier characterization as definitely selection-dependent was too strong.

## AlternativeWork review

- `Eligible` includes company crew in manual mode; autotask off is not a blanket
  bypass of every module operation.
- `WorkSelectionPatch` wraps `ClaimNextTask` with `Selection` while enabled.
- The wrapper installs/restores `taskUpstream`, spawned-priority counts, and a
  static search context around native `MoveNext`. It also tracks temporary fake
  airtight state and borrowed pathfinder origins for cleanup.
- `WorkDelayedClaimPatch` intercepts `ClaimTaskDirect` for interactions without
  `bManual`, adding permissions and path validation. Interaction `bManual` and
  character `IsAIManual` are different flags. This was an initial suspect, but
  there is no proof the stalled kiosk order traversed this hook.
- Travel scoring uses a separate incremental route field. No direct kiosk GUI,
  scheduler, or movement-order replacement was found in this module.
- During stuck samples, `activeSelections=0`, workers were empty, and selection
  counters were unchanged. This argues against an actively running altwork search
  holding the current blockage, but does not exclude residual state or a timing
  effect from earlier selections.

The installed mod and current Debug DLL have different overall hashes. However,
an offline Mono.Cecil comparison found **all 77 AlternativeWork method bodies
identical** between them, including nested types. Comparison used opcodes and
operands, normalizing branch targets to offsets. Do not use the overall hash
difference as evidence of different altwork implementation.

Installed path:
`<game>/BepInEx/plugins/Ostrabus/Ostrabus.dll`.

Debug path: `bin/Debug/netstandard2.1/Ostrabus.dll`.

The installed `Assembly-CSharp.dll` was also inspected with Mono.Cecil to confirm
the native scheduler contains the relevant `Run`, `DrainPriorityCredits`, and
`UpdateClocks` methods and calls. This was static inspection, not live field access.

## Remaining work

The current API cannot distinguish the candidate scheduler paths. At the next
stall, the decisive diagnostic capture would include:

- World epoch versus `Scheduler.ProcessedTime`, and per-character local epoch/rate.
- Which early return `Scheduler.Run` takes, with a counter and reason.
- Priority-clock debt owners and amounts, including before/after catch-up values.
- Dispatcher active owner, pending owners, request age, and active operation.
- Ayden's continuation handle state, current/next ticker, due time, and priority.
- Current interaction cancellation, manual, urgent, and raised-UI flags.
- Native deferred pathfinding completion state, if the active request is waiting
  on a path operation.

Capture before the workaround and again immediately after recovery. Separately,
repeat the same kiosk sequence from an equivalent starting save with altwork off
throughout versus on. Do not change scheduler logic and altwork selection logic
together before identifying the blocker.

**Authorization boundary:** user authorized investigation without code changes.
Adding diagnostic fields, patching, rebuilding, deployment, or changing game state
has not been authorized by this hand-over request.
