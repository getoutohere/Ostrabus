# Ship mission executive: feasibility and decision

Status: researched design proposal for game 1.0.1.10. **No runtime executive,
patch changes, asset changes, deployment or live intervention is included.**

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

The first Navy implementation may temporarily increase the integration surface.
Shared fixes must remain for other roles and fallback actors. No hook-removal
total or implementation-time estimate is claimed before the native-adapter
prototype meets its acceptance gates.
