# Signal-box regression harness

See [the original audit](../../MiscBugFixes/SIGNAL-BOX-AUDIT.md) for native
reproductions and run instructions, and the
[production notes](../../MiscBugFixes/SignalBoxes/README.md) for implemented scope.
The harness runs the installed assembly under the game's bundled Mono runtime;
it does not modify the running game or saves.

**4,980 assertions pass** in three sequential phases:

1. **67 native audit/logging/offscreen checks:** reproduce seven findings before
   behavior repairs; verify diagnostic routing and native offscreen save-back.
2. **2,417 prototype checks:** install independent test-only candidate hooks,
   including an endpoint authority/conversion matrix and 300-event network.
3. **2,496 production checks:** install the actual `SignalBoxes` repairs together
   with the logging hooks, rerun the behavioral matrix/network, and exercise
   lifecycle boundaries, queue restoration, legacy ghosts, GUI label restoration
   and weak-reference collection. Candidate hooks are removed first.

The production lifecycle checks include the live cleanup-before-initialization
regression: incomplete sources must not throw or publish an invalid snapshot;
native initialization must preserve the removal without deleting unrelated
outputs. Multiple/repeated cleanup, explicit reconnection, preserving conversion
and weak cleanup state on a still-registered ship are covered.

Native gate, queue, audit, serialization and delivery methods execute with
explicit engine identity, lookup, clock, conditions, power and placement fixtures.
The public ModeSwitch wrapper runs with modeled Unity placement/validation leaves;
this is not a complete live Unity scene or power-grid test. Expected Unity icall
warnings can appear during JIT compilation: use the exit code and final assertions
to distinguish those from harness failures.

The [prototype notes](CANDIDATES.md) retain the design progression and original
baseline limits. The module notes record current production behavior and limits.
