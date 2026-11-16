# Ship mission executive: feasibility and decision

Status: researched design and working offline prototype, requalified against
game 1.1.0.0. See the [1.1 rework](Replacement/UPGRADE-1.1.0.0.md).
**No production executive, patch changes, asset changes, deployment or live
intervention is included.** The prototype runs under bundled Mono with explicit
world fixtures; its results do not satisfy every production acceptance gate.

The evidence supports a bounded replacement of command execution and mission
lifecycle. It does not support rewriting the entire ship/world simulation, or
expecting a replacement of `AIShip.RunAI` alone to enforce reliable ownership.

The proposed executive retains native role rules, faction/sensor decisions,
authored comms and interactions, native flight/docking abilities, crew lifetimes,
markets and physical simulation. It owns mission identity, priority, interruption,
control handoffs, durable progress and time-advance coordination.

Start with the complete **Navy patrol -> encounter -> native combat/retreat ->
home approach -> native station arrival** lifecycle, including failed/interrupted
actions, incoming comms directives, save/load and each time-advance source.
This is an offline prototype milestone; a patrol-only demonstration is insufficient.

- [Decision, design, rollout and acceptance gates](MISSION-EXECUTIVE.md)
- [Source/assembly/native-probe evidence](EVIDENCE.md)
- [Offline prototype, results and remaining blockers](PROTOTYPE.md)
- [Replacement mission model and executable ability contracts](Replacement/README.md)
- [Machine-readable boundaries and patch disposition](execution-boundaries.json)
- [Reproducible offline study tools](../../Tools/ShipExecutiveStudy/README.md)

The study scanned the shipped assembly independently of the existing behavior
graph: 13 concrete pilots, 70 concrete command types, 338 selected direct call
sites and 395 selected direct field writes. Eight native probes passed 16
characterization assertions. These numbers describe evidence coverage, not new
bug counts, proposed hook counts or proof of every reachable interaction.

The most consequential result is that commands share ship-global goals and many
contain private subordinate controllers. A scheduler wrapped around unchanged
command objects would retain the same ownership and persistence weaknesses.

The [replacement model](Replacement/README.md) now executes a qualified return
lifecycle without the opaque native retreat controller. Its mission/ability
contracts are normative and separate from the descriptive behavior graph.
The original tree-scheduling prototype remains a reference and negative control.
Neither implementation is installed in the game.
The latest [Navy lifecycle qualification](Replacement/NAVY-LIFECYCLE.md) covers
combat/reactions, selected authored inputs and actual native world entrypoints;
475 assertions pass across 90 combined scenarios. The
[retained world-service pass](Replacement/WORLD-SERVICES.md) integrates existing
scopes offline and reproduces a remaining shared playback-completion blocker.
The [native save/arrival pass](Replacement/SAVE-ARRIVAL.md) runs the real collector,
worker and wire parser under data/scene providers. Full Unity load/arrival remains
an adoption gate.
The [command-effect pass](Replacement/COMMAND-EFFECTS.md) qualifies receipt-time
fuel construction and selected native dialogue/reload behavior under stated
providers, including messages still in transit.

The first Navy implementation may temporarily increase the integration surface.
Shared fixes must remain for other roles and fallback actors. No hook-removal
total or implementation-time estimate is claimed before the native-adapter
prototype meets its acceptance gates.
