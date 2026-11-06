# Phantom gravity after transit

`DockedGravityPatch` is an always-on correction in MiscBugFixes.

## Cause and observed reproduction

Transit away from and back to a docked apartment/tug/derelict chain can
reconstruct the derelict with `Ship.fGravity`'s constructor default of 0.3 g.
`StarSystem.UpdateShip` normally leaves dock-group gravity to the group update,
but `DockGroup.TimeAdvance` propagates gravity only for unanchored groups.
An orbit-locked group consequently retains the reconstructed member's default.

In the captured reproduction, Ayden's mass was 101.5 kg and encumbrance became
30.45 despite the microgravity status remaining active. `CondOwner.UpdateGravity`
uses the ship's raw gravity for encumbrance without updating `StatGrav`, explaining
the disagreement. The derelict was attached through a tug to an orbit-locked
apartment; no thrust or ground contact explained the change.

## Correction and scope

A postfix on positive-time `DockGroup.TimeAdvance` runs after the world's
individual ship updates. For an anchored group, it finds an existing anchored
member whose gravity initialization has completed, then copies that native
gravity result to materialized, unanchored members whose value differs.
Actual station gravity is preserved; this does not impose zero gravity on all
docked ships. Free groups retain native behavior. Anchored members, mooring
targets, shallow members, destroyed ships and stale group references are skipped.

The native `Ship.Gravity` setter refreshes crew encumbrance and `StatGrav`.
During this bookkeeping repair, `bNeedsGravSet` is temporarily set and restored
in `finally`, suppressing artificial maneuver wear, creaks and knockdowns.
Existing incorrect gravity is repaired on the next positive simulation update.
There is no save-format change, independent polling loop or retained ship/crew
reference. Unchanged groups cause no condition writes.

Detailed ship inspection now exposes `interiorGravityG` and
`needsGravityInitialization` for comparison with crew conditions.

## Offline verification

`Tests/DockedGravityCheck` passes 47 checks under the game's bundled Mono.
The native group update reproduces the stale 0.3 g before the patch, and the
installed Harmony postfix repairs it afterward. Checks cover genuine station
gravity, crew encumbrance/status, zero-time updates, scope exclusions,
idempotence, invalid values, unchanged maneuver wear and flag restoration
when a condition write throws. Unity orbital integration and condition storage
are fixtures; the native group update and gravity/encumbrance methods execute.

Build and run:

```powershell
dotnet build Tests/DockedGravityCheck/DockedGravityCheck.csproj -c Release --nologo
dotnet run --project Tests/SaveJsonCompatibility/Host -c Release -- Tests/DockedGravityCheck/bin/Release/netstandard2.1/DockedGravityCheck.dll
```
