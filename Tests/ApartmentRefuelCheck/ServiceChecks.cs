using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Ostrabus.ApartmentRefuel;
using TMPro;
using UnityEngine;

static partial class Program {
    static readonly Dictionary<Ship, Dictionary<string, List<CondOwner>>> equipment = new Dictionary<Ship, Dictionary<string, List<CondOwner>>>();
    static readonly Dictionary<Vector2, List<CondOwner>> at = new Dictionary<Vector2, List<CondOwner>>();
    static readonly Dictionary<CondOwner, Vector2> ports = new Dictionary<CondOwner, Vector2>();
    static readonly Dictionary<Component, Dictionary<Type, Component>> components = new Dictionary<Component, Dictionary<Type, Component>>();
    static readonly Dictionary<Component, GameObject> gameObjects = new Dictionary<Component, GameObject>();
    static readonly Dictionary<GameObject, RefuelQuote> quotes = new Dictionary<GameObject, RefuelQuote>();
    static readonly Dictionary<CondOwner, Interaction> interactions = new Dictionary<CondOwner, Interaction>();
    static readonly Dictionary<GasContainer, CondOwner> gases = new Dictionary<GasContainer, CondOwner>();
    static readonly Dictionary<Vector2, Tile> floorTiles = new Dictionary<Vector2, Tile>();
    static int nextItem, transfers;

    static bool ObjectFor(Component __instance, ref GameObject __result) { __result = gameObjects[__instance]; return false; }
    static bool ComponentFor(Component __instance, Type __0, ref Component __result) {
        __result = null;
        if (__0 == typeof(RefuelQuote) && gameObjects.TryGetValue(__instance, out var go) && quotes.TryGetValue(go, out var quote)) __result = quote;
        else if (components.TryGetValue(__instance, out var map)) map.TryGetValue(__0, out __result);
        return false;
    }
    static bool AddComponent(GameObject __instance, Type componentType, ref Component __result) {
        if (componentType != typeof(RefuelQuote)) throw new Exception("Unexpected component creation: " + componentType);
        var quote = Empty<RefuelQuote>(); quotes[__instance] = quote; __result = quote; return false;
    }
    static bool Current(CondOwner __instance, ref Interaction __result) { interactions.TryGetValue(__instance, out __result); return false; }
    static bool QueryItems(Ship __instance, CondTrigger objCondTrig, ref List<CondOwner> __result) {
        __result = equipment.TryGetValue(__instance, out var map) && map.TryGetValue(objCondTrig.strName, out var items) ?
            new List<CondOwner>(items) : new List<CondOwner>();
        return false;
    }
    static bool AtItems(Vector2 vPos, List<CondOwner> aOut) { if (at.TryGetValue(vPos, out var items)) aOut.AddRange(items); return false; }
    static bool Port(CondOwner __instance, ref Vector2 __result) { __result = ports[__instance]; return false; }
    static bool Add(CondOwner __instance, string strName, double fAmount) {
        Value(__instance, strName, __instance.GetCondAmount(strName) + fAmount); return false;
    }
    static bool RowUnits(GUIStationRow __instance, string strService, float fPrice, float fInv, float fPurchased, float fOnBoard, float fMax, float fTotal) {
        __instance.strService = strService; __instance.fPrice = fPrice; __instance.fInv = fInv; __instance.fPurchased = fPurchased;
        __instance.fOnBoard = fOnBoard; __instance.fMax = fMax; __instance.fTotal = fTotal; return false;
    }
    static bool RowFixed(GUIStationRow __instance, int nType, string strService, float fTotal, float fPrice) {
        __instance.nType = nType; __instance.strService = strService; __instance.fTotal = fTotal; __instance.fPrice = fPrice; return false;
    }
    static bool GasRun(GasContainer __instance) {
        // Thermodynamics/scene boundary. Native AddGasMols and the native GUI's
        // kg-to-moles/price/tank-capacity calculations remain in execution.
        var owner = gases[__instance];
        foreach (var change in __instance.mapDGasMols.ToArray()) {
            Value(owner, "StatGasPressure", owner.GetCondAmount("StatGasPressure") + change.Value * .008314000442624092 * owner.GetCondAmount("StatGasTemp") / owner.GetCondAmount("StatVolume"));
            __instance.mapDGasMols[change.Key] = 0;
            transfers++;
        }
        return false;
    }
    static Powered Power(CondOwner item) => components.TryGetValue(item, out var map) && map.TryGetValue(typeof(Powered), out var power) ? (Powered)power : null;
    static IEnumerable<CodeInstruction> PowerBoundary(IEnumerable<CodeInstruction> instructions) {
        foreach (var code in instructions) {
            if (code.operand is MethodInfo method && method.IsGenericMethod && method.Name == "GetComponent" && method.GetGenericArguments()[0] == typeof(Powered)) {
                code.opcode = OpCodes.Call; code.operand = AccessTools.Method(typeof(Program), nameof(Power));
            }
            yield return code;
        }
    }
    static bool TileAt(float fX, float fY, ref Tile __result) {
        floorTiles.TryGetValue(new Vector2((float)Math.Floor(fX), (float)Math.Floor(fY)), out __result); return false;
    }
    static bool Realtime(ref float __result) { __result = 0; return false; }
    static bool Marker(ref IntPtr __result) { __result = IntPtr.Zero; return false; }
    static bool NoSelection(ref CondOwner __result) { __result = null; return false; }
    static bool Disconnected(ref bool __result) { __result = false; return false; }

