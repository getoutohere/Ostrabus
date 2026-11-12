# Cargo, PASS and civilian traffic audit

Reviewed against Ostranauts 1.0.1.10. Follow-up fixes repair the coordinate,
conversion and player-connected cleanup defects; other findings retain their stated uncertainty. See the machine model's
source fingerprints and assembly dependency manifest for exact native references.

## Role boundaries

`AIShipManager.CheckTradeRoutes` selects an economic route, chooses a cargo hull,
checks reachability, loads virtual market stock, sets the buyer as destination
and registers the controller under INTERREGIONAL. Its pilot's home is the source,
not necessarily the current buyer. Reachability is tested before cargo loading;
the later flight command plans with the resulting ship state. Hull sizing and
the preflight/loaded-mass difference require further quantitative analysis.

`SpawnPassShip` also creates a HaulerCargo pilot, but without a freight market.
It moves up to four eligible occupants from an overpopulated station to a station
with spare capacity. More than four spare destination candidates are required.
Otherwise native population policy can remove eligible people instead of
creating a voyage. This behavior is explicit in the graph, not presented as a
newly discovered bug or a simulated prisoner-transfer system.

`SpawnFactionShips` creates local civilian controllers according to station
quotas, owner-specific ship/crew definitions and spawn visibility restrictions.
Civilian visitors choose asteroids, nonstation hulls or stations; they do not
follow the cargo/PASS delivery loop. The constructor trees are exported from
actual native constructors, including priority commands, surrender, decoys,
armed engagement and escape branches.

## Findings and certainty

| Finding | Evidence | Current status |
| --- | --- | --- |
| Civilian SFF chooses a regional station with `(position.x, position.x)` in two branches, ignoring Y. | Native role FFWD executed against two actual regional station positions; the wrong station is selected. | Repaired by the coordinate transpiler; both sites and native continuation controls pass. |
| Civilian's owner-filtered `new virtual GetTargetHome` is bypassed through `IAICharacter`; the base unfiltered method executes. | Native interface map plus direct and interface calls with recorded filter arguments. | Dispatch difference confirmed. Review individual-owner fallback before prescribing faction-return policy. |
| Civilian targeting accepts a hidden reserve hull which the spawn pool can also select. | Native selectors and creator reproduce selection/repositioning with unchanged estimates. | Repaired selection and retired/restored visits with native priority/SFF ownership and physical encounter protection. 82 production lifecycle assertions pass. See [scope and consequences](HIDDEN-VISIT-INVESTIGATION.md). |
| Our first target-removal hook invalidated another actor's independent asteroid goal during native Ship-to-stellar conversion. | Native shallow `Undock.AttemptToUnMoor` run with and without production lifetime hooks. | Repaired by an exception-safe conversion scope; independent/copied/direct targets, restoration, failure and nesting pass. |
| Ordinary Dock can retire a freight carrier without the cargo-specific market handoff. | Source review and existing native station-arrival fixtures distinguish Dock from DockCargoAndDespawn. | Manifest remains aboard a hidden carrier; permanent loss or subsequent reuse consequences are not established. |
| Missing destination market is checked after the carrier market is unregistered. | `MarketManager.UnregisterCargoShip` source ordering. | Further inventory-lifetime reproduction needed; no assumption of permanent goods destruction. |
| Empty-fuel civilian Hold has no equivalent of scavenger SOS routing. | Hold/TargetNewIfNone and role tree review. | Authored behavior gap; first establish whether fuel was legitimately exhausted. |

Existing cargo-loading conservation and chronological SFF economy repairs are
included in the effective overlay. They are not new findings or new hooks from
this audit. PASS is excluded from freight catch-up because it has no cargo market.

Normal Hold's fuel block means less than 1 kg RCS and no lift-rotor thrust,
not simply an empty tank. Civilian SFF's free-flight check instead tests RCS > 0.
The model keeps these tests separate and includes the rotorcraft exception.

## Asset ownership of policy

The selected native JSON entries are tracked for future revalidation. No installed
asset files were edited and this audit introduces no asset overrides.

- `RandomPassShip` selects Medium Ferry. It does not make PASS into a trade route.
- Regional 4/8-pod cargo selects Ostrich A4R/A8R; interregional 4/8 selects IbexCargo.
  Both 36-pod selections use MesaCargo. The role and route choose the entry.
- `RandomCivilianShip` selects the CCRE/Galcon civilian pools with equal weight;
  owner-specific entries remain authoritative. These pool labels alone do not
  establish a ship's current faction, itinerary or passenger purpose.
- `TIsNPCPrunable` requires human and excludes player, player crew, social combat
  and nontraffic people. `TIsTrafficAI` additionally excludes combat and unconscious
  people. Station transit and passenger population policy consume different tests.
