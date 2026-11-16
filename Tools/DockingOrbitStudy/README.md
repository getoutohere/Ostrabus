# Docking attachment investigation

Offline investigation of [docked orbital state and gravity ownership](../../Navigation/DOCKED-ORBIT-LOCK.md).
The harness loads and patches the actual full Debug Ostrabus DLL. The earlier
study-only prototype was removed after promotion into production code.
The tool does not contact the live game or write saves/assets.

The current candidate preserves an existing authored attachment for a passive
visit, with required release on applied control or authorized course execution.
It leaves the native Hold gravity exemption intact. Native Hold then
tether/unmoor copies the civilian's exemption into the anchor, but overriding
that copy alone is not a generally safe repair.

Requires a current full Debug Ostrabus DLL for the existing towing/completion
and SFF compatibility checks. From the repository root, run sequentially:

```powershell
dotnet build Tools/DockingOrbitStudy -c Release
dotnet Tests/SaveJsonCompatibility/Host/bin/Release/net8.0/MonoHost.dll Tools/DockingOrbitStudy/bin/Release/netstandard2.1/DockingOrbitStudy.dll
```

The bundled-Mono run passes **449 checks**. First it executes native docking
without the candidate to reproduce lock loss. It then tests a candidate
docking-expression replacement and group-input handoff, including the failure
of lock preservation alone. Finally it installs the existing production towing
and route-completion patches and exercises their composition with the candidate.
Eight independent assertions also execute native Hold/MoorTo/UnMoorFrom and
show that rebuilding the pair disables group gravity. Correcting only that
numeric anchor flag restores the expected response to an applied gravity force.

`GravityPhysics.cs` adds 20 checks using captured Sol/Mars/Phobos parameters in
`PhobosBodies.json`. Actual body ephemerides, native group-world integration and
the current production gravity correction run for ten game minutes at two time
steps. With gravity ignored, mismatch grows to 286 m/s and relocking fails.
With gravity active, mismatch is 0.18-4.13 m/s and native relocking succeeds.
The actual SFF preview excludes the former and admits the latter. Body-clock
stubs used by the earlier isolated tests are removed for this physical study.
The fixture contains celestial parameters only, captured from the now-rotated
November 16 `autosave_3_adventure_1`; it contains no people or ship inventories.

`HoldSafety.cs` adds 14 assertions for a constructed state accepted by native
Hold: initially stationary relative to Mars, 300 km above its surface and above
every authored atmosphere layer. Native group gravity integration crosses the
surface after 435.5 seconds when the ignore flag is cleared; the ignored-gravity
control remains aloft. This gravity-only counterexample excludes aerodynamic
response, collision resolution and subsequent AI decisions. It is not a live
incident or proof of full-tree reachability, but disproves universal Hold safety.

`AttachmentPhysics.cs` adds 84 assertions using real ephemerides, native docking,
Hold, tether/unmoor, locked-member and group integration, and undocking. Twelve
combinations cover both docking directions, static/circular attachments, small
steps over ten minutes and one two-hour step. The existing attachment follows
its original trajectory while native Hold still disables gravity; visitor
departure leaves the wreck attached and the visitor free. The two-hour step is
a physical integration check, not full SFF behavior-tree/fuel validation.

The actual production attachment and towing hooks execute, with no study-only
release prefixes. Additional controls reject future/competing/wrong-owner and
anchor-owned routes, protect stations, release all members of a three-hull group
and ignore residual spin without applied angular control.

`LifecycleChecks.cs` runs native retrieving-pilot Main/HaulShip, actual RCS input,
native brace setup, paused-haul saved-situation/group reconstruction and ordinary
departure. It also runs native civilian Main/Hold/Undock and FFWD queue callbacks
through both actual SffAdvance adapters. Real Phobos ephemerides advance two
hours, with fixed groups deferred to native following and no silent motion
fallback. Ordinary resumed turns then end the visit and undock naturally.
Calendar/scene/economy, threat/help observations and Unity angular steering are
explicit fixture boundaries. No complete live job or market delivery is claimed.
The broader proposal to restore gravity for every space wait remains withdrawn.

`CollateralChecks.cs` independently adds 222 checks: real native `SetThrust`
composed with the group-mass repair, native braced/unbraced tow wear, zero input,
shutdown, both driving members and docking directions, post-release save state,
and permanent station identities. It reproduced and closes a missing
substation/ground-identity guard in manual/course release. The controlled wear
stays below the random damage threshold; actual destructive damage is not claimed.
An additional native A/B demonstrates that completed-course remainder coasting
requires the existing route-completion repair; custom towing now includes that
dependency. See [the independent review](../../Navigation/DOCKED-ORBIT-LOCK.md#independent-collateral-review).

Native docking/group formation, follow-up movement, soft/circular attachment,
detach, moor/unmoor and saved-situation reconstruction execute. Unity equality,
selected crew, mass, pilot lookup, fees, logging/audio and wear are environment
fixtures. Controlled body cache changes isolate the relative-motion invariant.
The SFF checks execute capture/exclusion and shared adapters with native role
continuation; they are not a full GUI/PASS scene playthrough. See the investigation
for production checks and live-validation limits.
