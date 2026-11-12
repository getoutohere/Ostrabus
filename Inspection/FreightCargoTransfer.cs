using System;
using System.Collections.Generic;
using HarmonyLib;
using Ostranauts.Core;
using Ostranauts.Ships.AIPilots;
using Ostranauts.Trading;

namespace Ostrabus.Inspection;

internal static partial class FreightRecovery {
    static readonly System.Reflection.FieldInfo MarketSingleton = AccessTools.Field(typeof(MonoSingleton<MarketManager>),"_instance");
    static readonly System.Reflection.MethodInfo ShallowClone = AccessTools.Method(typeof(object),"MemberwiseClone");
    static readonly AccessTools.FieldRef<MarketManager,Dictionary<string,ShipMarket>> MarketMap = AccessTools.FieldRefAccess<MarketManager,Dictionary<string,ShipMarket>>("_market");
    static readonly AccessTools.FieldRef<MarketManager,Dictionary<string,List<string>>> Hubs = AccessTools.FieldRefAccess<MarketManager,Dictionary<string,List<string>>>("_regionalMarketMap");
    static readonly AccessTools.FieldRef<ShipMarket,Dictionary<string,MarketActorConfig>> Actors = AccessTools.FieldRefAccess<ShipMarket,Dictionary<string,MarketActorConfig>>("_marketMapping");
    static readonly AccessTools.FieldRef<ShipMarket,Dictionary<string,int>> StockCache = AccessTools.FieldRefAccess<ShipMarket,Dictionary<string,int>>("_stock");
    static readonly AccessTools.FieldRef<ShipMarket,Dictionary<string,int>> CapacityCache = AccessTools.FieldRefAccess<ShipMarket,Dictionary<string,int>>("_maxVirtualInventory");
    static readonly AccessTools.FieldRef<ShipMarket,List<Producer>> Recipes = AccessTools.FieldRefAccess<ShipMarket,List<Producer>>("_productionMaps");
    static readonly AccessTools.FieldRef<MarketActorConfig,List<Producer>> ActorRecipes = AccessTools.FieldRefAccess<MarketActorConfig,List<Producer>>("_productionMaps");
    static readonly AccessTools.FieldRef<ShipMarket,List<MarketActor>> LoadedActors = AccessTools.FieldRefAccess<ShipMarket,List<MarketActor>>("_loadedMarketActors");
    static readonly AccessTools.FieldRef<ShipMarket,double> CachedMass = AccessTools.FieldRefAccess<ShipMarket,double>("_cachedTotalMass");
    const int MaxActors=1024, MaxItems=4096, MaxEntries=16384;

