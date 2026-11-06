# PDA settings app

## Goal

Control the mod's existing live options from the PDA home screen, without typing
console commands. Reuse native PDA presentation, input and audio. Settings retain
the same lifetime and behavior as their console/config counterparts.

## Native integration

`DataHandler.LoadMod` reads `data/pda_apps/` into `dictPDAAppIcons` and merges
`data/strings/` into the string table. `GUIPDAHomepage` builds icons from that
dictionary; `GUIPDAApp.Toggle` calls the hard-coded `GUIPDA.OpenApp` dispatcher.
The native loader does not provide executable app implementations.

Use the unique app ID `template_settings`. Ship an ordinary asset mod with
`mod_info.json`, one app definition and tooltip strings. The approved gear/sliders
icon lives in `Assets/images/pda_apps/`. Do not override the
core app list, item definitions or user hotbar preferences.

The existing BepInEx DLL intercepts only our app ID. Construct the panel on demand
inside the PDA, using Unity UI and the PDA's TMP font. Use Home as the native frame
host while temporarily hiding its icon canvas; restore it before every native
state transition, including a Home-to-Home transition. Home, close, other apps and
scene teardown must release/hide the custom panel correctly. Do not invent enum
values, modify native prefabs on disk, or serialize custom UI state into saves.

If the asset mod is disabled, its icon is absent. If the DLL is absent, the native
unknown-app fallback returns to Home. No game-save dependency is introduced.

## Initial controls

| Control | Lifetime | Existing entry point |
| --- | --- | --- |
| Alternative work | Saved config | `AlternativeWorkSystem.SetEnabled` |
| Supply window, 1–30 | Saved config | `WorkSupplyPlan.SetWindow` |
| Crew hotkeys | Saved config | `FunctionalHotkeys.Flag.Value` |
| Kang auto-vents | Saved config | `QoL.AutoVents.AutoVentControl.SetEnabled` (feature toggle only) |
| Orbital correction | This session | `OrbitalSimulation.SetEnabled` |
| Orbital coasting | This session | `CoastIntegration.SetEnabled` |
| Super fast forward correction | This session | `SffSimulation.Enabled` |
| Work diagnostics | This session | `WorkTrace.SetEnabled` |

Show the setting's lifetime in each row. Explain that coasting/SFF require Orbital
to be enabled, but allow their preferences to be set independently, as the console
does. Do not expose destructive repair/prune commands as settings. Restart-only
startup options and editing launch defaults are outside this first version.

A small typed registry owns display metadata, current-value readers and existing
setters. The UI invokes setters on the Unity thread; it does not simulate console
input or duplicate module cleanup logic. Refresh values while the panel is visible
so console changes are reflected. No Update, coroutine or timer runs for a closed
settings panel. Retain no ship, crew or item references.

## Distribution

One package contains the BepInEx plugin and the native asset mod. The installer
uses the user's configured native loading-order path (falling back to the game's
Mods folder), copies assets, and appends our directory once after core while
preserving all other entries, disabled states and metadata. Never replace the
whole load order with a sample file. Install while the game is closed.

The project build copies assets beside its DLL. Development deployment and release
packaging share the installer. Native asset changes require a game restart.

## Validation

Build the Debug library; validate asset JSON against native DTOs and string-table
shape, unique IDs and icon resolution. Exercise registry bounds and actual module
setters offline where possible. Validate package/install behavior in a temporary
game layout, including an existing load order, custom mod path and repeated
installation. Inspect UI lifecycle hooks and ensure no permanent polling is added.
Actual panel layout, pointer interaction and scaling need a first in-game check;
offline fixtures cannot substantiate those visual behaviors.
