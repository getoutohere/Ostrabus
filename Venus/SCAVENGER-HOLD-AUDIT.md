# VORB scavengers drifting while holding

Original investigation against game 1.0.1.10, retained below as historical
evidence. Its proposed repairs/policies are now implemented and validated in
[scavenger recovery and orbital waiting](../Navigation/SCAVENGER-WAITING.md).

## Observed failure

Before the player's departure for Mercury, two VORB ScavPilots had living pilots,
full RCS tanks, no target/route/combat/attachment, zero commanded acceleration and
Hold active with bIgnoreGrav true:

- O-Y134 / Mesmer Soldier: Autumn Cassidy Porter, 1,873.4117 kg RCS; later
  3,964 km from VORB at 4.947 km/s relative speed.
- O-YM3L / Kinetic Rock n Roll: Jada Orr, 749.3647 kg RCS; later 1,194 km
  from VORB at 2.638 km/s relative speed.

Both had repeating visit countdowns and positive fAIPauseTimer values near the
absolute game epoch. Player.log records "Could not find a valid target,
unregistering" for each. Our recovery patch retains these controllers, so the
native log text does not establish actual unregistration. O-Y134's live comms
record SHIPSosStranded to VNCA at epoch 65633393023.032494 and
SHIPNegativeNoAction about one second later, despite its full tank.

Read-only evidence is in ignored obj/VenusDepartureSurvey. The player advanced
SFF and left Venus during investigation. Later cleanup is not autonomous recovery.
Individual rejected candidates and pre-departure help-condition tickers were not
captured; post-cleanup NPC conditions cannot establish their earlier state.

## Authored role versus mechanism

Native ScavPilot.Main is Undock -> TargetNewIfNone -> FlyTo -> Dock -> Hold.
GetTarget chooses a wreck-search branch with 50% probability; otherwise it picks
a random regional station. Wreck checks include derelict damage state, docking
ports, no existing attachment, local-authority/tutorial exclusions, 5,000 km
range and fuel feasibility. These are not exclusively local salvage vessels.

The installed TXTVNCASpawnStations asset contains VNCA, VCBR, VLA01, VENC and
VORB. RandomScavShipVORB contains Squall/Halberd/Myna/Ocelot; native
AIShip.SpawnNewShip actually prefers that station-specific table. Atmospheric
and orbital destinations therefore share this role. Its selection branching is
hardcoded C#, not JSON behavior policy. Ship/station weights alone cannot repair
the continuation below.

TargetNewIfNone.FindTarget makes up to nine random candidate attempts, not an
exhaustive reachable-destination search. Failure invokes RequestHelp without
first proving literal tank exhaustion. The asset's "out of fuel" message can
therefore appear on a full ship. This audit does not establish why each historical
candidate was rejected, or that all possible routes were infeasible.

## Confirmed defect chain, including our recovery gap

1. Native station-SOS handling unregisters the scavenger. Our existing
   MiscBugFixes/ScavEmergencyRecovery.RequestHelp returns true to retain it.
2. The native caller then returns Success with no target, setting the absolute
   deadline fAIPauseTimer = StarSystem.fEpoch + 64. Main advances to FlyTo.
3. FlyTo waits for that deadline, then its targetless manual branch fails.
   Dock.CanExecute rejects the absent target. StickySequence reaches Hold.
   Our earlier recovery tests covered the immediate SOS result, not this
   continuation. Retaining the controller was necessary but insufficient.
4. Hold normally rejects relative speeds above 25 m/s. A positive pause value
   or nonnegative visit timer bypasses that check. Hold decrements the pause as
   a duration, although its producers and flight consumers use an absolute
   deadline. A past epoch thus continues bypassing speed eligibility.
5. Hold cuts thrust and disables gravity; it does not match station velocity.
   ShipSitu preserves linear motion while VORB follows its prescribed orbit.
   Relative speed can increase without thrust. This is not a torch burn.
6. Active ShipRequestedHelp can retain Hold across timer expiry despite full
   fuel. Its asset duration is nominally three minutes. CondOwner's normal
   deferred ticker update skips shallow ships; catch-up is a separate path.
   Actual shallow expiry must be established before claiming permanent flags.

OrbitalSimulation, CoastIntegration and SffSimulation explicitly exclude
bIgnoreGrav situations. Overriding every such state in Orbital would repair the
wrong layer and affect other modes.

## Original recommended direction

- Repair the retained-controller handoff: stay at a throttled selection/recovery
  step until a valid target exists, rather than advancing into visit Hold after
  failed selection. Keep authored SOS, reply and dispatch evaluation authoritative.
- Correct pause-clock semantics at the consumer, auditing civilian, dispatch,
  rendezvous and atmospheric Hold users before generalizing.
- Separately classify free orbital waiting under gravity as a simulation
  improvement. Preserve aircraft hovering, docked groups, body locks, navigation
  and combat. Actual station keeping additionally needs a reference and native
  steering/fuel behavior; enabling gravity alone is orbital coasting.
- An optional selection improvement could try a feasible native return after
  unsuccessful sampling or favor local salvage for orbital scavengers. Preserve
  capable ships' legitimate Venus travel. The existing asset interface cannot
  supply this hardcoded role with a new fallback algorithm.

First repair the continuation, then reassess policy. Do not force successful
journeys, erase momentum, refill fuel or teleport ships as an automatic repair.
Verify refused/delayed/failed assistance, actual shallow help expiry, save/reload
and SFF before considering that repair complete.

## Offline reproduction

```powershell
dotnet build Tests/ScavFuelDiagnosis -c Release
dotnet run --project Tests/SaveJsonCompatibility/Host -c Release --no-build -- Tests/ScavFuelDiagnosis/bin/Release/netstandard2.1/ScavFuelDiagnosis.dll vorb-hold
```

The initial pre-repair reproduction passed 44 cumulative assertions: 23 existing
asset/SOS checks and 21 new checks. It ran native Main/StickySequence, selection caller, targetless
FlyTo/manual failure, docking eligibility and Hold with the actual five scav
recovery patches. Native contrast requests unregistration; patched execution
retains the controller and reaches targetless gravity-exempt Hold. Help presence
blocks repeatedly; removing it releases Hold. Timer bypasses and clock mismatch
are also exercised.

Candidate-search failure, speed/atmosphere inputs, message delivery/application,
help-condition presence, clock and Unity maneuvering are explicit fixtures.
This does not reproduce the original random search, route solver, condition expiry
or trajectory. No live game state was changed.

The scav behavior graph previously left post-RequestHelp caller/tree continuation
outside its expanded boundary. It now includes that continuation and the repair.
The updated `vorb-hold` suite lives in Waiting.cs/HelpExpiry.cs and passes 156
checks; see [current results and fixture limits](../Tests/ScavFuelDiagnosis/WAITING.md).
