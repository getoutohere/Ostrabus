# MVOL apartment transit menu fails to initialize

Date: 2026-11-11

Status: Investigation only. No implementation or game-state changes.

## Problem and symptoms

Using the transit kiosk in a player-purchasable apartment at MVOL reportedly opens
a menu labeled **Azikiwe Commercial Station**, an OKLG location. Destination
buttons may also be absent or incorrectly displayed, preventing normal transit.

Static inspection identifies a missing residential transit configuration for
MVOL. The menu opens but skips initialization when that configuration is absent.
This directly explains missing generated buttons and is consistent with the
incorrect title remaining from the UI prefab.

## Confirmed cause

The residence broker generates apartment registrations using the broker station's
ID followed by `|RES_` and a number. An apartment purchased at MVOL therefore gets
an ID such as `MVOL|RES_1`, regardless of which apartment template is sold.

`DataHandler.GetTransitConnections()` normalizes a registration containing `|`
to its prefix through that separator. The apartment lookup is consequently
`MVOL|`, not `MVOL` or the apartment template's original station.

The installed `data/transit/transit.json` contains entries for `MVOL`, `MVOL_Med`,
and `MVOL_Lab`, but **no `MVOL|` entry**. There is no fallback to the parent station
in the lookup method.

`GUITransit.Init()` obtains that configuration and calls the connection filter,
UI selection, and `SetData()` only when the result is non-null. When lookup fails,
the UI remains open without those initialization steps. Generic `SetData()` is
responsible for both assigning the current location's title and instantiating
the clickable destination buttons.

The precise source of the displayed **Azikiwe Commercial Station** string remains
an inference: uninitialized prefab text fits the observed behavior, but the
serialized Unity prefab was not inspected to confirm its default label.

## Comparison with working OKLG transit

| Mechanism | OKLG apartment | MVOL apartment |
| --- | --- | --- |
| Example generated registration | `OKLG|RES_1` | `MVOL|RES_1` |
| Normalized transit lookup | `OKLG|` | `MVOL|` |
| Residential transit definition | Present | Missing |
| Title and destination generation | Runs | Skipped |
| Station network includes residential access | Yes, via `OKLG_RES` | No residential connection in MVOL's authored network |

OKLG's `OKLG|` definition uses the generic transit interface and supplies apartment
and station destinations. OKLG's separate custom transit interface,
`GUITransitOKLG`, is selected for particular public station areas and hardcodes
OKLG destination IDs. It is not selected by MVOL's normal transit configuration.

MVOL's broker inventory (`ItmMVOLResBrokerInv`) offers `ResMSUZ01`, `ResMSUZ02`, and
`ResEJDR01`. All three templates contain `ItmKioskTransit03b` with the generic
`GUITransit/GUITransit` prefab. The templates are therefore not explicitly
requesting OKLG's custom interface. Their transit lookup uses the generated MVOL
registration, not their template names.

MVOL's public transit definition currently includes only MVOL, the medical
facility, and the laboratory. The latter two routes require express-lift origin
and destination triggers. It also lacks an inbound residential wildcard route.
Adding only an apartment exit would leave that residential network incomplete.

## Evidence and source locations

Native C# inspected in the decompiled 1.0.1.9 assembly:

- `Ostranauts.ShipGUIs.ShipBroker/GUIShipBroker.cs`, `SetupApartments()` around
  lines 295–341: station-derived residential registrations.
- `DataHandler.cs`, `GetTransitConnections()` around line 2816: prefix
  normalization and exact dictionary lookup, with no parent-station fallback.
- `GUITransit.cs`, `Init()` around line 232 and `SetData()` around line 262:
  initialization guarded by a non-null configuration; title and button creation.
- `JsonTransit.cs`, `GetConnectionsForKiosk()`: authored origin-trigger filtering
  and wildcard expansion against instantiated ship IDs.
- `Ostranauts.ShipGUIs.Transit/GUITransitOKLG.cs`: hardcoded OKLG custom screen.

Installed JSON inspected under
`<game>/Ostranauts_Data/StreamingAssets/`:

- `data/transit/transit.json`: MVOL public routes, absent `MVOL|`, and working
  `OKLG|` / `OKLG_RES` definitions. MSUZ also provides a useful generic residential
  network comparison.
- `data/loot/loot.json`: `ItmMVOLResBrokerInv` apartment template selection.
- `data/ships/ResMSUZ01.json`, `ResMSUZ02.json`, and `ResEJDR01.json`: generic
  apartment transit kiosks.
- `data/ships/ResOKLG01.json`: comparison apartment kiosk configuration.

## Verification completed and limits

- Confirmed the apartment registration algorithm and normalized lookup key.
- Confirmed that the installed transit JSON lacks `MVOL|` and public residential
  access from MVOL.
- Confirmed that missing configuration skips title assignment and generated
  destination buttons without preventing the menu from opening.
- Compared OKLG's working residential configuration and MVOL's actual offered
  apartment templates.
- Read the current live ship registry: no MVOL apartment was present, so this
  investigation did not reproduce the apartment UI in the running game.

The initial symptom is user-reported. Missing configuration and its native code
consequences are confirmed statically; the exact prefab default title and visual
appearance still require a reproduction or serialized prefab inspection.

## Repair scope for a future implementation

The primary defect belongs to the authored transit assets. Use the native JSON
modding interface, following the repository's PdaSettings packaging/loading
approach, to add a `MVOL|` residential definition and appropriate inbound access
from MVOL. Do not edit installed vanilla files directly.

Preserve native origin/destination triggers, ownership conditions, wildcard
expansion, and UI generation. Inspect MVOL's physical transit stops and their
conditions before selecting the residential route triggers; do not copy express
lift requirements blindly. Determine which existing MVOL stop should provide
residential access from the authored layout.

A separate defensive C# improvement could explicitly report missing transit data
instead of displaying an uninitialized screen. That is not a substitute for the
missing routes. Renaming the title alone, forcing an OKLG destination, or adding
a parallel transit controller would leave the underlying defect unresolved.

## Reproduction and acceptance checks still needed

1. Purchase an apartment at MVOL and record its generated registration.
2. Use its transit kiosk; record the title and visible/clickable destinations.
3. After a future repair, verify the apartment-to-MVOL trip and the return trip
   through the intended public stop.
4. Verify residential ownership gating and wildcard behavior with multiple
   apartments, including when no residence exists.
5. Verify save/reload behavior and each apartment template offered at MVOL.
6. Verify that MVOL medical/laboratory express lifts and known-good OKLG transit
   retain their existing behavior.
