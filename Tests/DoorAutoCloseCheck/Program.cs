using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using Ostrabus.MiscBugFixes;

static class Program {
    static readonly string Data = Ostrabus.Development.InstallationPaths.Data;
    static int checks;
    static double autoClose;
    static bool zeroed;
    static readonly Loot removal = new Loot { strName = "CTRemoveAutoClose" };
    static readonly Loot addition = new Loot { strName = "CTAddAutoClose" };
    static T Empty<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    static JArray Rows(string path) => JArray.Parse(File.ReadAllText(Data + path));
    static JObject Named(JArray rows, string id) => (JObject)rows.Single(r => (string)r["strName"] == id);
    static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
    static bool Equal(UnityEngine.Object __0, UnityEngine.Object __1, ref bool __result) { __result = ReferenceEquals(__0, __1); return false; }
    static bool Unequal(UnityEngine.Object __0, UnityEngine.Object __1, ref bool __result) { __result = !ReferenceEquals(__0, __1); return false; }
    static bool Quiet() => false;
    // Isolate asset loading, dice/trigger eligibility and condition storage.
    // Native ApplyLootCT and CondTrigger.ApplyChanceID still execute.
    static bool Load(Interaction __instance, JsonInteraction jsonIn) {
        __instance.strName = jsonIn.strName; __instance.bNoRemember = true;
        __instance.LootCTsUs = jsonIn.LootCTsUs == removal.strName ? removal : jsonIn.LootCTsUs == addition.strName ? addition : null;
        return false;
    }
    static bool Loot(Loot __instance, ref List<CondTrigger> __result) {
        __result = new List<CondTrigger> { new CondTrigger { strCondName = "IsAutoClose", fCount = __instance == removal ? -1 : 1 } };
        return false;
    }
    static bool Trigger(ref bool __result) { __result = true; return false; }
    static bool Add(string strName, double fAmount) {
        if (strName == "IsAutoClose") { autoClose += fAmount; if (autoClose <= 0) { autoClose = 0; zeroed = true; } else zeroed = false; }
        return false;
    }
    static void Apply(Interaction interaction) => AccessTools.Method(typeof(Interaction), "ApplyLootCT").Invoke(interaction,
        new object[] { interaction.LootCTsUs, null, Empty<CondOwner>(), null, 1f });
    static JsonInteraction Json(JObject row) => new JsonInteraction { strName = (string)row["strName"], LootCTsUs = (string)row["LootCTsUs"] };
    static int Main() {
        var modes = Rows("interactions/interactions_modeswitch.json");
        var owners = Rows("condowners/condowners.json");
        var powers = Rows("powerinfos/powerinfos.json");
        var triggers = Rows("condtrigs/condtrigs.json");
        var conditions = Rows("conditions/conditions.json");
        Check((bool)Named(conditions, "IsAutoClose")["bPersists"], "Auto-close preference is not persistent");
        Check((double)Named(triggers, "TDnAutoClose")["fCount"] == -1, "Removal trigger changed");
        Check(Named(owners, "ItmDoor01OpenOn")["aStartingConds"].Values<string>().Contains("IsAutoClose=1.0x1"), "Powered-open default changed");
        Check(!Named(owners, "ItmDoor01Open")["aUpdateCommands"].Values<string>().Any(s => s == "Pledge,AutoClose"), "Unpowered door now auto-closes");
        var h = new Harmony("offline.doorautoclose.fixture");
        h.Patch(AccessTools.Method(typeof(UnityEngine.Object), "op_Equality"), prefix: new HarmonyMethod(typeof(Program), nameof(Equal)));
        h.Patch(AccessTools.Method(typeof(UnityEngine.Object), "op_Inequality"), prefix: new HarmonyMethod(typeof(Program), nameof(Unequal)));
        h.Patch(AccessTools.Method(typeof(Interaction), "SetData"), prefix: new HarmonyMethod(typeof(Program), nameof(Load)));
        h.Patch(AccessTools.Method(typeof(Loot), "GetCTLoot"), prefix: new HarmonyMethod(typeof(Program), nameof(Loot)));
        h.Patch(AccessTools.Method(typeof(CondTrigger), "Triggered", new[] { typeof(CondOwner), typeof(string), typeof(bool) }), prefix: new HarmonyMethod(typeof(Program), nameof(Trigger)));
        h.Patch(AccessTools.Method(typeof(CondOwner), "AddCondAmount"), prefix: new HarmonyMethod(typeof(Program), nameof(Add)));
        h.Patch(AccessTools.Method(typeof(CondOwner), "RememberEffects2"), prefix: new HarmonyMethod(typeof(Program), nameof(Quiet)));
        var baseline = new Interaction(Json(Named(modes, "MSDoor01OpenShutdownAllow")));
        autoClose = 1; zeroed = false; Apply(baseline);
        Check(autoClose == 0 && zeroed, "Native shutdown did not reproduce persistent removal");
        h.CreateClassProcessor(typeof(DoorAutoClosePatch)).Patch();
        foreach (string state in new[] { "Closed", "ClosedLocked", "Open", "OpenLocked" }) {
            string off = "MSDoor01" + state + "ShutdownAllow";
            string on = "MSDoor01" + state + "PowerupAllow";
            var offJson = Json(Named(modes, off)); var onJson = Json(Named(modes, on));
            Check(offJson.LootCTsUs == removal.strName && onJson.LootCTsUs == null, "Native power transition effects changed");
            string powerId = state.EndsWith("Locked") ? "Door01" + state.Substring(0, state.Length - 6) + "Locked" : "Door01" + state;
            Check((string)Named(powers, powerId)["strIntPowerOff"] == off && (string)Named(powers, powerId)["strIntPowerOn"] == on, "Power wiring changed");
            foreach (bool enabled in new[] { false, true }) {
                autoClose = enabled ? 1 : 0; zeroed = !enabled;
                var interaction = new Interaction(offJson);
                Check(interaction.LootCTsUs == null, "Shutdown removal was retained");
                Apply(interaction);
                Check(autoClose == (enabled ? 1 : 0) && zeroed == !enabled, "Shutdown changed the existing preference");
                // Native ModeSwitch copies persistent conditions and explicit zeros.
                // Verify the preserved state has the correct inputs for that copy.
                interaction.ResetObject(onJson); Apply(interaction);
                Check(autoClose == (enabled ? 1 : 0) && zeroed == !enabled, "Power-up changed the preserved preference");
                interaction.ResetObject(offJson); Apply(interaction);
                Check(autoClose == (enabled ? 1 : 0) && zeroed == !enabled, "Pooled interaction lost the correction");
            }
        }
        var manual = new Interaction(Json(Named(modes, "MSDoor01ClosedOnOpenAllowRemoveAuto")));
        autoClose = 1; zeroed = false; Apply(manual);
        Check(autoClose == 0 && zeroed, "Manual hold-open stopped working");
        var close = new Interaction(Json(Named(modes, "MSDoor01OpenOnCloseAllowAddAuto"))); Apply(close);
        Check(autoClose == 1 && !zeroed, "Manual close stopped re-enabling auto-close");
        var other = new Interaction(new JsonInteraction { strName = "OtherShutdown", LootCTsUs = removal.strName });
        Check(ReferenceEquals(other.LootCTsUs, removal), "Unrelated interaction changed");
        var modified = new Interaction(new JsonInteraction { strName = "MSDoor01OpenShutdownAllow", LootCTsUs = addition.strName });
        Check(ReferenceEquals(modified.LootCTsUs, addition), "Non-native replacement effect changed");
        Check(Named(modes, "MSDoor01OpenShutdownAllow")["LootCTsUs"].Value<string>() == removal.strName, "Source definition mutated");
        Console.WriteLine(checks + " door auto-close checks passed.");
        return 0;
    }
}
