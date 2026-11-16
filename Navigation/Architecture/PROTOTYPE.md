# Offline Navy executive prototype

Status: first-stage reference prototype, 2026-11-14. **Not production-ready or installed.**
Its recorded baseline passes 108 assertions across 16 scenarios under the game's bundled
Mono runtime. See [the recorded results](prototype-results.json) and
[reproduction instructions](../../Tools/ShipExecutiveStudy/README.md).

The subsequent [replacement model](Replacement/README.md) defines explicit
mission/ability contracts and replaces the opaque return lifecycle. The current
combined runner now passes 529 assertions across 101 scenarios on 1.1.0.0; the original 16 remain
as reference tests, including a negative control for its missing pre-invocation
arrival barrier. Details below describe the first-stage implementation.

The result supports centralizing mission ownership and durable execution state.
It does not establish that every ship role, authored directive, physical action
or world-time entrypoint can safely be adopted yet. No hooks have been removed.

## What actually runs

The prototype constructs the real `NavyPilot` and imports its actual ordered
command objects. It distinguishes native selectors, reactive sequences, sticky
sequences and the incoming queue. It does not substitute a hand-written patrol
personality or run the native root dispatcher alongside the executive.

The executive owns coarse ordinary/combat/directive goals, sticky progress,
control handoff, generation checks, bounded incoming directives, state capture
and a common offline travel-advance entrypoint. Native commands still choose
combat targets, select retreat stations, offer surrender, steer, request docking
clearance and request station arrival. The native subordinate retreat and flight
controllers remain real command objects with shared identity.

| Area | Executed code | Explicit fixture / limit |
| --- | --- | --- |
| Role selection | Actual Navy constructor tree and native guards, combat initiation, neutralization, damage and surrender commands | Threat perception, weapon eligibility, engagement profile and regional station candidates are controlled inputs. Native sensor/faction realism is not measured. |
| Movement | Native `FlyTo`, `FlyToPatrol`, `FlyToPath` dispatch and native `NavData.TimeAdvance` | The planner returns a prescribed 40-second trajectory costing 5 kg. No proof of route feasibility, orbital accuracy, gravity or collision avoidance. Engine actuators and ephemeris updates are fixtures. |
| Combat | Native `KeepWithinFiringDistance`, steering math, nested `LowFuelRetreat` and `Shoot` body | Weapon inventory is empty. A Shoot request proves the combat path was reached, not that a projectile fired or hit. Sensor switching records device requests. |
| Return | Native station selection, flight, clearance request and `Dock` execution | Clearance outcome is controlled. `CrewSim.DockAndDespawn` is replaced by a scene-outcome fixture that can acknowledge or refuse the request. Actual crew transfer, population and materialization are not validated. |
| Time | Same executive advance path, actual route boundary and native fuel settlement | Normal/SFF/PASS are input labels and interval partitions here. Native `StarSystem.Update`, GUI SFF and passenger-transit entrypoints are not redirected. Bulk combat is rejected explicitly. |
| Save/load | Fresh native pilot/controller/command graph; native ShipSitu/NavData JSON reconstruction; explicit sidecar state | The fixture world checkpoint is not a game-save writer. No save key or migration is installed. |

Every fixture and retained service patch method is listed in the generated result
report. The native assembly SHA-256 is checked before execution. Checkpoints bind
to the native and plugin module IDs; a changed binding is refused.

The root role policy and native action bodies are not assumed pure. Some guards
mutate the blackboard. The executive distinguishes those decision operations from
actions that need a movement ownership handoff. This is currently a Navy-specific
binding, not a universal classification inferred from command names.

## Review of the newer repository repairs

The starting repository includes other ongoing work beyond commit `d52a6fb`.
The prototype reviewed these additions rather than relying only on the older
architecture study:

| Repair | Implication for a replacement |
| --- | --- |
| No available pilot means wait; an existing course may continue | Retained actual `FlightPilotAvailabilityPatch` in the offline process and tested both cases, including arrival waiting for pilot recovery. Do not equate unavailable crew with successful flight. |
| Refresh a retained destination to the current epoch before SFF docking | Preserve the repaired ephemeris/destination service. The fixture records refresh requests but cannot validate real moving-station coordinates. |
| SFF pilot/group availability gates | Keep world movement authority outside role callbacks. The prototype does not replace or certify the current dock-group/SFF integration. |
| Civilian visit waits and asteroid stays | Preserve durable waiting and separate intentional visit-duration policy. Navy adoption must leave these actors with their current controller. |
| Freight loading, committed delivery, persistence and recovery | Preserve delivery-before-retirement and market conservation. These are native world/economy service obligations, not reasons to invent a second freight policy in the executive. |

