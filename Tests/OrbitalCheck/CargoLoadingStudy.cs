using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Emit;
using HarmonyLib;
using Newtonsoft.Json;
using Ostrabus.Orbital.Economy;
using Ostranauts.Core;
using Ostranauts.Core.Models;
using Ostranauts.Trading;

static partial class EconomyChecks {
    static bool StudyQuiet() => false;

    // Preserve the old deployed correction for the regression reproduction.
    static IEnumerable<CodeInstruction> StudyLegacyRemainder(IEnumerable<CodeInstruction> instructions) {
        var original = AccessTools.Method(typeof(MarketActorConfig), nameof(MarketActorConfig.TryAddStock),
            new[] { typeof(string), typeof(List<MarketItem>) });
        foreach (var instruction in instructions) {
            if (instruction.Calls(original)) {
                instruction.opcode = OpCodes.Call;
                instruction.operand = AccessTools.Method(typeof(CargoLoadingPatch), nameof(CargoLoadingPatch.Add));
            }
            yield return instruction;
        }
    }

    // Execute vanilla, previous repair and production repair against native
    // selection/loading and real pod definitions. No live game mutation.
    internal static void CargoStudy(Action<bool, string> check) {
        var fixture = new Harmony("offline.cargo-study.boundaries");
        var deployed = new Harmony("offline.cargo-study.deployed");
        void Patch(Type type, string name, string method) => fixture.Patch(
            AccessTools.Method(type, name), prefix: new HarmonyMethod(typeof(EconomyChecks), method));
        try {
            fixture.Patch(AccessTools.PropertyGetter(typeof(MonoSingleton<MarketManager>), "Instance"),
                prefix: new HarmonyMethod(typeof(EconomyChecks), nameof(Manager)));
            fixture.Patch(AccessTools.PropertyGetter(typeof(ShipMarket), nameof(ShipMarket.BlockadeDiscount)),
                prefix: new HarmonyMethod(typeof(EconomyChecks), nameof(Blockade)));
            Patch(typeof(ShipMarket), nameof(ShipMarket.RequestTraderUpdate), nameof(Sync));
            fixture.Patch(AccessTools.PropertyGetter(typeof(Ship), nameof(Ship.ShipCO)),
                prefix: new HarmonyMethod(typeof(EconomyChecks), nameof(ShipCo)));
            fixture.Patch(AccessTools.Method(typeof(CondOwner), nameof(CondOwner.HasCond), new[] { typeof(string) }),
                prefix: new HarmonyMethod(typeof(EconomyChecks), nameof(HasCond)));
            fixture.Patch(AccessTools.Method(typeof(UnityEngine.Debug), "Log", new[] { typeof(object) }),
                prefix: new HarmonyMethod(typeof(EconomyChecks), nameof(StudyQuiet)));
            fixture.Patch(AccessTools.Method(typeof(UnityEngine.Debug), "LogWarning", new[] { typeof(object) }),
                prefix: new HarmonyMethod(typeof(EconomyChecks), nameof(StudyQuiet)));
            string data = Path.Combine(Ostrabus.Development.InstallationPaths.Game,
                "Ostranauts_Data", "StreamingAssets", "data", "market");
            var configs = JsonConvert.DeserializeObject<JsonMarketActorConfig[]>(File.ReadAllText(
                Path.Combine(data, "Markets", "market_actor_configs.json")));
            var specs = JsonConvert.DeserializeObject<JsonCargoSpec[]>(File.ReadAllText(
                Path.Combine(data, "CargoSpecs", "cargoSpec.json")));
            var template = configs.Single(c => c.strName == "CargoPodSpec");
            check(template.aStock.Length == 0 && template.aVirtualInventorySize.Length == 0 &&
                template.aSupplyDemandMaps.Length == 0, "Authored specialist pod starts empty.");

            Setup(); Hull("SECOND-DEST"); Market("SECOND-DEST", Producer("second-factory", "Product", 3, "Metal"), 0);
            var selected = MarketManager.GetTradeRoute(null);
            check(selected != null && selected.Count == 2 && ReferenceEquals(selected[0], selected[1]),
                "Actual native selection repeats its seed DTO when multiple global routes exist.");

            (int source, int cargo, int product, int occupied, int sourceProduct) Load(string name, int sourceCount, bool duplicate, bool bundle, bool normalize = false, double mass = 1) {
                Setup();
                Field(DataHandler.dictDataCoCollections["Metal"], "_avgMass", mass);
                DataHandler.dictMarketConfigs = new Dictionary<string, JsonMarketActorConfig> { { template.strName, template } };
                DataHandler.dictCargoSpecs = specs.ToDictionary(s => s.strName);
                var ship = Hull("LOADING-STUDY");
                ship.MarketConfigs = new Dictionary<string, string> { { "pod-one", "CargoPodSpec" }, { "pod-two", "CargoPodSpec" } };
                var origin = markets["SOURCE"].GetConfigs()[0];
                origin.Stock["Metal"] = Enumerable.Range(0, sourceCount).Select(_ => new MarketItem { COCollectionName = "Metal", COName = "metal" }).ToList();
                if (bundle) origin.Stock["Product"] = Enumerable.Range(0, 20).Select(_ => new MarketItem { COCollectionName = "Product", COName = "metal" }).ToList();
                markets["SOURCE"].BuildConfig();
                var first = new TradeRouteDTO { OriginStation = "SOURCE", DestinationStation = "DEST",
                    CoCollection = DataHandler.dictDataCoCollections["Metal"], Amount = 50 };
                var routes = new List<TradeRouteDTO> { first };
                if (duplicate) routes.Add(first); // Native GetTradeRoute's same DTO instance.
                if (bundle) routes.Add(new TradeRouteDTO { OriginStation = "SOURCE", DestinationStation = "DEST",
                    CoCollection = DataHandler.dictDataCoCollections["Product"], Amount = 20 });
                if (normalize) {
                    var corrected = new List<TradeRouteDTO> { routes[0] };
                    CargoTradeBundlePatch.Append(corrected, routes.Skip(1));
                    routes = corrected;
                }
                MarketManager.RegisterAICargoHauler(ship, routes);
                var result = (source: markets["SOURCE"].GetStockCountForCategory("Metal"),
                    cargo: markets[ship.strRegID].GetStockCountForCategory("Metal"),
                    product: markets[ship.strRegID].GetStockCountForCategory("Product"),
                    occupied: markets[ship.strRegID].GetConfigs().Count,
                    sourceProduct: markets["SOURCE"].GetStockCountForCategory("Product"));
                Console.WriteLine(name + ": " + JsonConvert.SerializeObject(new { sourceMetal = result.source,
                    cargoMetal = result.cargo, cargoProduct = result.product, occupiedPods = result.occupied,
                    sourceProduct = result.sourceProduct }));
                return result;
            }

            var native = Load("native-single", 50, false, false);
            check(native.source == 50 && native.cargo == 50, "Native single load duplicates cargo back to source.");
            var nativeDuplicate = Load("native-duplicate", 50, true, false);
            check(nativeDuplicate.source == 50 && nativeDuplicate.cargo == 50, "Native readdition masks the duplicate-route overwrite.");
            deployed.Patch(AccessTools.Method(typeof(MarketManager), nameof(MarketManager.RegisterAICargoHauler)),
                transpiler: new HarmonyMethod(typeof(EconomyChecks), nameof(StudyLegacyRemainder)));
            var single = Load("deployed-single", 50, false, false);
            check(single.source == 0 && single.cargo == 50, "Deployed correction conserves a single consignment.");
            var duplicate = Load("deployed-duplicate", 50, true, false);
            check(duplicate.source == 0 && duplicate.cargo == 0, "Deployed duplicate-route path reproduces total shipment loss.");
            var enough = Load("deployed-duplicate-source100", 100, true, false);
            check(enough.source == 0 && enough.cargo == 50, "Duplicate overwrite also loses stock when origin has another full load.");
            var bundle = Load("deployed-two-categories-two-pods", 50, false, true);
            check(bundle.source == 0 && bundle.cargo == 0 && bundle.product == 20 && bundle.occupied == 1,
                "Distinct bundled category overwrites first pod instead of using second empty pod.");

            deployed.UnpatchSelf();
            deployed.CreateClassProcessor(typeof(CargoLoadingPatch)).Patch();
            deployed.CreateClassProcessor(typeof(CargoTradeBundlePatch)).Patch();
            Setup(); Hull("SECOND-DEST"); Market("SECOND-DEST", Producer("second-factory", "Product", 3, "Metal"), 0);
            selected = MarketManager.GetTradeRoute(null);
            check(selected != null && selected.Count == 1, "Production patch corrects native bundle construction.");
            var correctedDuplicate = Load("candidate-duplicate", 50, true, false, true);
            check(correctedDuplicate.source == 0 && correctedDuplicate.cargo == 50,
                "Candidate duplicate normalization and native pod reuse conserve cargo.");
            var correctedBundle = Load("candidate-two-categories-two-pods", 50, false, true, true);
            check(correctedBundle.source == 0 && correctedBundle.cargo == 50 && correctedBundle.product == 20 && correctedBundle.occupied == 2,
                "Candidate preserves distinct commodities in separate native pods.");
            var correctedBoth = Load("candidate-duplicate-and-bundle", 50, true, true, true);
            check(correctedBoth.source == 0 && correctedBoth.cargo == 50 && correctedBoth.product == 20,
                "Candidate handles native repeated seed plus a genuine bundled commodity.");
            var correctedEnough = Load("candidate-source100", 100, true, false, true);
            check(correctedEnough.source == 50 && correctedEnough.cargo == 50,
                "Duplicate normalization preserves the second source load instead of consuming it.");
            var partial = Load("candidate-source-shortfall", 10, true, false, true);
            check(partial.source == 0 && partial.cargo == 10, "Short source load cannot erase accepted cargo.");
            var split = Load("candidate-native-overflow-two-pods", 50, true, false, true, 40);
            check(split.cargo == 50 && split.source == 0 && split.occupied == 2,
                "Native overflow preserves both accepted pod loads.");
            var full = Load("candidate-no-free-pod-for-next-category", 50, false, true, true, 40);
            check(full.cargo == 50 && full.product == 0 && full.sourceProduct == 20,
                "Full pods preserve the first category and return uncarried stock to origin.");
        } finally { deployed.UnpatchSelf(); fixture.UnpatchSelf(); }
    }
}
