# Multiple-ship barter reproductions and repair checks

The harness first reproduces the native defects, then installs the production
repairs offline and verifies their corrected behavior and controls.

Build, then run sequentially from the repository root:

```powershell
dotnet build Tests/MultiShipBarterAudit -c Release --nologo
dotnet run --project Tests/SaveJsonCompatibility/Host -c Release --no-build -- Tests/MultiShipBarterAudit/bin/Release/netstandard2.1/MultiShipBarterAudit.dll
```

Uses the configured game installation and its shipped Mono/Assembly-CSharp.
Current result: **274 assertions**, exit code zero. No game process, save file or
installed plugin is changed. Host staging/logs belong in ignored `obj/`.

## Native code and fixture boundaries

- `GUITrade.BuildSellList`: actual grouping, key selection and duplicate handling.
  Prefab instantiation, row rendering, warning output and Unity object equality
  are substituted. A guarded test-only transpiler requires exactly one row
  factory replacement. Equal zone names on different ship IDs lose a row;
  distinct-name control creates both. With the production QoL patches, both
  equal-name zones remain visible; callsign/zone ordering, personal-row stability,
  distinct keys and unmodified JSON names are checked. The production header
  postfix runs with the original rendering setup suppressed and text captured.
- `AsyncShipLoader.Unload`: actual cancellation iteration, cache removal and
  readiness queries. Coroutine handles have no live Unity backing; stopping
  them records the call and destruction is stubbed. Their native finalizers
  are suppressed. Before repair, unrelated B is left cached, unavailable and
  unfinished. After repair, unloading A cancels only A; B can finish normally.
  Unknown IDs and global unload are controls. Native barter/preview/rating and
  optional paced initialization iterators all identify their correct owner.
  Job metadata is empty after cleanup; these tests never run scene creation.
- `BarterZoneShip.SaveChangedCOs`: actual marker insertion, JSON layout membership
  and commit logic. First commit succeeds, native specialized removal clears A,
  then a fixture moves the same CO into B. Transform position and item/CO
  serialization are substituted because they require scene objects. B throws
  on the duplicate marker and leaves the runtime item displaced and uncommitted.
  Repeat with the production native writeback safety patch, and then with its
  optimized counterpart: both persist the item and restore its position. Fault
  injection at GetJsonItem/GetJSONSave checks exact restoration, preserved marker
  payloads, failed layout commits and successful retries.
- `TradeRoundtripChecks`: native `GUITrade.OnBuy -> OnSell -> OnBuy`, including
  `GUITradeBase.BuyItems`, `TileUtils.DropCOsNearby`, `TryFitItem`,
  `CondOwner.PopHeadFromStack`/`RemoveFromCurrentHome`, normal and barter-copy
  `Ship.AddCO`/`RemoveCO`, `RecordTransaction`, and loader saveback. Three
  placement/addition/removal calls per sequence are asserted. No fixture performs
  the physical transfer or changes the item's owner between transactions.
  The loose oxygen alarm's starting conditions and the relevant Fixer filters
  come from shipped assets; native trigger evaluation confirms eligibility.
  Twelve sequences cover same/different destination and marker retained/cleared
  on vanilla, repaired native and repaired optimized paths. Native unload then
  discards the proxy: vanilla leaves the failed delivery absent from JSON,
  while both repaired paths retain it in the destination's saved inventory.

For native-handler sequences, selected rows/toggles/dropdown state are supplied
as fixtures rather than clicking rendered controls. Prices are fixed at 10 test
units and condition currency storage is scalar; the native payment method runs.
Scene queries supply empty valid floor space; item fit, transforms, component
lookups and visual/tile/room/UI/audio bookkeeping are scene-engine boundaries.
`DropCOsNearby` and `TryFitItem` execute their actual native placement algorithms;
only `Item.CheckFit` supplies a positive geometry result. Component lookups are
replaced by guarded test-only transpilers (exactly two substitutions per method).
Logs, achievements, ledger notifications and list redrawing are suppressed.
The cleared-marker control removes just that metadata; it does not emulate or
claim to execute the complete ship-loading process. There is no save ZIP rewrite
or full save/reload fixture.

All fixture hooks are confined to the harness process and unpatched on exit.
The repair checks install the actual production patch classes; transfer, payment
and final saved-layout assertions use native methods rather than a substitute
transaction implementation.

See [findings and implemented repairs](../../MiscBugFixes/MULTI-SHIP-BARTER-AUDIT.md).
