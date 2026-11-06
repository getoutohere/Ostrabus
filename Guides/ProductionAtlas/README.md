# Production Atlas

Open **index.html** directly in Edge, Chrome, Firefox, or another modern browser.
No server, installation, package manager, network connection, or third-party
library is required. Keep the HTML, CSS, and JavaScript files together. All assets
are bundled plain text; there are no external fonts, images, analytics, or CDNs.
Local data is loaded through a normal script tag, so file-URL fetch restrictions
do not apply.

## Explore a chain

1. Select your departure station and one of its produced goods.
2. Choose **Net surplus** (gross output minus configured local consumption) or
   **All output** (gross production, even when local recipes also need it).
3. Select a destination from the left. Its output branches are separate ways to
   use the incoming cargo; they are not simultaneous allocations.
4. Select an output branch. Review its batch recipe, cargo usage, leftover cargo,
   output rate, and every co-input required at that rate.
5. Choose **Continue with this output** to carry that rate into the next leg.
   Breadcrumbs return to earlier points. Six transformations are supported;
   repeating the same destination/recipe is blocked to avoid circular expansion.
6. Search destinations or products, filter a region, or include consumption-only
   routes. **Save notes** downloads a text summary of the explored chain plus the
   selected next leg. Nothing is written to the game.

The diagram is horizontally scrollable on small screens. Every interactive
choice is a standard button or form control and can be operated with a keyboard.
Printing uses a simplified layout.

## What the numbers mean

This is a static **potential-production** model, not a live market, navigation
planner, investment recommendation, or profit predictor. Rates use market category
units per game hour, not cargo mass. Category units can represent different items.

At the first station, gross output is the sum of recipe outputs, and local use is
the sum of every recipe's inputs at full nominal operation. Net surplus is their
difference, clamped to zero for the departure cargo chooser. This assumes the
station has all required inputs and capacity. Those nominal rates can compete for
materials, and may not be sustainable without imports.

For a destination recipe, the supported cycle rate is:

```text
min(recipe cycles/hour, incoming cargo/hour ÷ cargo units/cycle)
```

Output and co-input rates are that cycle rate multiplied by their batch amounts.
All co-inputs are conditional requirements. The app reports the destination's
gross production of each co-input for context, but does not assume that output is
free or unclaimed. It does not include existing destination output in the new
branch rate. Continuing a branch carries only that branch's calculated output;
unused cargo is reported but not automatically routed elsewhere.

Shipping has no time, mass, loss, or cost in this model. Production still depends
on actual input stock, output storage, recipe update ordering, and delivery in the
game. No direct price information or profit rankings are implied. Destinations
are sorted by distinct producible outputs, then maximum incoming cargo used by
one matching recipe. Region filters are convenient group labels, not calculated
ATC boundaries or flight distances. Market hubs are labels, not shared inventory.

## Data provenance

Snapshot: **2026-11-06**. Covers 19 station cargo-market profiles, 239 recipe
definitions, and 240 recipe attachments. Data was derived from the installed:

```text
Ostranauts_Data/StreamingAssets/data/market/Markets/market_actor_configs.json
Ostranauts_Data/StreamingAssets/data/market/Production/production_maps.json
Ostranauts_Data/StreamingAssets/data/market/CoCollections/cocollections.json
Ostranauts_Data/StreamingAssets/data/star_systems/star_system.json
```

Friendly station labels follow the standard station metadata; OFLT maps to the
Flotilla (OKLG_FLOT). The app uses the actual recipe fields, not names/descriptions:
OKLG's `OKLGIndustrialProductsToHVACConverter` produces Control Systems. Omitted
`nOutputPerRun` is normalized to 1, matching the game's Producer constructor.
The `fOutputPerHour` field is a cycle rate; multi-unit batch outputs are multiplied
by it. Inventory capacities and initial stock ranges are retained in data.js but
are not simulated. Save-specific configurations may differ from this snapshot.

Source SHA-256 hashes are embedded in `ATLAS_DATA.meta.sourceHashes`.
The detailed companion reference is [STATION-PRODUCTION-CHAINS.md](../../STATION-PRODUCTION-CHAINS.md).
To distribute independently, copy this directory; the companion document link is
optional and does not affect the app.

## Files and checks

- `index.html`, `style.css`: interface and layout.
- `data.js`: normalized, bundled game-data snapshot.
- `engine.js`: pure rate, balance, and route calculations.
- `app.js`: browser interface and chain navigation.
- `checks.html`: offline engine regression checks; open directly in a browser.

No build step exists. Editing app assets does not require touching the mod.
