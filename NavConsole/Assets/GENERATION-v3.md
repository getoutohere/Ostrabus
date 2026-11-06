# Round finger knob, revision three

Generated with the built-in image_gen tool, editing revision two. The unchanged
output is `RoundKnob-v3.png`, now embedded under the existing resource name.
Older revisions remain as source history and are not embedded.

Six shallow, broad finger grips replace the pronounced lobes. The face is smooth
and slightly domed, with a simpler bevel. Lighting/shadow stay fixed. The UI's
separate indicator retains its 2×8-unit size and now uses pale gray RGB 160/161/167,
sampled from a native pointer in `navuiv2.PNG`, per Jon's follow-up request.
The image rectangle grows from 30 to 33.5 UI units to compensate for increased
transparent padding; measured opaque center widths are 914 versus 820 pixels on
the same 1254-pixel canvas. This preserves the visible knob diameter.

## Prompt

Use case: precise-object-edit. Refine this spacecraft UI knob into a simple molded-plastic finger knob, readable at 24 pixels. Preserve exactly the square canvas, centered body, overall body diameter, warm dark charcoal plastic, upper-left soft highlight, lower-right soft cast shadow and actual transparent background. CHANGE THE SILHOUETTE: remove the eight pronounced gear-like lobes and deep valleys. Make the rim almost circular with only SIX very shallow broad smoothly rounded finger-grip depressions, subtle enough that the silhouette reads first as a round knob, not a gear or a star. Shallow contour variation no more than about 3 percent of radius. One smooth slightly domed face that blends into a simple rounded bevel; remove the strong separate concentric face ring and all fine texture or knurling. Chunky understated worn plastic game art, softly painted, low detail, no noisy texture. No teeth, serrations, sharp corners, decorative rings, metallic reflections, labels, text, ticks, numbers or index line. The orange pointer is rendered separately in code, so generate only the clean knob body and its fixed shadow. One transparent PNG sprite.
