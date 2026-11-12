# Torch autopilot with braced tows

Implemented November 11, 2026 against Ostranauts 1.0.1.10. Offline validated;
normal powered towing and a six-hour powered SFF confirmed live November 12.
The later midpoint reading confirms intact towing through the transition to coast.
The final-approach reading confirms intact towing through braking as well.
**Live autopilot completion/arrival validation remains pending.** Always on in a full development build;
custom builds select `torch-towing`. This feature does not depend on the separately
selectable [torch fuel/control corrections](../TORCH-COURSE-FIX.md).

## Confirmed native defects

`Ostranauts.ShipGUIs.NavStation.NavModCoursePlot.EngageTrip` installs its native
`NavData` on the piloted ship. `ShipSitu.CopyFrom` does not copy that course into
the synthetic `SolarSystem.DockGroup.Anchor`.

`StarSystem` updates individual ships before its docking groups. A member's
`ShipSitu.TimeAdvance` can therefore execute autopilot, update controls and consume
fuel, only for `DockGroup.TimeAdvance` to overwrite its position, velocity and
orientation from an independently integrated anchor. The anchor can also preserve
thrust/spin that the course already stopped. This is a movement ownership defect,
not an intentional ban on torch-drive towing.

The related engine/planning defect is that `Ship.GetMaxTorchThrust` and
`Ship.SetThrust` divide by the powered ship's mass alone. RCS already includes
attached ships. `FusionIC.Fusion` also multiplies the reported acceleration by
mass to recover engine force; that inverse conversion must use the same mass or
the tow would be penalized twice.

The baseline native fixture reproduces fuel consumption followed by lost course
movement, and unchanged torch acceleration after adding tow mass. The older
[retriever investigation](../../Haulers/OKLG-RETRIEVER-RUNAWAYS.md) independently
reproduced the group-motion fault with an RCS course. Its authored HaulShip handoff
repair remains applicable; this module repairs the shared movement mechanism.

## Repair

`DockedCourseMemberPatch` defers a member's plotted movement until the group's
integration step. `DockedCourseGroupPatch` replaces only the anchor's integration
call: it executes the owning member's original `ShipSitu.TimeAdvance` once, then
transforms that ship's position/orientation back into anchor coordinates.
This happens before native group G-load/wear calculations, so a coast or abort
does not expose the crew to the previous burn's load for an entire SFF step.
The later native anchor integration call is skipped for that scoped step. Native
group lift/drag calculation still runs exactly once, before course playback.
The native group loop propagates the result to every member using existing rigid
offsets. The source retains sole ownership of its course, fuel, completion and
abort state; no copied course or second fuel debit is introduced.

This supports one native course controlling a free connected group, including
chains with more than two ships. It applies to the shared plotted-motion path,
including RCS segments, rather than inventing separate torch trajectories.
Anchored/orbit-locked ships, mooring targets, groups already owning an anchor
course and groups with conflicting member courses retain their native path.
Choosing between competing pilots is an unresolved native policy, not something
this repair silently decides by list order.

`TorchTowMassPatch` substitutes combined live group mass at the native mass
conversions in `GetMaxTorchThrust`, `SetThrust` and `FusionIC.Fusion`, with the
moored-departure preview distinction below. Physical item/ship masses and the
engine's force and fuel formulas stay intact. The same
throttle produces the same engine force, lower acceleration and a longer trip
when towing. Manual torch burns receive the same mass correction. Existing
solo-ship behavior is unchanged. Replot a course made before this repair for
correct tow-aware acceleration and ETA; saved legacy waypoints are not rewritten.

## Departure previews while docked at a station

The first live post-trip preview at VORB exposed an overbroad mass sum: the
station was included alongside the tug and tow, yielding 284,135 kg and roughly
2.66 G at full throttle. The shipped star-system JSON classifies VORB and its
auxiliary as orbital stations. Native `Ship.IsStation(true)`, substation/ground
classification and `DockGroupMember.IsMooringTarget` provide the boundaries;
no station names or asset overrides are required.

For a prospective departure from a station/mooring, `PredictionMass` starts at
the powered vessel and follows direct connections secured by a native clamped
towing brace on either side, including downstream chains. It stops at station/
mooring members, even if a brace happens to secure that link. Ordinary docking
does not imply an intended tow. Other ships reachable only through the station
are excluded. Native brace markers/port associations remain authoritative, and
the calculation reads current links/brace state rather than caching membership.

