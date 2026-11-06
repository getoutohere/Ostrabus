# Ordinary stack removal subtracts container mass twice

Confirmed November 5, 2026. Prevention implemented in `StackMassPatch.cs`.

This is separate from the frozen remote-barter/save-back defect. It reproduces
on an ordinary Full ship with every CO unfrozen.

## Native failure path

An individual non-head stack member can have its enclosing container as
`objCOParent`, with `coStackHead` referring to another item in that container.

1. `CondOwner.RemoveFromCurrentHome` delegates to the enclosing owner's RemoveCO,
   then `Container.RemoveCO` calls `RemoveCOSimple` for that member.
2. `Container.RemoveCOSimple` calls `Ship.RemoveCO` before clearing membership.
3. `Ship.RemoveCO`'s `coStackHead != null` branch subtracts the removed member's
   total mass from the stack head's parent, and removes it from the head's stack.
4. Back in `RemoveCOSimple`, the enclosing owner subtracts the same total mass
   again. Native `AddMass` propagates both deductions through all ancestors.

`CondOwner.RemoveCO` also has a stack-member branch with its own subsequent
parent-mass deduction. That is an additional source-review lead, not the branch
used in the reproduction below.

## Offline reproduction

`Tests/DollyMassCheck/StackRemovalDiagnosis.cs` executes native
RemoveFromCurrentHome -> CondOwner.RemoveCO -> Container.RemoveCO/RemoveCOSimple
-> Ship.RemoveCO -> AddMass/GetTotalMass. Condition scalar storage and Unity
scene/UI/registry I/O are fixtures; mass-transfer methods themselves execute.

- Crate tare 10 kg, containing two 20 kg floor objects in one stack: starts at
  50 kg. Remove one non-head member: native crate mass becomes 10 kg; expected
  30 kg, because one floor remains.
- Crate tare 10 kg containing a backpack tare 2 kg with the same floor stack:
  backpack starts at 42 kg and outer crate at 52 kg. Remove one member: backpack
  becomes 2 kg instead of 22, outer crate 12 kg instead of 32.
- Control: remove the whole loaded 42 kg backpack from the 52 kg crate. Native
  result is correct: 10 kg crate and 42 kg backpack. Whole-backpack removal alone
  did not reproduce this bug.

The suite passes 43 checks. Output: `obj/NORMAL-STACK-MASS-CHECK.txt`.
This establishes a normal-inventory cause of underweight containers; it does not
establish the historical operations responsible for each live repaired crate.

## Fix direction

Implemented: each nested removal operation shares an owner/item mass charge.
The native AddMass calls at the three removal sites pass through a scoped helper;
the enclosing call applies only the difference from a charge already made inside
it. A direct Ship.RemoveCO still applies its original debit. Scratch is reused per
thread and cleared at the outermost finalizer, including errors, leaving no
retained owner/item references. There is no background repair pass.

80 container checks pass. The native container, stack-head and direct-ship member
paths all produce correct mass for simple/nested containers. Whole loaded bags,
head pops, whole stacks and frozen loading are covered. Exception tests verify
scope depth/reference cleanup. Output: `obj/STACK-MASS-FIX-CHECK.txt`. The separate
barter suite also verifies both fixes compose with remote stack transfers.

Original design considerations:

Make one layer responsible for stack-member mass removal across the enclosing
container, stack-head and direct Ship.RemoveCO entry points. Preserve direct
ship removals, full-stack/head removal, partial stack transfers, nested ancestors,
slot paths and frozen-load semantics. Avoid simply deleting the ship-level
deduction globally, because callers that remove members directly need it.

The existing `massfix` can repair eligible outer containers using current child
masses. It skips unaudited backpack definitions; therefore it cannot guarantee
complete repair when an underweight backpack remains inside another container.
