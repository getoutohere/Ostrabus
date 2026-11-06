# VORB Hauler return patch

Status: refactored hooks are installed in the running game. O-QFNF completed a fallback return and O-TK0 completed a normal planned return. The original distant haulers are braking; their eventual arrival and live refuelling conservation remain pending.

## Observed behavior

During the live inspection, VORB Haulers `O-APJH` and `O-D5GM` were hundreds of kilometres from VORB but had no active command, no queued work, no target, and no navigation plan. Both were registered `HaulerDeployer` AI ships with VORB as their home station, had full RCS fuel, and were not docked or paused. This points to idle command state rather than fuel exhaustion or a damaged ship. It is a small sample, so confirm on another hauler before treating it as a general VORB fleet issue.

The later `O-QFNF` test exposed a second lifecycle point: it undocked derelict `E-UEK5` and transitioned from `HaulShip` to `FlyTo`, but had not yet run an empty-hauler `HaulShip` call. No target-restoration log was recorded. At the time of inspection it remained in `FlyTo` with no scan target and a situation target tied to `VORB|Aux`, so return to the VORB hull was not yet confirmed.

## Relevant vanilla flow

`HaulerDeployingPilot` runs the command sequence `Dock -> HaulShip -> AnchorDockedShip -> FlyTo`. Its `GetTarget()` returns a hauling target when it carries a derelict; when empty, it resolves the AI ship's registered home station and returns that as the target.

`HaulShip.RunCommand()` fills a missing target from the pilot, then, if the hauler has no docked cargo, clears its target and returns failure. The sticky sequence advances after failure. Since `AnchorDockedShip` cannot run while the hauler is undocked, the sequence reaches `FlyTo` without the home target that `GetTarget()` could supply. That plausibly explains the targetless idle state.

Relevant game source:

- `Ostranauts.Ships.AIPilots/HaulerDeployingPilot.cs` — command sequence and `GetTarget()`
- `Ostranauts.Ships.Commands/HaulShip.cs` — target assignment and no-cargo failure path
- `Ostranauts.Ships.Commands/StickySequenceCommand.cs` — failure advances to the next child
- `Ostranauts.Ships.Commands/AnchorDockedShip.cs` — anchoring step applicability

The source root inspected was `<decompiled-source>`.

## Fix rationale

A Harmony postfix on `HaulShip.RunCommand()` restores the registered home-station target after the no-cargo failure path clears it. This lets existing `FlyTo` and `Dock` commands handle navigation and arrival.

Both hooks are gated to VORB's registered `HomeStation`. Broader `HaulerDeployer` coverage can be considered after this behavior is confirmed in game.

## Implementation

The generic recovery now lives in `Haulers/` (`Ostrabus.Haulers`); see
[Haulers/README.md](Haulers/README.md). It applies at all home stations and also
handles an existing target being discarded by an empty HaulShip call. Venus-only
route validation/manual navigation policies remain in this module.

The generalized anchor patch observes deploying haulers starting with a docked
derelict. On successful empty/undocked/targetless completion it stops the abandoned
maneuver and restores the verified native home target. Pending state and diagnostic
counters use weak ship keys with scalar values and are cleared on scene unload.

Both hooks leave the original command result unchanged. The patch does not persist state or hold ship references between calls. It logs successful restorations and catches/logs exceptions so patch errors do not escape into the AI command loop.

`dotnet build Ostrabus.csproj --no-restore` completed with 0 warnings and 0 errors after adding the dropoff hook. Runtime behavior still needs in-game verification. The currently running game still has the previous build; this patch will not retroactively catch `O-QFNF`'s already-completed undock event.

## Navigation-plan failure and guard

The inspection API now shows both `O-APJH` and `O-D5GM` still targeting VORB through `FlyTo`, but their stored `NavData` is unusable. For each, waypoint 47 is a normal recent point and waypoint 48 jumps to an arrival time around `10^20` simulation seconds and a position around `10^13` AU. Their final waypoint is another order of magnitude farther away. The next segment has zero acceleration, so it is an effectively endless coast. The simulation was unpaused during the latest snapshot; both were over 21,000 km from VORB and separating at about 10 km/s.

