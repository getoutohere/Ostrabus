using System;
using System.Collections.Generic;
using Ostranauts.Trading;

namespace Ostrabus.Inspection;

internal static partial class FreightRecovery {
    static string FitAvailable(ShipMarket destination, Dictionary<string,List<MarketItem>> original,
        out Dictionary<string,List<MarketItem>> selected, out int total) {
        selected=new Dictionary<string,List<MarketItem>>(StringComparer.Ordinal);total=0;
        var error=Totals(destination,true,out var stock,out var capacity);
        if (error != null) return error;
        foreach (var pair in original) {
            stock.TryGetValue(pair.Key,out long have);capacity.TryGetValue(pair.Key,out long cap);
            int count=(int)Math.Min(pair.Value.Count,Math.Max(0,cap-have));
            if (count==0) continue;
            selected.Add(pair.Key,pair.Value.GetRange(0,count));total+=count;
        }
        return original.Count>0 && total==0 ? "No destination capacity is currently available; surviving cargo retained." : null;
    }

    static void CommitStock(ShipMarket current, ShipMarket staged, Dictionary<string,List<MarketItem>> affected) {
        foreach (var pair in Actors(current)) foreach (var category in affected.Keys) {
            var from=Actors(staged)[pair.Key].Stock;
            if (!from.TryGetValue(category,out var incoming)) {
                if (pair.Value.Stock.TryGetValue(category,out var removed)) removed.Clear();
                pair.Value.Stock.Remove(category);
            } else if (pair.Value.Stock.TryGetValue(category,out var existing)) {
                existing.Clear();existing.AddRange(incoming);
            } else pair.Value.Stock.Add(category,incoming);
        }
        StockCache(current).Clear();foreach (var pair in StockCache(staged)) StockCache(current).Add(pair.Key,pair.Value);
        current.PriceModifiers.Clear();foreach (var pair in staged.PriceModifiers) current.PriceModifiers.Add(pair.Key,pair.Value);
        CachedMass(current)=-1;
    }

    static InspectionApi.Reply RecoverPartial(Dictionary<string,ShipMarket> markets, string id, ShipMarket source,
        ShipMarket destination, Dictionary<string,List<MarketItem>> selected, object plan) {
        var error=ValidateDestination(markets,id,destination,selected,out var destinationBefore);
        if (error != null) return Reply(409,"error",error);
        error=Totals(source,false,out var sourceBefore,out _);
        if (error != null) return Reply(409,"error",error);
        ShipMarket sourceStage,destinationStage;
        var moved=new Dictionary<string,List<MarketItem>>(StringComparer.Ordinal);
        Dictionary<string,long> sourceAfter;
        try {
            sourceStage=CopyStock(source,selected);
            foreach (var pair in selected) {
                int remaining=pair.Value.Count;
                var items=new List<MarketItem>(remaining);
                foreach (var actor in Actors(sourceStage).Values) {
                    var taken=actor.TakeOutStock(pair.Key,remaining);
                    items.AddRange(taken);remaining-=taken.Count;
                    if (remaining==0) break;
                }
                if (remaining!=0) throw new InvalidOperationException("Source withdrawal disagreed with preflight.");
                moved.Add(pair.Key,items);
            }
            sourceStage.BuildConfig();
            destinationStage=Stage(destination,moved);
            error=Totals(sourceStage,false,out sourceAfter,out _);
            var destinationError=Totals(destinationStage,true,out var destinationAfter,out _);
            if (error == null) error=destinationError;
            if (error != null) throw new InvalidOperationException(error);
            foreach (var pair in moved) {
                sourceAfter.TryGetValue(pair.Key,out long left);
                destinationBefore.TryGetValue(pair.Key,out long before);
                if (left!=sourceBefore[pair.Key]-pair.Value.Count || destinationAfter[pair.Key]!=before+pair.Value.Count)
                    throw new InvalidOperationException("Partial transfer conservation check failed.");
            }
        } catch (Exception ex) {
            return Reply(500,"id",id,"applied",false,"error","Partial staging failed; both markets retained: "+ex.GetType().Name+": "+ex.Message);
        }
        CommitStock(destination,destinationStage,moved);
        CommitStock(source,sourceStage,moved);
        string syncError=null;
        try { destination.RequestTraderUpdate(); } catch (Exception ex) { syncError=ex.GetType().Name+": "+ex.Message; }
        var remainingCargo=new Dictionary<string,object>();long remainingUnits=0;
        foreach (var pair in sourceAfter) if (pair.Value>0) { remainingCargo.Add(pair.Key,pair.Value);remainingUnits+=pair.Value; }
        return Reply(200,"id",id,"eligible",true,"applied",true,"credited",true,"partial",true,
            "sourceMarketRemoved",false,"remainingCargo",remainingCargo,"remainingUnits",remainingUnits,
            "staleControllersRemoved",0,"traderSynchronizationError",syncError,"plan",plan,
            "note","Credited the selected cargo only; original remaining items, market, ownership and controller references retained. No hull or crew changed.");
    }
}
