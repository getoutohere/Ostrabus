# PDA settings

The **MOD SETTINGS** app controls existing live mod options. Install both the
BepInEx DLL and the accompanying `TemplateSettings` native asset mod, then restart.
Open the app from the PDA home screen. Each row identifies whether changes persist
to config or apply only to the current session.

**Station-docked hull exemption** controls visiting-hull collisions, avoidance
and proximity warnings across station-docked chains. Saved, default on.

**Hull-shaped collisions** uses the displayed exterior for ordinary free-ship
impacts, while weapon hits and planetary/station handling remain native. Saved,
default on; off immediately restores native circles. See the
[collision model](../Navigation/Collisions/README.md).

**Helmet and EVA battery shortcut** defaults to **Shift+F**. Click its button and
press a new keyboard chord; **Esc** cancels and **RESET** restores Shift+F. The
binding is saved. **Crew hotkeys** enables/disables it alongside Shift+R/T.

**EM propagation** and **Contact position estimates** are independent saved
switches, default off. See [sensor A/B testing](../Sensors/README.md#live-ab-testing)
for the combinations.

**Venus-specific wrecks** is a saved 0–100% slider (default 50%) for new VORB
hauler deployments. The remaining probability uses the general wreck pool.

**Torch burn diagnostics** is a session-only recorder, default off. Enable before
engaging a course, disable after arrival; reactor/autopilot data is written under
`BepInEx/TorchTraces`. Switching off removes its temporary observer hooks.
See [trace fields, APIs and bounds](../Diagnostics/README.md#torch-burn-diagnostics).

See [DESIGN.md](DESIGN.md) and [IMPLEMENTATION.md](IMPLEMENTATION.md) for the design,
entry points, packaging and validation status.

Development: build Debug, then run the existing `Deploy-Mod.cmd`. `Package-Mod.ps1`
stages the DLL, native assets and installer into a ZIP. No core game files are
overwritten. The native mod entry loads after `core`.