    static void SceneBoundaries(Harmony h) {
        void Prefix(Type type, string method, string callback, Type[] args = null) => h.Patch(AccessTools.Method(type, method, args), prefix: new HarmonyMethod(typeof(Program), callback));
        Prefix(AccessTools.TypeByName("Unity.Profiling.LowLevel.Unsafe.ProfilerUnsafeUtility"), "CreateMarker", nameof(Marker),
            new[] { typeof(string), typeof(ushort), AccessTools.TypeByName("Unity.Profiling.LowLevel.MarkerFlags"), typeof(int) });
        Prefix(typeof(Component), "get_gameObject", nameof(ObjectFor));
        Prefix(typeof(Component), "GetComponent", nameof(ComponentFor), new[] { typeof(Type) });
        Prefix(typeof(GameObject), "AddComponent", nameof(AddComponent), new[] { typeof(Type) });
        Prefix(typeof(CondOwner), nameof(CondOwner.GetInteractionCurrent), nameof(Current));
        Prefix(typeof(CondOwner), nameof(CondOwner.AddCondAmount), nameof(Add));
        Prefix(typeof(CondOwner), nameof(CondOwner.GetPos), nameof(Port));
        Prefix(typeof(Ship), nameof(Ship.GetICOs1), nameof(QueryItems));
        Prefix(typeof(Ship), nameof(Ship.GetCOsAtWorldCoords1), nameof(AtItems));
        Prefix(typeof(Ship), nameof(Ship.GetTileAtWorldCoords1), nameof(TileAt));
        Prefix(typeof(TileUtils), nameof(TileUtils.WouldConnectShips), nameof(Disconnected));
        Prefix(typeof(Time), "get_realtimeSinceStartup", nameof(Realtime));
        Prefix(typeof(GUIStationRow), nameof(GUIStationRow.SetColor), nameof(Quiet));
        Prefix(typeof(GUIStationRow), nameof(GUIStationRow.Init), nameof(RowUnits), new[] { typeof(string), typeof(float), typeof(float), typeof(float), typeof(float), typeof(float), typeof(float), typeof(Action) });
        Prefix(typeof(GUIStationRow), nameof(GUIStationRow.Init), nameof(RowFixed), new[] { typeof(int), typeof(string), typeof(float), typeof(float) });
        Prefix(typeof(TMP_Text), "set_text", nameof(Quiet));
        Prefix(typeof(AudioManager), nameof(AudioManager.PlayAudioEmitter), nameof(Quiet));
        Prefix(typeof(Ledger), nameof(Ledger.GetMonthCash), nameof(Quiet));
        Prefix(typeof(GasContainer), nameof(GasContainer.Run), nameof(GasRun));
        foreach (string name in new[] { "SetupFields", "OnSubmit" })
            h.Patch(AccessTools.Method(typeof(GUIStationRefuel), name), transpiler: new HarmonyMethod(typeof(Program), nameof(PowerBoundary)));
        AudioManager.am = Empty<AudioManager>();
    }

