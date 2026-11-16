# CO2 scrubber audio regression

Ostranauts 1.1.0.0 removed the `TIsAirPumpAudioProducer` eligibility check from
`GasPump.Start()`. Previously only non-EVA devices with `IsAirPump` received
the gas profile's generic pump/air audio components. The CO2 scrubber is not
an `IsAirPump` device, but its unchanged `AtmoScrubber01` profile contained
`AirPumpActiveAir` and `AirPumpActive` sound entries. The broader 1.1 initializer
now creates those extra components.

`GasPump.Respire2()` refreshes its pump-active timestamp when its actor/signal
checks pass, before checking the amount of CO2 transferred. The generic pump
loop can therefore be audible even when no CO2 is transferred. This change
exposes an audio path that was inert in 1.0; it is not evidence that the 1.1
change newly bypasses AUTO or consumes scrubber cartridges continuously.
The existing CO2 profile's signal settings and native control behavior predate
1.1 and are not redesigned here.

The asset-only `OstrabusScrubberAudio` mod replaces exactly one native gas
profile, `AtmoScrubber01`, with the same definition except its two extra audio
entries are explicitly null. Native mods replace records by name, so the full
small profile is supplied. The scrubber retains its separate native
`ItmAtmoScrubberOnSound` device hum. No alarm, OFF/AUTO/ON setting, gas conversion,
filter consumption, heating or electrical behavior changes. Other scrubbers,
pumps, humans and suit respiration profiles remain untouched.

This is a bug repair, not a quieter-life-support option. Restoring the old C#
eligibility gate globally would also remove sounds newly supported for other
devices. Editing just the stale CO2 profile avoids that collateral change and
adds no Harmony hooks or runtime scans.

Full builds copy and install these assets through `Deploy-Mod.cmd`. Custom
builds select `co2-scrubber-audio`; their ordinary native-mod installer disables
the mod when omitted. Native assets require a game restart. Saved scrubber
items resolve the current gas profile when reconstructed; no save migration or
rewiring is required. Installed vanilla files are never edited.

`Tests/ScrubberAudioCheck` passes 39 checks using the 1.1 assembly and bundled
Mono runtime. It executes native profile loading and `GasPump.Start()` with
fixtures only for Unity object construction/audio configuration, reproduces the
two extra emitters, verifies their absence after loading the override, checks
every other profile and all non-audio fields for preservation, checks repeated
loading, and preserves the authored device hum. These checks do not claim an
audible Unity-scene playtest or a complete atmospheric simulation.

Run `powershell -NoProfile -ExecutionPolicy Bypass -File
Tests/ScrubberAudioCheck/Run.ps1` from the repository root. Its installer checks
use a disposable workspace directory and cover repeat installation, exact asset
hashes, omitted-feature disabling, preservation of user-disabled state and
explicit custom-feature re-enabling. The builder self-test passes 567 checks;
the audio-only custom build compiles/packages and binds zero Harmony classes,
with zero frame calls. The full Debug build copies the asset and retains the
same plugin DLL SHA-256 as the preceding compatibility candidate.
