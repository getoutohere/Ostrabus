# Complete native AI station arrival

`GET /ships/{id}/arrival` previews completion at the AI's recorded HomeStation.
`POST /ships/{id}/arrival` revalidates and completes it immediately. An explicit
destination uses `/ships/{id}/arrival/{stationId}` with the same GET/POST semantics.
Use exact registry IDs. Requests execute through the existing main-thread queue.

This supports visible shallow police, salvagers, deploying haulers and retrieving
haulers. Cargo/passenger/interplanetary and other combat controller types are
excluded. It does not create people, substitute a controller, refill fuel, enforce
an artificial distance/fuel requirement or install another Harmony hook.

## Native behavior

- Police/salvagers release any eligible attached empty wreck through native
  tow-brace release and Ship.Undock. Those wrecks remain in the simulation.
- Haulers retain eligible empty tows for native DockCargoAndDespawn delivery;
  its normal processing detaches, retires and destroys those derelict hulls.
  Preview explicitly lists the wrecks that will be consumed.
- Both paths cancel abandoned return intent/navigation, bind the station and use
  native PlaceWithinDockingRange. Police/salvagers then execute Dock.ResolveInstantly;
  haulers execute DockCargoAndDespawn.ResolveInstantly. Both reach native
  CrewSim.DockAndDespawn.
- Native cleanup transfers original crew objects into the station, changes their
  ship claims/permissions, schedules ordinary station activity, hides the hull,
  records its despawn station and unregisters AI. Crew can appear when that
  station is materialized. There is no physical dock-and-hold operation.
- The retained hidden hull follows vanilla reserve/reuse and later cleanup policy.
  Permanent hull preservation is not promised; the API itself does not destroy it.

This is the native station-return lifecycle requested for rescue, rather than
the player UI's physical instant-dock or the earlier 100 km approach/refuel repair.

## Crew and connected-party protection

All original crew must be resolvable and non-player. Preview checks the current
TIsValidStationDropOff result and the native station-faction Dislikes rejection.
POST first performs native crew CatchUp, then repeats those checks against the
updated state before entering arrival cleanup. A dead, otherwise rejected or
hostile-to-station crew member refuses the entire arrival instead of intentionally
entering native's deletion branch. An unconscious living NPC is allowed: this is
completion of arrival, not an assertion they can pilot the ship.

POST CatchUp may legitimately update conditions even when arrival is subsequently
refused; GET does not call it. No revival, identity replacement or faction rewrite
is performed. Execution occurs synchronously at one game epoch, so native cleanup's
second CatchUp does not represent another elapsed journey.

Player/plot hulls, player crew, materialized source ships, pending-removal hulls,
invalid destinations, crewed attachments, controlled attachments and unresolved
docking groups are protected. Connected hulls must be directly attached empty
non-player/non-plot shallow derelicts. This prevents inclusive native crew
enumeration from accidentally collecting a neighboring vessel's occupants.

POST verifies that each original crew object is alive on the destination, the
original hull is still registered in the world but hidden with the correct
despawn station, its AI is gone, and tow outcomes match the selected path.
Unexpected native partial failure returns HTTP 500 with observed state rather
than claiming success. A failed ordinary detachment returns 409 without station
retirement. There is no synthetic rollback of native side effects; inspect a
failure before retrying. Repeating a completed request is refused because there
is no longer an active controller to retire.

## Offline validation

Build Debug Ostrabus and Release Tests/AIShipLifecycleCheck. Run its DLL with
argument `arrival` through the bundled Mono host in Tests/SaveJsonCompatibility/Host.

136 checks cover all four roles with both shallow and Full destination fixtures,
GET preview, default/explicit station selection, native crew transfer decisions,
original identities and ship claims, controller removal, hull retirement,
hauler cargo/tow processing, police/salvager wreck release, no refueling, repeat
requests and refusal cases. CatchUp changing crew eligibility and native exceptions after partial tow processing are explicitly tested.

The shipped Dock/DockCargoAndDespawn, CrewSim.DockAndDespawn, crew claim changes,
target placement and AI unregistration run. Unity MoveCO/visualization, actual
scene undocking and wreck object destruction, market transfer, condition/faction
data and logging are fixture boundaries. This does not claim a full live Unity
station scene or save-file roundtrip test. Native MoveCO/AddCO source review
establishes the ordinary loaded/shallow station handoff; live arrival remains
observable through the API's resulting crew/hull state. No live ship was moved
during implementation.
