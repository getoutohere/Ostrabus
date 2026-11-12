using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using Ostranauts.Core;
using Ostranauts.Core.Models;
using Ostranauts.Ships;
using Ostranauts.ShipGUIs.Trade;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

static partial class Program {
    static readonly Dictionary<string, double> tradeMoney = new Dictionary<string, double>();
    static readonly Dictionary<Component, Item> tradeComponents = new Dictionary<Component, Item>();
    static GameObject tradeGameObject;
    static Transform tradeTransform;
    static JObject tradeItemDefinition;
    static int nativeDrops, nativeAdds, nativeRemovals;

    static bool TradeTruth(UnityEngine.Object __0, ref bool __result) {
        __result = !ReferenceEquals(__0, null);
        return false;
    }
    static bool TradeGameObject(ref GameObject __result) { __result = tradeGameObject; return false; }
    static bool TradeTransform(ref Transform __result) { __result = tradeTransform; return false; }
    static bool TradeParent(ref Transform __result) { __result = null; return false; }
    static bool TradeInactive(ref bool __result) { __result = false; return false; }
    static bool TradeTrue(ref bool __result) { __result = true; return false; }
    static bool TradeLayer(ref int __result) { __result = 0; return false; }
    static bool TradeMarker(ref IntPtr __result) { __result = IntPtr.Zero; return false; }
    static bool TradeTile(ref Tile __result) { __result = null; return false; }
    static bool TradePathfinder(ref Pathfinder __result) { __result = null; return false; }
    static bool TradeWorld(ref Vector2 __result) { __result = new Vector2(300, 300); return false; }
    static bool TradeString(string strName, ref string __result) { __result = strName; return false; }
    static bool TradeMoneyRead(CondOwner __instance, string strName, ref double __result) {
        __result = strName == "StatMoney" && tradeMoney.TryGetValue(__instance.strID, out double balance) ? balance : 0;
        return false;
    }
    static bool TradeMoneyAdd(CondOwner __instance, string strName, double fAmount) {
        if (strName != "StatMoney") throw new Exception("Unexpected condition write: " + strName);
        tradeMoney[__instance.strID] = tradeMoney[__instance.strID] + fAmount;
        return false;
    }
    static bool TradeZoneQuery(Ship __instance, CondTrigger ct, ref List<CondOwner> __result) {
        // Stand-in for the scene's physics query, not its item transfer.
        __result = Items(__instance).Values.Where(co => co.objCOParent == null && (ct == null || ct.Triggered(co))).ToList();
        return false;
    }
    static void CountDrop() { nativeDrops++; }
    static void CountAdd() { nativeAdds++; }
    static void CountRemoval() { nativeRemovals++; }
    static T TradeComponent<T>(Component component) where T : Component {
        if (typeof(T) == typeof(Item) && tradeComponents.TryGetValue(component, out var item)) return (T)(Component)item;
        return null;
    }
    static IEnumerable<CodeInstruction> TradeComponentCalls(IEnumerable<CodeInstruction> instructions) {
        int replacements = 0;
        foreach (var instruction in instructions) {
            if (instruction.operand is MethodInfo method && method.DeclaringType == typeof(Component) &&
                method.Name == "GetComponent" && method.IsGenericMethod) {
                instruction.opcode = OpCodes.Call;
                instruction.operand = AccessTools.Method(typeof(Program), nameof(TradeComponent)).MakeGenericMethod(method.GetGenericArguments());
                replacements++;
            }
            yield return instruction;
        }
        if (replacements != 2) throw new Exception("Native trade fixture expected exactly two component lookup substitutions.");
    }
    static void TradePatch(Harmony fixture, Type type, string method, string callback, Type[] parameters = null) {
        fixture.Patch(AccessTools.Method(type, method, parameters), prefix: new HarmonyMethod(typeof(Program), callback));
    }
    static void TradeProperty(Harmony fixture, Type type, string property, string callback, bool setter = false) {
        var method = setter ? AccessTools.PropertySetter(type, property) : AccessTools.PropertyGetter(type, property);
        fixture.Patch(method, prefix: new HarmonyMethod(typeof(Program), callback));
    }
    static CondOwner TradeCO(string id, Ship ship, bool item = false) {
        var co = Empty<CondOwner>();
        Set(co, "_strID", id);
        co.strName = co.strCODef = item ? (string)tradeItemDefinition["strName"] : id;
        co.strNameFriendly = co.strName;
        co.tf = Empty<Transform>();
        co.aStack = new TrackingCollection<CondOwner>();
        co.aLot = new TrackingCollection<CondOwner>();
        co.mapConds = new Dictionary<string, Condition>();
        co.mapGUIPropMaps = new Dictionary<string, Dictionary<string, string>>();
        Set(co, "_objShip", ship);
        if (ship != null) Items(ship)[id] = co;
        if (item) {
            foreach (string expression in tradeItemDefinition["aStartingConds"].Values<string>()) {
                int equals = expression.IndexOf('=');
                if (equals < 0) throw new Exception("Unexpected non-scalar fixture condition: " + expression);
                string name = expression.Substring(0, equals);
                var condition = Empty<Condition>();
                condition.strName = condition.strNameFriendly = name;
                condition.fCount = double.Parse(expression.Substring(expression.LastIndexOf('x') + 1), CultureInfo.InvariantCulture);
                co.mapConds[name] = condition;
            }
            var component = Empty<Item>();
            component.nWidthInTiles = component.nHeightInTiles = 1;
            Set(co, "_itemComponentReference", component);
            tradeComponents.Add(co, component);
        }
        tradeMoney[id] = 1000;
        DataHandler.mapCOs[id] = co;
        return co;
    }
    static Toggle TradeToggle(bool value) {
        var toggle = Empty<Toggle>();
        Set(toggle, "m_IsOn", value);
        return toggle;
    }
    static GUITrade TradeWindow(CondOwner player, CondOwner trader, Ship local, BarterZoneShip destination, CondOwner item) {
        var gui = Empty<GUITrade>();
        var row = Empty<GUITradeRowSingle>();
        Set(row, "_coIDs", new List<string> { item.strID });
        Set(row, "_amountToTrade", 1);
        row.Price = 10;
        Set(gui, "_salesList", new List<GUITradeRowBase> { row });
        Set(gui, "_coUser", player);
        Set(gui, "coSelfTemp", trader);
        Set(gui, "_selectedCurrency", "StatMoney");
        Set(gui, "ctThemBuy", DataHandler.GetCondTrigger("TIsBarterOKLGFixerBuy"));
        Set(gui, "ctThemSell", DataHandler.GetCondTrigger("TIsBarterOKLGFixerSell"));
        Set(gui, "txtError", Empty<TextMeshProUGUI>());
        Set(gui, "chkBuy", TradeToggle(true));
        Set(gui, "chkSell", TradeToggle(false));
        gui.chkSellToZone = TradeToggle(true);
        var dropdown = Empty<Dropdown>();
        var optionsField = AccessTools.Field(typeof(Dropdown), "m_Options");
        var options = Activator.CreateInstance(optionsField.FieldType);
        AccessTools.Property(optionsField.FieldType, "options").SetValue(options, new List<Dropdown.OptionData> { new Dropdown.OptionData(destination.strRegID) });
        optionsField.SetValue(dropdown, options);
        Set(gui, "ddShipSelection", dropdown);
        Set(gui, "_mapZonesSelf", new Dictionary<JsonZone, Ship> { [new JsonZone { strName = "FixerCargo", strRegID = local.strRegID, aTiles = new[] { 0 } }] = local });
        Set(gui, "_mapZonesUser", new Dictionary<JsonZone, Ship> { [new JsonZone { strName = "Cargo", strRegID = destination.strRegID, aTiles = new[] { 0 } }] = destination });
        return gui;
    }
    static void InvokeTrade(GUITrade gui, string method) {
        try { AccessTools.Method(typeof(GUITrade), method).Invoke(gui, null); }
        catch (TargetInvocationException error) { throw error.InnerException; }
    }
    static void NativeTradeRoundTrip(bool clearMarkerControl, bool returnToSameShip, bool repaired) {
        tradeMoney.Clear();
        tradeComponents.Clear();
        nativeDrops = nativeAdds = nativeRemovals = 0;
        var a = Copy("ROUNDTRIP-A", true);
        var b = returnToSameShip ? a : Copy("ROUNDTRIP-B", true);
        a.gameObject = b.gameObject = tradeGameObject;
        var originalA = Empty<Ship>();
        var originalB = returnToSameShip ? originalA : Empty<Ship>();
        originalA.json = a.json.Clone();
        originalB.json = b.json.Clone();
        var local = Empty<Ship>();
        local.strRegID = "TRADER-STATION";
        local.gameObject = tradeGameObject;
        Set(local, "mapICOs", new DictionaryArray<string, CondOwner>());
        Set(local, "nLoadState", Ship.Loaded.Full);
        CrewSim.system = Empty<StarSystem>();
        CrewSim.system.dictShips = new Dictionary<string, Ship>();
        CrewSim.system.dictShips[a.strRegID] = originalA;
        CrewSim.system.dictShips[b.strRegID] = originalB;
        var loader = Empty<AsyncShipLoader>();
        var copies = new Dictionary<string, IAsyncLoadable>();
        copies[a.strRegID] = a;
        copies[b.strRegID] = b;
        Set(loader, "_loadStateDict", copies);
        Set(loader, "_loadingCoroutine", new List<Coroutine>());
        AccessTools.Field(typeof(MonoSingleton<AsyncShipLoader>), "_instance").SetValue(null, loader);
        var player = TradeCO("roundtrip-player", null);
        var trader = TradeCO("roundtrip-trader", local);
        var item = TradeCO("roundtrip-loose-item", local, true);
        Check(DataHandler.GetCondTrigger("TIsBarterOKLGFixerBuy").Triggered(item), "Actual loose alarm asset is not eligible for the Fixer's high-value buy mode.");
        Check(DataHandler.GetCondTrigger("TIsBarterOKLGFixerSell").Triggered(item), "Actual loose alarm asset is not eligible for the Fixer's sell filter.");
        itemPosition = new Vector3(300, 300, 0);

        var firstPurchase = TradeWindow(player, trader, local, a, item);
        InvokeTrade(firstPurchase, "OnBuy");
        Check(ReferenceEquals(item.ship, a) && originalA.json.aItems.Any(entry => entry.strID == item.strID), "First native purchase failed to deliver and save to A.");
        Check(item.mapGUIPropMaps.ContainsKey(GUITradeBase.ASYNCIDENTIFIER), "First native purchase did not leave the native AsyncItem marker.");
        Check(tradeMoney[player.strID] == 990 && tradeMoney[trader.strID] == 1010, "First native purchase did not charge 10.");

        // The control differs only in marker state, as after full materialization.
        // This fixture does not claim to run full Ship.GetItems.
        if (clearMarkerControl) item.mapGUIPropMaps.Remove(GUITradeBase.ASYNCIDENTIFIER);
        Set(firstPurchase, "chkBuy", TradeToggle(false));
        Set(firstPurchase, "chkSell", TradeToggle(true));
        InvokeTrade(firstPurchase, "OnSell");
        Check(ReferenceEquals(item.ship, local) && !Items(a).ContainsKey(item.strID), "Native sale did not return the same item to trader storage.");
        Check(!originalA.json.aItems.Any(entry => entry.strID == item.strID), "Native sale retained source A's saved cargo.");
        Check(item.mapGUIPropMaps.ContainsKey(GUITradeBase.ASYNCIDENTIFIER) != clearMarkerControl, "Native sale unexpectedly changed marker state.");
        Check(tradeMoney[player.strID] == 1000 && tradeMoney[trader.strID] == 1000, "Native sale did not credit 10.");

        var secondPurchase = TradeWindow(player, trader, local, b, item);
        bool failed = false;
        try { InvokeTrade(secondPurchase, "OnBuy"); }
        catch (ArgumentException error) {
            if (!error.Message.Contains(GUITradeBase.ASYNCIDENTIFIER)) throw;
            failed = true;
        }
        bool expectedSuccess = clearMarkerControl || repaired;
        Check(failed != expectedSuccess, "Unexpected native rebuy result for marker/repair state.");
        Check(ReferenceEquals(item.ship, b) && Items(b).ContainsKey(item.strID), "Native rebuy did not transfer the same physical CO to B.");
        Check(originalB.json.aItems.Any(entry => entry.strID == item.strID) == expectedSuccess, "Unexpected destination persistence after native rebuy.");
        Check(tradeMoney[player.strID] == 990 && tradeMoney[trader.strID] == 1010, "Native rebuy did not charge before writeback.");
        Check(itemPosition == new Vector3(expectedSuccess ? 300 : 100, expectedSuccess ? 300 : 100, 0), "Unexpected position after native rebuy writeback.");
        Check(nativeDrops == 3 && nativeAdds == 3 && nativeRemovals == 3, "Round trip bypassed native placement, ship addition or removal.");
        Console.WriteLine("NATIVE GUI ROUNDTRIP " + (repaired ? "REPAIRED " : "VANILLA ") + (clearMarkerControl ? "MARKER-CLEARED CONTROL" : "RETAINED MARKER") +
            ": buy A -> sell -> buy " + (returnToSameShip ? "A" : "B") + "; charged=" + (1000 - tradeMoney[player.strID]) +
            "; destinationSaved=" + originalB.json.aItems.Length + "; throws=" + failed + "; marker=" + item.mapGUIPropMaps.ContainsKey(GUITradeBase.ASYNCIDENTIFIER));
        // Transit calls this native method before rematerializing ships. It does
        // not perform another writeback; destruction is the engine-only stub.
        loader.Unload();
        Check(copies.Count == 0 && !DataHandler.mapCOs.ContainsKey(item.strID), "Native unload retained the failed-delivery proxy item.");
        Check(originalB.json.aItems.Any(entry => entry.strID == item.strID) == expectedSuccess, "Native unload unexpectedly repaired or lost saved destination cargo.");
    }

