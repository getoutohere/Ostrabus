# PDC ammunition regenerated during conversion

Always enabled by `PdcConversionAmmoPatch`.

## Root cause

All 15 vanilla PDC definitions (three models, each active/off/loose/damaged/
damaged-loose) specify `strLoot = ItmLootSpawnPDC20mm`. The recipe creates
`ItmAmmo20mm=1x50-100` every time the item is initialized with loot enabled
and without a saved CO state.

Installables.Create generates finishing mode-switch interactions for installing
and uninstalling PDCs. Interaction.ApplyEffects constructs their replacement
via DataHandler.GetCondOwner, passing the existing identity in strIDOld and
normally enabling starting loot. For installation, that identity can be the
construction placeholder's persistent PDC input. The source PDC is still in
DataHandler.mapCOs when the factory is entered.

CondOwner.SetData therefore stocks the new PDC with a freshly rolled magazine.
ModeSwitchInternal subsequently transfers the existing container contents
into the replacement. New ammunition has already been created before that
transfer; if the combined inventory does not fit, native overflow handling can
drop rounds nearby. Repeating conversion can repeatedly create ammunition,
including refilling a previously empty gun. The same mechanism can affect
other PDC mode conversions if their caller enables loot.

## Patch boundary

The prefix changes only the factory's bLoot argument when all are true:

- Loot is requested and no saved state is being restored.
- The destination name belongs to the ItmShipWeaponPDC family and its definition
  still uses the native ItmLootSpawnPDC20mm recipe.
- strIDOld resolves to a live, non-destroyed CO with IsShipWeaponPDC.

This runs before the native factory registers the replacement under that ID.
It prevents creation of the extra rounds rather than deleting objects after
they have been generated. Native condition inheritance, container/slot transfer,
stacking, overflow handling, mass updates, identity changes and item removal
remain untouched. An empty PDC stays empty; partially used ammunition can be
transferred in its current state. This does not alter weapon firing or reload.

New spawns retain their starting loot. Save restoration, unknown IDs,
non-PDC source items and modified loot recipe names retain native handling.
There are no new saved fields, retained game-object references, collections,
timers or Update hooks. The factory prefix's common rejection path checks its
arguments before looking up definitions or objects.

## Offline evidence

`Tests/PdcAmmoCheck` executes the actual DataHandler.GetCondOwner overload and
native Loot parser/roll/recursive factory calls against the installed game's
definitions. Unity object construction and the engine-heavy SetData leaf are
stubbed; that leaf retains the native `bLoot && jCOSIn == null` gate and calls
the actual loot generator. A deterministic midpoint roll produces 75 rounds.

Before patching, constructing the replacement of an empty PDC creates 75 new
rounds before ModeSwitch could run. After patching, conversion creates none.
Tests cover all 15 destination and source variants, unchanged references and
damage state of existing rounds, shared identity, ordinary new spawns,
loot-disabled initialization, saved state, absent/stale/non-PDC source IDs,
modified recipes and installable output mappings. Full Unity scene placement
and ModeSwitch execution are not simulated by this factory-level fixture.
