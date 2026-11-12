using System;
using System.Collections.Generic;
using Ostranauts.Trading;

namespace Ostrabus.Inspection;

internal static partial class EconomyInspection {
    static InspectionApi.Reply AuditPage(Dictionary<string,ShipMarket> markets, ControllerInspection controllers, int offset, int limit, long began) {
        int index=0;
        bool more=false;
        var rows=new List<object>();
        foreach (var pair in markets) {
            if (index<offset) {
                if (Expired(began)) return Error(503,"Offset scan exceeded the inspection budget; use a direct market audit.");
                index++;continue;
            }
            if (rows.Count>=limit || (rows.Count>0 && Expired(began))) { more=true;break; }
            rows.Add(pair.Value == null ? Map("id",pair.Key,"invalidMarket",true) : Audit(pair.Key,pair.Value,controllers,began));
            index++;
        }
        return new InspectionApi.Reply(200,Map("items",rows,"scannedThrough",index,"nextOffset",more ? (object)index : null,
            "budgetLimited",more && Expired(began),"controllerScanComplete",controllers.Complete,
            "pagination","Live market order; requests can skip or repeat entries as the simulation changes. Stock and controller completeness are reported separately."));
    }
    static object Audit(string id, ShipMarket market, ControllerInspection controllers, long began) {
        var ship=Hull(id);
        var registrations=controllers.Snapshot(id,ship);
        var stock=CompareStock(market,began);
        bool pod=HasPod(market,out bool podsIncomplete);
        var observations=new List<string>();
        if (ship == null) observations.Add("unresolvedMarketHull");
        if (ship?.bDestroyed == true) observations.Add("destroyedMarketHull");
        if (!string.Equals(id,market.ShipRegId,StringComparison.Ordinal)) observations.Add("marketIdentityMismatch");
        if (Equals(registrations["duplicateRegistrations"],true)) observations.Add("duplicateLiveHullRegistrations");
        if ((int)registrations["staleHullReferences"]>0) observations.Add("staleControllerHullReference");
        if (Equals(stock["matches"],false)) observations.Add("cachedStockMismatch");
        return Map("id",id,"storedMarketShipId",market.ShipRegId,"shipResolved",ship != null,
            "destroyed",ship?.bDestroyed,"hidden",ship?.HideFromSystem,"hasCargoPod",pod ? (object)true : podsIncomplete ? null : (object)false,
            "podScanIncomplete",podsIncomplete,"controllers",registrations,"stockComparison",stock,"observations",observations,
            "note","Current observations only. A missing hull does not establish failed delivery, invalid ownership or safe-to-delete cargo. No repair, transfer or cache rebuild is performed.");
    }
    // Native GetStockTotals sums List.Count by stock dictionary key. Do that
    // directly without flattening manifests, creating COs or rebuilding caches.
    static Dictionary<string,object> CompareStock(ShipMarket market, long began) {
        const int maxActors=512, maxEntries=4096, maxCategories=256, maxMismatches=64;
        var cached=new Dictionary<string,long>(StringComparer.Ordinal);
        var backing=new Dictionary<string,long>(StringComparer.Ordinal);
        var rawCache=Stock(market);
        var actors=Actors(market);
        int work=0, actorsRead=0, entriesRead=0;
        bool complete=rawCache != null && actors != null, bounded=false, invalid=false;
        long cachedTotal=0, backingTotal=0;
        bool Stop() => work>=maxEntries || (work>0 && Expired(began));
        if (rawCache != null) foreach (var pair in rawCache) {
            if (Stop() || cached.Count>=maxCategories) { complete=false; bounded=true; break; }
            work++;
            cached.Add(pair.Key,pair.Value);cachedTotal+=pair.Value;
        }
        if (actors != null) foreach (var actor in actors.Values) {
            if (Stop() || actorsRead>=maxActors) { complete=false; bounded=true; break; }
            work++;actorsRead++;
            if (actor?.Stock == null) { complete=false; invalid=true; continue; }
            foreach (var pair in actor.Stock) {
                if (Stop() || (backing.Count>=maxCategories && !backing.ContainsKey(pair.Key))) { complete=false; bounded=true; break; }
                work++;entriesRead++;
                if (pair.Value == null) { complete=false; invalid=true; continue; }
                backing.TryGetValue(pair.Key,out long count);
                backing[pair.Key]=count+pair.Value.Count;backingTotal+=pair.Value.Count;
            }
            if (bounded) break;
        }
        var mismatches=new List<object>();
        int differences=0;
        if (complete) {
            foreach (var pair in cached) {
                backing.TryGetValue(pair.Key,out long actual);
                if (actual == pair.Value) continue;
                differences++;
                if (mismatches.Count<maxMismatches) mismatches.Add(Map("category",pair.Key,"cached",pair.Value,"backing",actual));
            }
            foreach (var pair in backing) {
                if (cached.ContainsKey(pair.Key) || pair.Value == 0) continue;
                differences++;
                if (mismatches.Count<maxMismatches) mismatches.Add(Map("category",pair.Key,"cached",0L,"backing",pair.Value));
            }
        }
        return Map("complete",complete,"matches",complete ? (object)(differences == 0) : null,
            "cacheAvailable",rawCache != null,"actorsAvailable",actors != null,"budgetOrLimitReached",bounded,"invalidBackingData",invalid,
            "cachedQuantityObserved",cachedTotal,"backingQuantityObserved",backingTotal,
            "actorsExamined",actorsRead,"stockEntriesExamined",entriesRead,
            "cachedCategoriesExamined",cached.Count,"backingCategoriesExamined",backing.Count,
            "mismatchCount",complete ? (object)differences : null,"mismatches",mismatches,"mismatchesTruncated",differences>mismatches.Count,
            "note","Observed totals are partial unless complete. Counts use stock keys and list lengths, not mass or individual item validation. Unknown comparisons never imply matching stock.");
    }
}
