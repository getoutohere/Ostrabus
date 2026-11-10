# Return intent with native pilot priorities

The former StationReturn RunAI prefix executed FlyTo/Dock directly and skipped
the pilot tree. It yielded to explicit reactions but bypassed native queued,
combat, threat and support decisions. Returning via native flight leaves did not
make that scheduling native.

StationReturn now registers the root selector's existing ordinary Main branch.
One narrowly matched SelectorCommand.RunCommand call-site patch delegates that
branch to the pending return; all preceding branches retain native CanExecute,
ordering and result semantics. Other selectors/branches run their original
commands. AIShip.RunAI itself runs normally. Unknown tree layouts remain native;
the pending intent is not a license to bypass their controller.

The existing RunAI hook only registers the mapping, including after reload. It
does not suppress execution. Request stores the destination/docking intent and
migrates legacy custom queues, but does not change a current target, fuel
prediction or NeedsRepairs flag. Those navigation effects occur only when the
ordinary branch gains control. A higher-priority action may interrupt return;
later ordinary execution rebinds home if necessary and resumes native approach.
The boarding return monitor no longer writes a missing scan target directly;
it records pending intent through the same integration instead.

SFF suppresses the pilot's disposable callback only while the active command is
actually one produced by the return controller. Pending intent alone no longer
suppresses an active combat/queued command's native SFF behavior. No new motion
or fuel approximation is introduced here.

Native tree nodes and save paths are unchanged. Destination/stage use the same
existing Blackboard keys; no new save format or custom serialized command is
introduced. Root mappings have weak keys and weak controller references.
Command caches remain weakly keyed by AI. Registration inspects the tree once;
the selector call site adds a weak-table lookup without per-call allocations.

Validated with 93 bundled-Mono checks, including real Navy/Pirate/Police tree
constructors and native dispatch, priority interruption, reload and return
completion. See [test boundaries](../Tests/AiPlannerDiagnosis/PRIORITY-AUDIT.md).
This build has not yet been tested live for combat during a pending return.
