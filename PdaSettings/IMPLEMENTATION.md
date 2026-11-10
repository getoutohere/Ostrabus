# Implementation and validation

## Files and entry points

- `Assets/` is a native mod directory, installed as `TemplateSettings` beside the
  native `loading_order.json`. It adds app metadata and tooltip strings. Its first
  icon is the approved transparent gear/sliders PNG `pda_apps/IcoTemplateSettings.png`.
- `SettingsRegistry.cs` defines the controls and delegates to existing module
  setters. It does not parse console commands or retain world objects.
  Shortcut rows have a string binding and validated setter, separate from numeric
  settings. Station-docked collision exemption and Hull-shaped collisions are
  independent saved toggles; neither creates custom game-save state.
- `SettingsPanel.cs` builds a scrollable Unity UI panel lazily. Font and click
  audio come from the native PDA. Toggle buttons and window increment/decrement
  buttons apply immediately; the window remains bounded to 1–30. The Venus wreck
  percentage uses a whole-number 0–100 slider with a percentage readout, default
  50%, saved through the module's validated config setter. Refresh uses
  `SetValueWithoutNotify` to avoid feedback writes. A permanent
  vertical scrollbar supports dragging and track clicks, alongside wheel scrolling.
  Descriptions are shortened for one-line presentation at the standard 1080p UI
  size. Rows use TMP's measured text height with a six-pixel gap before controls;
  wrapping at narrower widths grows the row. Layout runs on opening/resizing,
  not during the periodic value refresh. A shortcut button captures keyboard input
  only while rebinding; Esc, focus loss, closing or switching apps cancels it.
  Capture reads the Input System each frame; ordinary refresh stays at 4 Hz.
- `SettingsAppPatch` intercepts exactly `template_settings` in `GUIPDA.OpenApp`.
  Other app IDs continue through the original dispatcher.
- `SettingsStatePatch` restores Home before a native state change, including a
  repeated Home assignment. It does nothing unless our panel is active, avoiding
  exposing Home over another native app.
- Plugin destruction calls `SettingsPanel.Stop`. Scene-owned UI destruction clears
  the static reference. The closed GameObject is inactive and runs no Update. An
  open panel checks values four times per real second and changes text only when
  values change, reflecting console changes even while the game is paused.

Native Home hosts the PDA frame with its icon CanvasGroup temporarily hidden. Our
sibling panel copies its RectTransform bounds. No native enum, hotbar list, prefab
asset or saved world is modified. Home restores the grid, Close closes the PDA,
and another app hides our panel before native dispatch. Errors restore Home and
log through BepInEx. Opening without a PDA instance is a no-op.

## Build, deploy and package

`dotnet build Ostrabus.csproj -c Debug --nologo` copies assets to
`bin/Debug/netstandard2.1/PdaSettings/Assets` beside the DLL/PDB. The project now
references the game's `UnityEngine.UIModule.dll`; Unity libraries are not bundled.

The existing `Deploy-Mod.cmd`/`.ps1` deploys the DLL/PDB and invokes
`Install-Assets.ps1`. The game must be closed. The helper reads
`UserSettings.strPathMods`, using its directory and the loader's fixed
`loading_order.json` basename. It appends `TemplateSettings` once after existing
mods, retains unknown records/metadata and disabled entries, and backs up the load
order before changing it. JSON remains a UTF-8 array. An already-disabled
TemplateSettings entry stays disabled. Existing core assets are not overwritten.

Run `./Package-Mod.ps1` after building. A fresh timestamped folder and ZIP under
`dist` contain `Plugin/`, native assets, deployment scripts and these documents.
Release builds use `-Configuration Release`. Extract and double-click
`Deploy-Mod.cmd`; the packaged-source fallback selects `Plugin/`. The deploy
script targets Jon's existing Steam install, as before; the helper alone accepts
an alternate game directory/settings path.

Disable Ostrabus Settings in the game's Mods screen and restart to remove the
app. Removing the plugin also removes its code patches. Saves need no conversion.

## Completed checks

### Ostrabus identity migration (November 6)

The project is `Ostrabus.csproj`, the assembly is `Ostrabus.dll`, and production
namespaces use `Ostrabus` throughout. The entry point is `Ostrabus.OstrabusPlugin`.
Embedded knob resource names, reflection lookups, test references, packaging,
deployment and module documentation follow the new identity. The old source
filenames are removed. The native mod's displayed name is **Ostrabus Settings**.

Compatibility identifiers intentionally remain stable: BepInEx/Harmony ID
`com.ostranauts.template` (and its existing config filename), native asset folder
`TemplateSettings`, app ID `template_settings`, and existing GUI/save string keys.
In particular, `TemplateExample.ProximityRangeKm`, `TemplateExample.ProximityShip`
and `TemplateExample.StationReturn.*` are persisted data keys, not namespaces.
No save conversion or config reset is required.

Deployment now installs to `BepInEx/plugins/Ostrabus`. After verifying the new
plugin and assets, it disables known `TemplateExample.dll`/`.pdb` predecessors in
the old folder, new folder or plugin root by moving them to uniquely named
non-DLL backups. Other plugins remain untouched. A failed deployment disables
the new copy and restores any predecessor files already moved. The installer
must run with the game closed. No actual game installation was changed in tests.

Validation: all 25 test projects rebuild, 11,927 selected runtime checks pass,
and `DeployRenameCheck.ps1` verifies predecessor isolation, reversible backups,
repeat deployment, unrelated-plugin preservation and rollback in temporary folders.

- Debug build: zero warnings/errors.
- `Tests/PdaSettingsCheck`: 32 checks under the game's bundled Mono. Native LitJson
  parses all shipped DTOs; the packaged icon resolves. The production registry
  exercises Altwork, window bounds, hotkeys, Orbital, coasting and SFF setters.
  Config disk/event plumbing and engine equality are fixture substitutes; module
  setters are production code. Session orbital changes leave the launch default
  unchanged. Both Harmony hooks install against native GUIPDA. Opening without an
  instance is safe.
- `Tests/PdaSettingsCheck/InstallCheck.ps1`: 16 checks using temporary workspace
  folders: fresh/repeated installs, custom mod path, backups, asset hashes,
  metadata/other-mod preservation, disabled state, and rejection of malformed or
  missing-core load orders.
- Distribution archive: required files and matching DLL/asset hashes checked.

No checks install into the running game. The offline harness cannot render native
scenes. Jon confirmed the first in-game version worked, identifying the missing
visible scrollbar as its only defect. The scrollbar is now added and the build
passes; its rendered appearance and pointer interaction await the next launch.

## Outside version one

Restart-only settings, changing Orbital launch defaults, destructive repair/prune
actions, asset bundles and asset hotloading. Always-active fixes keep
their existing activation behavior; the app exposes existing live controls.
