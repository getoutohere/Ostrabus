# Hull radius integration audit

This audit follows the enlarged nav hulls into native movement/placement code.
The regressions were in our integration: keeping shared game radii intact was
necessary for combat compatibility, but leaving every non-physical consumer on
those old radii was not sufficient.

## Evidence and scope

Reviewed game 1.0.1.10, assembly SHA-256
`8058f7a07f93d5d44678aaf8d7f749050c0d9b1c9ed1c7419eb521ddcd2097e7`.
[HullConsumerAudit](../../Tools/HullConsumerAudit/Program.cs) enumerates **169
direct references in 106 methods**, including generated coroutine/lambda bodies,
Size accessors, ShipDraw wrappers and placement callers. The local report is
`obj/hull-consumer-native-inventory.json`. This is a reproducible search boundary,
not a proof of every possible gameplay interaction.

Native C# owns the geometry expressions. Shipped installed-item socket definitions
provide the footprints; the harness loads actual parts and hull assets. Reviewed
`guipropmaps/guipropmaps.json`'s DockSys binding and `condtrigs/condtrigs.json`'s
TIsCollisionSafe: the latter is an OR of new-player and undock-grace conditions,
not a docking range or navigation policy. No asset correction or second AI
controller is appropriate for these integration failures. Native clearance,
crime, triggers, fees, command selection and emergency behavior remain authoritative.

## Consumer dispositions

| Consumer family | Disposition and reason |
| --- | --- |
| CheckCollisions / Ship-Ship ProcessCollision | Existing optional exact hull contacts and contact-face response. Shared native damage/impulse/grace retained. |
| GUIDockSys CanDock, ConvertShipToRing, SetUI | Corrected cached bounds; original alignment, clearance, ports and 10% margin. Positive approach window for hulls exceeding 10 km. |
| PlaceAtAirlocks; CreateDockingGroupFormation, RebuildDockingFormation, group merge callers | Corrected individual bounds for an existing shared formation; bounds enclosing both complete groups when joining separate groups. No repeated inflation of an already-shared group. Native port rotation and group propagation retained. |
| PushbackFrom / PlaceOrbitPosition; pilot creation, BaseCommand instant arrival, Dock.ResolveInstantly, GUI instant dock, endgame callers | Resolve exact live identities only. Use complete bounds around a ship or anchor, original 10 m gap and native lock guards. Unknown copied positions and excluded types keep native placement. Reset swept-motion history after explicit relocation. |
| SetSituToSafeCoords, SetSituToRandomSafeCoords, SetSituToRandomSafeCoordsWithinBelt | Snapshot free-ship radii once per explicit search. Keep native sampling, retry limits, return values, planet checks and beacon exclusion size. A moved anchor ignores its own still-unpropagated members. Anonymous spawn points retain their own native size while avoiding known larger hulls. |
| BaseCommand.GetHandOffDistance; Dock, FlyTo, FlyToManual, FlyToPath, TargetNewIfNone and fuel-progress callers | Correct the input for eligible actual ship targets; retain native multipliers. No route/fuel/controller replacement. Target-less points, stations and celestial destinations remain native. |
| ShakeDown.HandlePlayer | Correct its two local radius reads and ensure its acceptance range includes the existing FlyTo handoff. Native 5 km minimum previously hid the two-versus-three-radius mismatch on smaller hulls; enlarged hulls/groups could otherwise cause repeated re-approach until timeout. Crime/boarding decisions remain native. |
| Undock departure clearance | Original max(current distance * 2, native radii * 3) remains. Corrected pushback already puts the departure outside both models; this chooses an outward retreat and cannot require penetrating its former partner. |
| HaulShip trimming / delivery | Native authored station destinations are excluded from the free-hull correction. Cargo delivery thresholds and fuel accounting retain their original semantics. No new free-space delivery policy is introduced. |
| FlyToPath.PlanTrip4 RCS endpoint offset / torch endpoint | Native endpoint construction retained. The ordinary actual-target handoff is corrected separately; predicted/anonymous points are not rebound to hull identities. Torch endpoint and trajectory algorithms are outside this feature. |
| AIShipManager.IsOnCollisionCourse | Feed the native predictor numeric scratch situations with complete hull/group bounds. Keep its doubled moving circle, obstacle exclusions, stopping-distance filter and native evasion choice. Compose with the existing future-only correction in either patch installation order. |
| Existing AIShipAvoidance.SelectEscape / ObstacleSafe | Within that one AI query, pass matching numeric bounds to the existing planet-safe alternative check. No extra planet/ship controller; no registry lookup per candidate. Scope releases actor situation references on return/exception. |
| NavModMooringControl CanMoor / UpdateSliders | Correct actual ship-to-ship tether clearance; retain 2.5 km margin and 50 m/s limit. Asteroids and stations remain native. |
| MFDDockingClearance / NavModTargetData ship and station-keeping range | Correct the local free-ship radius expression so displayed range and available targets agree with the docking approach. Celestial-body overloads untouched. |
| Projectile scans, missile launch, Shoot, WeaponsSystem, MM defense, weapon target-lock vectors | Untouched. Their radii are part of combat, not the new physical hull contract. |
| Planet/asteroid impact checks, gravity/atmosphere queries, SFF collision warnings, orbit/body creation | Untouched. New physical hull scale is not written to Size or used to enlarge planets/atmospheres. |
| Comms delivery time, Interaction.IsWithinShipRange, MFDShipSelect comm range | Untouched. Communication range/time policy does not authorize physical approach or positioning. |
| ShipDraw status markers, symbolic icons, map hit selection, stellar-object grouping | Native symbolic sizes retained; detailed outline selection/scale already uses the outline module. Clickable icon extents and weapon/status brackets are not physical collision authority. |
| PredictionRectangle | Native source has no construction/use outside its own class; not an active collision-avoidance path in this assembly. No speculative hook. |
| Size setters, initialization, saved JSON, CopyFrom, native DockGroup.CalculateAnchorSize | Untouched. Corrected bounds remain derived numeric data, not serialized native size mutations. Group integration reads existing member offsets, not obsolete member world positions after anchor relocation. |
| Native debug/test coroutines | Inherit corrected placement only for live eligible hulls. Their anonymous situations and explicit test sizes remain native. |

