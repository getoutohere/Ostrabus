# Docking permission disagrees with the displayed hull clearance

Confirmed November 16, 2026 against native 1.1.0.0 and the current hull-distance
integration. The report showed J-YGNB / Happy Mushroom rejecting forced docking
with CODE 6700 while target data displayed 4.21 km, inside the stated 5 km limit.

## Cause

The shipped `SHIPForceDockAllow` and `SHIPForceDockAllowLEO` interactions correctly
set `fTargetPointRange` to 5 km. Native `Interaction.IsWithinShipRange()` evaluates
that limit through `CollisionManager.GetRangeToCollisionKM(Ship, Ship)`, which
subtracts the native `ShipSitu` radii from center distance. This mechanism and
the relevant asset range are unchanged from 1.0.1.10.

Our `HullDockingGeometryPatch` changes target-data range to subtract the cached
exterior hull allowance. Before this repair it left `Interaction.IsWithinShipRange`
untouched as a general communication policy boundary. That classification missed
the fact that docking permission responses also use this evaluator. The result
is two different clearance measurements for the same physical approach.

The live cached radii for this pair were:

| Hull | Native radius | Corrected exterior radius |
| --- | ---: | ---: |
| O-GXD / Stellar Magnificence | 500 m | 1,145.425659 m |
| J-YGNB / Happy Mushroom | 280 m | 529.528076 m |
| Combined allowance | 780 m | 1,674.953735 m |

The permission check consequently reads 894.953735 m farther than target data.
A displayed 4.21 km corresponds to native clearance of 5.104954 km: correctly
rejected by the unadapted evaluator, despite the player's displayed range.
This is an omission in our hull integration, not a new native 1.1 change or a
floating-point rounding failure. Moving closer eventually crosses both limits.

## Reproduction and repair boundary

The ignored `obj/ForcedDockRangeAudit` harness executes compiled native
`Interaction.IsWithinShipRange` and `CollisionManager.GetRangeToCollisionKM`
under bundled Mono with the captured radii and a position reproducing the
reported 4.21 km display. It observes 5.104954 km and rejection, then moves the
target 150 m closer and verifies native acceptance. Only Unity null semantics
are substituted; no live hull, controller, radio message or saved game changes.
This reconstruction is not a recording of the historical decision tick.

Other docking-permission responses with the same range evaluator can disagree
in the same way.

## Implemented repair

The existing `HullDockingGeometryPatch` now also adapts the one distance query in
`Interaction.IsWithinShipRange`. `HullDocking.PermissionRangeKm` uses the cached
selected-hull pair allowance only when the response's authored `LootCTsThem`
effect is `TXTDockingClearance`. This covers ordinary, forced, law-enforcement
and unpiloted docking grants without an interaction-name allowlist. The native
effect adds the `DockingClearance` condition; no asset override is necessary.

The evaluator retains each asset's distance threshold and strict comparison,
zero-range and docked-only branches, self targeting and existing docking state.
Stations and station-connected chains retain native geometry. Mooring to an
asteroid does not enlarge the selected ship into the whole mooring group.
Feature-off calls the original range helper. General radio delivery, other
interaction effects, weapon ranges and shared native radii are unchanged.
The repair adds no frame callback, geometry scan, worker wait or persistent state.

`Tests/HullOutlinesCheck/DockingPermissionChecks.cs` executes the compiled native
evaluator before and after installing the production transpiler: **37 checks**
pass. They reproduce rejection at the reported 4.21 km clearance and verify its
correction, test 1 m inside/outside all 11 shipped positive-range clearance
grants, and cover unrelated/missing effects, the feature toggle, stations,
station-connected chains, asteroid mooring, unlimited range, docked-only range,
already-docked positive-range semantics and missing participants. Native mass
and Unity object equality are environment fixtures; the range evaluator and
geometry integration execute unchanged apart from the production patch.
The existing hull/docking/proximity suite also passes (1,231 integration,
60,687 worker/geometry and 12,465 contact checks). Live acceptance after deploying
the rebuilt DLL remains pending.
