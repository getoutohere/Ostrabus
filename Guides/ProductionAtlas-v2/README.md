# Production Atlas v2 — shipment planner

Open **index.html** directly in Edge, Chrome, Firefox, or another modern browser.
No server, installation, package manager, network connection, or third-party
library is required. Keep the HTML, CSS, and JavaScript files together. All assets
are bundled plain text; there are no external fonts, images, analytics, or CDNs.
Local data is loaded through a normal script tag, so file-URL fetch restrictions
do not apply.

## Explore a chain

Use **Ostranauts dark mode** in the header to switch to the cargo kiosk-inspired
charcoal, black, steel grey, and yellow palette, with green production outputs.
The browser remembers your choice when local storage is available; the toggle
also works without storage. The light palette remains available. Printing uses
the light palette. Both themes are fully bundled and work offline.

1. Select your departure station and one of its produced goods.
2. Choose **Net surplus** (gross output minus configured local consumption) or
   **All output** (gross production, even when local recipes also need it).
3. Enter **Cargo pods** (1,000 kg each) or **Total units** for the initial hop. Pod mode uses the displayed category-average kg per unit, which you can override. Changing the shipment resets the chain.
4. Select a destination from the left. Its output branches are separate ways to
   use the incoming cargo; they are not simultaneous allocations.
5. Select an output branch. Review its batch recipe, total cargo used, leftovers,
   total output, processing time, output pod requirement, and total co-inputs.
6. Choose **Continue with this output** to carry the full output into the next leg.
   Breadcrumbs return to earlier points. Six transformations are supported;
   repeating the same destination/recipe is blocked to avoid circular expansion.
7. Search destinations or products, filter a region, or include consumption-only
   routes. **Save notes** downloads a text summary of the explored chain plus the
   selected next leg. Nothing is written to the game.

The diagram is horizontally scrollable on small screens. Every interactive
choice is a standard button or form control and can be operated with a keyboard.
Printing uses a simplified layout.

## What the numbers mean

This is a static **potential-production** model, not a live market, navigation
planner or profit predictor. Shipment totals use market category units; the
departure supply context uses units per game hour. Categories can contain different items.

At the first station, gross output is the sum of recipe outputs, and local use is
the sum of every recipe's inputs at full nominal operation. Net surplus is their
difference, clamped to zero for the departure cargo chooser. This assumes the
station has all required inputs and capacity. Those nominal rates can compete for
materials, and may not be sustainable without imports.

For a destination recipe, the full run is:

```text
complete cycles = floor(incoming units / selected ingredient units per cycle)
used input      = complete cycles × selected ingredient units per cycle
leftover        = incoming units - used input
output          = complete cycles × output units per cycle
co-input totals = complete cycles × each other ingredient's batch amount
processing time = complete cycles / configured cycles per game hour
```

A slow recipe can eventually process the entire shipment; hourly throughput does
not cap total output. Times are nominal active processing time, excluding travel,
waiting, production tick timing, scheduling delays, competition, and existing stock.
The chain reports cumulative sequential processing time. Co-inputs are assumed
supplied separately, and output storage must be cleared as needed over the run.
Existing output at the receiving port is not added. Leftovers stay at the receiving
port; only the selected output proceeds. Branches are alternatives, not additive.

Initial pod capacity packs whole units separately in each pod:

```text
initial units = pod count × floor(1000 / initial kg per unit)
onward pods  = ceil(output units / floor(1000 / output kg per unit))
```

Counts must be whole numbers from 1 to 1,000,000. Unit mode works without a mass.
Kg per unit must be positive in pod mode. A unit heavier than 1,000 kg cannot fit.
Onward estimates cover output only, excluding leftovers, co-inputs, and pod tare.
All calculated output carries forward even when it needs more pods than the
initial load: the app warns you to use more pods or multiple trips, rather than
silently dropping goods. Required pod specifications appear beside the initial
shipment; onward cargo specifications still need checking in game.

Mass defaults are **offline estimates**, not a live game's cached average. They
follow `DataCoCollection.GetAverageMass`'s mean of nonzero `StatMass` values among
matching base and overlay item definitions, filtered by category conditions and
forbidden conditions. Overlay loot flags were expanded statically (including
possible branches), so randomized overlays and modded definitions can differ.
Masses are rounded to six decimal places. The initial kg/unit can be overridden;
onward estimates continue to use each output category's bundled mass. Exact
production quantities depend on recipe units, not these mass estimates.

Example: 100 Industrial Products sent from VORB to VNCA can become 300 Plastics.
Those can become 1,200 Electronics at MSUZ, conditional on 300 Metals supplied
separately. This is production potential, not guaranteed player-owned inventory
or a profitable trade. The model does not reserve a station's output for you.

Destinations are sorted by distinct outputs, then maximum incoming cargo used by
one recipe. Region filters are group labels, not ATC boundaries or flight
measurements. Market hubs are labels, not shared inventory.
## Data provenance

Snapshot: **2026-11-06**. Covers 19 station cargo-market profiles, 239 recipe
definitions, and 240 recipe attachments. Data was derived from the installed:

```text
Ostranauts_Data/StreamingAssets/data/market/Markets/market_actor_configs.json
Ostranauts_Data/StreamingAssets/data/market/Production/production_maps.json
Ostranauts_Data/StreamingAssets/data/market/CoCollections/cocollections.json
Ostranauts_Data/StreamingAssets/data/star_systems/star_system.json
Ostranauts_Data/StreamingAssets/data/condowners/*.json
Ostranauts_Data/StreamingAssets/data/cooverlays/*.json
Ostranauts_Data/StreamingAssets/data/loot/*.json
```

Friendly station labels follow the standard station metadata; OFLT maps to the
Flotilla (OKLG_FLOT). The app uses the actual recipe fields, not names/descriptions:
The mislabeled `OKLGIndustrialProductsToHVACConverter` is intentionally excluded
from v2: 238 definitions and 239 attachments are modeled. This exclusion applies
to routes, departure supplies, and all production balances. The remaining Control
Systems recipe keeps its own configured rate. Omitted
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
- `theme.css`, `theme.js`: optional Ostranauts dark palette and saved theme toggle.
- `data.js`: normalized, bundled game-data snapshot.
- `engine.js`: finite batch, cargo capacity, balance, and route calculations.
- `app.js`: browser interface and chain navigation.
- `checks.html`: offline engine regression checks; open directly in a browser.

No build step exists. Editing app assets does not require touching the mod.
The original app remains separately available in `../ProductionAtlas/`.
Validation: 21 offline engine checks and 41 browser interaction checks; tested at
1440 px desktop and 390 px mobile width with no page overflow.
