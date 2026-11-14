# Preview cargo-market isolation

Implemented for 1.0.1.10. The independent native diagnosis, save evidence and
reproduction harness are in [CargoPodDiagnosis](../Tests/CargoPodDiagnosis/README.md).

`AsyncShipLoader.LoadRatingShip` copies the real call sign, while
`AsyncRatingShip.Init` gives copied items new GUIDs. Installed cargo-pod assets
correctly include `IsMarketActor` and the `Trader/CargoPodSpec` property map.
Native `MarketActor.Update` consequently registers the copied pods against the
real ship's persistent market. Preview destruction removes the temporary items
and loaded actors but retains market configs, as required for real ship unloading.
The ghosts survive save/reload and appear as extra cargo-pod panels.

The independent inspection found four physical O-GXD pods versus eight global
market configs; native replay reproduces the same growth. This is a C# isolation
defect. Asset changes would also disable genuine cargo pods.

`PreviewMarketIsolation` guards `MarketManager.RegisterTrader` for actors owned
by `AsyncRatingShip` or `AsyncPreviewShip`. It clears only the presentation
actor's local `_strMarketConfig`, which also prevents the following native
inventory sync and subsequent transaction reports from changing real stock.
Native Update marks initialization complete, avoiding repeated attempts.

Real ships and authoritative `BarterZoneShip` copies retain native registration,
unloading, stock and persistence. Existing records are not deleted: legitimate
unloaded items can lack live COs, and old ghosts may contain paid-for cargo.
Reconciliation therefore needs separate physical/save ownership evidence and
cargo-preserving recovery. The prevention patch does not clean historical ghosts.
Use the explicit [cargopodfix command](CARGO-POD-FIX.md) to inspect and reconcile
verified empty ghosts on player-owned ships.

One registration hook; no frame callback, polling, caches, journals or save fields.
The independent custom feature `preview-market-isolation` is included in bugfix
presets, also includes `cargopodfix`, and does not require async-cleanup or
remote-barter patches.

Validation: 11 native baseline and 30 patched assertions in bundled Mono. Native
registration, removal, enumeration, market serialization/reload and the actual
production guard execute. Unity equality/destruction callbacks, singleton access,
blockade lookup and prepared real/preview objects are fixture boundaries. Controls
cover both preview types, matching real IDs, inventory-producing actors,
transactions, absent preview-only markets, loaded/unloaded offscreen barter cargo
and preserved historical orphaned stock. No live market or save is modified.
