using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Ostranauts.Ships;
using Ostranauts.Trading;

static partial class Program {
    sealed class ProducingPreviewActor : MarketActor {
        protected override bool AllowedToSpawnItems => true;
    }
    static void Unload(MarketActor actor) => AccessTools.Method(typeof(MarketActor), "OnDestroy").Invoke(actor, null);
    static int Loaded(ShipMarket market) => ((List<MarketActor>)AccessTools.Field(typeof(ShipMarket), "_loadedMarketActors").GetValue(market)).Count;
    static void Seed(MarketActorConfig config, int count) {
        config.Stock["TestMetal"] = Enumerable.Range(0, count).Select(_ => new MarketItem { COName = "ItmTestMetal", COCollectionName = "TestMetal" }).ToList();
    }
    static void PreviewControls(Ship real) {
        var market = MarketManager.GetShipMarket(real.strRegID);
        var stock = market.GetConfigForMarketActor("real-1"); Seed(stock, 3);
        var originalOwner = DataHandler.mapCOs["real-1"];
        int originalLoaded = Loaded(market), originalConfigs = market.GetConfigs().Count;
        foreach (Ship visual in new Ship[] { Empty<AsyncRatingShip>(), Empty<AsyncPreviewShip>() }) {
            visual.strRegID = real.strRegID;
            var actor = Pod(visual, "real-1", producing: true);
            Check(actor.GetMarketConfig() == null, "Visual actor retained a live transaction/sync configuration");
            Check((bool)AccessTools.Field(typeof(MarketActor), "bInit").GetValue(actor), "Visual actor would reattempt registration every frame");
            Check(Loaded(market) == originalLoaded && market.GetConfigs().Count == originalConfigs, "Same-ID visual actor joined the real market");
            Check(ReferenceEquals(stock, market.GetConfigForMarketActor("real-1")) && stock.Stock["TestMetal"].Count == 3, "Visual initialization changed real stock");
            actor.SyncInventoryToMarket();
            actor.ReportTransaction(originalOwner, 1);
            Check(stock.Stock["TestMetal"].Count == 3, "Visual sync/transaction changed real stock");
            Unload(actor);
            Check(Loaded(market) == originalLoaded && stock.Stock["TestMetal"].Count == 3, "Visual destruction unregistered a real actor or changed cargo");
            DataHandler.mapCOs["real-1"] = originalOwner;

            visual.strRegID = "PREVIEW-ONLY-" + visual.GetType().Name;
            Pod(visual, "nonworld-" + visual.GetType().Name, producing: true);
            Check(!MarketManager.GetJSONSave().mapMarket.ContainsKey(visual.strRegID), "Visual-only ship created a persistent empty market");
        }
        var remote = Empty<BarterZoneShip>(); remote.strRegID = "REAL-REMOTE";
        var remoteActor = Pod(remote, "remote-real");
        var remoteMarket = MarketManager.GetShipMarket(remote.strRegID);
        var remoteConfig = remoteMarket.GetConfigForMarketActor("remote-real"); Seed(remoteConfig, 2);
        Check(remoteConfig != null && Loaded(remoteMarket) == 1 && remoteActor.GetMarketConfig() == "CargoPodSpec", "Real offscreen barter registration was blocked");
        Unload(remoteActor);
        Check(Loaded(remoteMarket) == 0 && remoteConfig.Stock["TestMetal"].Count == 2 && remoteMarket.GetConfigs().Count == 1,
            "Ordinary offscreen unloading discarded persistent cargo");
        Check(MarketManager.GetJSONSave().mapMarket[remote.strRegID].configMap["remote-real"].aStock.Single() == "TestMetal|ItmTestMetal=2",
            "Real unloaded cargo was not preserved in native market serialization");

        // A previously saved ghost might already have paid-for cargo. Prevention
        // never sweeps missing IDs or removes these records.
        var legacy = DataHandler.GetMarketConfig("CargoPodSpec"); legacy.COwnerId = "old-ghost"; Seed(legacy, 7);
        market.AddMarketConfig(legacy.COwnerId, legacy);
        var anotherPreview = Empty<AsyncRatingShip>(); anotherPreview.strRegID = real.strRegID;
        Pod(anotherPreview, "another-preview");
        Check(ReferenceEquals(market.GetConfigForMarketActor("old-ghost"), legacy) && legacy.Stock["TestMetal"].Count == 7,
            "Prevention silently deleted old ghost cargo");
        Check(MarketManager.GetJSONSave().mapMarket[real.strRegID].configMap["old-ghost"].aStock.Single() == "TestMetal|ItmTestMetal=7",
            "Existing orphaned stock was not retained for separate reconciliation");
    }
}
