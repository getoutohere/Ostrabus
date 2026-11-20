# Ship executive prototype postmortem

**Disposition: abandoned at the project owner's direction on 2026-11-19.**

The study produced a tested offline mission model, but did not demonstrate the
architectural simplification that justified undertaking it. Its central mistake
was choosing a reuse boundary that preserved too much of the game's cyclic,
side-effectful control flow. Explicit mission state around those entrypoints
could not, by itself, establish independent execution ownership.

The implementation, test receipts and design documents remain historical
reference material. They are not an active implementation plan. No replacement
executive was installed in the game, and this study established no basis for
removing production hooks. Abandonment does not revert the separate production
bugfixes or simulation improvements.

## Objective and outcome

Repeated ship-navigation, lifecycle and time-skip defects had raised a reasonable
question: could a coherent replacement reduce the number of places responsible
for preserving the same rules? The desired outcome was believable independent
actors whose missions could survive interruption, time advancement and changes
in world representation, while retaining authored faction and interaction rules.
Fewer special-case repairs and clearer compatibility boundaries were the intended
payoff.

The study first scheduled native command trees, then introduced a separate
mission reducer and explicit Navy return lifecycle. It added combat/reaction
adapters, incoming-command handling, shared world-time integration, checkpoint
correlation and arrival readback. It was subsequently adapted to game 1.1.

The final [receipt](Replacement/test-results.json) records **529 assertions across
101 scenarios**, covering all 27 transitions declared by the replacement model.
Those are valid results for the documented fixture profile. They are not 529
independent game defects resolved, coverage of the game's full behavior graph,
or proof that the replacement would simplify live ship AI.

The executive remained limited to one adopted Navy actor. An existing freighter
and native dispatch probes exercised integration and isolation; they did not
constitute a replacement fleet controller. Actual Unity scene lifetimes, native
route generation in the executive's flight fixtures, materialized equipment and
broad role coverage remained incomplete. No live performance improvement or net
reduction in production hooks was established.

## Primary architectural failure

We conflated preserving authored behavior with preserving the native mechanisms
that execute it. Authored rules describe why an actor chooses to patrol, fight,
assist, surrender or return. They do not require retaining command objects that
also mutate shared goals, manage subordinate controllers and enter broader world
lifecycle operations.

The source investigation had already identified shared ship-global targets,
private nested controllers and side effects outside the root AI dispatcher.
Nevertheless, the prototype continued to build adapters around important parts
of that machinery. The design therefore required compatibility projections,
private-field reflection, interception and special ownership bindings around
the proposed owner.

| Intended separation | What remained coupled |
| --- | --- |
| Policy chooses an intention | Native tree bindings retained predicates and selectors with mutable blackboard/target effects. |
| An ability performs a bounded operation | Native `FlyTo` still contained manual/path subcontrollers; native `Dock` entered broader attachment and retirement behavior. |
| One executive owns execution | Native statuses, queues, shared ship fields and consumers still required coordinated projections and interception. |
| Lifecycle effects have explicit results | Native world methods retained combined crew-transfer, hull-retirement and controller-lifetime responsibilities; the prototype added correlated observation around them. |
| Persistence follows the new state model | Checkpoints and handback still needed to reconstruct or project native command/tree state and private control fields. |

Concrete examples remain in the [return adapter](../../Tools/ShipExecutiveStudy/Prototype/NativeReturnActor.cs),
[native policy binding](../../Tools/ShipExecutiveStudy/Prototype/NavyPolicyBinding.cs)
and [world fixture](../../Tools/ShipExecutiveStudy/Prototype/NativeWorld.cs).

The explicit return lifecycle genuinely removed one opaque retreat controller.
That local improvement did not establish that the larger control graph had been
decomposed. A typed adapter interface cannot make its implementation a bounded
service when the implementation retains wider control responsibilities.

The project's final discussion identified the stronger architectural boundary:
policy produces intentions, the executive produces effect requests, domain
services perform operations, and results return through explicit events at
defined processing boundaries. That alternative was discussed, not implemented
or validated, before the project was abandoned.

## Why the process did not expose this early enough

**The tests became more complete within a boundary that was still too forgiving.**
Synthetic route geometry and prescribed travel/fuel inputs made mission and
accounting checks reproducible. Scene and subsystem providers made genuine native
methods executable offline. These were useful techniques, but they also excluded
important paths through which native coupling could defeat the proposed design.
Transition coverage of our own model could not establish that those excluded
paths respected its ownership rules.

**Compatibility work displaced the architectural decision.** The 1.1 port
correctly addressed deferred representations, RCS metadata, orbital phase,
serialization shape and shared SFF/PASS coordination. It restored the bounded
prototype's current-version correctness. It did not answer whether retaining
those native command entrypoints was an effective replacement strategy.

**Reporting emphasized passing milestones over the unresolved central claim.**
I repeatedly led with assertion counts and increasingly detailed qualification
gates. The reports disclosed their fixture limits, but the emphasis still made
incremental adapter correctness sound like stronger architectural progress than
it was. The owner had to push the discussion back to the cyclic call graph.
Choosing and defending that boundary was my responsibility; the game's existing
coupling was a known input, not an adequate explanation for the study's outcome.

The cost was additional prototype, adapter, fixture and compatibility complexity
without a demonstrated reduction in the live system's maintenance burden. No
reliable elapsed-effort or token-cost total is recorded here.

## Findings worth retaining

- The [mission reducer](Replacement/mission-model.json) demonstrated bounded
  interruption, generation checks, explicit phases and correlated pending
  effects without depending on Unity or Harmony.
- Native source/asset inventories and probes exposed real ownership,
  construction-time side-effect and persistence contracts. They remain useful
  evidence within their recorded versions and coverage.
- The time-service experiments demonstrated why recursively advancing the world
  inside a prefix is unsafe, and why interval/accounting ownership must be shared.
  The final prototype reused the existing production coordinator.
- Native archive and arrival tests established useful distinctions: a missing
  object is not proof of destruction or delivery; matching IDs are insufficient
  without lifecycle context; empty and absent world arrays can mean different
  things. These results do not certify complete live save/load behavior.
- Versioned receipts and negative controls preserve what actually ran. The
  [1.0 baseline](Replacement/Baselines/1.0.1.10/test-results.json) and
  [1.1 rework](Replacement/UPGRADE-1.1.0.0.md) remain available separately.

## Lessons and closure

Preserve authored intent through explicit policy contracts. Reuse a native
operation only after checking its transitive effects and ownership requirements;
a familiar method name is not evidence of an appropriate service boundary.
Architectural success needs a demonstrated closed execution path with bounded
dependencies, not just successful orchestration of the old controllers.

Tests should challenge that separation early, including effect feedback,
interruption, representation changes and multiple actors. Domain identity must
remain explicit: natural bodies, manufactured orbital stations, regional
references, substations and ground stations are distinct categories. Neither
`BodyOrbit` nor `Ship` alone establishes an object's physical role.

These are conclusions from an abandoned study, not a new backlog or an
authorization to build the alternative discussed above. Keep the artifacts for
reference and reproduction; leave runtime adoption, deployment and any future
replacement effort closed unless separately requested.