Source review points to the iterative correction loop in `FlyToPath.PlanTrip4()` and its coast-duration calculation in `PlanTrip4Sub()`: it repeatedly projects the target to the previous estimated arrival and does not reject non-finite or absurd waypoint times/positions before assigning the plan. The exact arithmetic step that first diverges is not yet proven.

Venus orbit calculations are relevant to that failure. `BodyOrbit.UpdateTime()` calculates an orbit from mean anomaly using `((time + periodShift) % period)`, runs a bounded Newton solve for eccentric anomaly, and estimates velocity by comparing positions one second apart. `ShipSitu.UpdateTime()` delegates to the shared `BodyOrbit` for orbit-locked targets, while `FlyToPath.PlanTrip4()` repeatedly advances its destination to each estimated arrival. If the planner's estimate has already exploded, projecting a Venus body to an epoch near `10^20` seconds is outside meaningful double-precision orbital timing: adding or subtracting one second no longer changes that epoch, and modulo-period phase loses precision. This can compound a bad plan or corrupt projected velocity. It is a credible contributor, but current evidence does not establish that it is the first bad calculation; the route guard contains the failure rather than changing global Venus orbital behavior.

### Confirmed immediate RCS drain

The source explains why O-QFNF's half tank disappeared despite the noncombat-AI maneuver fuel skip. `Ship.Maneuver()` calls `RemoveGasMass()` with `aiSkip=true` for a shallow AI ship when `shipCombatTarget` is null, so that direct maneuver path does not deduct RCS fuel. But `NavData.TimeAdvance()` separately interpolates the route's `FuelLevel`, compares it to `GetRCSRemain()`, and calls `RemoveGasMass()` without `aiSkip`. O-QFNF's stored route had negative RCS fuel values (its active and final waypoint predictions were below zero). `GetRCSRemain() - negativeFuelLevel` therefore exceeded its whole remaining tank; the shallow `RemoveGasMass()` branch capped the deduction at the actual fuel and reduced it to zero. This is the immediate drain mechanism, not a speculative maneuver burn.

The console recorded `refuelai` changing O-QFNF from 0 to 1,686.07 of 3,372.14 kg. Later live snapshots showed 0 kg while the route was active and predicted negative fuel. This matches the source-level mechanism; the console has no command timestamp and the removal call itself was not instrumented, so the precise depletion interval is unproven. The route was near the moving `VORB|Aux` point, while `scanTarget`, `targetRegId`, and `TargetData` were null, so this was not a normal hull-directed return route.

### Guard behavior

`VorbNavPlanGuard` runs around `FlyTo.RunCommand()` for VORB `HaulerDeployer` ships, excluding materialized ships under player navigation control. It first restores the registered VORB home target when an empty, undocked hauler is targetless. It rejects plans with non-finite values, waypoint coordinates beyond 100 AU, velocity components beyond 0.01 AU/s, an arrival more than `10^9` seconds ahead, non-monotonic waypoint times, an invalid/expired active segment, or predicted RCS fuel below -0.001 kg. It validates generated plans in the same call after vanilla planning returns.

Rejection clears `NavData` and requests manual flight for the current target. A postfix on the private `FlyTo.HasValidPathTarget()` selector returns false during fallback. The original `FlyTo.RunCommand()` then executes its normal manual branch, retaining gravity setup, pause/towing checks, the body-lock check, `HandleRCSFuel()`, completion fuel accounting, and `LocalizedResult()`. The returned command remains `FlyTo`, preserving `HaulerDeployingPilot.FFWD()` dispatch and the sticky sequence's handoff to `Dock`. A new invalid route generated during a call is cleared before leaving that call; manual steering begins on the next eligible update. The fallback remains selected during running/failed retries for that target and clears on completion or target change. A null planner result still follows vanilla rejected-path handling.

Changes made through `TargetData.ApplyToShip()` invalidate the old route immediately when the logical destination changes: registration ID, orbital binding/offsets, or an independent inertial waypoint. Equivalent object replacement or target reapplication preserves the route and fallback. Direct changes are also detected at route validation. Recovery therefore cannot continue following the previous `VORB|Aux` route after selecting the VORB hull. Native unload/replacement integration is covered by the 132 AiPlannerDiagnosis lifecycle checks.

