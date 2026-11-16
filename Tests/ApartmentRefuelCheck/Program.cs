using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Ostrabus.ApartmentRefuel;
using UnityEngine;

static partial class Program {
    static int checks;
    static string lastString;
    static readonly string Data = Ostrabus.Development.InstallationPaths.Data;
    static string Assets;
    static T Empty<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    static void Check(bool ok, string why) { checks++; if (!ok) throw new Exception(why); }
    static void Field(object obj, string field, object value) => AccessTools.Field(obj.GetType(), field).SetValue(obj, value);
    static bool Equal(UnityEngine.Object __0, UnityEngine.Object __1, ref bool __result) { __result = ReferenceEquals(__0, __1); return false; }
    static bool Unequal(UnityEngine.Object __0, UnityEngine.Object __1, ref bool __result) { __result = !ReferenceEquals(__0, __1); return false; }
    static bool Quiet() => false;
    static bool Shader(ref int __result) { __result = 1; return false; }
    static bool NativeString(string strName, ref string __result) { lastString = strName; __result = strName; return false; }
    static bool ValidateParent() => false;
    static T[] Read<T>(string file) => JsonConvert.DeserializeObject<T[]>(File.ReadAllText(file));
    static void LoadAssets() {
        DataHandler.dictConds = Read<JsonCond>(Data + "conditions/conditions.json").GroupBy(c => c.strName).ToDictionary(g => g.Key, g => g.Last());
        var simple = new Dictionary<string, JsonSimple>();
        foreach (string file in Directory.GetFiles(Data + "conditions_simple", "*.json"))
            foreach (var row in Read<JsonSimple>(file)) simple[row.strName] = row;
        foreach (var row in Read<JsonSimple>(Assets + "/data/conditions_simple/apartment_refuel.json")) simple[row.strName] = row;
        AccessTools.Method(typeof(DataHandler), "ParseConditionsSimple").Invoke(null, new object[] { simple });
        DataHandler.dictCTs = new Dictionary<string, CondTrigger>();
        foreach (string file in Directory.GetFiles(Data + "condtrigs", "*.json", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal))
            foreach (var row in Read<CondTrigger>(file)) DataHandler.dictCTs[row.strName] = row;
        foreach (var row in Read<CondTrigger>(Assets + "/data/condtrigs/apartment_refuel.json")) DataHandler.dictCTs[row.strName] = row;
        foreach (var row in DataHandler.dictCTs.Values) row.PostInit();
        DataHandler.dictCOs = Read<JsonCondOwner>(Data + "condowners/condowners.json").GroupBy(c => c.strName).ToDictionary(g => g.Key, g => g.Last());
        foreach (var co in Read<JsonCondOwner>(Assets + "/data/condowners/apartment_refuel.json")) DataHandler.dictCOs[co.strName] = co;
        DataHandler.dictInteractions = Read<JsonInteraction>(Data + "interactions/interactions.json").GroupBy(c => c.strName).ToDictionary(g => g.Key, g => g.Last());
        foreach (var ia in Read<JsonInteraction>(Assets + "/data/interactions/apartment_refuel.json")) DataHandler.dictInteractions[ia.strName] = ia;
        DataHandler.dictLoot = Read<Loot>(Data + "loot/loot.json").GroupBy(c => c.strName).ToDictionary(g => g.Key, g => g.Last());
        foreach (var loot in Read<Loot>(Assets + "/data/loot/apartment_refuel.json")) DataHandler.dictLoot[loot.strName] = loot;
        DataHandler.dictInstallables2 = new Dictionary<string, JsonInstallable>();
        DataHandler.dictShips = new Dictionary<string, JsonShip>();
        foreach (string name in Eligibility.Templates) DataHandler.dictShips[name] = Read<JsonShip>(Data + "ships/" + name + ".json")[0];
        DataHandler.mapCOs = new Dictionary<string, CondOwner>();
        DataHandler.dictCOOverlays = new Dictionary<string, JsonCOOverlay>();
        DataHandler.dictCondRules = new Dictionary<string, CondRule>();
        DataHandler.dictCondRulesLookup = new Dictionary<string, string>();
        foreach (var job in Read<JsonInstallable>(Assets + "/data/installables/apartment_refuel.json")) Installables.Create(job);
    }

