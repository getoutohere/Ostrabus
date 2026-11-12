# MVOL Apartment Transit Fix

Standalone native Ostranauts JSON mod, packaged using the developer's
`SampleMod.zip` folder and metadata format. No Ostrabus, BepInEx, compiled code,
or package manager is required. Built against installed game data version 1.0.1.10.

## Changes

- Adds the missing `MVOL|` transit definition used by purchased apartments such
  as `MVOL|RES_1`. This allows the generic transit UI to initialize its location
  title and destination buttons.
- Adds a Private Residence destination to MVOL's existing express lift, gated
  by the native `TIsHomeownerMVOL` condition. Native wildcard expansion lists the
  instantiated MVOL apartments.
- Routes apartment departures back to that express lift at Upsilon Docking.
- Retains MVOL's three existing transit entries without alteration. Medical
  and laboratory definitions, OKLG transit, and apartment templates are untouched.

The residential stop is an explicit completion of the missing residential
network. Apartments do not gain direct medical/laboratory routes: change at
Upsilon Docking. Residence-to-residence visibility follows the existing native
wildcard convention; it does not add individual apartment-owner checks beyond
the game's station-homeowner gate, matching existing residential networks.

## Install

1. Find the game's active native Mods directory. Normally this is
   `Ostranauts_Data/Mods` under the game installation; a custom location can be
   configured by `UserSettings.strPathMods` in the game's settings.json. That
   setting points to the active loading_order.json file.
2. Copy the **MvolApartmentTransit** directory from this package into that Mods
   directory. Its `mod_info.json` must be directly inside
   `Mods/MvolApartmentTransit/`, with the transit JSON below `data/transit/`.
3. Back up your existing `loading_order.json`. Add `"MvolApartmentTransit"` to
   its `aLoadOrder`, after `"core"` and after any mod replacing MVOL transit.
   Preserve all existing entries, records, and ignore patterns. The bundled
   `loading_order.example.json` is a minimal example, not a replacement for your
   existing configuration. Do not copy SampleMod's sample ignore patterns.
4. Fully restart the game and load your save. Existing apartments should use the
   new definition without repurchasing; this still requires an in-game check.

No vanilla StreamingAssets files should be edited. A later mod replacing the
`MVOL` or `MVOL|` transit records can override this fix; native records are
replaced by name, not merged connection by connection.

## Uninstall

Remove its load-order entry and its own MvolApartmentTransit folder, then restart.
This restores the original transit data, including the original apartment exit
problem. Move the player and crew out of an MVOL apartment before uninstalling.

## Validation and remaining game checks

Static checks verify valid JSON, two uniquely named transit records, unchanged
vanilla MVOL connections, valid native trigger references, matching apartment
and station kiosk conditions, and both directions of the residential connection.
The standalone ZIP contains only this package and needs no code build.

Not yet play-tested: purchase/enter each offered apartment type, open the exit
menu, travel to Upsilon Docking and back, verify arrival placement at the lift,
check ownership locking and multiple-residence listing, save/reload, and confirm
medical/laboratory and OKLG transit remain functional. Exact disappearance of the
reported Azikiwe title requires that UI reproduction; its prefab origin was
inferred during diagnosis rather than inspected directly.

Developer reference: installed `SampleMod.zip`, especially its loading_order.json,
SampleMod/mod_info.json, and native `data/` directory layout. Design evidence is
recorded separately in `Notes/MVOL-APARTMENT-TRANSIT-BUG.md` in the investigation
workspace; the installed mod does not depend on that document or Ostrabus.
