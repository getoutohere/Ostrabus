using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using Ostranauts.Core;
using Ostranauts.Core.Models;
using Ostranauts.Ships;
using Ostranauts.Trading;
using UnityEngine;

static partial class Program {
    static int checks;
    static MarketManager manager;
    static readonly List<MarketActor> previewActors = new List<MarketActor>();
    static T Empty<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    static void Field(object obj, string name, object value) => AccessTools.Field(obj.GetType(), name).SetValue(obj, value);
    static void Check(bool ok, string message) { checks++; if (!ok) throw new Exception(message); }
    static bool Equal(UnityEngine.Object __0, UnityEngine.Object __1, ref bool __result) { __result = ReferenceEquals(__0, __1); return false; }
    static bool Unequal(UnityEngine.Object __0, UnityEngine.Object __1, ref bool __result) { __result = !ReferenceEquals(__0, __1); return false; }
    static bool Manager(ref MarketManager __result) { __result = manager; return false; }
    static bool NoBlockade(ref float __result) { __result = 0; return false; }
    static bool DestroyPreview(UnityEngine.Object obj, bool allowDestroyingAssets) {
        foreach (var actor in previewActors) AccessTools.Method(typeof(MarketActor), "OnDestroy").Invoke(actor, null);
        return false;
    }
    static MarketActor Pod(Ship ship, string id, bool producing = false) {
        var co = Empty<CondOwner>();
        Field(co, "_strID", id);
        Field(co, "_objShip", ship);
        co.mapGUIPropMaps = new Dictionary<string, Dictionary<string, string>> {
            ["Trader"] = new Dictionary<string, string> { ["strMarketConfig"] = "CargoPodSpec" }
        };
        DataHandler.mapCOs[id] = co;
        var actor = producing ? (MarketActor)Empty<ProducingPreviewActor>() : Empty<MarketActor>();
        Field(actor, "COSelf", co);
        AccessTools.Method(typeof(MarketActor), "Update").Invoke(actor, null);
        return actor;
    }
    static void NativeChecks(bool patched = false, bool repair = false) {
        var h = new Harmony("offline.cargo-pod-diagnosis.fixture");
        void Patch(MethodBase method, string prefix) => h.Patch(method, prefix: new HarmonyMethod(typeof(Program), prefix));
        try {
            Patch(AccessTools.Method(typeof(UnityEngine.Object), "op_Equality"), nameof(Equal));
            Patch(AccessTools.Method(typeof(UnityEngine.Object), "op_Inequality"), nameof(Unequal));
            Patch(AccessTools.PropertyGetter(typeof(MonoSingleton<MarketManager>), "Instance"), nameof(Manager));
            Patch(AccessTools.PropertyGetter(typeof(ShipMarket), "BlockadeDiscount"), nameof(NoBlockade));
            Patch(AccessTools.Method(typeof(UnityEngine.Object), "DestroyImmediate", new[] { typeof(UnityEngine.Object), typeof(bool) }), nameof(DestroyPreview));
            if (patched) h.CreateClassProcessor(typeof(Ostrabus.MiscBugFixes.PreviewMarketIsolation)).Patch();

            manager = Empty<MarketManager>();
            Field(manager, "_market", new Dictionary<string, ShipMarket>());
            DataHandler.mapCOs = new Dictionary<string, CondOwner>();
            string path = Path.Combine(Ostrabus.Development.InstallationPaths.Data, "market", "Markets", "market_actor_configs.json");
            var config = JArray.Parse(File.ReadAllText(path)).Single(x => (string)x["strName"] == "CargoPodSpec").ToObject<JsonMarketActorConfig>();
            DataHandler.dictMarketConfigs = new Dictionary<string, JsonMarketActorConfig> { [config.strName] = config };
            DataHandler.dictCargoSpecs = config.aCargoPodSpecs.ToDictionary(x => x, x => new JsonCargoSpec { strName = x });

            var real = Empty<Ship>(); real.strRegID = "O-GXD";
            for (int i = 0; i < 4; i++) Pod(real, "real-" + i);
            Check(MarketManager.GetCargoPods(real.strRegID).Count == 4, "Real pod baseline failed.");
            var preview = Empty<AsyncRatingShip>(); preview.strRegID = real.strRegID;
            var map = new DictionaryArray<string, CondOwner>();
            Field(preview, "mapICOs", map);
            for (int i = 0; i < 4; i++) {
                var actor = Pod(preview, "preview-" + i);
                previewActors.Add(actor);
                map["preview-" + i] = DataHandler.mapCOs["preview-" + i];
            }
            int expectedPods = patched ? 4 : 8;
            Check(MarketManager.GetCargoPods(real.strRegID).Count == expectedPods, "Preview registration did not match expected isolated/native behavior.");
            Check(((List<MarketActor>)AccessTools.Field(typeof(ShipMarket), "_loadedMarketActors").GetValue(MarketManager.GetShipMarket(real.strRegID))).Count == expectedPods, "Loaded actor count failed.");
            preview.Destroy();
            Check(DataHandler.mapCOs.Count == 4 && DataHandler.mapCOs.Keys.All(x => x.StartsWith("real-")), "Preview destroy did not remove temporary item identities.");
            Check(((List<MarketActor>)AccessTools.Field(typeof(ShipMarket), "_loadedMarketActors").GetValue(MarketManager.GetShipMarket(real.strRegID))).Count == 4, "OnDestroy did not unregister loaded preview actors.");
            Check(MarketManager.GetCargoPods(real.strRegID).Count == expectedPods, "Unexpected market configurations after preview destruction.");
            var saved = MarketManager.GetJSONSave();
            Check(saved.mapMarket[real.strRegID].configMap.Count == expectedPods, "Unexpected market configuration count in save.");
            var reloaded = new ShipMarket(saved.mapMarket[real.strRegID]);
            Check(reloaded.GetConfigs().Count == expectedPods && reloaded.GetConfigs().All(x => x.IsCargoPod), "Native market reload changed the expected configurations.");
            Check(reloaded.GetConfigs().Where(x => x.COwnerId.StartsWith("preview-")).All(x => x.Stock.Count == 0), "Preview ghosts unexpectedly contain cargo.");
            MarketManager.RemoveMarketActorFromShip(real, "real-0");
            Check(MarketManager.GetShipMarket(real.strRegID).GetConfigForMarketActor("real-0") == null, "Native permanent-removal control retained its configuration.");
            Check(MarketManager.GetCargoPods(real.strRegID).Count == expectedPods - 1, "Permanent-removal control affected other pods.");
            if (patched) PreviewControls(real);
            if (repair) RepairChecks(h);
            Console.WriteLine(patched ? "CORRECTED: native rating-copy initialization, destruction and save/reload retain only real cargo-pod market entries." :
                "CONFIRMED: native rating-copy registration leaves persistent ghost cargo pods after destruction and save/reload.");
            Console.WriteLine(checks + " native assertions passed. Production preview isolation " + (patched ? "enabled." : "not installed."));
        } finally { h.UnpatchSelf(); }
    }
}