    static CondOwner Equipment(Ship ship, string trigger, params (string Name, double Amount)[] values) {
        var item = Owner("equipment-" + ++nextItem, trigger); item.ship = ship; Value(item, "IsInstalled", 1);
        foreach (var value in values) Value(item, value.Name, value.Amount);
        if (!equipment.TryGetValue(ship, out var map)) equipment[ship] = map = new Dictionary<string, List<CondOwner>>();
        if (!map.TryGetValue(trigger, out var list)) map[trigger] = list = new List<CondOwner>();
        list.Add(item); return item;
    }
    static GasContainer Gas(CondOwner item) {
        var gas = Empty<GasContainer>(); Field(gas, "co", item); gas.mapDGasMols = new Dictionary<string, double>();
        gas.mapGasMols1 = new Dictionary<string, double>(); gases[gas] = item;
        Field(item, "_gasContainerComponentReference", gas);
        Field(gas, "fGasMass", 1.0);
        return gas;
    }
    static CondOwner Can(Ship ship, string trigger) {
        var can = Equipment(ship, trigger, ("StatVolume", 10), ("StatGasPressureMax", 100), ("StatGasPressure", 20), ("StatGasTemp", 293));
        Gas(can); return can;
    }
    static GUIStationRefuel Panel(Ship home, CondOwner user, bool privateKiosk = true) {
        var kiosk = Owner("kiosk-" + ++nextItem, privateKiosk ? Runtime.Item : "ItmKiosk01"); kiosk.ship = home; Value(kiosk, "IsInstalled", 1);
        interactions[kiosk] = Empty<Interaction>(); interactions[kiosk].objThem = user;
        var panel = Empty<GUIStationRefuel>(); Field(panel, "coSelfTemp", kiosk);
        gameObjects[panel] = Empty<GameObject>();
        foreach (string field in new[] { "rowFuelHe3", "rowFuelH2", "rowFuelRCS", "rowFuelABL", "rowFuelConnect", "rowFuelTotal", "rowLifeO2", "rowLifeN2", "rowLifeBlack", "rowLifeExchange", "rowLifeTotal", "rowPower", "rowPowerHookup", "rowPowerTotal", "rowDock", "rowDockTow", "rowDockTotal", "rowFines", "rowFinesTotal" })
            Field(panel, field, Empty<GUIStationRow>());
        foreach (string field in new[] { "txtStationName", "txtNameSub", "txtTotal", "txtMessage" }) Field(panel, field, Empty<TextMeshProUGUI>());
        return panel;
    }
    static GUIStationRow Row(GUIStationRefuel panel, string name) => (GUIStationRow)AccessTools.Field(typeof(GUIStationRefuel), name).GetValue(panel);
    static void Setup(GUIStationRefuel panel, Ship target) {
        try { AccessTools.Method(typeof(GUIStationRefuel), "SetupFields").Invoke(panel, new object[] { target }); }
        catch (TargetInvocationException e) { throw new Exception("Native setup failed near " + lastString, e.InnerException); }
    }
    static void SubmitOrder(GUIStationRefuel panel) => AccessTools.Method(typeof(GUIStationRefuel), "OnSubmit").Invoke(panel, null);
    static void Order(GUIStationRefuel panel, string row, float amount) {
        var r = Row(panel, row); r.fPurchased = amount; r.fTotal = amount * r.fPrice;
    }

