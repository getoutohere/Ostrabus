# PDA settings

The **MOD SETTINGS** app controls existing live mod options. Install both the
BepInEx DLL and the accompanying `TemplateSettings` native asset mod, then restart.
Open the app from the PDA home screen. Each row identifies whether changes persist
to config or apply only to the current session.

See [DESIGN.md](DESIGN.md) and [IMPLEMENTATION.md](IMPLEMENTATION.md) for the design,
entry points, packaging and validation status.

Development: build Debug, then run the existing `Deploy-Mod.cmd`. `Package-Mod.ps1`
stages the DLL, native assets and installer into a ZIP. No core game files are
overwritten. The native mod entry loads after `core`.
