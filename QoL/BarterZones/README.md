# Multiple-ship barter zones

Sell-list zone groups display `O-GXD / Cargo`, sorted by callsign and then zone
name. Personal inventory keeps its existing order ahead of the ship zones.
The native still-loading notice follows actual ship groups. This applies to the
ordinary and faction trading interfaces through their shared row builder.

The row registry uses a ship/zone key distinct from item IDs, so two ships with
a zone called `Cargo` both remain accessible. Saved `JsonZone.strName` values
are unchanged. Purchase destination selection, zone permissions, item eligibility,
prices, transfers and overlap deduplication remain native.

`BarterZoneListPatch` sorts the supplied display rows and replaces only the
native zone-row key expression. `BarterZoneHeaderPatch` changes zone header text.
No scene polling, per-frame work, saved fields or retained row/ship references.
A temporary list proportional to the number of zones is allocated when the
sell list is rebuilt; personal item rows are not copied into it.

Always enabled in the normal plugin. Custom feature: `barter-zones` (**QoL**),
independent of the two [barter lifecycle/persistence bugfixes](../../MiscBugFixes/MULTI-SHIP-BARTER-AUDIT.md).

Offline validation is part of `Tests/MultiShipBarterAudit`: equal-name groups,
ordering, personal-row stability, unchanged saved names, distinct composite keys,
loading notices and production header hooks, plus native trade transactions.