    static void ServiceChecks(Harmony environment, Harmony production) {
        World(); SceneBoundaries(environment); Ledger.Init(null); GUIStationRefuel.SetPrices();
        var host = MakeShip("VORB", true); var authority = MakeShip("VNCA", true); authority.objSS.bIsRegion = true;
        var wrongRegion = MakeShip("WRONG", true); wrongRegion.objSS.bIsRegion = true; wrongRegion.objSS.vPosx = 10;
        AIShipManager.strATCLast = wrongRegion.strRegID;
        var home = MakeShip("VORB|RES_1", true, "ResAero01"); Owners[home.strRegID] = "player";
        var target = MakeShip("O-REFUEL"); var foreign = MakeShip("O-NOT-ELIGIBLE"); Link(home, target); Link(target, foreign, "tow-port", "wreck-port");
        var he = Equipment(target, "TIsCanisterLHe02Installed", ("StatVolume", 1), ("StatSolidHe3", 10));
        var water = Equipment(target, "TIsCanisterLH02Installed", ("StatVolume", 1), ("StatLiqD2O", 100));
        var reactor = Equipment(target, "TIsFusionReactorCore01Installed", ("StatICABLWall", 50));
        var oxygen = Can(target, "TIsRTAO2Installed");
        var life = Can(target, "TIsRTAN2Installed"); var badLife = Can(foreign, "TIsRTAN2Installed");
        var rcs = Can(target, "rcs"); var badRcs = Can(foreign, "rcs");
        var pump = Equipment(target, "TIsAirPump02Installed"); pump.mapPoints = new Dictionary<string, Vector2> { ["GasInput"] = new Vector2(20, 20) }; ports[pump] = new Vector2(20, 20);
        var secondPump = Equipment(target, "TIsAirPump02Installed"); secondPump.mapPoints = pump.mapPoints; ports[secondPump] = new Vector2(20, 20);
        var distro = Equipment(target, "distro"); distro.mapPoints = new Dictionary<string, Vector2> { ["GasInput"] = new Vector2(10, 10) }; ports[distro] = new Vector2(10, 10);
        Field(target, "aRCSDistros", new List<CondOwner> { distro }); Field(target, "ctRCSGasInput", DataHandler.GetCondTrigger("TIsRTAN2Installed"));
        at[new Vector2(10, 10)] = new List<CondOwner> { rcs, rcs, badRcs };
        at[new Vector2(20, 20)] = new List<CondOwner> { life, badLife };
        var battery = Equipment(target, "TIsShipBatteryInstalled", ("StatPower", 10));
        var power = Empty<Powered>(); Field(power, "fMaxStored", 100.0);
        components[battery] = new Dictionary<Type, Component> { [typeof(Powered)] = power };
        var publicPanel = Panel(home, CrewSim.coPlayer, false);
        Setup(publicPanel, target);
        Check(Row(publicPanel, "rowFuelRCS").fMax > RefuelAdapter.GasCapacity(rcs, .028013398870825768) * 2.5, "Native cross-hull/alias RCS preview not reproduced");
        Check(Row(publicPanel, "rowLifeN2").fMax > RefuelAdapter.GasCapacity(life, .028013398870825768) * 3.5, "Native shared-pump N2 capacity duplication not reproduced");
        var panel = Panel(home, CrewSim.coPlayer);
        Setup(panel, target);
        Check(Math.Abs(Row(panel, "rowFuelRCS").fMax - RefuelAdapter.GasCapacity(rcs, .028013398870825768)) < .001, "Private RCS preview includes foreign/duplicate tank");
        Check(Math.Abs(Row(panel, "rowLifeN2").fMax - RefuelAdapter.GasCapacity(life, .028013398870825768)) < .001, "Private N2 preview includes foreign tank");
        Check(RefuelAdapter.ProviderId(null, panel) == authority.strRegID, "Billing used stale ATC or residence instead of actual regional authority");
        Check(RefuelAdapter.Vessels(home, panel).SequenceEqual(new[] { target }), "Indirect target admitted into private list");
        double beforeMoney = CrewSim.coPlayer.GetCondAmount("StatUSD");
        float expected = 0;
        foreach (string row in new[] { "rowFuelHe3", "rowFuelH2", "rowFuelRCS", "rowFuelABL", "rowLifeO2", "rowLifeN2", "rowPower" }) { Order(panel, row, 1); expected += Row(panel, row).fTotal; }
        SubmitOrder(panel);
        Check(Math.Abs(CrewSim.coPlayer.GetCondAmount("StatUSD") - (beforeMoney - expected)) < .01, "Native transaction charged wrong amount");
        Check(he.GetCondAmount("StatSolidHe3") == 11 && water.GetCondAmount("StatLiqD2O") == 101, "Fusion fuel not delivered natively");
        Check(reactor.GetCondAmount("StatICABLWall") == 49 && battery.GetCondAmount("StatPower") == 11, "Native ablator/battery service failed");
        Check(rcs.GetCondAmount("StatGasPressure") > 20 && life.GetCondAmount("StatGasPressure") > 20 && oxygen.GetCondAmount("StatGasPressure") > 20 && transfers == 3, "Native gas purchase did not reach three eligible tanks");
        Check(badRcs.GetCondAmount("StatGasPressure") == 20 && badLife.GetCondAmount("StatGasPressure") == 20, "Fuel crossed into ineligible hull");
        var paid = (Dictionary<string, List<LedgerLI>>)AccessTools.Field(typeof(Ledger), "dictPayees").GetValue(null);
        Check(paid.Keys.Single() == authority.strRegID + "GUI_REFUEL_PORT_SUFFIX" && paid.Values.Single().All(li => li.Paid && li.strObjId == target.strRegID), "Ledger provider/target/payment wrong");
        double charged = CrewSim.coPlayer.GetCondAmount("StatUSD"); SubmitOrder(panel);
        Check(CrewSim.coPlayer.GetCondAmount("StatUSD") == charged && he.GetCondAmount("StatSolidHe3") == 11, "Double click repeated previous order");
        Order(panel, "rowFuelHe3", 1); Value(CrewSim.coPlayer, "StatUSD", 0); SubmitOrder(panel);
        Check(he.GetCondAmount("StatSolidHe3") == 11, "Insufficient funds still deliver fuel"); Value(CrewSim.coPlayer, "StatUSD", charged);
        Setup(panel, target); Order(panel, "rowFuelHe3", 1); Value(he, "StatSolidHe3", 20); SubmitOrder(panel);
        Check(he.GetCondAmount("StatSolidHe3") == 20 && CrewSim.coPlayer.GetCondAmount("StatUSD") == charged && Row(panel, "rowFuelHe3").fPurchased == 0, "Stale tank quote charged/delivered or failed to refresh");
        foreach (string failure in new[] { "undocked", "owner", "removed", "damaged", "nan", "negative" }) {
            Owners[home.strRegID] = "player"; panel.COSelf.ship = home; Value(panel.COSelf, "IsInstalled", 1); Value(panel.COSelf, "IsDamaged", 0); Link(home, target);
            Setup(panel, target); Order(panel, "rowFuelHe3", 1);
            if (failure == "undocked") ((Dictionary<string, Ship>)AccessTools.Field(typeof(Ship), "aDocked").GetValue(target)).Clear();
            if (failure == "owner") Owners[home.strRegID] = "npc";
            if (failure == "removed") Value(panel.COSelf, "IsInstalled", 0);
            if (failure == "damaged") Value(panel.COSelf, "IsDamaged", 1);
            if (failure == "nan") Row(panel, "rowFuelHe3").fPurchased = float.NaN;
            if (failure == "negative") Order(panel, "rowFuelHe3", -1);
            SubmitOrder(panel);
            Check(CrewSim.coPlayer.GetCondAmount("StatUSD") == charged && he.GetCondAmount("StatSolidHe3") == 20, "Rejected " + failure + " mutates funds/fuel");
        }
        Owners[home.strRegID] = "player";
        Value(panel.COSelf, "IsInstalled", 1); Value(panel.COSelf, "IsDamaged", 0); Link(home, target);
        string portPayee = authority.strRegID + "GUI_REFUEL_PORT_SUFFIX";
        var connection = new LedgerLI(portPayee, CrewSim.coPlayer.strID, 28.8f, "GUI_REFUEL_SERVICE_FUEL_CONNECT", target.strRegID);
        var shared = new LedgerLI(portPayee, CrewSim.coPlayer.strID, 5.5f, "GUI_REFUEL_SERVICE_POWER_HOOKUP", null);
        var otherShipDebt = new LedgerLI(portPayee, CrewSim.coPlayer.strID, 999f, "GUI_REFUEL_SERVICE_FUEL_CONNECT", foreign.strRegID);
        var otherPortDebt = new LedgerLI("WRONGGUI_REFUEL_PORT_SUFFIX", CrewSim.coPlayer.strID, 777f, "GUI_REFUEL_SERVICE_FUEL_CONNECT", target.strRegID);
        foreach (var bill in new[] { connection, shared, otherShipDebt, otherPortDebt }) Ledger.AddLI(bill);
        Setup(panel, target);
        Check(Math.Abs(Row(panel, "rowFuelConnect").fTotal - 28.8f) < .001 && Math.Abs(Row(panel, "rowPowerHookup").fTotal - 5.5f) < .001, "Quote hides native station debt or mixes vessel/port debt");
        double beforeDebt = CrewSim.coPlayer.GetCondAmount("StatUSD");
        SubmitOrder(panel);
        Check(connection.Paid && shared.Paid && !otherShipDebt.Paid && !otherPortDebt.Paid, "Private services do not preserve native selected-vessel/shared fee rules");
        Check(Math.Abs(CrewSim.coPlayer.GetCondAmount("StatUSD") - beforeDebt + 34.3) < .01, "Native obligations charged incorrectly");
        Setup(panel, target); Order(panel, "rowFuelHe3", 1);
        var lateBill = new LedgerLI(portPayee, CrewSim.coPlayer.strID, 100f, "GUI_REFUEL_SERVICE_DOCK", target.strRegID); Ledger.AddLI(lateBill);
        double beforeLate = CrewSim.coPlayer.GetCondAmount("StatUSD"); SubmitOrder(panel);
        Check(!lateBill.Paid && CrewSim.coPlayer.GetCondAmount("StatUSD") == beforeLate && he.GetCondAmount("StatSolidHe3") == 20, "Changed obligations bypass quote review");
        Check(Row(panel, "rowFuelHe3").fPurchased == 0 && Math.Abs(Row(panel, "rowDock").fTotal - 100) < .001, "Stale obligation rejection did not reset quote");
        Ledger.PayLI(lateBill); Setup(panel, target);
        double beforeDuplicate = CrewSim.coPlayer.GetCondAmount("StatUSD");
        Order(panel, "rowFuelHe3", 1); SubmitOrder(panel);
        Check(he.GetCondAmount("StatSolidHe3") == 20 && CrewSim.coPlayer.GetCondAmount("StatUSD") == beforeDuplicate, "Same-time repeated charge bypassed native paid-ledger dedup guard");
        StarSystem.fEpoch += 1; Setup(panel, target);
        double beforeRounding = CrewSim.coPlayer.GetCondAmount("StatUSD");
        Order(panel, "rowFuelH2", -.00001f); Order(panel, "rowFuelHe3", 1); SubmitOrder(panel);
        Check(he.GetCondAmount("StatSolidHe3") == 21 && water.GetCondAmount("StatLiqD2O") == 101 && Math.Abs(CrewSim.coPlayer.GetCondAmount("StatUSD") - beforeRounding + GUIStationRefuel.dictPrices["rowFuelHe3"]) < .001, "Native slider lower-stop roundoff blocks transaction or produces a refund: He3=" + he.GetCondAmount("StatSolidHe3") + " water=" + water.GetCondAmount("StatLiqD2O") + " debit=" + (beforeRounding - CrewSim.coPlayer.GetCondAmount("StatUSD")));
        Setup(publicPanel, target); double beforePublic = CrewSim.coPlayer.GetCondAmount("StatUSD");
        Order(publicPanel, "rowFuelHe3", 1); SubmitOrder(publicPanel);
        double afterPublic = CrewSim.coPlayer.GetCondAmount("StatUSD");
        Check(he.GetCondAmount("StatSolidHe3") == 22 && Math.Abs(beforePublic - afterPublic - GUIStationRefuel.dictPrices["rowFuelHe3"]) < .001, "Public paid-service control changed");
        Order(publicPanel, "rowFuelHe3", 1); SubmitOrder(publicPanel);
        Check(he.GetCondAmount("StatSolidHe3") == 23 && CrewSim.coPlayer.GetCondAmount("StatUSD") == afterPublic, "Native paid-ledger dedup free-refill defect not reproduced in public control");
    }
}
