# Civilian random visits exclude derelicts

Implemented as a simulation policy, not a claim that vanilla's choice is a
programming error. Ordinary civilian traffic no longer chooses derelict hulls
as random docking destinations. Scavengers retain their salvage selection.

## Native mechanism and ownership

`Ostranauts.Ships.AIPilots.CivilianPilot.GetTarget()` draws between asteroid
prospecting, ship visits and stations. The ship branch builds a list using its
private `CanDockWith(Ship, Ship)`. That helper admits visible, shallow,
unregistered derelicts with ports. No asset trigger controls this choice.

Reviewed `loot/loot.json`: `RandomCivilianShip` and its CCRE/Galcon pools define
hull selection, not roaming target eligibility. Native `ScavPilot.GetTarget`
uses its separate `ScavCanDockWith`, which requires
`Ship.Damage.Derelict`. No JSON override can express this target restriction
through the reviewed mechanism; one C# prefix applies the requested policy.

`CivilianDerelictVisitsPatch` rejects that same native damage category only in
the civilian helper. Asteroid-class objects retain native eligibility even if
their damage field says Derelict. Other damage categories still pass through
the remaining native rules; crewlessness alone does not redefine a derelict.

The filter does not redraw random numbers, change weights or select a substitute
destination. Native sampling, reservations, range/fuel checks and station
fallback remain authoritative. Removing wreck candidates can make a station
visit more likely when no ordinary ship is suitable.

## Scope and existing saves

This changes future random selections. Existing assigned or docked visits finish
normally; loading the mod does not detach ships, erase destinations or rewrite
saved AI progress. Remembered-partner FFWD continuation and explicit orders keep
their native handling. Scavengers, retrieving/deploying haulers, freight/PASS,
police/combat targeting and player navigation do not use this private civilian
candidate helper and are not restricted by this prefix.

No new saved state, runtime registry, polling or periodic scan is added. Work
per candidate is a damage/classification check; rejecting it avoids subsequent
native eligibility work. The custom-builder feature
`civilian-no-derelict-visits` is independently selectable under Simulation and is
included by Recommended/Full, not Bugfixes-only. It composes with the existing
hidden-visit bugfix and asteroid-visit duration policy.

## Verification

`Tests/AiPlannerDiagnosis/DerelictVisitPolicyChecks.cs` executes the shipped
selector and private eligibility methods. Native control chooses a derelict;
the production filter rejects it and permits native station fallback. Controls
cover all native damage categories, ordinary ship visits, asteroid eligibility,
scavenger eligibility, explicit cargo/PASS targets and hidden-visit composition.
RNG, crew construction and fuel affordability are fixture boundaries; this is
not a full live voyage test. Run the `civilian-derelicts` harness mode.

Validation: 23 policy assertions plus 64 shared native traffic controls pass.
The existing visit-lifecycle (146), hidden-visit (113) and visit-waiting (47)
suites also pass. All 303 full-plugin patch classes bind without failures or
warnings. Standalone and combined visit-policy packages compile, preserve
full-build ordering and bind cleanly; 601 custom-builder self-checks pass. The
Traffic graph has 577 checks, and the reviewed source/assembly/asset audit is
clean. Full Debug DLL/PDB build without warnings. Pending live playtest.