- `TIsValidStationDropOff` rejects dead, derelict victims and OKLG pirates.
  C# also considers station reputation before transferring people.

The X/X coordinate defect and interface dispatch mismatch reside in C# mechanisms;
JSON cannot correct those calls. Missing civilian assistance or different travel
preferences would be policy questions, not permission to invent parallel C# AI.

## Representation conversion regression

For an unvisited asteroid with a template, shallow unmooring calls `Ship.Destroy`
then `StarSystem.CreateStellarObject` with the same ID, template and situation.
The logical destination survives. The first removal hook treated intermediate
Ship destruction as permanent loss and cleared another observer's independent
stellar target and route. Native behavior preserves that goal. The corrected
hook resolves the lifetime after conversion, preserving the goal and route on
success, rebinding direct references and clearing retired Ship pointers. Failed
conversion still performs permanent-loss cleanup and propagates the exception.

`traffic-conversion` executes the native unmooring path. Physical unmoor/pushback
and new stellar-object construction are fixture boundaries; the native call
sequence, Ship destruction and production lifetime hooks execute. The shared
suite now has 303 checks covering independent/copied/direct goals, restoration,
unchanged routes/momentum/fuel, unrelated coordinate courses, nested/failed
conversion and permanent asteroid destruction outside conversion. This is distinct
from `DestroyAndReload`, whose replacement scope the repair also handles.

## Arrival, storage and cleanup

Successful `DockCargoAndDespawn` transfers the market manifest, then uses native
crew drop-off and hull retirement. Ordinary `Dock` uses crew/hull retirement
without the market transfer. Native drop-off assets and reputation decide whether
each person moves to the station or is removed. Hidden hulls remain in the world
registry for reuse; hidden and destroyed are separate states.

Region cleanup iterates the departing local ATC bucket. INTERREGIONAL cargo/PASS
remain scheduled globally. Local crew relocation can succeed, fall back, or fail;
the model does not guarantee a station destination. Source review also finds that
the player-docked check suppresses crew relocation but not the later unconditional
hull Destroy. [Native caller-path reproduction](REGIONAL-CLEANUP.md) now establishes
reachability for direct/chained full/shallow groups, including player-owned hulls
left behind while the player travels elsewhere. Remaining crew reach CO destruction.
No runtime correction is implemented yet. Stale cleanup has separate age,
connection, ownership and plot guards.

Station population can also change through `HandleTransits` without a ship voyage.
Passenger hull counts therefore cannot establish that all travel was simulated.

## SFF and saved state

Role FFWD and normal behavior-tree execution are different entrypoints. Existing
navigation data can preserve the active flight command. Without it, cargo FFWD
attempts cargo-specific instant resolution; civilians may instant-dock and hold,
reuse a remembered departing partner, or use the defective regional lookup.
Success, failure and Running are separate outcomes.

FFWD queues a `OneShotWrapperCommand` for a returned leaf. The wrapper calls
RunCommand once and reports Finished even when that call returns Running.
Queue completion therefore does not prove arrival, unloading or crew transfer.
Active reactions preempt normal RunAI; FFWD uses its last active command instead.

Ostrabus's bounded travel catch-up can suppress the native instant path for a
captured, applied, not-yet-arrived party. Uncaptured and eligible arrival cases
retain native behavior, so that guard is not a general repair for civilian X/X.
Chronological economy handling owns only eligible real freight and suppresses
its duplicate native update/FFWD. Combat, connected, loaded and otherwise
ineligible cases retain their documented fallback.

Home station, scheduling region, sticky command progress, destinations and cargo
are separate saved state. A valid same-ID Ship replacement is not a destroyed
logical destination. Stellar conversion is another identity-preserving boundary
and must be treated separately from permanent removal.

## Evidence limits

Model scenarios test the reference's consistency, not the simulation's physics.
The role harness executes native constructors, control containers, target selection
and FFWD, with prescribed physical docking outcomes and asset/crew creation stubs.
The conversion harness executes the narrower native boundary described above.
Existing economy and station-arrival harnesses supply separate evidence.

Numeric path planning, weapon damage, all character agendas, every loot variant
and every possible event interleaving are not fully modeled. Constructor coverage,
source bindings and an accepted assembly baseline are not claims of whole-world
semantic completeness. Follow-up runtime repairs address the civilian coordinate,
stellar-conversion and player-connected regional cleanup defects. The latter's
native queue/save checks and limits are documented in [REGIONAL-CLEANUP.md](REGIONAL-CLEANUP.md).
No new travel, rescue or faction-return policy is introduced.
