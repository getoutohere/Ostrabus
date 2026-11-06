# Shipboard action simulation

This module simulates combat after hostile AI ships dock or moor together. It is
implemented separately under `Ostrabus.ShipboardAction` and runs from an
`AIShipManager.Update` Harmony postfix.

## Encounter lifecycle

- The module scans docked groups every 10 in-game seconds. A group starts an
  encounter when it contains at least two of CCRE, Galilean Confederacy, and
  Belt Pirates, and every ship in the group is docked and is not a station.
- Those three factions are treated as mutually hostile.
- An encounter schedules one casualty event after a random 90–300 in-game
  seconds. The side's risk is inversely weighted by its active crew count and
  detected weapon score. A selected crew member has a 20% chance of critical
  injury; otherwise they are killed and pruned when `TIsNPCPrunable` allows it.
- An encounter pauses while a participant is materialized above `Shallow`, or
  while the player's ship is connected to the participant group by docking or
  mooring. The game handles loading the ships and NPCs; the module only stops
  its shallow simulation. It resumes from the current crew state after the
  group is no longer player-connected and all encounter ships are shallow.
- The casualty interval and random state are saved on the participating AI
  Blackboards as primitive values. The module mirrors encounter state to all
  participant AIs at start and after each casualty. If one AI is unregistered,
  another participant's copy can continue the encounter. The current code has
  no revision-based reconciliation if copies ever diverge; it assumes these
  main-thread writes keep them identical.

## Resolution

- When only one faction has active crew, it wins. If no faction has active
  crew, the faction with the most living crew wins; ties are chosen randomly.
- Living losing crew are killed and pruned when the game's trigger permits it.
  Participating ships are undocked.
- A winning side can take one losing ship as a prize if it has more than one
  living crew member and at least one healthy pilot candidate who is not an
  existing AI captain. Captains remain on their original ships.
- The prize hull must not be derelict, a home station for the winning faction
  must exist, and the game must report enough RCS delta-v for the trip home.
  The selected winner is transferred aboard, the hull owner/faction changes to
  the winner, and a Navy AI is assigned to return it. Existing enemy/combatant
  references to the prize are cleared from the winning faction's AI ships.
  The return route uses the game's `FlyTo` and `Dock` commands. A prize that
  fails the capture checks is left adrift.
- A surviving winner ship returns home if its crew has a critical injury, it
  has fewer than two crew, or the game's low-fuel or homeward delta-v check
  triggers. The shared `Navigation/StationReturn` controller drives the game's
  `FlyTo` and `Dock` commands, preserving destination and docking stage across
  reload without queued custom command trees. Old return flags migrate on the
  existing return monitor; unrelated queued commands are retained. The return
  monitor calls `CrewSim.DockAndDespawn` when it docks at its home station.

## Injury and equipment checks

The module treats unconsciousness, stun/recovery, coughing blood, internal
bleeding, pulmonary hemorrhage, and severe blood conditions as critical. A
nested object marked `IsWoundVital` is not sufficient by itself: its blunt plus
cut wound amount must reach the game's 0.5 stun threshold. This avoids treating
vital anatomy as an injury.

Weapon reporting and casualty weighting use the module's detected conditions:
currently wielded ranged weapons score highest, carried ranged weapons next,
and other detected weapons lower. These checks do not prove a weapon is
functional or usable in the shallow simulation.

## Inspection

`GET /shipboard` in the read-only Inspection API reports active encounters,
returning ships, and the latest resolution. During an encounter, each
participant includes current crew/load state and its start readiness string:

`alive/active/rN/wN/uN`

- `alive`: living crew at encounter start.
- `active`: living crew not classified as critically injured.
- `rN`: crew with a detected ranged weapon.
- `wN`: crew with another detected weapon and no detected ranged weapon.
- `uN`: crew with no detected weapon.

The encounter also reports `startEpoch`, `nextCasualtyEpoch`, and
`secondsUntilNextCasualty`. After resolution, `lastResolution` records winner,
survivors, healthy non-captain pilot candidates, prize result, and prize checks.
The detailed start snapshot is logged when an encounter begins. Runtime
snapshots and logs reset when the plugin/game session restarts; encounter state
itself is stored on AI Blackboards for save persistence.

## Timing and performance

The encounter scan runs every 10 in-game seconds, and return-to-home checks run
every 5 in-game seconds. Scratch collections are reused for simulation scans;
the start snapshot and resolution paths allocate small temporary collections.
No background thread accesses Unity objects. Inspection captures data on the
Unity thread and returns detached values through the HTTP API.
