# Explicit station-hauler rescue

The purpose is to preserve an existing stranded pilot/crew and let them return
to the station population through native arrival behavior. Pruning a wreck must
not require deleting its living hauler crew. This is an operator-requested repair,
not an automatic simulation rule or another fuel/navigation guard.

- `GET /ships/{id}/hauler/rescue`: eligibility and full repair preview.
- `POST /ships/{id}/hauler/rescue`: revalidate and apply that repair.

No call signs, save names or regional distance thresholds are hardcoded. The
endpoint supports visible shallow station-local HaulerRetriever/HaulerDeployer
controllers whose native home exists in the current ATC region. Cargo/passenger
controllers are excluded, as are player/plot ships, active combat/reactions,
pending priority commands, unknown fuel capacity and already-requested returns.
Any connected hull must be an uncrewed non-player, non-plot shallow derelict with
no AI controller. The hauler must have resolved living non-player crew and at
least one conscious pilot. No replacement people or controllers are created.

## Apply behavior

1. Before mutation, validate every connected hull, home and crew member. Check
   native TIsValidStationDropOff and the same station-faction rejection used by
   CrewSim.DockAndDespawn, refusing a rescue that would currently delete its crew.
2. Stop the abandoned maneuver and use native tow-brace release/Ship.Undock for
   reciprocal docking links and group teardown. Verify detachment succeeded.
3. Explicitly repair the hauler's position and motion: place it 100 km outward
   from home relative to home's nearest physical body, match home's velocity,
   clear thrust/accelerations/spin and discard the old route. Preserve heading
   modulo one revolution. This deliberately relocates the bug-stranded ship;
   it is not claimed to be a physically simulated trip from its corrupt position.
4. Use native AIRefuel with the known shallow tank capacity, then request the
   existing persistent StationReturn behavior. Native FlyTo/Dock handles the
   approach and CrewSim.DockAndDespawn handles eventual retirement/crew transfer.

The ship, NPC identities, crew placement, owner and existing controller are
retained at request completion. The native return is requested, not instantaneously
declared complete. Normal subsequent simulation still applies, including injury,
combat or changed station eligibility; a preflight check is not a permanent
guarantee of every future NPC outcome.

Wrecks remain at their existing locations and are not pruned, moved or refueled.
After successful detachment they can be submitted individually to the existing
pruning API. Incomplete native detachment reports failure without relocation;
earlier successful detachments are not artificially rolled back.

The preview exposes crew/wreck IDs, relocation position, approach distance,
velocity/spin repair, fuel before/after and return behavior. There is no automatic
sweep, polling or new Harmony hook. Apply uses the existing main-thread API queue.
Return intent uses the existing StationReturn save-compatible scalar keys and
weakly keyed command cache; the endpoint adds no persistent object references.

## Offline validation

Build Debug Ostrabus and Release Tests/AIShipLifecycleCheck, then run that test
assembly through Tests/SaveJsonCompatibility/Host with argument `rescue`.
Eighteen checks exercise the production API, explicit physical repair, native
AIRefuel, target/return intent, retained pilot/controller, preview/repeat behavior
and refusal of invalid/hostile drop-off, dead/unconscious crew, player tow, combat,
missing home, unknown capacity, materialized state and queued work.

World/plot/faction/condition eligibility data and scene-undocking boundaries are
substituted in this harness. It does not execute an entire live Unity docking
scene or assert that a later station arrival has already occurred. The endpoint
requires deployment/restart before it can inspect or rescue the four observed
live haulers; no rescue or wreck pruning was applied during implementation.
