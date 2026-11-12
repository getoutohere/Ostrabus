# Work idleness: live capture and native scheduler timing

Game 1.0.1.10, 2026-11-11. Read-only inspection during player wall installation
and later idle/restore selection, with AlternativeWork and worktrace enabled.
Raw private captures are ignored under `obj/WorkInvestigation`.

## Established observations

- Between two captures, world time advanced 552.334 seconds, scheduler processed
  time 283.030 seconds, and Ayden's local clock 293.323 seconds. His offset from
  the world grew from 537.587 to 796.598 seconds. A clock offset alone is not
  failure evidence; the different advancement over the same interval matters.
- AlternativeWork recorded 14 new searches, 10 claims, no additional failed
  attempts, 227.237 ms of planning and 263.362 ms of selection CPU time. Planning
  is included within selection; these values must not be added as independent
  totals or equated with wall time. Supply planning used 1.237 ms.
- Wall installation repeatedly executes five native InstallAllow progress
  interactions. The installable JSON specifies 0.001-hour duration and
  CONDInstallProgressx5. Those repeated steps are intended work, not idle retries.
- In the idle capture Ayden had no queued interaction, no active work claim and
  a pending end-turn continuation. The trace also recorded native auto-restore
  validation failures for nav modules and a loose O2 alarm. This is separate
  from the AlternativeWork explicit work-order selector.
- Ayden had Hungry, Sustained, StatSatiety=3 and IsSeekFoodCooldown. The native
  condition asset explicitly describes a recent attempt to find food and lasts
  0.017 hours (61.2 local seconds). This establishes an attempt, not its duration
  or that every captured pause was a food search. Later captures were paused
  with IsAIManual present; those states must not be treated as continued
  autonomous failure.

## Native food search

`Ostranauts.Pledges.PledgeEat.Do` tests manual mode/cooldown/combat, sets the
cooldown, then calls `BasePledgeFindItem.FindItem` when its food/satiety tests
permit it. The authored AIEat pledge has priority 7. Idle
`CondOwner.EndTurnSteps` processes pledges before calling GetWork.

FindItem checks carried contents first, then searches the current ship and
eligible docked ships. Each `Ship.GetCosDeferred` gathers the matching set before
the pledge tests candidates' reachability. This is a scan of objects/containers,
not literally every spatial tile or the entire solar system. Shore-leave,
ownership and emergency gates constrain the connected hulls considered.

Current worktrace does not observe pledge entry/exit or those inventory scans.
It therefore cannot assign a particular long pause to food search. No new live
hooks were installed and no runtime behavior or crew state was changed here.

## Reproduced scheduler time loss

`Condowner.Utils.Scheduler.Run` returns before UpdateClocks when an end-turn
dispatcher exhausts its budget. AddSkippedDeltasToPriorityCOs credits only
eligible dispatch priority >=2. Automated crew were at priorities 0/1 in the
captures. Ordinary clocks follow Scheduler.ProcessedTime, not StarSystem.fEpoch.

`Tests/WorkTraceCheck`, mode `scheduler`, executes native Run, EndTurnDispatcher.Tick
and Clock. Engine time and the empty work-manager leaf are fixture boundaries;
the actual exhausted-deadline branch is used. Four blocked half-second updates
lose two seconds from an ordinary clock. Clearing the dispatcher and advancing
again does not repay that time. Seven checks pass without Ostrabus gameplay
patches. This proves the conditional mechanism, not that food caused the captured
budget exhaustion or that assigning every actor urgent priority would be safe.

```powershell
dotnet build Tests/WorkTraceCheck -c Release
dotnet run --project Tests/SaveJsonCompatibility/Host -c Release --no-build -- Tests/WorkTraceCheck/bin/Release/netstandard2.1/WorkTraceCheck.dll scheduler
```

Further diagnosis must attribute the time to native pledge/inventory/path phases
and distinguish intended work duration, native time throttling and selection
latency. A correction needs to preserve authored needs and emergency handling,
not silently remove hunger or advance all delayed effects in an unsafe burst.
