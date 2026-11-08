# Character mass reconstruction

Audited against 1.0.1.10. `massfix` now repairs eligible containers, anatomical
components and humans on materialized world ships. See the implementation below.

## Verified accounting

`Crew01` starts with 35.15 kg. That is its core CO, not a complete naked body.
`ItmCrewSlots` supplies twelve limb COs with fixed empty masses totaling
35.2845 kg. Upper limbs own lower limbs, which own hands/feet. Hands can also
own held equipment, so a limb's aggregate mass can include equipment.

`PersonSpec.MakeCondOwner` creates the core, applies profile/career effects and
equips limb loot. `PersonSpec.EquipLoot` uses `Slots.SlotItem`, which charges
each component's total to its actual slot owner through `AddMass`. Unslotting
reverses that accounting. `Crew.SetBodyFaceSkin` can replace limb COs through
`ModeSwitch`; actual present anatomy must therefore be used.

The installed condition/loot scan finds two trait mass modifiers: `IsStrong`
uses `CONDStrongPer` (+15 kg), and `IsFeeble` uses `CONDFeeblePer` (-10 kg).
The other non-overlay per-unit mass effect is extinguishing chemical. Item
overlays also change item masses; an asset-wide mass scan must include them.
Custom definitions, unsupported conditions and explicit layout mass overrides
require separate eligibility checks.

## Native tests

`CharacterBaselineDiagnosis.cs` reads installed definitions and exercises native
slot search, slot/unslot ownership, `AddMass` and actual trait loot. Rendering,
slot-action effects and inventory UI are adapters; this is not a full Unity
generation run.

| Default intact body | Naked mass |
| --- | ---: |
| Neither trait | 70.4345 kg |
| Strong | 85.4345 kg |
| Feeble | 60.4345 kg |

Removing/replacing the upper-arm subtree removes/restores its entire current
mass. Equipment held through a hand contributes once. These cases run before
and with live propagation hooks. Repair regressions additionally cover corrupt
anatomy, nested equipment, missing limbs, traits and idempotence.

## Save evidence

`Tools/CharacterMassAudit` streams global records and per-ship local records,
including crew layouts. Loaded characters need not be in the global array.
Saved `DEFAULT` expands through base/overlay lookup in native loading order;
ignoring this can falsely assign zero mass to clothing or anatomy.

In the audited latest save, 628 of 670 auditable humans agree within 1e-6 kg
with core + current traits + current immediate owned-root masses. The remaining
42 are discrepancies for investigation, not 42 independently proven historical
bugs. The formula preserves any child corruption. The archive hash was unchanged.

The player and crew mate have discrepancies at both character and anatomical
levels. One upper-arm record has zero mass despite a positive fixed base mass.
A character-only adjustment would leave such anatomical bookkeeping errors in
place. Rebuilt-anatomy figures still preserve current non-anatomical cargo mass,
so they are not unconditional repair targets.

## Implemented repair

1. Repair eligible nested containers with the existing conservative exclusions.
2. Repair reviewed anatomical COs bottom-up: fixed component mass plus actual
   owned contents. Preserve held items, wounds and the actual missing/replaced
   anatomy; exclude slot aliases and duplicate stack accounting.
3. Reconstruct eligible `Crew01` aggregate from core, current traits and corrected
   immediate contents. Apply the delta through native `AddMass` to update burden.
4. Skip/report uncertain graphs, overrides, definitions or mass-changing effects.

`MassRepairPolicy` allows the twelve reviewed limb base definitions and their
overlays without additional condition loot, and standard `Crew01` humans.
Strong/Feeble adjustments are read from the installed per-unit loot; malformed
trait states and unknown condition-driven mass effects are skipped. Missing
limbs are not generated. Held items, wounds, stack members and container contents
are counted from actual ownership; ambiguous dynamic cargo retains its mass.

`MassRepairBatch` plans each ownership tree before writing. A local correction
is known tare + current immediate contents - current aggregate. Adding all local
corrections up the tree yields its final totals. This handles a zero-mass arm
carrying a heavy crate without temporarily clamping its already-corrupt parents.
Final totals, freeze/load state, cycles and native condition clamps are validated.

Planned totals are applied deepest-first through native `AddMass`, with its parent
propagation suppressed for that write: every affected ancestor already has its
own planned total. Native labels and encumbrance, including the Venus dolly
discount, remain in use. A write failure attempts to restore the entire tree;
independent valid trees can still be repaired. All planning buffers are local
to the explicit command; no new hooks, polling or persistent references are added.

Rejected tare reconstruction does not prevent a parent receiving a valid cargo
correction. The log therefore distinguishes skipped policy reasons from the list
of objects whose final aggregate changed. Unknown definitions/overrides are never
replaced with a guessed tare.

Validation: 475 standard mass checks; the optional save-graph run passes 710
checks, including 231 detached objects from the latest player/crew inventory
graph. That graph produced 14 corrections and 22 conservative skips; the second
run made no corrections. Predicted equipment-inclusive totals were 175.0895 kg
and 112.2745 kg for the player and crew mate. The source save and detached input
hashes were unchanged. These are offline results, not a live repair or a claim
to identify every historical source of corruption.