Current flight-control handoff, shallow torch shutdown, low-fuel progression and
five flight-fuel repair patch classes are applied only inside the offline process.
They are reused services, not fixes the prototype has eliminated. Civilian,
freight and orbital runtime modules are not initialized by this test.

The shipped `loot/loot.json` and `interactions/interactions_ship_to_ship.json`
still define meaningful commands and social outcomes: for example `CMDComeHere`,
`CMDConsiderSurrender` and `CMDDeployHauler`. Nothing in this experiment rewrites
those assets. The native comms/loot protocol must remain authoritative in any
future implementation; incomplete adapter coverage is a production blocker.

## Evidence from the scenarios

The tests cover these distinct contracts; multiple assertions within a scenario
are checks, not additional independently simulated cases:

- Navy patrol yields to a threat; native combat runs; native low-fuel retreat
  selects home, traverses its route and requests one station arrival.
- Ordinary patrol admission does not monopolize the urgent queue. Ownership
  changes clear obsolete controls/routes while preserving inertial velocity;
  a stale generation cannot complete the new mission.
- A fresh-object reload halfway through native retreat preserves private stage
  flags, canonical goals, route progress, fuel settlement and final arrival.
- Supported deterministic return produces the same final epoch, fuel and single
  arrival with 10-, 20- and 60-second partitions labelled normal, PASS and SFF.
- An unavailable pilot waits without advancing to docking; existing playback
  can continue. Recovery allows native docking to resume.
- Failed approach does not fall through to docking. An arrival request with no
  observed world effect remains pending and is not replayed, even after reload.
- Native damage/surrender retains its authored return goal instead of restoring
  the suspended patrol over it.
- A restricted tagged blackboard codec preserves primitive types, empty ship
  lists and an emergency signal tuple; unsupported types fail explicitly.
- Incoming supported native commands retain FIFO/one-shot state across reload;
  an urgent native dodge resumes combat. Unvalidated directives are rejected.
- The incoming queue enforces its 16-command capacity and releases completed
  transient commands instead of accumulating them in the persistence graph.
- Same-ID target reconstruction rebinds canonically; disappearance revokes its
  route; a stale actor binding and incompatible checkpoint are refused.
- Nested command references and the public active-command projection survive
  reconstruction without duplicating command identities.
- Ordinary-flight export is repeatable and native dispatch can continue from
  its projected cursor. Unsupported handbacks are explicitly refused.
- Combat bulk advancement returns an integration blocker without advancing time.

The typed codec and reflected primitive/command-link snapshot are research tools.
They do not establish complete persistence coverage for every reachable native
object field. A production adapter needs an explicit field inventory and codec
for each supported stateful ability, including timers, contexts and nullable state.

## Remaining gates and verdict

| Gate | Current result |
| --- | --- |
| Native Navy tree import and selected full lifecycle paths | Passed with the listed fixtures. |
| Goal ownership, failed approach, reload, bounded directives and fuel accounting | Passed for the listed scenarios. |
| Arbitrary authored comms/loot and long-running directive interruption | **Blocked.** Only patrol admission, sensor commands and the tested dodge path have adapters. ComeHere, surrender consideration, assistance and other payloads still need complete closure. |
| Safe disable/removal during nested retreat, queued directives or uncertain arrival | **Blocked.** The prototype refuses handback. Vanilla cannot losslessly represent all this progress. Ordinary-flight export is not a general removal guarantee. |
| Common real-world normal/SFF/PASS integration | **Blocked.** The common offline advance method is tested; native world entrypoints and movement/economy ordering remain untouched. |
| Physical docking, tow groups, damaged navigation, crew/scene transfer and lifecycle reuse | **Not validated.** Requires additional native adapters and ultimately Unity validation. |
| Runtime scheduling/GC improvement | **Not established.** No FPS or hook-count saving is claimed. |

A warmed 2,000-dispatch sample records elapsed time and native call count, solely
as a local diagnostic. Bundled Mono's allocation counter returned zero even for
a deliberate retained 1 MB allocation. The report marks allocation measurements
unavailable, not zero. This prototype also deliberately allocates snapshots and
uses reflection; it is not a production allocation design.

These gaps motivated the separate [replacement-model pass](Replacement/README.md).
Its explicit return profile now has ability state, correlated arrival preparation
and a qualified native handback subset. The full authored incoming-command
closure and production world-service/save integration remain open. The later
[Navy lifecycle pass](Replacement/NAVY-LIFECYCLE.md) adds explicit combat/reaction
adapters, selected directives and actual native world-entrypoint tests offline.
The subsequent [world-service pass](Replacement/WORLD-SERVICES.md) adds real
Orbital/SFF/Economy scope integration and a native playback-completion negative control.

All 398 pre-existing production source/JSON/project inputs captured at the start
of this work were verified unchanged. The prototype is excluded from the plugin's
compile inputs and adds no runtime hooks to the running game.