A second prefix validates the current plan before `NavData.TimeAdvance()` advances position or deducts fuel. It clears a rejected plan and returns false, allowing `ShipSitu` to continue its ordinary motion update until the next AI command. This protects already-loaded routes before `FlyTo.RunCommand()` runs. Full validation is cached per plan and the active segment is checked on each advance. Runtime trip state is keyed by ship using `ConditionalWeakTable`, with weak references to the target and validated plan, and is reset on scene unload. It retains no ship or crew objects through strong references. The 100 AU, `10^9` second, and -0.001 kg thresholds are broad sanity checks, not navigation limits.

Valid routes store absolute remaining-fuel forecasts, so even a positive forecast can discard a mid-trip refuel. Accepted shallow refills from the corrected `RefuelRCS` prefix shift every waypoint's fuel by exactly the amount actually accepted, preserving the planned burn cost. Materialized VORB hauler refills use before/after fuel readings only at the refuel call; shallow `AIRefuel()` top-ups are also covered. Invalid routes are rejected before rebasing, rather than repaired by adding fuel to their predictions. The existing game manual branch still skips direct shallow noncombat maneuver fuel consumption; its optional `TripFuelPrediction` accounting is preserved when that key exists. This patch does not invent a fuel budget when the game supplies none.

`navigation.vorbNavigationGuard` in both ship and AI inspection responses reports the fallback request, rejection reason/count for the current target, target ID, and cumulative forecast rebasing in kg. Reading it does not create or alter guard state.

The manual fallback continuously steers toward the target's updated current position, so it avoids repeatedly asking the faulty planner for a path. It may be less fuel-efficient and does not repair bad orbital projections themselves. Further diagnosis of the first divergent iteration would require logging each `PlanTrip4()` iteration's estimated duration, projected target position/velocity, and `PlanTrip4Sub()` output for a reproduced VORB return.

## Runtime verification still needed

Live run after the refactor, 2026-11-04: baseline epoch 65631744965.20253. O-QFNF rejected one negative-fuel route, acquired the VORB hull target, approached from 187 km, braked, and unregistered at epoch 65631745257.967758 (about 4.9 game minutes). Nearby inspection confirmed `hiddenFromSystem=true`, `destroyed=false`, matching the normal station `DockAndDespawn` outcome. Its fallback request cleared on completion. Its tank started and remained empty, so this confirms the vanilla shallow-AI maneuver shortcut, not live refill preservation.

O-TK0 recovered the VORB hull target, kept an accepted 86-point route, and unregistered at epoch 65631745344.592468 (about 6.3 game minutes after baseline). It is hidden and undestroyed. Stored fuel changed from 2049.63 to 1906.91 kg; it did not use fallback or reject a route. Its released derelict V-SU4N (Mesmer Specter) remains visible about 191.75 km away and is body-locked to `VORB|Aux`.

O-APJH and O-D5GM each rejected their astronomical route once, retained `FlyTo` as their active command, and kept manual fallback without generating further routes. By epoch 65631745405 their outward range rates had fallen from roughly 6 km/s to 3.04 and 4.11 km/s respectively; their ranges were about 42,195 and 42,457 km. Their return is still in progress. O-TRRP also rejected a negative-fuel route and continued closing toward VORB. O-X6P was still hauling J-V5P; O-AK6 remains a distant HaulerCargo outside this patch's AI-type scope. Raw samples and completion evidence are in `VORB-POST-RELOAD-OBSERVATIONS.jsonl`.

1. Confirm a VORB hauler releasing a derelict logs restoration and gets a target for the VORB hull before its return `FlyTo`.
2. Confirm an already-idle empty hauler still recovers, and loaded or explicitly targeted haulers do not receive the recovery target.
3. Confirm pending ship-ID state clears on command failure and scene unload.
4. Check that O-QFNF retains refueled RCS when its negative-fuel NavData is cleared, then receives the VORB hull as its target and approaches the station.
5. Check that `O-APJH` and `O-D5GM` recover from their existing impossible plans using `FlyToManual`, then approach VORB instead of continuing to drift.
6. Check a fresh VORB derelict delivery for both target restoration and the route guard; capture a `PlanTrip4()` trace if the guard fires again.
7. Refill a valid active route and confirm the route's forecast increases by the accepted fuel while its burn cost stays constant. Check that the next advance retains the added fuel.
8. Check the fallback's active command remains `FlyTo`, then observe its normal `Dock` handoff and station arrival/despawn behavior. Check pause/towing and player-piloting transitions.

