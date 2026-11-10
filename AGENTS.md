# Bugfix implementation policy

Inspect both the native C# logic and the relevant game asset JSON before choosing
where to fix AI behavior or world simulation mechanics. Fix the cause at the
layer that owns the defect; preserve the game's authored behavior and emergent
interactions.

- When assets already define the desired behavior but a C# bug prevents it from
  working, patch the C# defect so the existing assets become effective. Keep
  native triggers, conditions, pledges, interactions and other asset evaluators
  authoritative rather than duplicating their policy in Harmony hooks.
- When the C# mechanism works correctly but the assets contain a logical flaw,
  favor correcting the assets through the game's native JSON modding interface,
  following the packaging/loading approach used by PdaSettings. Do not edit the
  installed vanilla asset files directly.
- If both layers are faulty, repair each at its own layer. Add C# integration
  only where the native asset interface cannot express or execute the correction.

Do not substitute symptom correction, forced outcomes or a parallel C# behavior
controller for a repair to the existing mechanism. Distinguish bug repairs from
new behavioral policy, and document any intentional policy changes separately.
