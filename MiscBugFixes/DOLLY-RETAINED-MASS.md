# SuperHandy retained cargo mass: remote barter diagnosis

Confirmed November 5, 2026 against the installed assembly and data. The on-demand
`massfix` repair command (formerly `dollyfix`) is implemented in `MassRepair.cs`.
Remote trading mass prevention is now implemented in `BarterMassPatch.cs`;
the historical failure path is recorded below. The existing barter item-removal
fix remains intact.

A separate normal-inventory double-subtraction defect is now reproduced in
[STACK-REMOVAL-MASS.md](STACK-REMOVAL-MASS.md). Remote barter is not the only
cause of incorrect container mass.

The asset definition `ItmDolly01` gives the Testudo SuperHandy an empty mass of
103 kg. `StatMass` includes container contents and is saved as a condition, rather
than being freshly recomputed whenever the inventory is displayed.

## Confirmed failure path

1. `CondOwner.Init` sets `bFreezeConds = true` when loading saved CO data.
2. Normal `Ship.PostGameLoad` eventually clears that flag. `BarterZoneShip.Init`
   calls each CO's `PostGameLoad` directly and does not perform the ship-level
   unfreeze pass. This preserves the saved aggregate mass during reconstruction,
   but also leaves mass bookkeeping frozen during trade.
3. Selling contained cargo calls native `Container.RemoveCOSimple`, which removes
   the item and calls the container's `AddMass(-cargo.GetTotalMass())`.
4. Native `CondOwner.AddMass` returns immediately when `bFreezeConds` is true.
   The cargo disappears but the container retains its mass.
5. `BarterZoneShip.SaveChangedCOs` refreshes `DataHandler.dictCOSaves` only for COs
   absent from the original layout (newly added items). Existing containers'
   saved condition records are not refreshed. This independently retains old
   mass even if runtime mass accounting is unfrozen.
6. The next normal load restores the stale aggregate mass while suppressing mass
   changes during parent reconstruction. Missing cargo therefore does not repair
   the saved mass.

The previous `BarterRemovalPatch` corrects dispatch to the specialized barter
removal method, preventing sold cargo from surviving in layout JSON. It does not
address frozen mass accounting or existing containers' condition save-back.
This defect can affect other containers and nested ancestors as well as dollies.

## Evidence

Read-only inspection of `Saves/pathcheck/pathcheck.zip` found dolly
`e2b34ef1-79f6-464b-bcb8-c892af21c8a7` in `BCER|RES_1` with saved mass 714 kg
and no attached container or slot children. This proves persisted excess mass;
the historical operation that produced that particular value is not recorded.
The saved dolly `806b48e7-1ecc-4e30-bef1-e27495453471` is also empty but records
100.9 kg. Do not attribute this separate underweight discrepancy to the same
history without further evidence. Other heavier dollies in the save have cargo;
their raw mass alone is not evidence of corruption.

`Tests/BarterPersistenceCheck/MassDiagnosis.cs` reproduces the failure with a
103 kg dolly and 611 kg cargo. Native container removal, native AddMass and
GetTotalMass arithmetic, production barter removal dispatch, and native
SaveChangedCOs execute in the bundled Mono runtime. Condition scalar storage
and Unity scene/UI access are fixtures; no save is edited.

- Frozen remote copy: empty runtime dolly remains 714 kg; saved mass remains 714 kg.
- Unfrozen control: empty runtime dolly reaches 103 kg; saved mass remains 714 kg.
- Both cases remove cargo from runtime container and saved source layout.
- The complete barter suite passes 51 checks and verifies the source save hash
  remains unchanged.

Temporary saved-state extraction artifacts are under `obj/DollySave`.

## Fix direction

Implemented: native mass bookkeeping is permitted only during real transfer calls
on a fully loaded BarterZoneShip. Freeze flags are restored even after exceptions;
reconstruction and ordinary offscreen simulation remain frozen. Actual changed
masses are recorded as IDs/scalars under weak copy keys and saved on native
SaveChangedCOs completion. Only saved StatMass/explicit mass overrides change;
existing electrical GPM data and other conditions remain intact. DEFAULT condition
expressions are expanded without random evaluation so they cannot replace the
corrected mass on reload. Destroying a barter copy drops pending state.

101 barter checks pass, including additions/removals with frozen/unfrozen controls,
nested ancestors, mass/default/override persistence, wiring preservation, incomplete
loading, exception cleanup, remote stack members and the existing duplication fix.
Source save SHA256 is unchanged. Output: `obj/BARTER-MASS-FIX-CHECK.txt`.

Original design considerations:

Keep conditions frozen during reconstruction so saved cargo mass is not counted
twice. After reconstruction, allow controlled mass adjustments for real transfers
and persist the changed aggregate mass for existing containers and their ancestor
chain. Do not indiscriminately unfreeze all condition processing, reset every
object to its asset mass, or run a periodic full-world mass audit. Any repair of
already corrupted containers must account for current contents and legitimate
mass variations; prevention and historical repair are separate responsibilities.
