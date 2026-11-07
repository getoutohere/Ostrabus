using System;
using System.Collections.Generic;
using System.Diagnostics;
using HarmonyLib;
using Ostranauts.Core;
using Ostranauts.Ships.AIPilots;
using Ostranauts.ShipGUIs.Utilities;
using Ostranauts.Trading;

namespace Ostrabus.Inspection;

// Main-thread, request-scoped reads only. In particular, do not call the singleton
// accessor, Producer.IsReady, GetTradeRoute, lazy price/mass getters or UpdateTime.
internal static class EconomyInspection {
    static readonly System.Reflection.FieldInfo Singleton = AccessTools.Field(typeof(MonoSingleton<MarketManager>), "_instance");
    static readonly System.Reflection.FieldInfo TradeClock = AccessTools.Field(typeof(AIShipManager), "fTimeOfNextTradeCheck");
    static readonly AccessTools.FieldRef<MarketManager, Dictionary<string, ShipMarket>> Markets = AccessTools.FieldRefAccess<MarketManager, Dictionary<string, ShipMarket>>("_market");
    static readonly AccessTools.FieldRef<MarketManager, Dictionary<string, List<string>>> Hubs = AccessTools.FieldRefAccess<MarketManager, Dictionary<string, List<string>>>("_regionalMarketMap");
    static readonly AccessTools.FieldRef<MarketManager, bool> Initialized = AccessTools.FieldRefAccess<MarketManager, bool>("_init");
    static readonly AccessTools.FieldRef<MarketManager, double> LastUpdate = AccessTools.FieldRefAccess<MarketManager, double>("_lastUpdateTime");
    static readonly AccessTools.FieldRef<ShipMarket, Dictionary<string, int>> Stock = AccessTools.FieldRefAccess<ShipMarket, Dictionary<string, int>>("_stock");
    static readonly AccessTools.FieldRef<ShipMarket, Dictionary<string, int>> Capacity = AccessTools.FieldRefAccess<ShipMarket, Dictionary<string, int>>("_maxVirtualInventory");
    static readonly AccessTools.FieldRef<ShipMarket, Dictionary<string, MarketActorConfig>> Actors = AccessTools.FieldRefAccess<ShipMarket, Dictionary<string, MarketActorConfig>>("_marketMapping");
    static readonly AccessTools.FieldRef<ShipMarket, List<Producer>> Recipes = AccessTools.FieldRefAccess<ShipMarket, List<Producer>>("_productionMaps");
    static readonly AccessTools.FieldRef<Producer, Dictionary<DataCoCollection, int>> Inputs = AccessTools.FieldRefAccess<Producer, Dictionary<DataCoCollection, int>>("_dictInputCollections");
    static readonly AccessTools.FieldRef<Producer, float> Rate = AccessTools.FieldRefAccess<Producer, float>("_outputPerHour");
    static readonly AccessTools.FieldRef<Producer, int> Batch = AccessTools.FieldRefAccess<Producer, int>("_outputPerRun");
    static readonly AccessTools.FieldRef<MarketActorConfig, List<Producer>> ActorRecipes = AccessTools.FieldRefAccess<MarketActorConfig, List<Producer>>("_productionMaps");
    static readonly AccessTools.FieldRef<MarketActorConfig, string> ActorName = AccessTools.FieldRefAccess<MarketActorConfig, string>("_name");
    static readonly AccessTools.FieldRef<AIShip, Ship> RawShip = AccessTools.FieldRefAccess<AIShip, Ship>("_shipUs");
    static readonly AccessTools.FieldRef<NavData, List<NavDataPoint>> Points = AccessTools.FieldRefAccess<NavData, List<NavDataPoint>>("_navPoints");
    static readonly AccessTools.FieldRef<DataCoCollection, double> UnitPrice = AccessTools.FieldRefAccess<DataCoCollection, double>("_avgPrice");
    static readonly AccessTools.FieldRef<DataCoCollection, double> UnitMass = AccessTools.FieldRefAccess<DataCoCollection, double>("_avgMass");
    static readonly AccessTools.FieldRef<Ship, CondOwner> RawShipCo = AccessTools.FieldRefAccess<Ship, CondOwner>("<ShipCO>k__BackingField");
    static Dictionary<string, object> Map(params object[] pairs) => SnapshotJson.Map(pairs);
    static bool Finite(double n) => !double.IsNaN(n) && !double.IsInfinity(n);
    static bool Expired(long began) => Stopwatch.GetTimestamp() - began >= Stopwatch.Frequency / 1000;
    static int Amount(Dictionary<string, int> dictionary, string category) => dictionary != null && category != null && dictionary.TryGetValue(category, out int count) ? count : 0;
    static Ship Hull(string id) => CrewSim.system?.dictShips != null && CrewSim.system.dictShips.TryGetValue(id, out var ship) ? ship : null;
    // Body flag is deliberately reported separately from native IsStation's docking-port gate.
    static bool Station(Ship ship) => ship?.objSS != null && ship.objSS.bIsBO;
    static InspectionApi.Reply Error(int status, string message) => new InspectionApi.Reply(status, Map("error", message));

