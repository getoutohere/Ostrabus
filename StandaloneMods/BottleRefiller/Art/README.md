# Bottle Refiller artwork

`../Assets/images/` contains the five textures shipped with the mod: healthy
and damaged equipment in installed and loose forms, plus a neutral normal map.
This directory retains only the current source artwork and regeneration tools.
Rejected versions, enlarged test sprites, comparison sheets and obsolete
generation notes have been removed.

## Retained inputs

- `bottle-refiller-undamaged-v9-master.png`: generated source for the approved
  healthy equipment, with its indicator recess on the exposed bottle side.
- `bottle-refiller-damaged-v1-master.png`: generated damage-state edit of the
  custom equipment, retaining its layout and identity.
- `bottle-refiller-undamaged-v9-16x48.png`: native-sized healthy source.
- `bottle-refiller-damaged-v1-export-16x48.png`: raw damage export, before
  clipping protrusions to the healthy silhouette.
- `bottle-refiller-sleeve-v12-layer-16x48.png`: fitted native tan wrapping.
- `bottle-refiller-loose-v12-16x48.png`,
  `bottle-refiller-damaged-v1-16x48.png` and
  `bottle-refiller-damaged-loose-v1-16x48.png`: final composition outputs used
  by `Generate-Assets.ps1` and the artwork regression checks.

The version suffixes identify the selected source revisions, not alternate
runtime options. The small exports are build inputs; they are not test sprites.
The healthy and damaged masters were made with the image-generation tool.
Their final native-sized compositions were reviewed and approved by the user.

## Regeneration

Run from the repository root:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File StandaloneMods/BottleRefiller/Art/Export-NativeSprite.ps1 -Source StandaloneMods/BottleRefiller/Art/bottle-refiller-undamaged-v9-master.png -Destination StandaloneMods/BottleRefiller/Art/bottle-refiller-undamaged-v9-16x48.png
powershell -NoProfile -ExecutionPolicy Bypass -File StandaloneMods/BottleRefiller/Art/Export-NativeSprite.ps1 -Source StandaloneMods/BottleRefiller/Art/bottle-refiller-damaged-v1-master.png -Destination StandaloneMods/BottleRefiller/Art/bottle-refiller-damaged-v1-export-16x48.png
powershell -NoProfile -ExecutionPolicy Bypass -File StandaloneMods/BottleRefiller/Art/Compose-NativeSleevePreview.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File StandaloneMods/BottleRefiller/Art/Compose-DamagedSprites.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File StandaloneMods/BottleRefiller/Generate-Assets.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File Tests/BottleRefillerCheck/Test-Art.ps1
```

Export uses cell-center sampling, no smoothing, and removes near-transparent
matte noise. The composition scripts preserve the approved source and sleeve
hashes. Default previews and comparisons go to ignored `obj/BottleRefillerArt/`;
the composition scripts also accept `-PreviewDirectory`.

The wrapping is extracted from the native regular pump's installed/loose
textures, `ItmAirPump02Off.png` and `ItmAirPump02OffLoose.png`. Their 378 visible
differences must be the same tan RGBA `(186,167,138,255)`. The sleeve's lower
fold moves up five pixels to fit the shorter custom equipment. It covers 103
equipment pixels and leaves 319 exposed pixels unchanged. It is an overlay,
not a replacement pump or an invented packing cradle.

The damaged export is clipped to the healthy equipment silhouette, then wrapped
with the same sleeve. Damage remains visible above and below the wall tile.
No extra native pump machinery is copied into the custom device.

Native item rendering uses point filtering and sizes ordinary textures from
their pixel dimensions; enlarged masters must never be installed directly.
The green/amber indicator overlays sit at native image pixel `(11,37)` outside
the wall-hidden middle tile. They use a blank light color, so they do not emit
room illumination. The shared normal map is neutral. Pixel and composition
checks preserve these contracts; actual lighting and scene appearance remain
in-game acceptance checks.
