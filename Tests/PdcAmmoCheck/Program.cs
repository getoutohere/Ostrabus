using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using Ostrabus.Tests;
using UnityEngine;

static class Program {
    sealed class RefComparer<T> : IEqualityComparer<T> where T : class {
        public bool Equals(T a, T b) => ReferenceEquals(a, b);
        public int GetHashCode(T value) => RuntimeHelpers.GetHashCode(value);
    }
    static readonly Dictionary<Component, GameObject> objects = new Dictionary<Component, GameObject>(new RefComparer<Component>());
    static readonly Dictionary<GameObject, Item> items = new Dictionary<GameObject, Item>(new RefComparer<GameObject>());
    static readonly Dictionary<CondOwner, List<CondOwner>> contents = new Dictionary<CondOwner, List<CondOwner>>(new RefComparer<CondOwner>());
    static int checks, nextId, ammoCreated;
    static bool lastLoot;
    static T Empty<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    static void Check(bool ok, string message) { checks++; if (!ok) throw new Exception(message); }
    static void Field(object obj, string name, object value) => AccessTools.Field(obj.GetType(), name).SetValue(obj, value);
    static bool Equal(UnityEngine.Object __0, UnityEngine.Object __1, ref bool __result) { __result = ReferenceEquals(__0, __1); return false; }
    static bool Unequal(UnityEngine.Object __0, UnityEngine.Object __1, ref bool __result) { __result = !ReferenceEquals(__0, __1); return false; }
    static bool Quiet() => false;
    static bool Shader(ref int __result) { __result = 1; return false; }
    static bool ID(ref string __result) { __result = "fixture-" + ++nextId; return false; }
    static bool Mesh(ref GameObject __result) { __result = Empty<GameObject>(); return false; }
    static bool ObjectOf(Component __instance, ref GameObject __result) { __result = objects[__instance]; return false; }
    static bool ItemOf(CondOwner __instance, ref Item __result) { __result = items[objects[__instance]]; return false; }
    static bool SetId(CondOwner __instance, string value) { Field(__instance, "_strID", value); DataHandler.mapCOs[value] = __instance; return false; }
    static bool AddComponent(GameObject __instance, Type componentType, ref Component __result) {
        if (componentType == typeof(Item)) {
            var item = Empty<Item>(); items[__instance] = item; objects[item] = __instance; __result = item;
        } else if (componentType == typeof(CondOwner)) {
            var co = Empty<CondOwner>(); objects[co] = __instance; __result = co;
        } else throw new Exception("Unexpected component " + componentType);
        return false;
    }
    static bool Roll(float min, float max, ref float __result) { __result = (min + max) * .5f; return false; }
    static bool RollDouble(double min, double max, ref double __result) { __result = (min + max) * .5; return false; }
    // Engine-heavy SetData leaf. Preserve its exact bLoot && no-save gate and
    // execute the real loot parser/roll/factory recursively for starting rounds.
    static bool SetData(CondOwner __instance, JsonCondOwner jid, bool bLoot, JsonCondOwnerSave jCOSIn) {
        __instance.strName = jid.strName; __instance.strCODef = jid.strName;
        __instance.mapConds = new Dictionary<string, Condition>();
        if (jid.aStartingConds != null) foreach (string cond in jid.aStartingConds) {
            string name = cond.Split('=')[0]; __instance.mapConds[name] = new Condition(null) { strName = name, fCount = 1 };
        }
        contents[__instance] = new List<CondOwner>();
        if (jid.strName == "ItmAmmo20mm") ammoCreated++;
        if (bLoot && jCOSIn == null && jid.strLoot != null) contents[__instance].AddRange(DataHandler.GetLoot(jid.strLoot).GetCOLoot(__instance, false));
        if (jid.strName.StartsWith("ItmShipWeaponPDC", StringComparison.Ordinal)) lastLoot = bLoot;
        return false;
    }
    static CondOwner Create(string name, string oldId = null, bool loot = true, JsonCondOwnerSave save = null) =>
        DataHandler.GetCondOwner(name, null, null, loot, null, save, oldId);
    static void Prefix(Harmony h, Type type, string method, string prefix, Type[] args = null) => h.Patch(AccessTools.Method(type, method, args), prefix: new HarmonyMethod(typeof(Program), prefix));
    static JArray Assets(string relative) => JArray.Parse(File.ReadAllText(Ostrabus.Development.InstallationPaths.Data + relative));
    static void NativeRecipes(string root, bool repaired) {
        DataHandler.dictInteractions=JArray.Parse(File.ReadAllText(Path.Combine(root,"interactions/interactions.json")))
            .ToObject<JsonInteraction[]>().GroupBy(j=>j.strName).ToDictionary(g=>g.Key,g=>g.Last());
        DataHandler.dictInstallables2=new Dictionary<string,JsonInstallable>();
        foreach(var file in new[]{"loot/loot_self_reference.json","loot/loot_components.json"})
            foreach(var row in JArray.Parse(File.ReadAllText(Path.Combine(root,file))).ToObject<Loot[]>())
                DataHandler.dictLoot[row.strName]=row;
        int recipes=0;
        foreach(var file in new[]{"installables/installables.json","installables/installables_repair.json"})
        foreach(var job in JArray.Parse(File.ReadAllText(Path.Combine(root,file))).ToObject<JsonInstallable[]>()
            .Where(j=>j.strName.StartsWith("ShipWeaponPDC",StringComparison.Ordinal))) {
            Installables.Create(job);
            string finish=DataHandler.dictInteractions["MS"+job.strName].objLootModeSwitch;
            var output=DataHandler.GetLoot(finish);
            Check(output!=null && output.bSuppress==repaired,"Native recipe suppression differs: "+job.strName);
            var old=Create("ItmShipWeaponPDC01Loose",loot:false);
            var retained=Create("ItmAmmo20mm"); contents[old].Add(retained);
            int before=ammoCreated;
            // The native ApplyEffects call uses exactly !objLootModeSwitch.bSuppress.
            // Its scene-heavy ModeSwitch/content placement remains outside this fixture.
            var converted=Create(output.GetLootNames().Single(),old.strID,loot:!output.bSuppress);
            Check(ammoCreated-before==(repaired?0:75),"Native recipe/factory did not reproduce expected magazine roll: "+job.strName);
            Check(contents[old].Single()==retained,"Recipe generation changed original ammunition");
            Check(contents[converted].Count==(repaired?0:75),"Replacement inventory differs from native recipe policy");
            recipes++;
        }
        Check(recipes==21,"PDC install/uninstall/repair recipe coverage changed");
        foreach(var definition in DataHandler.dictCOs.Values.Where(d=>d.strName.StartsWith("ItmShipWeaponPDC",StringComparison.Ordinal))) {
            Check(definition.strName.IndexOf("Open",StringComparison.Ordinal)<0 && definition.strName.IndexOf("Closed",StringComparison.Ordinal)<0,
                "CrewSim direct Open/Closed replacement can now select a PDC");
            Check(DataHandler.GetLoot(definition.strName).bSuppress,"Direct PDC mode recipe enables new starting loot");
        }
        foreach(var row in JArray.Parse(File.ReadAllText(Path.Combine(root,"interactions/interactions_modeswitch.json")))) {
            string name=(string)row["objLootModeSwitch"];
            if(name!=null && name.StartsWith("ItmShipWeaponPDC",StringComparison.Ordinal)) {
                var mode=DataHandler.GetLoot(name);
                if(mode.GetLootNames().Any(id=>DataHandler.dictCOs.TryGetValue(id,out var co) &&
                    co.strName.StartsWith("ItmShipWeaponPDC",StringComparison.Ordinal)))
                    Check(mode.bSuppress,"PDC damage/power mode enables a new magazine: "+name);
            }
        }
        Console.WriteLine("Native installables/factory without the historical hook: "+recipes+" PDC recipes; fresh rounds per conversion="+(repaired?0:75)+".");
    }
    static void Main(string[] args) {
        var fixture = new Harmony("offline.pdc.ammo.fixture"); var production = new Harmony("offline.pdc.ammo.production");
        try {
            Prefix(fixture, typeof(Shader), "PropertyToID", nameof(Shader), new[] { typeof(string) });
            Prefix(fixture, typeof(UnityEngine.Object), "op_Equality", nameof(Equal));
            Prefix(fixture, typeof(UnityEngine.Object), "op_Inequality", nameof(Unequal));
            Prefix(fixture, typeof(DataHandler), "GetNextID", nameof(ID)); Prefix(fixture, typeof(DataHandler), "GetMesh", nameof(Mesh));
            Prefix(fixture, typeof(GameObject), "AddComponent", nameof(AddComponent), new[] { typeof(Type) });
            Prefix(fixture, typeof(Component), "get_gameObject", nameof(ObjectOf)); Prefix(fixture, typeof(GameObject), "set_name", nameof(Quiet));
            Prefix(fixture, typeof(Item), "SetData", nameof(Quiet), new[] { typeof(string), typeof(float), typeof(float) });
            Prefix(fixture, typeof(Item), "VisualizeOverlays", nameof(Quiet)); Prefix(fixture, typeof(CondOwner), "get_Item", nameof(ItemOf));
            Prefix(fixture, typeof(CondOwner), "set_strID", nameof(SetId)); Prefix(fixture, typeof(CondOwner), "SetData", nameof(SetData));
            Prefix(fixture, typeof(MathUtils), "Rand", nameof(Roll), new[] { typeof(float), typeof(float), typeof(MathUtils.RandType), typeof(string) });
            Prefix(fixture, typeof(MathUtils), "Rand", nameof(RollDouble), new[] { typeof(double), typeof(double), typeof(MathUtils.RandType), typeof(string) });
            DataHandler.mapCOs = new Dictionary<string, CondOwner>(); DataHandler.dictCOSaves = new Dictionary<string, JsonCondOwnerSave>();
            DataHandler.dictCOs = Assets("condowners/condowners_ship_combat.json").ToObject<JsonCondOwner[]>().ToDictionary(d => d.strName);
            DataHandler.dictConds = new Dictionary<string, JsonCond> { ["UniqueID"] = new JsonCond { strName = "UniqueID" } };
            DataHandler.dictLoot = new Dictionary<string, Loot>();
            var jsonLoot = Assets("loot/loot.json").Single(l => (string)l["strName"] == "ItmLootSpawnPDC20mm").ToObject<JsonLoot>();
            Check(jsonLoot.aCOs.Single() == "ItmAmmo20mm=1x50-100", "Native starting-ammo recipe changed");
            DataHandler.dictLoot[jsonLoot.strName] = Loot.CloneFromJSON(jsonLoot);
            var variants = DataHandler.dictCOs.Values.Where(d => d.strName.StartsWith("ItmShipWeaponPDC", StringComparison.Ordinal)).ToArray();
            Check(variants.Length == 15 && variants.All(d => d.strLoot == jsonLoot.strName && d.aStartingConds.Any(c => c.StartsWith("IsShipWeaponPDC="))), "PDC variant coverage changed");
            var original = Create("ItmShipWeaponPDC01Loose");
            Check(contents[original].Count == 75, "Native factory/loot fixture did not create its expected starting rounds");
            contents[original].Clear(); // Empty weapon; converting it must remain empty.
            int before = ammoCreated;
            var broken = Create("ItmShipWeaponPDC01Off", original.strID);
            Check(contents[broken].Count == 75 && ammoCreated - before == 75, "Native conversion failed to reproduce free ammunition before transfer");
            Console.WriteLine("Native replacement of an empty PDC created " + (ammoCreated - before) + " fresh 20 mm rounds before contents transfer.");
            production.CreateClassProcessor(typeof(HistoricalPdcConversionAmmoPatch)).Patch();
            foreach (var definition in variants) {
                var old = Create("ItmShipWeaponPDC01Loose", loot: false);
                Check(old.HasCond("IsShipWeaponPDC"), "Source marker missing");
                var retained = Create("ItmAmmo20mm"); retained.mapConds["StatDamage"] = new Condition(null) { strName = "StatDamage", fCount = .5 };
                contents[old].Add(retained); before = ammoCreated;
                var replacement = Create(definition.strName, old.strID);
                Check(!lastLoot && contents[replacement].Count == 0 && ammoCreated == before, "Conversion rerolled ammunition for " + definition.strName);
                Check(contents[old].Single() == retained && retained.mapConds["StatDamage"].fCount == .5, "Factory patch altered existing ammunition");
                Check(replacement.strID == old.strID, "Replacement identity changed");
                var fresh = Create(definition.strName);
                Check(lastLoot && contents[fresh].Count == 75, "Genuine new PDC lost native starting ammunition");
                var noLoot = Create(definition.strName, loot: false);
                Check(!lastLoot && contents[noLoot].Count == 0, "Loot-disabled creation changed");
                before = ammoCreated;
                var fromEmpty = Create("ItmShipWeaponPDC01Off", noLoot.strID);
                Check(!lastLoot && contents[fromEmpty].Count == 0 && ammoCreated == before, "Empty source variant refilled ammunition: " + definition.strName);
            }
            var unknown = Create("ItmShipWeaponPDC02Loose", "unregistered-id");
            Check(lastLoot && contents[unknown].Count == 75, "Unknown identity was treated as an existing PDC");
            var nonPdc = Create("ItmAmmo20mm"); before = ammoCreated;
            var fromNonPdc = Create("ItmShipWeaponPDC02Off", nonPdc.strID);
            Check(lastLoot && contents[fromNonPdc].Count == 75, "Non-PDC source unexpectedly suppressed loot");
            var saved = Create("ItmShipWeaponPDC01Off", save: new JsonCondOwnerSave { strID = "saved", strCondID = "saved" });
            Check(lastLoot && contents[saved].Count == 0, "Save restoration behavior changed");
            var stale = Create("ItmShipWeaponPDC01Loose", loot: false); stale.bDestroyed = true;
            Create("ItmShipWeaponPDC01Off", stale.strID); Check(lastLoot, "Destroyed source incorrectly qualified as a live conversion");
            var own = Create("ItmShipWeaponPDC03Loose", loot: false);
            var custom = DataHandler.dictCOs["ItmShipWeaponPDC03Off"]; string recipe = custom.strLoot; custom.strLoot = null;
            Create(custom.strName, own.strID); Check(lastLoot, "Overrode modified/non-native loot definition"); custom.strLoot = recipe;
            production.UnpatchSelf();
            NativeRecipes(Ostrabus.Development.InstallationPaths.Data,true);
            if(args.Length==2 && args[0]=="--old-data") NativeRecipes(args[1],false);
            Console.WriteLine(checks + " native factory/loot, PDC variant, identity and scope checks passed.");
        } finally { production.UnpatchSelf(); fixture.UnpatchSelf(); }
    }
}
