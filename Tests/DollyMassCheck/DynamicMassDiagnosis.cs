using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using Ostrabus.MiscBugFixes;
using Ostranauts.Ships;

static partial class Program {
    static Loot massAuditLoot;
    static bool liveWrites, throwMassWrite;
    static readonly Dictionary<string, Condition> massConditions = new Dictionary<string, Condition>();
    static Action<CondOwner> initializeAction;
    static bool Initialize(CondOwner __instance) { Check(LiveMass.Initializing > 0, "SetData initialization scope missing"); initializeAction(__instance); return false; }
    static CondOwner LiveCO(string name, double mass, Ship ship, bool human = false) {
        var co = CO(name, mass); AccessTools.Field(typeof(CondOwner), "_objShip").SetValue(co, ship);
        if (human) values[co.strID]["IsHuman"] = 1;
        return co;
    }
    static Ship LiveShip(Ship.Loaded loaded = Ship.Loaded.Full) {
        var ship = Empty<Ship>(); AccessTools.Field(typeof(Ship), "nLoadState").SetValue(ship, loaded);
        AccessTools.Field(typeof(Ship), "fGravity").SetValue(ship, 0.3d); return ship;
    }
    static void CheckLiveVariants(Harmony fixture) {
        var ship = LiveShip();
        var actor = LiveCO("runtime-person", 115, ship, true); var bag = LiveCO("runtime-bag", 15, ship); var item = LiveCO("runtime-item", 5, ship);
        Contents(actor, bag); Contents(bag, item);
        AccessTools.Field(typeof(Ship), "_mass").SetValue(ship, 1234d);
        item.AddCondAmount("StatMass", 2);
        Check(item.GetTotalMass() == 7 && bag.GetTotalMass() == 17 && actor.GetTotalMass() == 117, "Direct nested mass addition did not propagate exactly once");
        Check(Math.Abs(actor.GetCondAmount("StatEncumbrance") - 35.1) < 1e-9, "Nested addition left stale character encumbrance");
        Check((double)AccessTools.Field(typeof(Ship), "_mass").GetValue(ship) == 0, "Runtime mass change left cached ship mass stale");
        item.AddMass(-2);
        Check(item.GetTotalMass() == 5 && bag.GetTotalMass() == 15 && actor.GetTotalMass() == 115, "Native AddMass propagated twice");
        item.SetCondAmount("StatMass", 8);
        Check(bag.GetTotalMass() == 18 && actor.GetTotalMass() == 118, "SetCondAmount bypassed mass propagation");
        item.AddMass(-100);
        Check(item.GetTotalMass() == 0 && bag.GetTotalMass() == 10 && actor.GetTotalMass() == 110, "Clamped native mass delta over-debited ancestors");
        actor.AddCondAmount("StatMass", 15);
        Check(actor.GetTotalMass() == 125 && Math.Abs(actor.GetCondAmount("StatEncumbrance") - 37.5) < 1e-9, "Runtime body/trait mass did not refresh encumbrance");
        actor.AddCondAmount("StatMass", -15); Check(actor.GetTotalMass() == 110, "Body mass was normalized instead of preserving the delta");
        var other = new Condition(new JsonCond { strName = "StatFun" }) { fCount = 1 }; other.AddAmount(item, 2);
        Check(actor.GetTotalMass() == 110 && other.fCount == 3, "Non-mass condition changed inventory mass");
        foreach (string boundary in new[] { "frozen", "rules-frozen", "loading", "initializing", "shallow", "none", "unplaced", "editor", "barter" }) {
            var parent = LiveCO("boundary-parent", 20, ship);
            var child = LiveCO("boundary-child", 10, ship); Contents(parent, child);
            switch (boundary) {
                case "frozen": child.bFreezeConds = true; break;
                case "rules-frozen": child.bFreezeCondRules = true; break;
                case "loading": AccessTools.Field(typeof(CrewSim), "_finishedLoading").SetValue(CrewSim.objInstance, false); break;
                case "initializing": LiveMass.Initializing++; break;
                case "shallow": AccessTools.Field(typeof(CondOwner), "_objShip").SetValue(child, LiveShip(Ship.Loaded.Shallow)); break;
                case "none": AccessTools.Field(typeof(CondOwner), "_objShip").SetValue(child, LiveShip(Ship.Loaded.None)); break;
                case "unplaced": AccessTools.Field(typeof(CondOwner), "_objShip").SetValue(child, null); break;
                case "editor": CrewSim.bShipEdit = true; break;
                case "barter": var copy = Empty<BarterZoneShip>(); AccessTools.Field(typeof(Ship), "nLoadState").SetValue(copy, Ship.Loaded.Full); AccessTools.Field(typeof(CondOwner), "_objShip").SetValue(child, copy); break;
            }
            try {
                child.AddCondAmount("StatMass", 2);
                Check(parent.GetTotalMass() == 20 && child.GetTotalMass() == (boundary == "frozen" ? 10 : 12), "Reconstruction/excluded state propagated mass: " + boundary);
            } finally { LiveMass.Initializing = 0; CrewSim.bShipEdit = false; AccessTools.Field(typeof(CrewSim), "_finishedLoading").SetValue(CrewSim.objInstance, true); }
        }
        // Native condition clamping applies to direct writes as well as AddMass.
        var clampedParent = LiveCO("clamped-parent", 15, ship); var clamped = LiveCO("clamped-item", 5, ship); Contents(clampedParent, clamped);
        clamped.AddCondAmount("StatMass", -20); Check(clamped.GetTotalMass() == 0 && clampedParent.GetTotalMass() == 10, "Direct clamped change propagated requested instead of actual delta");
        var loopA = LiveCO("loop-a", 10, ship); var loopB = LiveCO("loop-b", 10, ship);
        AccessTools.Field(typeof(CondOwner), "_objCOParent").SetValue(loopA, loopB); AccessTools.Field(typeof(CondOwner), "_objCOParent").SetValue(loopB, loopA);
        loopA.AddCondAmount("StatMass", 1); Check(loopA.GetTotalMass() == 11 && loopB.GetTotalMass() == 10, "Malformed ownership started recursive propagation");
        fixture.Patch(AccessTools.Method(typeof(CondOwner), "SetData"), prefix: new HarmonyMethod(typeof(Program), nameof(Initialize)) { priority = Priority.Last });
        foreach (bool fail in new[] { false, true }) {
            var parent = LiveCO("init-parent", 20, ship); var child = LiveCO("init-child", 10, ship); Contents(parent, child);
            initializeAction = co => { co.AddCondAmount("StatMass", 3); if (fail) throw new InvalidOperationException("fixture initialization failure"); };
            bool threw = false;
            try { child.SetData(null); } catch (InvalidOperationException) { threw = true; }
            Check(threw == fail && parent.GetTotalMass() == 20 && LiveMass.Initializing == 0, "Initialization scope propagated, swallowed failure or leaked");
        }
        initializeAction = null;
        foreach (bool refresh in new[] { false, true }) {
            throwMassWrite = true; bool threw = false;
            try { if (refresh) LiveMass.Changed(new Condition(null) { fCount = 11 }, LiveCO("refresh-failure", 10, ship), 10); else actor.AddMass(1); }
            catch (InvalidOperationException) { threw = true; }
            finally { throwMassWrite = false; }
            Check(threw && LiveMass.Writer == null && LiveMass.Refreshing == null, "Mass write/refresh exception leaked a game reference");
        }
        // Re-run native merge/removal paths with condition-backed mass storage
        // and the live propagation hooks active alongside the older fixes.
        CheckNativeStackRemoval();
    }
    static bool RecordCondition(CondOwner __instance, Condition objCond) {
        if (objCond.fCount > 0) return true;
        __instance.mapConds.Remove(objCond.strName); return false; // Unity face/timer I/O.
    }
    static bool AuditLoot(string strName, ref Loot __result) {
        if (strName != "CONDStatFireChemPer") return true;
        __result = massAuditLoot; return false;
    }
    static void CheckDynamicMass() {
        var data = Ostrabus.Development.InstallationPaths.Data;
        var chargeDef = JArray.Parse(File.ReadAllText(data + "conditions/conditions.json")).Single(x => (string)x["strName"] == "StatFireChem");
        var lootDef = JArray.Parse(File.ReadAllText(data + "loot/loot.json")).Single(x => (string)x["strName"] == "CONDStatFireChemPer");
        var itemDef = JArray.Parse(File.ReadAllText(data + "condowners/condowners.json")).Single(x => (string)x["strName"] == "ItmToolFireEx01");
        massAuditLoot = Loot.CloneFromJSON(lootDef.ToObject<JsonLoot>());
        var fixture = new Harmony("offline.dynamic.mass.audit");
        var fix = new Harmony("offline.dynamic.mass.prevention");
        var oldSim = CrewSim.objInstance;
        fixture.Patch(AccessTools.Method(typeof(DataHandler), "GetLoot"), prefix: new HarmonyMethod(typeof(Program), nameof(AuditLoot)));
        fixture.Patch(AccessTools.Method(typeof(CondOwner), "UpdateCondRecords"), prefix: new HarmonyMethod(typeof(Program), nameof(RecordCondition)));
        try {
            liveWrites = true;
            CrewSim.objInstance = Empty<CrewSim>(); AccessTools.Field(typeof(CrewSim), "_finishedLoading").SetValue(CrewSim.objInstance, true);
            foreach (bool patched in new[] { false, true }) {
            if (patched) {
                fix.CreateClassProcessor(typeof(LiveMassConditionPatch)).Patch();
                fix.CreateClassProcessor(typeof(LiveMassAddPatch)).Patch();
                fix.CreateClassProcessor(typeof(LiveMassInitializationPatch)).Patch();
            }
            var charge = new Condition(chargeDef.ToObject<JsonCond>()) { fCount = 11 };
            Check(charge.aPer.Contains("CONDStatFireChemPer") && massAuditLoot.aCOs.Contains("StatMass=1.0x1.0"), "Installed charge-to-mass definition changed");
            Check(itemDef["aStartingConds"].Values<string>().Contains("StatMass=1.0x2.5") && itemDef["aStartingConds"].Values<string>().Contains("StatFireChem=1.0x11.0"), "Installed extinguisher mass/charge changed");
            var actor = CO("human-charge-fixture", 113.5);
            values[actor.strID]["IsHuman"] = 1;
            values[actor.strID]["StatEncumbrance"] = 113.5 * 0.3;
            var tool = CO("ItmToolFireEx01", 13.5); Contents(actor, tool);
            var ship = Empty<Ship>(); AccessTools.Field(typeof(Ship), "nLoadState").SetValue(ship, Ship.Loaded.Full); AccessTools.Field(typeof(Ship), "fGravity").SetValue(ship, 0.3d);
            AccessTools.Field(typeof(CondOwner), "_objShip").SetValue(actor, ship); AccessTools.Field(typeof(CondOwner), "_objShip").SetValue(tool, ship);
            // Native Condition -> per-unit loot -> ParseCondEquation. Only the
            // scalar condition storage is adapted, as in the removal harness.
            charge.AddAmount(tool, -1);
            Check(charge.fCount == 10 && tool.GetTotalMass() == 12.5, "Native charge consumption did not update tool mass");
            Check(actor.GetTotalMass() == (patched ? 112.5 : 113.5), "Charge mass carrier propagation mismatch");
            // Native AddMass is the accounting operation used by unslot/removal.
            // Demonstrate the residual after that unchanged debit, without
            // claiming to run the complete slot/UI operation here.
            actor.AddMass(-tool.GetTotalMass());
            Check(actor.GetTotalMass() == (patched ? 100 : 101), "Character removal accounting mismatch");
            Check(Math.Abs(actor.GetCondAmount("StatEncumbrance") - (patched ? 30 : 30.3)) < 1e-9, "Character encumbrance mismatch");
            Console.WriteLine("CHARGE " + (patched ? "FIXED" : "BASELINE") + ": carrier after removal=" + actor.GetTotalMass() + "kg (expected original 100kg body).");
            if (patched) CheckLiveVariants(fixture);
            }
        } finally { fix.UnpatchSelf(); fixture.UnpatchSelf(); massAuditLoot = null; liveWrites = false; throwMassWrite = false; massConditions.Clear(); CrewSim.objInstance = oldSim; }
    }
}