    internal static InspectionApi.Reply Query(string path, int offset, int limit, long began) {
        var manager = Singleton.GetValue(null) as MarketManager;
        if (ReferenceEquals(manager, null) || Markets(manager) == null) return Error(503, "Economy is not initialized; no singleton was created.");
        var markets = Markets(manager);
        if (path == "/economy") {
            double next = (double)TradeClock.GetValue(null);
            return new InspectionApi.Reply(200, Map("schemaVersion", 1, "initialized", Initialized(manager), "markets", markets.Count,
                "lastProductionUpdateEpoch", LastUpdate(manager), "productionUpdateIntervalSeconds", 10,
                "nextDispatchCheckEpoch", next, "secondsUntilDispatchCheck", next - StarSystem.fEpoch,
                "dispatchOverdue", StarSystem.fEpoch > next, "dispatchIntervalSeconds", 7200,
                "atcRegion", AIShipManager.strATCLast, "testCase", AIShipManager.IsTestCase, "paused", CrewSim.Paused,
                "orbitalEnabled", global::Ostrabus.Orbital.OrbitalSimulation.Enabled,
                "sffTravelEnabled", global::Ostrabus.Orbital.SffSimulation.Enabled,
                "sffEconomyEnabled", global::Ostrabus.Orbital.Economy.SffEconomy.Enabled,
                "lastSffEconomy", global::Ostrabus.Orbital.Economy.SffEconomy.Snapshot(),
                "routes", new[] { "/economy/stations", "/economy/markets", "/economy/freight", "/economy/hubs",
                    "/economy/stations/{id}/stock", "/economy/stations/{id}/production", "/economy/stations/{id}/actors",
                    "/economy/stations/{id}/freight", "/economy/stations/{id}/trade/{category}", "/economy/freight/{id}/manifest" },
                "note", "Read-only raw market state. Commodity prices are cached native values. No production, route selection, random roll, prediction, repair or synchronization is executed."));
        }
        string[] parts = path.Split('/');
        if (parts.Length < 3 || parts[1] != "economy") return Error(404, "Unknown economy endpoint.");
        string section = parts[2];
        if (parts.Length == 3) {
            if (section == "hubs") return Page(HubRows(manager), offset, limit, began);
            if (section == "markets" || section == "stations" || section == "freight") {
                bool indexIncomplete = false;
                var pilots = section == "freight" ? PilotIndex(began, out indexIncomplete) : null;
                return Page(MarketRows(markets, section, pilots, indexIncomplete), offset, limit, began);
            }
            return Error(404, "Unknown economy collection.");
        }
        if (section != "stations" && section != "markets" && section != "freight") return Error(404, "Unknown economy collection.");
        string id = Uri.UnescapeDataString(parts[3]);
        if (!markets.TryGetValue(id, out var market) || market == null) return Error(404, "Registered market not found.");
        if (section == "stations" && !Station(Hull(id))) return Error(404, "Market does not belong to an analytical station body.");
        if (parts.Length == 4) {
            var data = Summary(id, market);
            data["links"] = Map("stock", "/economy/markets/" + Uri.EscapeDataString(id) + "/stock",
                "production", "/economy/markets/" + Uri.EscapeDataString(id) + "/production",
                "actors", "/economy/markets/" + Uri.EscapeDataString(id) + "/actors",
                "freight", "/economy/markets/" + Uri.EscapeDataString(id) + "/freight",
                "manifest", "/economy/markets/" + Uri.EscapeDataString(id) + "/manifest");
            if (section == "freight") data["flight"] = Flight(Hull(id), PilotIndex(began, out bool incomplete), incomplete);
            return new InspectionApi.Reply(200, data);
        }
        if (parts.Length == 5) {
            switch (parts[4]) {
                case "stock": return Page(StockRows(market), offset, limit, began);
                case "production": return Page(ProductionRows(market), offset, limit, began);
                case "actors": return Page(ActorRows(market), offset, limit, began);
                case "manifest": return Manifest(market, offset, limit, began);
                case "freight": {
                    var pilots = PilotIndex(began, out bool incomplete);
                    var reply = Page(MarketRows(markets, "freight", pilots, incomplete, id), offset, limit, began);
                    ((Dictionary<string, object>)reply.Body)["aiIndexIncomplete"] = incomplete;
                    return reply;
                }
            }
        }
        if (parts.Length == 6 && parts[4] == "trade")
            return Page(TradeRows(manager, id, market, Uri.UnescapeDataString(parts[5])), offset, limit, began);
        return Error(404, "Unknown economy endpoint.");
    }

