using System.Collections.Generic;
using HarmonyLib;
using Ostranauts.Core;
using Ostranauts.Core.Models;
using Ostranauts.Trading;
using Ostrabus.MiscBugFixes;

static partial class LifecycleChecks {
    static MarketManager freightManager;
    static bool FreightManagerInstance(ref MarketManager __result) { __result = freightManager; return false; }
    // Execute native destruction and registry removal. The existing lifecycle
    // fixture replaces scene/UI boundaries; it does not replace Ship.Destroy,
    // StarSystem.RemoveShip or any market method.
    static void FreightDestructionChecks(StarSystem system, Harmony fixture, CrewSim crewSim) {
        var manager = Empty<MarketManager>();
        freightManager = manager;
        fixture.Patch(AccessTools.PropertyGetter(typeof(MonoSingleton<MarketManager>), "Instance"),
            prefix: new HarmonyMethod(typeof(LifecycleChecks), nameof(FreightManagerInstance)));
        InitializeCollections(manager);
        AccessTools.Field(typeof(MonoSingleton<MarketManager>), "_instance").SetValue(null, manager);
        var markets = (Dictionary<string, ShipMarket>)AccessTools.Field(typeof(MarketManager), "_market").GetValue(manager);
        ShipMarket Cargo(string id) {
            var market = new ShipMarket(id);
            var actor = Empty<MarketActorConfig>(); InitializeCollections(actor);
            actor.COwnerId = "fixture-cargo-pod";
            Field(actor, "<CargoSpecs>k__BackingField", new List<JsonCargoSpec>());
            actor.Stock["AnyFood"] = new List<MarketItem> {
                new MarketItem { COCollectionName = "AnyFood", COName = "fixture-food" }
            };
            market.AddMarketConfig(actor.COwnerId, actor); market.BuildConfig();
            markets[id] = market;
            return market;
        }
        foreach (bool repaired in new[] { false, true }) {
        if (repaired) fixture.CreateClassProcessor(typeof(ShipDestroyedTargetsPatch)).Patch();
        foreach (bool despawning in new[] { false, true }) {
            string id = "freight-" + repaired + "-" + despawning;
            var ship = ShipAt(id, new ShipSitu(false));
            ship.Classification = Ship.TypeClassification.Default;
            Field(ship, "nLoadState", Ship.Loaded.Shallow);
            system.dictShips[id] = ship;
            var market = Cargo(id);
            Check(MarketManager.GetShipMarket(id).GetStockCountForCategory("AnyFood") == 1,
                "Cargo fixture was not registered before destruction.");
            ship.Destroy(despawning);
            Check(ship.bDestroyed && !system.dictShips.ContainsKey(id),
                "Native destruction did not remove the hull immediately.");
            bool retained = object.ReferenceEquals(MarketManager.GetShipMarket(id), market);
            Check(retained == (!repaired || despawning), "Cargo lifetime disagrees with actual physical destruction versus despawn.");
            Check(market.GetStockCountForCategory("AnyFood") == 1 && market.GetConfigs()[0].Stock["AnyFood"].Count == 1,
                "Retirement rewrote stock or credited a destination.");
            // This is the separate operation that StarSystem.Update performs for
            // destroyed hulls still in its registry. Direct destruction has
            // already removed this hull from that registry.
            MarketManager.UnregisterShip(id);
            Check(MarketManager.GetShipMarket(id) == null,
                "Native explicit market cleanup did not remove the orphan.");
        }
        }
        fixture.CreateClassProcessor(typeof(ShipReplacementTargetsPatch)).Patch();
        var replacement = ShipAt("freight-native-replacement", new ShipSitu(false));
        replacement.Classification = Ship.TypeClassification.Default;
        system.dictShips[replacement.strRegID] = replacement;
        var replacementMarket = Cargo(replacement.strRegID);
        JsonShip saved = null; Save(replacement, replacement.strRegID, ref saved);
        AccessTools.Method(typeof(CrewSim), "DestroyAndReload").Invoke(crewSim, new object[] { replacement, saved });
        Check(replacement.bDestroyed && system.dictShips.TryGetValue(saved.strRegID, out var restored) &&
            !object.ReferenceEquals(replacement, restored), "Native hull replacement did not execute.");
        Check(object.ReferenceEquals(MarketManager.GetShipMarket(saved.strRegID), replacementMarket) &&
            replacementMarket.GetStockCountForCategory("AnyFood") == 1, "Native replacement lost its cargo market.");

        // Registry ownership distinguishes a real vessel from an unregistered
        // preview/copy with the same textual ID.
        var preview = ShipAt("freight-preview-copy", new ShipSitu(false));
        preview.Classification = Ship.TypeClassification.Default;
        var owner = ShipAt(preview.strRegID, new ShipSitu(false));
        system.dictShips[owner.strRegID] = owner;
        var previewMarket = Cargo(owner.strRegID);
        preview.Destroy(isDespawning: false);
        Check(object.ReferenceEquals(MarketManager.GetShipMarket(owner.strRegID), previewMarket),
            "Destroying an unregistered copy erased the registered market.");
        foreach (var classification in new[] { Ship.TypeClassification.OrbitalStation, Ship.TypeClassification.GroundStation,
            Ship.TypeClassification.GroundStationUnfinished, Ship.TypeClassification.Skyscraper }) {
            string id = "station-cargo-control-" + classification;
            var station = ShipAt(id, new ShipSitu(false)); station.Classification = classification;
            system.dictShips[id] = station;
            var market = Cargo(id);
            station.Destroy(isDespawning: false);
            Check(object.ReferenceEquals(MarketManager.GetShipMarket(id), market),
                "Mobile-cargo lifetime policy affected a permanent station: " + classification);
        }
        var substation = ShipAt("substation-cargo-control", new ShipSitu(false));
        Field(substation, "_subStation", true); system.dictShips[substation.strRegID] = substation;
        var substationMarket = Cargo(substation.strRegID);
        substation.Destroy(isDespawning: false);
        Check(object.ReferenceEquals(MarketManager.GetShipMarket(substation.strRegID), substationMarket),
            "Mobile-cargo lifetime policy affected a substation");
    }
}