## Confirmed reproductions and repairs

The saved Stellar Magnificence/Prophet case reproduces a hull impact at about
1720 m while native clamps required 1232 m. Its corrected placement is 1794.38 m.
Native undock pushback independently reset separation to the old sum; the earlier
group-removal test did not execute this path.

The expanded harness also reproduces undersized safe searches, native self-group
retries after pushback, an AI prediction missing an enlarged hull, tether clamps
requiring hull penetration, the inspection/handoff range gap, and artificial
swept impacts caused by short instantaneous placement. Controls deliberately
restore old self-group radius reads or disable motion invalidation to demonstrate
the group retries and artificial swept impacts before testing the repairs.

Actual native methods run under bundled Mono. Environment boundaries are Unity
random sampling, far-body lookup, mass/selected-crew services and UI curve/mesh
services; native positioning, safe-search loops, group merging/propagation,
mooring eligibility and AI prediction execute with production hooks. The police
inspection check validates range acceptance against the real patched handoff;
it does not emulate a full boarding/fine/crew scene.

The default consumer suite adds 61 assertions, including a two-versus-two undock
and four-member merge, scope cleanup after a thrown native search, both toggle
directions and warmed no-allocation prediction. The saved-pair run totals 1,356
integration assertions, including 378 docking/pushback checks, alongside 60,687
worker/ownership and 12,465 contact checks. Full-plugin AI interop executes both
patch installation orders and observes matching clearance reaching the existing
planet-safe evasion. Full-plugin binding and an isolated hull-only custom package
must also pass before deployment.

## Performance and remaining limits

No additional frame callback, repeated hull scan or worker wait. GUI reads are
cached numeric queries (plus small existing group-member loops). AI candidates
reuse two numeric scratch situations, with no retained NPC/ship references in
them. A bounded operation scope hands the chosen radii to evasion and clears
its transient actor references afterward.

Explicit safe searches add one registry pass to native searches which already
scan ships and may retry. Their dictionary capacity is reused; Clear releases
all actor keys. Exact anonymous-anchor identity lookup and relocation-history
invalidation also occur only on explicit placement, never every frame.

Cold header bounds and AI/placement circles are conservative enclosures, not
exact rotated hull packing. They can leave more approach space than necessary.
Physical narrow-phase contacts still use the exterior cover, and unknown-contact
outline visibility remains a sensor decision. This audit does not certify Unity
rendering, every live boarding scene, collision feel or a measured frame-time
improvement; live validation of this consolidated build is pending.
