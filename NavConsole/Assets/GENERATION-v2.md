# Round proximity knob, revision two

Generated using the built-in image_gen tool, with `navui.png` and the previous
`RoundKnob.png` as references. The original generated output is saved unchanged
as `RoundKnob-v2.png` and embedded under the existing runtime resource name.

The new body is stationary so its upper-left illumination and lower-right shadow
stay consistent with the panel. A separate Unity UI orange index rotates to each
detent; labels use equal angles and a fixed 20-unit radius. The original image is
retained for reference, but is not embedded in the DLL.

## Prompt

Use case: precise-object-edit. Create one replacement rotary knob body sprite for the small dial at the right of the WARNINGS module in reference 1. Reference 1 is the target game's UI: match the DISPLAY CONTROLS knobs' chunky worn black molded-plastic look, broad soft upper-left highlights and obvious lower-right cast shadow. Reference 2 is our previous knob: replace its fine machined detail and flat lighting with a more readable molded-plastic body. Keep a round, centered, top-down silhouette with just eight broad rounded finger-grip scallops around the rim, a raised broad smooth central cap, strong simple bevel and slight visible thickness. It must read clearly at only 24 pixels wide. No fine grooves, noisy texture, gear teeth, concentric decorative rings or photorealistic reflections. Dark warm charcoal, restrained gray highlight, softly painted game-asset rendering. IMPORTANT: omit the orange pointer entirely; a separate rotating orange line will be drawn by the game UI. No letters, numbers, ticks, controls, plate or background. Exactly one isolated knob BODY with a soft lower-right shadow on actual alpha transparency. Center the actual circular knob body exactly on the square canvas center, use about 76% of the canvas width for the body, leave enough transparent padding for the shadow. Do not reproduce either reference screenshot, just produce the single transparent knob sprite.
