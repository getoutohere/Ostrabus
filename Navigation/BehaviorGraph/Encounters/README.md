# Faction encounter staging slice

This independent entrypoint was missing from ordinary traffic and bounty
activation coverage. It is not a complete Navy/combat model.

The vanilla graph records the weighted BeatManager caller, deferred navigation
GUI caller and post-finale ambient battle, then native fresh/reserve versus
active-actor selection and placement. The effective overlay separately records
the intentional [local encounter policy](../../Encounters/README.md).
It also expands the downstream Navy patrol scheduler and queue persistence;
[the bugfix](../../PATROL-PRIORITY.md) preserves native emergency policy.

Run `generate --model encounters` with the AIBehaviorGraph tool for
[vanilla](VANILLA.md), [effective](EFFECTIVE.md) and [audit](AUDIT.md) outputs.
Sources reference native methods and repository policy with fingerprints.
`assembly-dependencies.json` tracks those method closures and the authored world
and event settings; a game update can require revalidation even when a directly
selected method did not change.

Graph inputs such as native selection and weighted eligibility are explicit
external outcomes. They are not probability predictions or assertions that an
encounter must happen. The protected-existing-actor scenario uses a full fleet
and one candidate, matching the native regression. All occupied cap slots remain
occupied after filtering; no replacement hull is guaranteed or forced.

[Offline native tests](../../../Tests/FactionEncounterCheck/README.md) reproduce
the actual staging and production transpiler, including native placement and
velocity writes. Subsequent numerical flight, construction, combat and human
behavior remain external. A successful graph check does not prove their safety.

Downstream scheduling scenarios reproduce priority patrol starvation, ordinary
patrol preemption, isolated versus mixed legacy queues, SFF with/without a save,
and saved FIFO reversal. The overlay routes ordinary Navy patrols through native
Main and preserves callback semantics and FIFO. Shared callback/Main leaf identity
and fuel-ledger continuity are covered by native tests, not a numerical graph
prediction. Emergency detection and leaf outcomes are controlled external facts;
the graph does not assert that any particular live ship must detect or win a fight.

The separate [combat-to-patrol handoff repair](../../PATROL-TARGET-HANDOFF.md)
closes a downstream gap that the earlier controlled combat results did not test:
empty detection returned Failure before native neutralization/retargeting, then
sticky Main reused the enemy destination. Two stateless ConditionCommand adapters
admit existing Combatants through those gates while retaining the original Navy
or Blockade detector and cleanup leaves. This reference pins the native methods
and production adapter as an explicit native-harness boundary; its scenario
counts are not expanded combat coverage. The two independent shipped-assembly
suites reproduce the failure and verify the actual constructor hooks.

The separate disabled-target acquisition boundary now covers both detector
implementations. Regular detection reuses its native controller lookup; Blockade
adds a fresh-candidate check before faction/warning work. Existing combatants
reach native cleanup, while later scans reject inactive below-Full AI hulls until
registration returns. Its source/method bindings and dedicated native checks
are tracked separately from this staging projection's conditional scenarios.
