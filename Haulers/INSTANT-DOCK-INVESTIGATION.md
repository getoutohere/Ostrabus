# Player debug instant docking versus AI instant arrival

Investigation only, 2026-11-09, game 1.0.1.10. No instant-dock API implemented and
no live ships moved as part of this investigation.

Follow-up: the requested behavior was clarified as native station retirement
and population transfer. That is now available through the separate
[station-arrival API](../Inspection/STATION-ARRIVAL.md), rather than physical
dock-and-hold. The physical-docking investigation below remains applicable.

## Actual player debug button

GUIOrbitDraw.OnTravelClick force-undocks the controlled ship if necessary, checks
destination docking compatibility, copies destination kinematics, places the ship
near it and calls GUIDockSys.ForceDock. ForceDock creates debug clearance and uses
the ordinary clamp/coroutine path into CrewSim.DockShip. Mooring has a separate
branch for targets without ports. The button depends on the active player's UI,
coroutines and scene state; it is not a ship-parameterized generic AI operation.

CrewSim.DockShip materializes the target, updates scene visibility, animations,
crime/tutorial/navigation state and can initiate hostile boarding pledges. It then
uses Ship.Dock with selected port IDs and positions the physical scene at airlocks.
Calling the whole UI route against arbitrary shallow AI would bring unrelated
player/scene behavior and should not be advertised as a safe direct reuse.

## Physical docking preserves the vessel and occupants

Ship.Dock's physical layer establishes reciprocal links and native DockGroup
formation, matches destination velocity/rotation, writes saved docking links and
runs normal docking fees/logging/wear/events. It does not call DockAndDespawn or
transfer/delete occupants. Geometry-compatible open ports still need validation.
Do not bypass those checks merely because the operation is an explicit teleport.

Tests/AIShipLifecycleCheck with argument `instant-dock` passes five checks using
the shipped CopyFrom, PlaceOrbitPosition, explicit-port Ship.Dock and native group
formation. It verifies reciprocal links, shared group, destination velocity,
unchanged living crew aboard original hulls and no hide/delete/retirement call.
Mass data, selected-crew UI, fees/log text and accrued wear are boundary substitutes;
native port geometry selection and the full Unity scene coroutine are not claimed
as tested. The existing lifecycle fixture separately demonstrates native AI instant
arrival reaching station cleanup, including its connected-crew enumeration.

## AI command with a similar name has different semantics

Ostranauts.Ships.Commands.Dock.ResolveInstantly calls AttemptToDock. At a body/station
target, its non-player branch calls CrewSim.DockAndDespawn instead of leaving the
ship physically parked. DockCargoAndDespawn.ResolveInstantly also performs native
station retirement and can process/destroy attached derelicts. Neither is a generic
substitute for the player button when the requirement is preserving hull and crew
aboard it.

DockAndDespawn transfers crew only when TIsValidStationDropOff and the station
faction check permit it; otherwise it explicitly destroys them. It hides the hull,
records the despawn station and unregisters AI. Hidden hulls can be reused by
FindShipForFaction, but this is not guaranteed permanent hull preservation:
unregistration may mark an unlocked hull IsStale, and CleanupStaleShips can later
retire eligible detached hulls. Do not promise that native AI instant arrival
unconditionally preserves either all NPCs or the hull forever.

## Proposed API semantics

For physical preservation, accept explicit source/destination registry IDs,
preview compatible ports and any current connections, reject player/plot/unsafe
connected-party cases, clear abandoned controls/routes, then use the native
physical layer. Verify reciprocal links and all original crew IDs afterwards.
Do not silently retire ships, consume attached wrecks or invoke player's scene UI.

Physical docking does not cancel the registered AI. Its next command may undock,
continue hauling or perform station retirement. A dock-and-hold operation therefore
needs an explicit controller policy and save/load/resume handling; simply claiming
the hull is durably rescued after one successful Dock call would be misleading.
This lifecycle decision should be settled before API implementation. The existing
hauler/rescue endpoint requests a normal station return and has different semantics.
