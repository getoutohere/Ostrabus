# Nav console enhancements

The proximity dial is always enabled with the plugin.

The PDA Mod Settings app also offers **Hide nav tutorial note**. This hides the
entire clickable "Don't crash!" paper, including its click target. The setting
persists in BepInEx config (`NavConsole.HideTutorialNote`, default false), applies
immediately to existing consoles and is applied when new nav UIs initialize.
There is no per-frame polling or save data change; turning it off restores the
native note. Removing the mod restores vanilla behavior.

## Proximity range dial

The warnings module receives a compact native-style knob beside the proximity
mute switch. Its four positions are **5, 10, 15 and 20 km**. The visible knob is about 24 UI
units across, centered directly below DOCK SYS CLAMP and horizontally level with
the proximity switch, inside a 44×44 input area. Four fixed numeric labels surround
the detents without a KM suffix, at 50-degree spacing along an upper arc
(165°, 115°, 65°, 15°) on a 20-unit radius. A centered RANGE caption occupies the
clear space below the knob.
They reuse the DISPLAY CONTROLS label dial's font, material, color, style and
gradient. A round charcoal molded-plastic body has six very shallow finger grips,
an upper-left highlight and lower-right shadow. The body stays stationary while
a separate thick pale-gray index rotates toward each fixed numeric label. Its
2×8-unit size is unchanged; RGB 160/161/167 comes from a native pointer sample in
the supplied screenshot. The image uses a 33.5-unit rectangle including padding,
preserving the visible diameter of the previous artwork. Revision three removes
the pronounced gear lobes and separate face ring.
The body has a neutral 65% brightness tint to match the darker native knobs;
the separate pointer retains its native sampled gray.
The body also uses a native Unity UI Shadow effect (2 units right/down, 85%
black opacity, respecting sprite alpha) to retain a clear cast shadow at the
small display size. The shadow remains stationary with the body.
`RotaryKnob` provides click/drag detents and the native knob click sound.
Left/right clicking or dragging changes position; it stops at the end
positions. No hover tooltip is added, matching the other console knobs.

All nav consoles on a ship share the selected range. The existing flip switches
retain their independent native mute behavior. Console UIs synchronize through
the existing NavModWarnings.UpdateUI message, not a new Update or timer. Alignment
is refreshed from the existing lamp/switch transforms during the same message.

## Alarm behavior

Vanilla ship-contact warning buffers are 5 km multiplied by relative speed / 750
m/s, clamped to 1–10: a 5–50 km buffer. Body warnings use a fixed 5 km buffer.
These buffers are added to native collision-boundary distances.

For ship contacts the new buffer is `max(selected km, vanilla speed-scaled km)`.
For body contacts it is the selected range. Selecting 20 does **not** multiply the
speed-based range into 200 km. At 5 both paths preserve native constants exactly.
The threshold comparison and normalized distance used for pitch receive the same
buffer. Actual collision boundaries, collision processing, exclusion filters,
time-scale reset, objectives, sound and mute logic remain native.

Changing range clears the ship's acknowledged proximity contacts, allowing the
next native collision check to warn under the new range. No collision scan is
invoked from a UI callback.

## Persistence and lifetime

The runtime value is in a ConditionalWeakTable keyed by Ship, containing only an
integer. Collision checks read it once per call, with a nesting/exception-safe
thread-local scope for their range helpers. No additional contact scan or
per-contact allocation is introduced. Scene unload and plugin teardown clear the
weak table; UI objects/callbacks are owned by their native warnings module.
Knob input is event-driven with no Update/coroutine. Drag steps use local UI
coordinates, respecting module/window scaling; release leaves the selected angle.
One lazily created body sprite/texture is shared by the UI instances. Its PNG is embedded
in the plugin, decoded with CPU readability disabled, and released on plugin
teardown. The index uses a solid UI color. No external image deployment or
native asset-mod enablement is needed for this dial.

Native ShipCO saves do not serialize GUI property maps. Instead, the shared value
is mirrored into the installed nav consoles' existing `Panel A` property maps as
`TemplateExample.ProximityRangeKm` and `TemplateExample.ProximityShip`. Native
JsonItem serialization already saves these maps. A matching ship ID restores the
single shared value, so moving a console to another vessel does not impose its
previous ship's setting. Changes synchronize all installed consoles; opening a
warnings module also refreshes their copies. Unrelated native properties remain
untouched. If every console carrying the setting is removed, that ship may revert
to 5 on reload; within the current session its cached selection remains intact.

Missing/malformed values default to 5; only the four explicit positions are
accepted. These are ordinary native string entries, requiring no custom save
DTOs, item definitions or sidecar files. Without the plugin vanilla ignores them.

## Patches and verification

- `ProximityCollisionPatch` wraps CheckCollisions and patches only the two
  ship-buffer uses and two body-buffer constants in the native warning branches.
  Unknown IL emits a warning and retains the original body. Collision instructions
  are otherwise unchanged.
- `ProximityDialInitPatch` adds the dial after the native warnings initialization.
- `ProximityDialRefreshPatch` synchronizes its readout after native UI updates.

`Tests/NavConsoleCheck` runs with the game's bundled Mono: 842 checks cover all
positions and native speed factors, shared console copies, malformed values,
native JsonItem/LitJson roundtrip, transfer to another ship, nested range scopes,
unchanged surrounding native IL, equal label angles/radii, index alignment, drag steps/end
stops, embedded PNG presence and installation of all three Harmony patches.
The count includes comparisons of each untouched native collision instruction.
Engine object equality is a fixture. Native UI rendering and pointer interaction
cannot be tested in that harness, so placement remains a first-launch check.

Build Debug and run the existing deployment script with the game closed. This
module needs only the plugin DLL/PDB; no new JSON or image assets are installed.