    static string Totals(ShipMarket market, bool destination, out Dictionary<string,long> stock, out Dictionary<string,long> capacity) {
        stock=new Dictionary<string,long>(StringComparer.Ordinal);capacity=new Dictionary<string,long>(StringComparer.Ordinal);
        var actors=Actors(market);
        if (actors == null || actors.Count>MaxActors || StockCache(market) == null || CapacityCache(market) == null || Recipes(market) == null)
            return "Market dictionaries are unavailable or exceed request bounds.";
        var lists=new HashSet<List<MarketItem>>();
        int work=0;
        foreach (var actor in actors.Values) {
            if (actor?.Stock == null || actor.MaxVirtualInventorySize == null || ActorRecipes(actor) == null) return "Incomplete market actor metadata.";
            foreach (var pair in actor.MaxVirtualInventorySize) {
                if (++work>MaxEntries || pair.Value<0) return "Invalid or excessive inventory capacity metadata.";
                capacity.TryGetValue(pair.Key,out long current);capacity[pair.Key]=current+pair.Value;
            }
            foreach (var pair in actor.Stock) {
                if (++work>MaxEntries || pair.Value == null || !lists.Add(pair.Value)) return "Invalid, shared or excessive backing stock data.";
                if (destination && (!actor.MaxVirtualInventorySize.TryGetValue(pair.Key,out int cap) ? pair.Value.Count>0 : pair.Value.Count>cap))
                    return "Destination actor stock already exceeds its configured capacity.";
                stock.TryGetValue(pair.Key,out long current);stock[pair.Key]=current+pair.Value.Count;
            }
        }
        bool Matches(Dictionary<string,int> cache, Dictionary<string,long> counts) {
            if (cache.Count>MaxEntries) return false;
            foreach (var pair in cache) if (pair.Value<0 || (counts.TryGetValue(pair.Key,out long count) ? count : 0)!=pair.Value) return false;
            foreach (var pair in counts) if (pair.Value!=(cache.TryGetValue(pair.Key,out int count) ? count : 0)) return false;
            return true;
        }
        return Matches(StockCache(market),stock) && Matches(CapacityCache(market),capacity) ? null : "Market aggregates disagree with backing stock/capacity; no transfer attempted.";
    }
    static string Manifest(ShipMarket source, out Dictionary<string,List<MarketItem>> manifest, out int total) {
        manifest=new Dictionary<string,List<MarketItem>>(StringComparer.Ordinal);total=0;
        var error=Totals(source,false,out _,out _);
        if (error != null) return error;
        if (Recipes(source).Count!=0 || LoadedActors(source)?.Count>0) return "Source has production or loaded traders; it is not an isolated freight manifest.";
        if (DataHandler.dictDataCOs == null || DataHandler.dictDataCoCollections == null) return "Commodity definitions are unavailable.";
        bool pod=false;
        foreach (var actor in Actors(source).Values) {
            if (!actor.IsCargoPod || ActorRecipes(actor).Count!=0) return "Every source actor must be a cargo pod without production.";
            pod=true;
            foreach (var pair in actor.Stock) foreach (var item in pair.Value) {
                if (++total>MaxItems) return "Manifest exceeds 4,096 units; no partial credit or deletion attempted.";
                if (item == null || item.COCollectionName!=pair.Key || string.IsNullOrEmpty(item.COName) ||
                    !DataHandler.dictDataCoCollections.ContainsKey(pair.Key) || !DataHandler.dictDataCOs.ContainsKey(item.COName))
                    return "Manifest contains null, mislabeled or unknown items.";
                if (!manifest.TryGetValue(pair.Key,out var items)) manifest.Add(pair.Key,items=new List<MarketItem>());
                items.Add(item);
            }
        }
        return pod ? null : "Source has no cargo pod configuration.";
    }
    static string ValidateDestination(Dictionary<string,ShipMarket> markets, string sourceId, ShipMarket destination,
        Dictionary<string,List<MarketItem>> manifest, out Dictionary<string,long> before) {
        var error=Totals(destination,true,out before,out var capacities);
        if (error != null) return error;
        foreach (var pair in manifest) {
            before.TryGetValue(pair.Key,out long have);capacities.TryGetValue(pair.Key,out long cap);
            if (cap-have<pair.Value.Count) return "Destination cannot accept the complete "+pair.Key+" manifest (needs "+pair.Value.Count+", free "+(cap-have)+").";
        }
        var source=markets[sourceId];
        var ownedActors=new HashSet<MarketActorConfig>(Actors(source).Values);
        var sourceLists=new HashSet<List<MarketItem>>();
        foreach (var actor in ownedActors) foreach (var list in actor.Stock.Values) sourceLists.Add(list);
        var destinationLists=new HashSet<List<MarketItem>>();
        foreach (var actor in Actors(destination).Values) foreach (var pair in actor.Stock) if (manifest.ContainsKey(pair.Key)) destinationLists.Add(pair.Value);
        if (markets.Count>4096) return "Market alias scan exceeds request bounds.";
        int work=0;
        foreach (var registered in markets) {
            var market=registered.Value;
            if (ReferenceEquals(market,source) && registered.Key!=sourceId) return "Source market object is registered under multiple IDs.";
            if (market == null || ReferenceEquals(market,source)) continue;
            var actors=Actors(market);
            if (actors == null) return "Market alias checks are incomplete.";
            foreach (var actor in actors.Values) {
                if (++work>MaxEntries || actor?.Stock == null) return "Market alias scan is invalid or exceeds request bounds.";
                if (ownedActors.Contains(actor)) return "Source pod configuration is shared with another market.";
                foreach (var list in actor.Stock.Values) {
                    if (++work>MaxEntries) return "Market alias scan exceeds request bounds.";
                    if (sourceLists.Contains(list) || !ReferenceEquals(market,destination) && destinationLists.Contains(list))
                        return "Affected stock lists are shared with another market.";
                }
            }
        }
        return null;
    }
    // Stage with native per-actor capacity/clone semantics. The public market
    // batch-adder can reuse a full input list after partial acceptance; native
    // UnregisterCargoShip deletes the source before capacity/errors are known.
    // Neither is suitable as an unchecked recovery transaction.
    static ShipMarket Stage(ShipMarket destination, Dictionary<string,List<MarketItem>> manifest) {
        var staged=(ShipMarket)ShallowClone.Invoke(destination,null);
        var actors=new Dictionary<string,MarketActorConfig>();
        foreach (var pair in Actors(destination)) {
            var actor=(MarketActorConfig)ShallowClone.Invoke(pair.Value,null);
            actor.Stock=new Dictionary<string,List<MarketItem>>(pair.Value.Stock);
            foreach (var category in manifest.Keys) if (actor.Stock.TryGetValue(category,out var list)) actor.Stock[category]=new List<MarketItem>(list);
            actors.Add(pair.Key,actor);
        }
        Actors(staged)=actors;StockCache(staged)=new Dictionary<string,int>(StockCache(destination));
        CapacityCache(staged)=new Dictionary<string,int>(CapacityCache(destination));Recipes(staged)=new List<Producer>(Recipes(destination));
        AccessTools.Field(typeof(ShipMarket),"_priceModifiers").SetValue(staged,new Dictionary<string,float>(destination.PriceModifiers));
        foreach (var pair in manifest) {
            var pending=pair.Value;
            foreach (var actor in actors.Values) {
                int remainder=actor.TryAddStock(pair.Key,pending);
                if (remainder==0) { pending=null;break; }
                if (remainder<pending.Count) pending=pending.GetRange(pending.Count-remainder,remainder);
            }
            if (pending != null && pending.Count>0) throw new InvalidOperationException("Native actor capacity disagreed with preflight.");
        }
        if (manifest.Count>0) staged.BuildConfig();
        return staged;
    }
    static InspectionApi.Reply Recover(MarketManager manager, string id, string stationId, ShipMarket source, ShipMarket destination,
        Dictionary<string,List<MarketItem>> manifest, HashSet<AIShip> stale, object plan) {
        var markets=MarketMap(manager);
        var error=ValidateDestination(markets,id,destination,manifest,out var before);
        if (error != null) return Reply(409,"error",error);
        ShipMarket staged;
        try {
            staged=Stage(destination,manifest);
            error=Totals(staged,true,out var after,out _);
            if (error != null) throw new InvalidOperationException(error);
            foreach (var pair in manifest) if (after[pair.Key]!=(before.TryGetValue(pair.Key,out long have) ? have : 0)+pair.Value.Count)
                throw new InvalidOperationException("Staged cargo conservation check failed.");
        } catch (Exception ex) {
            return Reply(500,"id",id,"applied",false,"error","Staging failed; source and destination retained: "+ex.GetType().Name+": "+ex.Message);
        }
        // Preserve market/config/list identity for any existing station GUI.
        foreach (var pair in Actors(destination)) foreach (var category in manifest.Keys) {
            if (!Actors(staged)[pair.Key].Stock.TryGetValue(category,out var incoming)) continue;
            if (!pair.Value.Stock.TryGetValue(category,out var current)) pair.Value.Stock[category]=incoming;
            else for (int i=current.Count; i<incoming.Count; i++) current.Add(incoming[i]);
        }
        StockCache(destination).Clear();foreach (var pair in StockCache(staged)) StockCache(destination).Add(pair.Key,pair.Value);
        destination.PriceModifiers.Clear();foreach (var pair in staged.PriceModifiers) destination.PriceModifiers.Add(pair.Key,pair.Value);
        CachedMass(destination)=-1;
        MarketManager.UnregisterShip(id);
        var queue=(Ostranauts.Ships.AIShipQueue)AIQueue.GetValue(null);
        foreach (var ai in stale) queue.UnregisterShip(ai);
        foreach (var region in AIShipManager.dictAIs.Values) if (region != null) for (int i=region.Count-1;i>=0;i--) if (stale.Contains(region[i])) region.RemoveAt(i);
        var hubs=Hubs(manager);
        if (hubs != null) {
            hubs.Remove(id);
            foreach (var members in hubs.Values) if (members != null) for (int i=members.Count-1;i>=0;i--) if (members[i]==id) members.RemoveAt(i);
        }
        CrewSim.system.RemoveShip(id); // Hull is absent; native removal drops residual ownership only.
        string syncError=null;
        try { destination.RequestTraderUpdate(); } catch (Exception ex) { syncError=ex.GetType().Name+": "+ex.Message; }
        OstrabusPlugin.Logger?.LogInfo("[Freight Recovery] "+id+" -> "+stationId+": orphan cargo credited; removed market and "+stale.Count+" stale controllers.");
        return Reply(200,"id",id,"eligible",true,"applied",true,"credited",true,"sourceMarketRemoved",!markets.ContainsKey(id),
            "staleControllersRemoved",stale.Count,"traderSynchronizationError",syncError,"plan",plan,
            "note",syncError == null ? "Existing virtual cargo credited once through native actor stocking. No hull or crew recreated/deleted." :
                "Cargo credit and cleanup committed. Trader synchronization failed; inspect the station inventory before trading. Retrying this ID cannot credit again.");
    }
}