    static CondOwner Owner(string id, string definition = "actor") {
        var co = Empty<CondOwner>(); Field(co, "_strID", id);
        co.strName = id; co.strCODef = definition;
        co.mapConds = new Dictionary<string, Condition>(); Field(co, "mapCondRules", new Dictionary<string, CondRule>());
        co.tf = Empty<Transform>(); DataHandler.mapCOs[id] = co;
        return co;
    }
    static void Value(CondOwner co, string name, double value) {
        if (!co.mapConds.TryGetValue(name, out var cond)) {
            cond = DataHandler.GetCond(name) ?? new Condition(null) { strName = name };
            co.mapConds[name] = cond;
        }
        cond.fCount = value;
    }
    static Ship MakeShip(string id, bool station = false, string template = null) {
        var ship = Empty<Ship>(); ship.strRegID = id; ship.json = new JsonShip { strName = template, aDocked = new Dictionary<string, string>() };
        Field(ship, "<objSS>k__BackingField", new ShipSitu(false) { bIsBO = station });
        Field(ship, "nLoadState", Ship.Loaded.Full); Field(ship, "aDocked", new Dictionary<string, Ship>());
        Field(ship, "aDocksys", new List<CondOwner> { Owner("dock-" + id) });
        Field(ship, "aDockingPorts", new List<string> { "port-" + id });
        CrewSim.system.dictShips[id] = ship;
        return ship;
    }
    static void Link(Ship a, Ship b, string portA = "port-A", string portB = "port-B") {
        ((Dictionary<string, Ship>)AccessTools.Field(typeof(Ship), "aDocked").GetValue(a))[portA] = b;
        ((Dictionary<string, Ship>)AccessTools.Field(typeof(Ship), "aDocked").GetValue(b))[portB] = a;
    }
    static Dictionary<string, string> Owners => (Dictionary<string, string>)AccessTools.Field(typeof(StarSystem), "dictShipOwners").GetValue(CrewSim.system);
    static void World() {
        CrewSim.system = Empty<StarSystem>(); CrewSim.system.dictShips = new Dictionary<string, Ship>();
        Field(CrewSim.system, "dictShipOwners", new Dictionary<string, string>());
        CrewSim.coPlayer = Owner("player"); Value(CrewSim.coPlayer, "StatUSD", 1000000);
        StarSystem.fEpoch = 10000;
    }
    static void AssetChecks() {
        Check(!Runtime.Ready, "Plugin started before its assets");
        var unit = Owner("test", Runtime.Item);
        Value(unit, Runtime.Marker, 1); Value(unit, "IsInstalled", 1);
        Check(!DataHandler.GetCondTrigger(Runtime.Gate).Triggered(unit), "Asset-only install can activate services");
        var heldGate = DataHandler.GetCondTrigger(Runtime.Gate);
        Runtime.Enable();
        Check(Runtime.Ready && DataHandler.GetCondTrigger(Runtime.Gate).Triggered(unit), "Runtime gate not enabled");
        Check(DataHandler.GetCondTrigger("TOstrabusApartmentRefuelWorking").Triggered(unit), "Working installed trigger failed");
        Value(unit, "IsDamaged", 1);
        Check(!DataHandler.GetCondTrigger("TOstrabusApartmentRefuelWorking").Triggered(unit), "Damaged kiosk is usable");
        var jobs = Read<JsonInstallable>(Assets + "/data/installables/apartment_refuel.json");
        foreach (var job in jobs) Check(DataHandler.dictInteractions.ContainsKey("ACT" + job.strName), "Native job failed generation: " + job.strName);
        foreach (string suffix in new[] { "", "Loose", "Dmg", "DmgLoose" }) {
            var definition = DataHandler.dictCOs[Runtime.Item + suffix];
            Check(definition.strLoot == null && !definition.aStartingConds.Any(c => c.StartsWith("IsRefuelKiosk=") || c.StartsWith("IsTraderNPC=") || c.StartsWith("IsIndestructable=") || c.StartsWith("IsCategoryIndustrialProducts=")), "Unsafe inherited kiosk behavior");
            Check(definition.aUpdateCommands.Any(c => c.StartsWith("Destructable,StatDamage,")), "No native damage transition");
            Check(!definition.aInteractions.Contains("ACTKioskUseNPC"), "Private home attracts idle NPCs");
            Check(definition.aStartingConds.Contains("IsOversized=1.0x1"), "Terminal can be packed as a small item");
            var terminal = Defined("handling-" + suffix, Runtime.Item + suffix);
            terminal.mapSlotEffects = new Dictionary<string, JsonSlotEffects>();
            for (int i = 0; i < definition.mapSlotEffects.Length; i += 2)
                terminal.mapSlotEffects[definition.mapSlotEffects[i]] = new JsonSlotEffects { strSlotPrimary = definition.mapSlotEffects[i] };
            foreach (string slotName in new[] { "heldL", "heldR", "back", "drag" }) {
                var slot = Empty<Slot>(); slot.strName = slotName; slot.bHoldSlot = true; slot.aCOs = new CondOwner[1];
                Check(slot.CanFit(terminal, bAuto: false) == (slotName == "drag"), "Native slot fit mishandles terminal in " + slotName);
            }
            foreach (string vendor in new[] { "TIsBarterOKLGFurnishingsKioskSell", "TIsBarterFurnishingsKioskSell" })
                Check(DataHandler.GetCondTrigger(vendor).Triggered(terminal) == suffix.EndsWith("Loose", StringComparison.Ordinal), "Native Furnishings sale filter mishandles terminal state " + suffix);
        }
        foreach (string damage in new[] { "", "Dmg" }) {
            var install = jobs.Single(j => j.strName == "OstrabusApartmentRefuel" + damage + "Install");
            var uninstall = jobs.Single(j => j.strName == "OstrabusApartmentRefuel" + damage + "Uninstall");
            Check(install.aInputs.Length == 1 && install.aInputs[0].EndsWith("=1.0x1"), "Free or multiple-input construction");
            Check(install.aLootCOs.Single() == Runtime.Item + damage && uninstall.aLootCOs.Single() == Runtime.Item + damage + "Loose", "Conversion duplicates or changes state");
        }
        Check(!DataHandler.GetCond("StatBasePrice").bPersists, "Installation carries loose retail valuation into the apartment");
        Runtime.Disable();
        Check(!Runtime.Ready && !DataHandler.GetCondTrigger(Runtime.Gate).Triggered(unit), "Plugin removal leaves services active");
        Runtime.Enable();
        for (int i = 0; i < 16; i++) {
            Runtime.Disable(); Check(!heldGate.Triggered(unit), "Already cached gate stayed enabled");
            Runtime.Enable(); Check(heldGate.Triggered(unit), "Already cached gate stayed disabled");
        }
    }
    static void PolicyChecks() {
        var host = MakeShip("OKLG", true); host.objSS.bIsRegion = true;
        var home = MakeShip("OKLG|RES_1", true, "ResOKLG01"); Owners[home.strRegID] = "player";
        var vessel = MakeShip("O-TEST"); Link(home, vessel);
        Check(Eligibility.Apartment(home, out var actual) && actual == host, "Owned residence rejected");
        home.json.strName = home.strRegID; home.json.strTemplateName = null; home.strTemplateName = null;
        Check(Eligibility.Apartment(home, out actual) && actual == host, "Existing saved residence without template metadata rejected (live BCER reproduction)");
        home.strTemplateName = "ResOKLG01";
        Check(Eligibility.Apartment(home, out _), "Saved residence with runtime template metadata rejected");
        home.strTemplateName = "PirateShip";
        Check(!Eligibility.Apartment(home, out _), "Explicit nonresidential template treated as a residence"); home.strTemplateName = null;
        home.json.strTemplateName = "ResOKLG01";
        Check(Eligibility.Apartment(home, out _), "Saved residence template metadata rejected"); home.json.strTemplateName = null;
        home.objSS.bIsBO = false;
        Check(!Eligibility.Apartment(home, out _), "Saved-name identity bypasses mobile hull exclusion"); home.objSS.bIsBO = true;
        Check(Eligibility.Vessel(home, vessel), "Directly docked vessel rejected");
        var other = MakeShip("O-TOWED"); Link(vessel, other, "tow-A", "tow-B");
        Check(!Eligibility.Vessel(home, other), "Tow-chain ship admitted");
        Check(!Eligibility.Vessel(home, host) && !Eligibility.Vessel(home, home), "Station offered as refuel target");
        var fake = MakeShip("OKLG|RES_9", true, "PirateShip"); Owners[fake.strRegID] = "player";
        Check(!Eligibility.Apartment(fake, out _), "Forged residential callsign admitted");
        Owners[home.strRegID] = "npc"; Check(!Eligibility.Apartment(home, out _), "Unowned apartment admitted"); Owners[home.strRegID] = "player";
        home.objSS.bIsBO = false; Check(!Eligibility.Apartment(home, out _), "Mobile hull admitted"); home.objSS.bIsBO = true;
        var tethered = MakeShip("O-TETHER"); Link(home, tethered, "MP|A", "MP|B");
        Check(!Eligibility.Vessel(home, tethered), "Tether counts as docking clamp");
        ((Dictionary<string, Ship>)AccessTools.Field(typeof(Ship), "aDocked").GetValue(vessel)).Clear();
        Check(!Eligibility.Vessel(home, vessel), "Broken reciprocal link admitted"); Link(home, vessel);
        Field(vessel, "nLoadState", Ship.Loaded.Shallow); Check(!Eligibility.Vessel(home, vessel), "Shallow hull sent into physical refuel path"); Field(vessel, "nLoadState", Ship.Loaded.Full);
        Check(Eligibility.Provider(home) == host, "Wrong port authority");
        var kiosk = Owner("terminal", Runtime.Item); kiosk.ship = home; Value(kiosk, "IsInstalled", 1);
        var action = Empty<Interaction>(); action.strName = Runtime.Use;
        Check(Eligibility.Action(action, CrewSim.coPlayer, kiosk), "Legal Use rejected");
        Value(kiosk, "IsInstalled", 0); Check(!Eligibility.Action(action, CrewSim.coPlayer, kiosk), "Loose terminal use admitted"); Value(kiosk, "IsInstalled", 1);
        Check(!Eligibility.Action(action, Owner("npc"), kiosk), "NPC can spend through private kiosk");
        var placeholder = Owner("placeholder"); placeholder.ship = vessel; placeholder.strPlaceholderInstallFinish = Runtime.Item;
        action.strName = "ACTOstrabusApartmentRefuelInstall";
        Check(!Eligibility.Action(action, CrewSim.coPlayer, placeholder), "Shipboard placeholder admitted");
        placeholder.ship = home; Check(Eligibility.Action(action, CrewSim.coPlayer, placeholder), "Apartment placeholder rejected");
        action.strName = "ACTOstrabusApartmentRefuelUninstall"; kiosk.ship = vessel;
        Check(Eligibility.Action(action, CrewSim.coPlayer, kiosk), "Invalid placement cannot be uninstalled");
    }
    static int Main() {
        string root = Path.GetFullPath(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(root, "Ostrabus.csproj"))) root = Directory.GetParent(root).FullName;
        Assets = Path.Combine(root, "StandaloneMods/ApartmentRefuel/Assets");
        var environment = new Harmony("offline.apartment-refuel.engine");
        var production = new Harmony("offline.apartment-refuel.production");
        try {
            environment.Patch(AccessTools.Method(typeof(UnityEngine.Shader), nameof(UnityEngine.Shader.PropertyToID), new[] { typeof(string) }), prefix: new HarmonyMethod(typeof(Program), nameof(Shader)));
            environment.Patch(AccessTools.Method(typeof(UnityEngine.Object), "op_Equality"), prefix: new HarmonyMethod(typeof(Program), nameof(Equal)));
            environment.Patch(AccessTools.Method(typeof(UnityEngine.Object), "op_Inequality"), prefix: new HarmonyMethod(typeof(Program), nameof(Unequal)));
            environment.Patch(AccessTools.Method(typeof(CondOwner), nameof(CondOwner.ValidateParent)), prefix: new HarmonyMethod(typeof(Program), nameof(ValidateParent)));
            environment.Patch(AccessTools.Method(typeof(DataHandler), nameof(DataHandler.GetString)), prefix: new HarmonyMethod(typeof(Program), nameof(NativeString)));
            LoadAssets(); World(); AssetChecks(); PolicyChecks();
            foreach (var type in typeof(Plugin).Assembly.GetTypes().Where(t => t.GetCustomAttributes(typeof(HarmonyPatch), false).Length > 0)) {
                var targets = production.CreateClassProcessor(type).Patch();
                Check(targets.Count == 1, "Unexpected patch count: " + type.Name);
                Console.WriteLine("BOUND " + type.Name);
            }
            ServiceChecks(environment, production);
            PlacementChecks();
            ConversionChecks();
            Console.WriteLine(checks + " apartment refuel checks passed.");
            return 0;
        } finally { production.UnpatchSelf(); environment.UnpatchSelf(); }
    }
}