## Evidence limits

The immediate RCS drain mechanism is supported by the live negative-fuel plan, refuel log, subsequent zero-fuel snapshot, and `NavData.TimeAdvance()` source; the actual deduction was not traced. The refactored hooks build with zero warnings/errors. A focused harness links the production navigation guard and shallow refuel prefix with game/Harmony doubles; 18 assertions cover fuel rebasing, retained burn cost, negative/astronomical saved routes, pre-deduction protection, target invalidation, persistent fallback, branch selection, player-control exclusion, and scene reset. Installed Harmony initialization cannot run in that .NET 8 harness; patch installation and actual Unity navigation remain integration checks after deployment/restart.

The first route-planning failure is confirmed by live plan data, but the exact arithmetic input that first diverges remains under investigation. Venus timing precision is a possible consequence/contributor, not a proven initial cause. Derelict anchoring also depends on `AnchorDockedShip` evaluating the origin station against `TIsAllowsAnchoring`; unlocked derelicts must not be attributed solely to the undock velocity check without inspecting that rule. The Venus anchoring additions below now address station permission and placeholder selection; they do not change orbital integration.

## Venus derelict fixes (2026-11-04)

The existing `Venus` module now also handles VORB wreck anchoring and spawn-pool selection. Remove/disable DerelictFixer before enabling this build in-game, especially its wreck-pool override, which would conflict with the 70/30 selection. Its cleanup, transponder removal, and hotkey behavior are not included.

- Before a VORB hauler runs `AnchorDockedShip`, add the vanilla `StationAllowsAnchoring` condition to VORB if missing. This is the anchoring mechanism identified by DerelictFixer: the game already tests `TIsAllowsAnchoring` and performs `ShipSitu.LockToBO`. The ordinary condition persists with the station and needs no mod-specific save objects.
- Player docking calls `UnlockFromBO` on both ships. Vanilla `Ship.Undock` then searches with `bIncludePlaceholders: false`, overlooking the `VORB|Aux` orbit used for deployed wrecks. The new transpiler changes only that lookup: an intact derelict situation may consider the nearest placeholder when its name is `VORB` or starts with `VORB|`. The original derelict damage check, unmanned-navigation checks, relative-speed limit (`CollisionManager.dMaxSafeV`), `LockToBO` call, offsets and docking-group handling remain in vanilla code. Active ships and other regions use the original reference. High-speed towing does not get an unconditional lock. Mooring/unmooring already preserve body locks and are not patched.
- Each VORB `HaulerDeployingPilot.SpawnDerelict` invocation samples once: 70% `RandomDerelictVenus`, 30% vanilla `RandomShip`. A failed first attempt retries the same selected pool. Other stations, explicit loot requests, and the game's `allowOverrides` behavior remain unchanged. A missing Venus pool leaves vanilla loot. Thread-local string scope is restored by a Harmony finalizer even on exceptions/nested calls; it retains no ships, NPCs or world references.

There are no world scans, timers, docking-history caches or custom persistence. Existing drifting wrecks are not retroactively snapped into place; a normal subsequent dock/undock near VORB can re-anchor an eligible slow, unmanned wreck. Existing inspection navigation fields (`bodyOrbitLocked`, `orbitBody`) and `[Venus]` console messages expose the result.

Validation: release library build has zero warnings/errors. `Tests/VenusFixCheck` runs against the actual bundled Mono runtime and the real `Ship.Undock` IL; 22 checks cover the lookup rewrite, unchanged surrounding speed/pilot/lock instructions, original branch-target retention, fail-closed installation, 70/30 boundaries, station/pool restrictions, retry scope, exception propagation and scope cleanup. Run `dotnet build Tests/VenusFixCheck/VenusFixCheck.csproj -c Release`, then `dotnet Tests/SaveJsonCompatibility/Host/bin/Release/net8.0/MonoHost.dll Tests/VenusFixCheck/bin/Release/netstandard2.1/VenusFixCheck.dll` from the workspace. The host must first be built with `dotnet build Tests/SaveJsonCompatibility/Host/MonoHost.csproj -c Release`.

In-game validation remains: fresh delivery stays body-locked, repeated player dock/undock retains a VORB reference, and a fast/piloted tow is not forcibly anchored. Verify with DerelictFixer disabled.
