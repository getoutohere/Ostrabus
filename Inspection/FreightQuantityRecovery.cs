using System;
using System.Collections.Generic;
using System.Globalization;
using Ostranauts.Trading;

namespace Ostrabus.Inspection;

internal static partial class FreightRecovery {
    internal sealed class Quantity {
        internal string Category;
        internal int Units, SourceStock, DestinationStock;
    }
    // Bound copies of affected stock lists as well as the transferred payload.
    // Other categories are counted through their list lengths, not flattened.
    const int MaxStagedCategoryItems = 16384;
    static readonly string[] QuantityParameters = { "category", "units", "sourceStock", "destinationStock" };

    internal static bool ParseQuantity(InspectionHttp http, out Quantity quantity, out string error) {
        quantity = null; error = null;
        bool present = false;
        foreach (string key in QuantityParameters) present |= http.QueryString[key] != null;
        string path = http.Url.AbsolutePath.TrimEnd('/');
        var parts = path.Split('/');
        bool route = IsRoute(path) && parts.Length == 6 && parts[4] == "recover-quantity";
        if (!route) {
            if (present) error = "category, units, sourceStock and destinationStock require /economy/freight/{id}/recover-quantity/{stationId}.";
            return error == null;
        }
        string category = http.QueryString["category"];
        try { if (category != null) category = Uri.UnescapeDataString(category); }
        catch (UriFormatException) { error = "Malformed category encoding."; return false; }
        if (!Id(category) ||
            !int.TryParse(http.QueryString["units"], NumberStyles.None, CultureInfo.InvariantCulture, out int units) || units < 1 || units > MaxItems ||
            !int.TryParse(http.QueryString["sourceStock"], NumberStyles.None, CultureInfo.InvariantCulture, out int source) || source < units ||
            !int.TryParse(http.QueryString["destinationStock"], NumberStyles.None, CultureInfo.InvariantCulture, out int destination)) {
            error = "Quantity recovery requires category, units 1..4096, sourceStock >= units and destinationStock >= 0 (integer balances).";
            return false;
        }
        quantity = new Quantity { Category = category, Units = units, SourceStock = source, DestinationStock = destination };
        return true;
    }

    static string QuantityManifest(ShipMarket source, ShipMarket destination, Quantity request,
        out Dictionary<string,List<MarketItem>> manifest, out long originalTotal) {
        manifest = new Dictionary<string,List<MarketItem>>(StringComparer.Ordinal); originalTotal = 0;
        if (request == null || !Id(request.Category) || request.Units < 1 || request.Units > MaxItems ||
            request.SourceStock < request.Units || request.DestinationStock < 0) return "Invalid exact-quantity request.";
        var error = Totals(source, false, out var stock, out _);
        if (error != null) return error;
        foreach (long amount in stock.Values) originalTotal += amount;
        stock.TryGetValue(request.Category, out long sourceCount);
        error = Totals(destination, true, out var destinationStock, out _);
        if (error != null) return error;
        destinationStock.TryGetValue(request.Category, out long destinationCount);
        if (sourceCount != request.SourceStock || destinationCount != request.DestinationStock)
            return "Expected source/destination category balances changed; refresh the ledger before retrying.";
        if (sourceCount > MaxStagedCategoryItems || destinationCount > MaxStagedCategoryItems)
            return "Affected source/destination category exceeds the 16,384-item staging bound.";
        if (Recipes(source).Count != 0 || LoadedActors(source)?.Count > 0)
            return "Source has production or loaded traders; it is not an isolated freight manifest.";
        if (DataHandler.dictDataCOs == null || DataHandler.dictDataCoCollections == null ||
            !DataHandler.dictDataCoCollections.ContainsKey(request.Category)) return "Commodity definitions are unavailable or category is unknown.";

        var selected = new List<MarketItem>(request.Units);
        foreach (var actor in Actors(source).Values) {
            if (!actor.IsCargoPod || ActorRecipes(actor).Count != 0) return "Every source actor must be a cargo pod without production.";
            if (!actor.Stock.TryGetValue(request.Category, out var items) || selected.Count == request.Units) continue;
            if (items.Count > 0 && (!actor.MaxVirtualInventorySize.TryGetValue(request.Category, out int cap) || cap <= 0))
                return "Source category stock is not withdrawable through its native cargo pod.";
            // Native TakeOutStock withdraws from each actor's list tail. Validate
            // exactly those concrete items, not an unrelated prefix of the list.
            for (int i = items.Count - 1; i >= 0 && selected.Count < request.Units; i--) {
                var item = items[i];
                if (item == null || item.COCollectionName != request.Category || string.IsNullOrEmpty(item.COName) ||
                    !DataHandler.dictDataCOs.ContainsKey(item.COName)) return "Selected manifest contains null, mislabeled or unknown items.";
                selected.Add(item);
            }
        }
        if (selected.Count != request.Units) return "Requested category quantity is unavailable; no partial credit attempted.";
        manifest.Add(request.Category, selected);
        return null;
    }
}
