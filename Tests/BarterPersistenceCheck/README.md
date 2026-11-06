# Unloaded barter inventory persistence reproduction

No runtime patch is included. The test reads O-GXD cargo from the latest inspected
save, wtf/wtf.zip (2026-11-04 20:00 local). That saved hull has no docking links;
this is a focused unloaded-barter-copy fixture, not a replay of an apartment /
Mescaform scene or the Fixer's complete UI transaction.

Fixture: ItmFloorAERO07Loose, 17babb0a-cdc5-486b-ba62-0c79392cbb34,
in floor crate ItmCrate01, d3d8da7c-a6c8-45c1-886f-3504df11e03c.
All saved item entries are retained in the fixture's JSON arrays.

The executable uses the real bundled game assemblies and Mono runtime:

- Native Container.RemoveCOSimple selects ordinary Ship.RemoveCO, bypassing
  the hidden BarterZoneShip.RemoveCO implementation.
- The outer Ship.RemoveCO entry is explicitly substituted to invoke its actual
  native RemoveInternal bookkeeping. This omits Unity scene, tile, inventory UI,
  mass and power side effects; it is not an end-to-end native transfer test.
- Native Ship.RemoveInternal removes the runtime map entry. The base Ship.json
  is null, as in the barter subclass initialization path; the subclass's separate
  json property still contains the sold item.
- Native BarterZoneShip.SaveChangedCOs writes that surviving entry back into
  the original ship JSON.
- Native GUITrade.RecordTransaction credits the player with an arbitrary 100
  test units. Only condition/currency storage is substituted; trader acceptance,
  actual price calculation and GUITrade.OnSell are not executed.
- Native RemoveFromLocalInstance followed by native SaveChangedCOs is the
  control: it removes exactly one item entry. This tests the specialized
  persistence operation, not the complete loose-item removal sequence.

Other explicit doubles cover Unity object identity, position/parent access,
CondOwner.Item, ship assignment side effects, parent validation, condition
queries, mass, ticker removal, rooms, container condition cleanup, and UI grid
bookkeeping. Unity icall warnings while compiling original bodies are expected
outside the engine. Tests complete successfully despite those warnings.

13 checks passed. The source archive SHA256 was unchanged:
723D3FA606DA67D7A9036E3DC7F3C24F44E760EB3013F3FF4062313B5AD14729

Before running, extract only ships/O-GXD.json from the archive into
obj/BARTER-SOURCE-O-GXD.json using .NET ZipFile/StreamReader in PowerShell.
The bundled Mono host cannot decompress this ZIP without MonoPosixHelper.
Then build Tests/BarterPersistenceCheck in Release and run its DLL through
Tests/SaveJsonCompatibility/Host. The executable hashes the archive before and
after execution to confirm it is unchanged; it never writes to the archive.

This confirms the non-virtual dispatch / separate-JSON persistence defect.
It does not yet prove the exact full in-game transaction route responsible for
every reported repeat sale, or independently reproduce stack-only behavior.

## Loose item follow-up

Added a real uncontained/unstacked fixture from the same save:
ItmWallWindow1x1Loose, 13731798-c6ad-4508-8fdd-4b26496b1d00, at (120,27).
Its JsonItem has no parent/slot parent, its saved CO has no stack, and no saved
item lists it as a parent.

Executed native PopHeadFromStack -> RemoveFromCurrentHome ->
BarterZoneShip.RemoveCO, then native payment and SaveChangedCOs. The specialized
RemoveCO method is NOT substituted: its actual body removes the runtime CO and
calls RemoveFromLocalInstance, which removes the shadow JSON entry. Save-back
keeps the loose item removed and preserves all other item entries.

Extra doubles cover component GameObject access, layer assignment/name lookup,
UpdateTiles, tile lookup, and native editor-only CheckTrue validation. These
replace scene bookkeeping rather than item/JSON persistence. No actual item
placement into the trader's inventory or GUITrade.OnSell UI execution occurs.

22 total checks now pass. The loose fixture does NOT reproduce duplication.
The container-dispatch finding therefore must not be presented as a complete
explanation for repeat sales of loose, unstacked items. Such a report would
require another cause or a broader end-to-end reproduction.

Output: obj/BARTER-LOOSE-TEST.txt. Source archive SHA256 remains unchanged.

## Production fix validation

The test now links MiscBugFixes/BarterRemovalPatch.cs. After the vanilla
reproduction and loose control, it removes the ordinary Ship.RemoveCO fixture
substitution and installs the actual production Harmony prefix. All redirected
calls execute native BarterZoneShip.RemoveCO and native removal/save-back.

42 total checks pass, covering contained cargo, a direct base-typed call,
bForce forwarding, the previously working loose-item path, repeated save-back,
and a partial three-item stack. The native stack-member caller
CondOwner.RemoveCO removes only the selected member; the head and unsold member
remain in runtime and persisted inventory.

The crate-sale fixture maps its full saved parent graph: one floor crate plus
114 descendants. Nested parent relationships are represented with Container
fixtures to exercise native descendant visitation and removal. The test checks
that precisely this graph disappears and all unrelated saved entries remain.

Additional engine doubles supply object hashing/equality, Item lookup, shader
property IDs, transforms/rotation/parenting, parent assignment's clock side
effects, and appearance updates. The stack fixture is controlled rather than a
complete GUI sale of a saved stack. Ordinary-ship scope is checked directly at
the prefix guard. Output is obj/BARTER-PATCH-TEST.txt.
# Retained container mass diagnosis

`MassDiagnosis.cs` also executes native mass arithmetic and source save-back for
a dolly containing 611 kg of cargo. Frozen remote-trade objects ignore removal's
mass subtraction; independently, existing container condition records are not
refreshed by `SaveChangedCOs`. Both frozen and unfrozen controls preserve 714 kg
in the saved condition record after the cargo has been removed from layout JSON.
See `MiscBugFixes/DOLLY-RETAINED-MASS.md`. These cases diagnose the native bug;
they do not represent an implemented mass fix.

