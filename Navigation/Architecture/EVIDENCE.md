# Evidence for a bounded executive replacement

Snapshot: 2026-11-13, shipped Assembly-CSharp 1.0.1.10 SHA-256
`8058f7a07f93d5d44678aaf8d7f749050c0d9b1c9ed1c7419eb521ddcd2097e7`.
Analysis used decompiled C#, shipped asset JSON, assembly IL, the existing Debug
plugin and eight fresh native characterization probes. No running-game API was
called and no production patch was enabled by the probes.

## Findings that change the design

| Observation | Native evidence | Consequence |
| --- | --- | --- |
| Priority selection does not cancel the old action. | `SelectorCommand.RunCommand` selects a higher branch without invoking the previous leaf's OnExit. Native probe: old action ran once, emergency ran once, old exits remained zero. | The executive must perform an explicit ownership transition. |
| No concrete action command supplies an OnExit override. | Assembly inventory of 70 concrete BaseCommand descendants; declared/non-base OnExit implementations belong to combinators/wrappers. | Calling OnExit alone cannot clear abandoned thrust, routes or action timers. |
| A native sequence is reactive. | Two native SequenceCommand ticks execute its earlier successful child twice. CanExecute=false skips that child rather than failing the sequence. | Translating every sequence to a saved program counter would change authored behavior. Preserve reactive versus sticky semantics explicitly. |
| Failure can advance a sticky sequence. | Native StickySequenceCommand runs a failed approach then starts docking in the same dispatch; failed child exits remain zero. | Transit phases require explicit failure edges. The existing retreat repair already corrects a concrete occurrence. |
| A command object does not own its goal. | Constructing a second native FlyTo changes the destination/combat fields subsequently observed by the first one. | Suspend/resume must preserve and revalidate mission goals independently of command references. |
| Blackboard is not a general durable state store. | A native EmergencySignal tuple and empty ship list disappear across GetJson/LoadSavedData; a primitive integer survives. | Separate ephemeral perception from durable mission progress and explicit event expiry. Loss of a transient cache is not automatically a defect. |
| Nested mission state is not uniformly saved. | LowFuelRetreat contains retreat/selection/docking flags and subordinate commands; setting those flags still produces no native GetSaveData payload. | Reusing a complex action requires an explicit state adapter or a deliberate phase decomposition. |
| SFF is not a shared synchronous lifecycle operation. | Native Navy FFWD schedules a one-shot continuation. Role-specific FFWD methods can instead change goals, resolve docking, reposition or unregister. | A new Advance method cannot just call all old FFWD callbacks after doing its own work. |

The probes intentionally characterize known mechanisms, including defects already
repaired by the current plugin. They are not 16 newly discovered bugs, proof of
an entire battle, or validation of a replacement that has not been implemented.

## Native execution surface

The metadata scanner found 26 native methods containing direct RunCommand calls.
Besides AIShip and the four main combinators, commands such as FlyTo,
KeepAtDistance, KeepWithinFiringDistance, LowFuelRetreat, ComeHere and ShakeDown
invoke subordinate commands. Several ResolveInstantly methods also call normal
execution. The outer tree therefore does not contain every running state machine.

There are 338 selected direct call sites and 395 selected direct control/target
field writes in the assembly scan. The selection includes relevant registration,
time-update, route, actuator and command calls. These are conservative reference
inventories: they include debug/test code and legitimate physics, and omit some
indirect calls, component/by-reference writes and transitive effects. They are
not 395 hooks that must be installed or 395 independently proven defects.

Three direct callers of `StarSystem.Update` are recorded in IL:

- `CrewSim.Update`
- `GUIFFWD.FFWD`'s generated MoveNext
- `CrewSim._TeleportCO`'s generated MoveNext

Within StarSystem.Update, ship/docked-group movement occurs before AIShipManager.FFWD,
collision/message processing and AIShipManager.Update. A replacement that only
changes post-update AI dispatch cannot make earlier bulk motion obey its new rules.
The existing [PASS investigation](../../Orbital/PASS-TIME-SKIPS.md) reproduces the
route-completion/ordinary-integration failure and identifies the common time seam.
This study does not implement that proposed repair.

The native AIShipQueue also matters: ordinary ships are distributed through its
queue while priority classes and explicitly prioritized ships receive different
dispatch treatment. Initial executive adoption should retain that scheduling
budget. "Interruptible" means at the next eligible dispatch, not an unmeasured
guarantee of instantaneous reaction at 16x time.

The `_activeReaction` field has a dispatch branch, but the direct native field
inventory finds only its clearing write, not a native non-null producer. Native
weapon dodges use AddUrgentCommand. The architecture must not mistake a dormant
extension branch for the game's normal combat mechanism.

## Authored asset boundary

The shipped `loot/loot.json` contains 13 records directly naming native command
types. Ten core `interactions/interactions_ship_to_ship.json` records directly
reference those command loot aliases. Examples include:

| Native asset alias | Command / behavior |
| --- | --- |
| CMDComeHere | ComeHere |
| CMDTakeFuel | TakeFuel |
| CMDConsiderSurrender | SurrenderConsiderOffer |
| CMDConsiderPlayerDemandSurrender | SurrenderConsiderPlayerDemand |
| CMDSurrenderArrestAndDespawn | SurrenderArrestAndDespawn |
| CMDDeployHauler / CMDDeployLeo | Native station assistance dispatch |
| CMDScuttle / CMDShoot / CMDTargetPlayer | Existing authored ship directives |
| CMDStartPractice / CMDStartRace / CMDCancelRaceSession | RaceComms directives and payload |

`Ostranauts.Ships.Comms.Comms.ApplyLoot` translates those aliases into
AIShip.AddCommandLoot and passes either the counterpart's ID or explicit loot
arguments as SaveData. The new executive must preserve that protocol, target
context and native interaction/condition evaluation. Filtering the incoming
queue down to navigation commands would remove real authored behavior.

The study also reviewed the existing encounter audit's evidence for
`plot_manager/pm_settings.json` and authored world infrastructure. Native Navy/Ceres trees,
command scheduling and movement ownership are C# mechanisms; the asset interface
does not supply a replacement scheduler. Faction weights, spawn composition,
station capabilities, interactions and boarding/crew outcomes remain authored
inputs. The 13/10 counts are direct records in the selected core files, not a
complete resolved mod/load-order or generated-stakes dependency graph.

## Repeatability and limits

[The tools](../../Tools/ShipExecutiveStudy/README.md) generate ignored reports
under `obj/ShipExecutiveStudy`: command capabilities, direct call/write lists,
command-bearing assets, relevant patch declarations, native Navy tree and probe
results. All identifiers in their metadata reports are relative files or full
native method/type names. They do not need a running game.

The snapshot Debug plugin contains 254 HarmonyPatch-decorated classes. A broad
namespace/name filter selected 90 for architectural review. That is not an
installed-hook count or a removal proposal. The disposition table in the design
names the specific groups the new executive could eventually subsume.

Still unproved: live Unity scene transitions, complete comms/stakes reachability,
all role-specific save state, physical docking/retirement atomicity, interrupted
in-flight effects, deterministic equality of stochastic combat across time
granularities, runtime performance and every integration path. These are explicit
acceptance work, not assertions supplied by a clean architecture diagram.