    static void CheckNativeTradeRoundTrips(bool repaired) {
        var fixture = new Harmony("offline.multi-barter.native-trade-fixture");
        var previousCOs = DataHandler.mapCOs;
        var previousConds = DataHandler.dictConds;
        var previousTriggers = DataHandler.dictCTs;
        var previousSystem = CrewSim.system;
        var previousCrewSim = CrewSim.objInstance;
        var previousInventory = CrewSim.inventoryGUI;
        var previousAudio = AudioManager.am;
        var singleton = AccessTools.Field(typeof(MonoSingleton<AsyncShipLoader>), "_instance");
        var previousLoader = singleton.GetValue(null);
        var lootTrigger = AccessTools.Field(typeof(GUITradeBase), "ctLootSpawnOK");
        var previousLootTrigger = lootTrigger.GetValue(null);
        try {
            DataHandler.mapCOs = new Dictionary<string, CondOwner>();
            DataHandler.dictConds = new Dictionary<string, JsonCond> { ["StatMoney"] = new JsonCond { strNameFriendly = "money" } };
            DataHandler.dictCTs = new Dictionary<string, CondTrigger>();
            var assetPath = Path.Combine(Ostrabus.Development.InstallationPaths.Data, "condtrigs", "condtrigs.json");
            foreach (var json in JArray.Parse(File.ReadAllText(assetPath)).OfType<JObject>()) {
                string name = (string)json["strName"];
                if (name == "Blank" || name == "TIsLootSpawnOK" || name == "TIsDataItem" || name == "TIsStorageFurnitureLoose" ||
                    name == "TIsBarter" || name == "TIsBarterOKLGFixerBuy" || name == "TIsBarterOKLGFixerSell")
                    DataHandler.dictCTs[name] = CondTrigger.CloneFromJSON(json.ToObject<JsonCondTrigger>());
            }
            tradeItemDefinition = JArray.Parse(File.ReadAllText(Path.Combine(Ostrabus.Development.InstallationPaths.Data, "condowners", "condowners.json")))
                .OfType<JObject>().Single(json => (string)json["strName"] == "ItmAlarmO2OffLoose");
            lootTrigger.SetValue(null, DataHandler.GetCondTrigger("TIsLootSpawnOK"));
            CrewSim.objInstance = Empty<CrewSim>();
            CrewSim.objInstance.coDicts = Empty<CODicts>();
            CrewSim.objInstance.coDicts.COMapList = new Dictionary<string, List<CondOwner>>();
            CrewSim.objInstance.coDicts.COAddEvents = new Dictionary<string, AddCOEventHandler>();
            CrewSim.objInstance.coDicts.CORemoveEvents = new Dictionary<string, RemovedCOEventHandler>();
            CrewSim.inventoryGUI = Empty<Ostranauts.Inventory.GUIInventory>();
            AudioManager.am = Empty<AudioManager>();
            tradeGameObject = Empty<GameObject>();
            tradeTransform = Empty<Transform>();

            TradePatch(fixture, typeof(UnityEngine.Object), "op_Implicit", nameof(TradeTruth));
            TradeProperty(fixture, typeof(Component), "gameObject", nameof(TradeGameObject));
            TradeProperty(fixture, typeof(GameObject), "transform", nameof(TradeTransform));
            TradeProperty(fixture, typeof(GameObject), "activeInHierarchy", nameof(TradeInactive));
            TradeProperty(fixture, typeof(GameObject), "layer", nameof(Skip), true);
            TradeProperty(fixture, typeof(Transform), "parent", nameof(TradeParent));
            TradeProperty(fixture, typeof(CondOwner), "Visible", nameof(Skip), true);
            TradeProperty(fixture, typeof(CondOwner), "Pathfinder", nameof(TradePathfinder));
            TradePatch(fixture, typeof(GameObject), "SetActive", nameof(Skip));
            TradePatch(fixture, typeof(Transform), "SetParent", nameof(Skip), new[] { typeof(Transform), typeof(bool) });
            TradePatch(fixture, typeof(LayerMask), "NameToLayer", nameof(TradeLayer));
            TradePatch(fixture, typeof(CondOwner), "ValidateParent", nameof(TradeInactive));
            TradePatch(fixture, typeof(CondOwner), "UpdateGravity", nameof(Skip));
            TradePatch(fixture, typeof(CondOwner), "CheckTrue", nameof(Skip));
            TradePatch(fixture, typeof(CondOwner), "GetCondAmount", nameof(TradeMoneyRead), new[] { typeof(string) });
            TradePatch(fixture, typeof(CondOwner), "AddCondAmount", nameof(TradeMoneyAdd));
            TradePatch(fixture, typeof(CondOwner), "LogMessage", nameof(Skip));
            TradePatch(fixture, typeof(Ship), "GetCOsInZone", nameof(TradeZoneQuery), new[] { typeof(JsonZone), typeof(CondTrigger), typeof(bool), typeof(bool) });
            TradePatch(fixture, typeof(Ship), "UpdateTiles", nameof(TradeInactive));
            TradePatch(fixture, typeof(Ship), "GetTileAtWorldCoords1", nameof(TradeTile));
            TradePatch(fixture, typeof(Ship), "GetWorldCoordsAtTileIndex1", nameof(TradeWorld));
            TradePatch(fixture, typeof(Ship), "RemoveInternalFromRooms", nameof(Skip));
            TradePatch(fixture, typeof(Ship), "LogAdd", nameof(Skip));
            TradePatch(fixture, typeof(Condowner.Utils.Scheduler), "RemoveTicker", nameof(Skip), new[] { typeof(CondOwner) });
            TradePatch(fixture, typeof(CrewSim), "ShowBlocksAndLights", nameof(Skip), new[] { typeof(CondOwner), typeof(bool) });
            TradePatch(fixture, typeof(CrewSim), "UnlockAchievement", nameof(Skip));
            TradePatch(fixture, typeof(Ostranauts.Inventory.GUIInventory), "RemoveAndDestroy", nameof(Skip));
            TradePatch(fixture, typeof(Shader), "PropertyToID", nameof(TradeLayer), new[] { typeof(string) });
            TradePatch(fixture, typeof(Item), "ResetTransforms", nameof(Skip));
            TradePatch(fixture, typeof(Item), "CheckFit", nameof(TradeTrue));
            TradePatch(fixture, AccessTools.TypeByName("Unity.Profiling.LowLevel.Unsafe.ProfilerUnsafeUtility"), "CreateMarker", nameof(TradeMarker),
                new[] { typeof(string), typeof(ushort), AccessTools.TypeByName("Unity.Profiling.LowLevel.MarkerFlags"), typeof(int) });
            TradePatch(fixture, typeof(TMP_Text), "set_text", nameof(Skip));
            TradePatch(fixture, typeof(AudioManager), "PlayAudioEmitter", nameof(Skip));
            TradePatch(fixture, typeof(Ledger), "RecordTransaction", nameof(Skip));
            TradePatch(fixture, typeof(DataHandler), "GetString", nameof(TradeString));
            TradePatch(fixture, typeof(GUITrade), "RebuildList", nameof(Skip));
            TradePatch(fixture, typeof(UnityEvent), "Invoke", nameof(Skip));
            foreach (var method in new[] { AccessTools.Method(typeof(GUITradeBase), "BuyItems"), AccessTools.Method(typeof(TileUtils), "DropCOsNearby") })
                fixture.Patch(method, transpiler: new HarmonyMethod(typeof(Program), nameof(TradeComponentCalls)));
            fixture.Patch(AccessTools.Method(typeof(TileUtils), "DropCOsNearby"), postfix: new HarmonyMethod(typeof(Program), nameof(CountDrop)));
            foreach (var type in new[] { typeof(Ship), typeof(BarterZoneShip) })
                fixture.Patch(AccessTools.Method(type, "AddCO", new[] { typeof(CondOwner), typeof(bool) }), postfix: new HarmonyMethod(typeof(Program), nameof(CountAdd)));
            fixture.Patch(AccessTools.Method(typeof(CondOwner), "RemoveFromCurrentHome"), postfix: new HarmonyMethod(typeof(Program), nameof(CountRemoval)));

            foreach (bool returnToSameShip in new[] { false, true }) {
                NativeTradeRoundTrip(false, returnToSameShip, repaired);
                NativeTradeRoundTrip(true, returnToSameShip, repaired);
            }
        } finally {
            fixture.UnpatchSelf();
            DataHandler.mapCOs = previousCOs;
            DataHandler.dictConds = previousConds;
            DataHandler.dictCTs = previousTriggers;
            CrewSim.system = previousSystem;
            CrewSim.objInstance = previousCrewSim;
            CrewSim.inventoryGUI = previousInventory;
            AudioManager.am = previousAudio;
            singleton.SetValue(null, previousLoader);
            lootTrigger.SetValue(null, previousLootTrigger);
            tradeComponents.Clear();
            tradeMoney.Clear();
        }
    }
}