This is a departure preview, not a change to physical coupling. In free flight,
the estimate still includes **all actually attached hulls**, including unbraced
ones. `SetThrust` always retains full physical group mass. Since native Fusion
recovers engine force by multiplying GetMaxTorchThrust's estimated acceleration
by mass, its inverse conversion uses the same prediction mass; otherwise the
preview correction would accidentally multiply engine force while moored.
Native fuel/force formulas, RCS steering, stress accumulation and catastrophic
unbraced damage remain unchanged. Existing anchored-group exclusions and the
separate user-requested torch-autopilot brace-release abort remain intact.

No new Harmony target is added. A thread-local scratch list/set is reused, with
references cleared in finally; reentrant calculations use independent scratch.
The offline checks measured zero allocation over 1,000 warmed repeated moored
previews. Preview checks cover either-side braces, chains, release, stations,
substations, ground stations, moorings and station visitors. Native SetThrust and
joint native Fusion checks verify that runtime mass and engine force do not
inherit the departure-only discount. The native unbraced stress accumulator is
also driven above its damage threshold, with only the damage/RNG boundary
substituted, confirming the catastrophic path remains reachable.

Validation: 343,871 towing assertions (including 100 station-departure checks),
402,667 joint torch assertions, all 228 full-build patch classes bound cleanly,
and a towing-only custom build compiled/finalized and bound its three patch
classes without failures/warnings. Custom builder self-test: 418 checks. The
Debug DLL/PDB are rebuilt; the moored preview correction is pending deployment
and live confirmation.

## Brace-release safety rule

This is an explicit user-requested safety policy in addition to the defect repairs:
before playing a torch course, check every direct docking link throughout the
group with native `Ship.TowBraceSecured`. A native secured brace on either side
satisfies a link. This catches a released brace between two downstream ships even
when the tug's own connection remains secured.

An unsecured link clears the course/target, idles FLOW and CYCLE, stops commanded
RCS/torch acceleration, removes a player autopilot controller when present and
sets native `IsAutopilotAborted` feedback. The nav log records the brace reason.
Momentum and physical docking remain; the group coasts. The old course does not
consume another budget or resume automatically after reclamping.

The shipped `ItmTowingBrace01Secured` definition and
`TIsTowingBrace01InstalledSecure` already define installed/secured brace identity.
Native `AccrueTowGroupLoad` supplies the secured-versus-unsecured wear behavior.
Those definitions, clamp release interactions and wear rules are preserved. The
course safety rule belongs at execution, not in an alternative C# brace system.

## Saves, performance and validation

No custom saved data, new serialized types, sidecar or persistent ship registry.
Normal native save/reload reconstructs the member course. The execution context
is thread-local, scoped in `try/finally` and cleared after exceptions.
Three patch classes target five methods; there is no new timer, coroutine or
world scan. Hot helpers traverse the existing small docking group and port maps;
they do not allocate new collections. Native playback retains its own costs.

`Tests/TorchTowCheck` first reproduces the faults without behavior patches, then
tests the actual production hooks against the shipped assembly under bundled Mono.
Coverage includes the full native `FlyToPath.PlanTrip4` planner, native generated
burn/coast/brake waypoints, 1-second/irregular/hourly/6-hour steps, native SFF
completion branches, fuel, offsets/orientation, reversed member order, combined
mass, native JSON course reconstruction, negative controls and exception cleanup.
Three-ship chains test indirect mass and release of either docking link. Native
brace assets and secured/unsecured wear methods are also checked.

`Tools/TorchNativeOracle` additionally executes native reactor math and tank debit
with these hooks. It verifies unchanged engine force, reduced group acceleration,
compatibility with the pending fuel/control repairs and brace-abort control/fuel
behavior. Its earlier thermal and lifecycle matrices still run.

Engine identity, scene services, pilot presence, planetary environment and selected
shipboard effects have explicit fixtures. These checks do not reproduce an entire
Unity session or all mod combinations. Test totals and binding results are recorded
in [the harness notes](../../Tests/TorchTowCheck/README.md).