    // A row is the smallest cooperative unit. Allow one row for forward progress,
    // then stop at the existing 1 ms inspection budget. Enumeration is never retained.
    static InspectionApi.Reply Page(IEnumerable<object> source, int offset, int limit, long began) {
        var rows = new List<object>();
        int index = 0; bool more = false, budget = false;
        using (var cursor = source.GetEnumerator()) {
            while (cursor.MoveNext()) {
                if (index < offset) { index++; if (Expired(began)) { more = budget = true; break; } continue; }
                if (cursor.Current == null) {
                    index++;
                    if (Expired(began)) { more = budget = true; break; }
                    continue;
                }
                if (rows.Count >= limit || (rows.Count > 0 && Expired(began))) { more = true; budget = Expired(began); break; }
                rows.Add(cursor.Current); index++;
            }
        }
        if (index < offset || (more && rows.Count == 0 && index == offset)) return Error(503, "Offset scan exceeded the inspection budget; use a direct market ID or a smaller offset.");
        return new InspectionApi.Reply(200, Map("items", rows, "scannedThrough", index, "nextOffset", more ? (object)index : null,
            "budgetLimited", budget, "pagination", "Live dictionary/list order. Mutations between requests can repeat or skip rows; pages are not an atomic census."));
    }
    static Dictionary<string, object> Summary(string id, ShipMarket market) {
        var ship = Hull(id);
        var co = ship == null ? null : RawShipCo(ship);
        bool? blockadePresent = co?.mapConds == null ? (bool?)null : co.mapConds.ContainsKey("DiscountBlockade");
        return Map("id", id, "storedMarketShipId", market.ShipRegId, "name", ship?.publicName,
            "shipResolved", ship != null, "stationBody", Station(ship), "destroyed", ship?.bDestroyed,
            "hidden", ship?.HideFromSystem, "aiFlag", ship?.IsAIShip, "loadState", ship?.LoadState.ToString(),
            "actorCount", Actors(market)?.Count, "recipeCount", Recipes(market)?.Count,
            "stockCategories", Stock(market)?.Count, "capacityCategories", Capacity(market)?.Count,
            "priceCategories", market.PriceModifiers?.Count,
            "blockadeConditionPresent", blockadePresent,
            "note", "Counts and quantities use cached aggregate market dictionaries. /actors and /manifest expose backing inventories for consistency checks.");
    }
    static IEnumerable<object> MarketRows(Dictionary<string, ShipMarket> markets, string section, Dictionary<Ship, AIShip> pilots, bool indexIncomplete, string stationId = null) {
        foreach (var entry in markets) {
            var market = entry.Value;
            if (market == null) { yield return section == "markets" ? Map("id", entry.Key, "invalidMarket", true) : null; continue; }
            var ship = Hull(entry.Key);
            if (section == "stations" && !Station(ship)) { yield return null; continue; }
            bool pod = false, podsIncomplete = false;
            if (section == "freight") {
                pod = HasPod(market, out podsIncomplete);
                if (!pod && !podsIncomplete) { yield return null; continue; }
            }
            bool incoming = stationId != null && ship?.shipScanTarget?.strRegID == stationId;
            bool home = stationId != null && ship != null && pilots != null && pilots.TryGetValue(ship, out var ai) && ai.HomeStation == stationId;
            bool origin = stationId != null && ship?.origin == stationId;
            if (stationId != null && !incoming && !home && !origin) { yield return null; continue; }
            var row = Summary(entry.Key, market);
            if (section == "freight") {
                row["hasCargoPod"] = pod ? (object)true : null;
                row["podScanIncomplete"] = podsIncomplete;
                row["flight"] = Flight(ship, pilots, indexIncomplete);
                if (stationId != null) {
                    row["incoming"] = incoming; row["homeMatchesStation"] = home; row["originMatchesStation"] = origin;
                }
            }
            yield return row;
        }
    }
    static bool HasPod(ShipMarket market, out bool incomplete) {
        incomplete = false;
        if (Actors(market) == null) return false;
        int examined = 0;
        foreach (var actor in Actors(market).Values) {
            if (actor?.CargoSpecs != null) return true;
            if (++examined == 512) { incomplete = Actors(market).Count > examined; break; }
        }
        return false;
    }
    static IEnumerable<object> HubRows(MarketManager manager) {
        if (Hubs(manager) == null) yield break;
        foreach (var hub in Hubs(manager)) {
            var members = new List<string>();
            if (hub.Value != null) for (int i = 0; i < hub.Value.Count && i < 64; i++) members.Add(hub.Value[i]);
            yield return Map("hub", hub.Key, "members", members, "totalMembers", hub.Value?.Count,
                "membersTruncated", hub.Value?.Count > members.Count,
                "note", "Stored hub mapping used by native AdjustForLocalMarkets, not a geographic proximity calculation.");
        }
    }
    static IEnumerable<string> Categories(ShipMarket market) {
        var stock = Stock(market); var capacity = Capacity(market); var prices = market.PriceModifiers;
        if (capacity != null) foreach (var key in capacity.Keys) yield return key;
        if (stock != null) foreach (var key in stock.Keys) if (capacity == null || !capacity.ContainsKey(key)) yield return key;
        if (prices != null) foreach (var key in prices.Keys)
            if ((capacity == null || !capacity.ContainsKey(key)) && (stock == null || !stock.ContainsKey(key))) yield return key;
    }
    static IEnumerable<object> StockRows(ShipMarket market) { foreach (string category in Categories(market)) yield return Commodity(market, category); }
    static Dictionary<string, object> Commodity(ShipMarket market, string category) {
        int stock = Amount(Stock(market), category), capacity = Amount(Capacity(market), category);
        double fraction = capacity != 0 ? (double)((float)stock / capacity) : 0;
        bool stored = market.PriceModifiers != null && market.PriceModifiers.TryGetValue(category, out _);
        float price = stored ? market.PriceModifiers[category] : 1;
        DataCoCollection collection = null;
        DataHandler.dictDataCoCollections?.TryGetValue(category, out collection);
        double? unitPrice = collection == null || UnitPrice(collection) < 0 ? (double?)null : UnitPrice(collection);
        double? unitMass = collection == null || UnitMass(collection) < 0 ? (double?)null : UnitMass(collection);
        bool output = false, input = false, truncated = false;
        int scanned = 0;
        var recipes = Recipes(market);
        if (recipes != null) foreach (var recipe in recipes) {
            if (++scanned > 128) { truncated = true; break; }
            if (recipe == null) continue;
            if (recipe.OutputCollection?.Name == category) output = true;
            int inputs = 0;
            if (Inputs(recipe) != null) foreach (var item in Inputs(recipe)) {
                if (++inputs > 32) { truncated = true; break; }
                if (item.Key?.Name == category) input = true;
            }
        }
        var supplyBlocks = new List<string>(); var demandBlocks = new List<string>();
        if (!output) supplyBlocks.Add("notAProductionOutput");
        if (!input) demandBlocks.Add("notAProductionInput");
        if (!(fraction > .2)) supplyBlocks.Add("stockFailsNativeSupplyGate");
        if (!((float)fraction < .3f)) demandBlocks.Add("stockFailsNativeDemandGate");
        if (!(price < 1)) supplyBlocks.Add("priceNotBelowOne");
        if (!(price > 1)) demandBlocks.Add("priceNotAboveOne");
        if (truncated) { supplyBlocks.Add("rolesIncomplete"); demandBlocks.Add("rolesIncomplete"); }
        return Map("category", category, "stock", stock, "capacity", capacity, "space", capacity - stock, "fillFraction", fraction,
            "priceMultiplier", price, "priceCached", stored, "productionOutput", output, "productionInput", input,
            "cachedUnitPrice", unitPrice, "cachedUnitMassKg", unitMass,
            "rolesIncomplete", truncated, "nativeSupplyEligible", truncated ? null : (object)(output && fraction > .2 && price < 1),
            "nativeDemandEligible", truncated ? null : (object)(input && (float)fraction < .3f && price > 1),
            "supplyBlockedBy", supplyBlocks, "demandBlockedBy", demandBlocks,
            "invalidQuantity", stock < 0 || capacity < 0 || stock > capacity);
    }
    static IEnumerable<object> ProductionRows(ShipMarket market) {
        if (Recipes(market) == null) yield break;
        foreach (var producer in Recipes(market)) {
            if (producer == null) { yield return Map("invalidRecipe", true); continue; }
            var inputs = new List<object>(); bool missing = false, truncated = false;
            if (Inputs(producer) != null) foreach (var item in Inputs(producer)) {
                if (inputs.Count == 32) { truncated = true; break; }
                string category = item.Key?.Name;
                int stock = Amount(Stock(market), category);
                bool lacks = stock < item.Value; missing |= lacks;
                inputs.Add(Map("category", category, "perCycle", item.Value, "stock", stock, "shortfall", Math.Max(0, item.Value - stock)));
            }
            string output = producer.OutputCollection?.Name;
            int batch = Batch(producer), space = Amount(Capacity(market), output) - Amount(Stock(market), output);
            bool full = output != null && space < batch;
            double interval = Math.Abs(3600f / Rate(producer));
            bool enabled = Finite(interval) && interval > 0;
            double? due = producer.LastUpdateTime == 0 || !enabled ? (double?)null : producer.LastUpdateTime + interval;
            double? cyclesDue = due == null || !Finite(producer.LastUpdateTime) ? (double?)null :
                Math.Max(0, Math.Floor((float)(StarSystem.fEpoch - producer.LastUpdateTime) / (float)interval));
            string status = truncated ? "unknown" : !enabled ? "disabledRate" : missing ? "missingInputs" : full ? "outputFull" :
                producer.LastUpdateTime == 0 ? "uninitializedClock" : StarSystem.fEpoch >= due ? "ready" : "waiting";
            yield return Map("recipe", producer.Name, "inputs", inputs, "inputsTruncated", truncated,
                "outputCategory", output, "outputPerCycle", batch, "outputSpace", output == null ? null : (object)space,
                "configuredRatePerHour", Rate(producer), "cycleIntervalSeconds", interval,
                "rateEnabled", enabled, "clockInitialized", producer.LastUpdateTime != 0, "cyclesDue", cyclesDue,
                "lastUpdateEpoch", producer.LastUpdateTime, "nextCycleEpoch", due, "secondsUntilCycle", due - StarSystem.fEpoch,
                "status", status, "missingInputs", missing, "outputFull", full, "canRunWithCurrentStock", !truncated && !missing && !full,
                "note", "Readiness is inferred without calling IsReady. An uninitialized native clock is left unchanged. Failed production attempts advance their time slot.");
        }
    }
    static IEnumerable<object> ActorRows(ShipMarket market) {
        if (Actors(market) == null) yield break;
        foreach (var entry in Actors(market)) {
            var actor = entry.Value;
            if (actor == null) { yield return Map("actorId", entry.Key, "invalidActor", true); continue; }
            var stock = new List<object>();
            if (actor.Stock != null) foreach (var pair in actor.Stock) {
                if (stock.Count == 64) break;
                stock.Add(Map("category", pair.Key, "count", pair.Value?.Count));
            }
            yield return Map("actorId", entry.Key, "ownerId", actor.COwnerId, "configuration", ActorName(actor), "hub", actor.HubRegId,
                "cargoPod", actor.CargoSpecs != null, "recipes", ActorRecipes(actor)?.Count, "stock", stock,
                "stockTruncated", actor.Stock?.Count > stock.Count);
        }
    }
    static InspectionApi.Reply Manifest(ShipMarket market, int offset, int limit, long began) {
        var rows = new List<object>(); int index = 0, examined = 0; bool more = false;
        if (Actors(market) != null) foreach (var actor in Actors(market)) {
            if (++examined > 1 && Expired(began)) {
                if (rows.Count == 0) return Error(503, "Manifest scan exceeded the inspection budget; inspect /actors or retry after warmup.");
                more = true; goto Done;
            }
            if (actor.Value?.Stock == null) continue;
            foreach (var stock in actor.Value.Stock) {
                if (++examined > 2 && Expired(began)) {
                    if (rows.Count == 0) return Error(503, "Manifest category scan exceeded the inspection budget; inspect /actors or retry after warmup.");
                    more = true; goto Done;
                }
                var items = stock.Value;
                if (items == null) continue;
                if (index + items.Count <= offset) { index += items.Count; continue; }
                for (int i = Math.Max(0, offset - index); i < items.Count; i++) {
                    if (rows.Count >= limit || (rows.Count > 0 && Expired(began))) { more = true; goto Done; }
                    var item = items[i];
                    rows.Add(Map("actorId", actor.Key, "slot", i, "stockCategory", stock.Key, "category", item?.COCollectionName, "itemType", item?.COName));
                }
                index += items.Count;
            }
        }
        Done:
        return new InspectionApi.Reply(200, Map("items", rows, "nextOffset", more ? (object)(offset + rows.Count) : null,
            "budgetLimited", more && Expired(began), "note", "Actual virtual MarketItem entries, not spawned physical COs. Slot offsets are live and may change between requests."));
    }
    static Dictionary<Ship, AIShip> PilotIndex(long began, out bool incomplete) {
        incomplete = false; var index = new Dictionary<Ship, AIShip>(); int examined = 0;
        if (AIShipManager.dictAIs == null) return index;
        foreach (var region in AIShipManager.dictAIs.Values) {
            if (region == null) continue;
            foreach (var ai in region) {
                if (++examined > 4096 || (examined > 1 && Expired(began))) { incomplete = true; return index; }
                if (ai == null) continue;
                var ship = RawShip(ai);
                if (ship != null) index[ship] = ai;
            }
        }
        return index;
    }
    static object Flight(Ship ship, Dictionary<Ship, AIShip> pilots, bool indexIncomplete) {
        if (ship == null) return null;
        bool found = pilots != null && pilots.TryGetValue(ship, out _);
        var pilot = found ? pilots[ship] : null;
        var course = ship.objSS?.NavData;
        var points = course == null ? null : Points(course);
        double? arrival = points?.Count > 0 ? points[points.Count - 1]?.ArrivalTime : null;
        bool cargo = pilot?.AIType == AIType.HaulerCargo;
        var target = ship.shipScanTarget?.objSS;
        double? distance = null;
        if (ship.objSS != null && target != null) {
            double dx = ship.objSS.vPosx - target.vPosx, dy = ship.objSS.vPosy - target.vPosy;
            distance = Math.Sqrt(dx * dx + dy * dy) * 149597870.7;
        }
        return Map("registered", found ? (object)true : indexIncomplete ? null : (object)false,
            "pilotType", pilot?.AICharacter?.GetType().Name, "home", pilot?.HomeStation,
            "storedHullOrigin", ship.origin,
            "activeCommand", pilot?.ActiveCommandName, "cargoPilot", pilot == null ? null : (object)cargo,
            "destination", ship.shipScanTarget?.strRegID, "targetRegId", ship.targetRegID,
            "storedDistanceToDestinationKm", distance, "aiDetails", "/ships/" + Uri.EscapeDataString(ship.strRegID) + "/ai",
            "targetReferenceMatches", ship.shipScanTarget == null ? null : (object)ReferenceEquals(ship.shipSituTarget, ship.shipScanTarget.objSS),
            "planPoints", points?.Count ?? 0, "planArrivalEpoch", arrival, "secondsUntilPlanEnd", arrival - StarSystem.fEpoch,
            "planEndIsDeliveryEta", false, "torchCapable", ship.fFusionThrustMax > 0,
            "rcsFuelKg", ship.fShallowRCSRemass, "rcsCapacityKg", ship.fShallowRCSRemassMax,
            "fuelCacheCurrent", ship.LoadState <= Ship.Loaded.Shallow,
            "note", "A cargo pod alone does not imply NPC freight: player cargo and parked hulls can have markets too. Plan end can precede final docking; it is not a promised delivery time.");
    }
    static IEnumerable<object> TradeRows(MarketManager manager, string id, ShipMarket market, string category) {
        var local = Commodity(market, category);
        foreach (var entry in Markets(manager)) {
            if (entry.Key == id || entry.Value == null) continue;
            var other = Commodity(entry.Value, category);
            bool import = local["nativeDemandEligible"] is bool ld && ld && other["nativeSupplyEligible"] is bool rs && rs;
            bool export = local["nativeSupplyEligible"] is bool ls && ls && other["nativeDemandEligible"] is bool rd && rd;
            yield return Map("counterpart", entry.Key, "category", category, "local", local, "remote", other,
                "importPairEligible", import, "importAmountBeforeHubRouting", import ? Math.Min((int)local["space"], (int)other["stock"]) : 0,
                "exportPairEligible", export, "exportAmountBeforeHubRouting", export ? Math.Min((int)local["stock"], (int)other["space"]) : 0,
                "eligibilityIncomplete", (bool)local["rolesIncomplete"] || (bool)other["rolesIncomplete"],
                "note", "Raw supply/demand pairing before hub redirection, blockade rolls, profit selection, pod/model selection and flight feasibility. Eligibility does not guarantee dispatch; use /economy/hubs for the stored hub map.");
        }
    }
}
