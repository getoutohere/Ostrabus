using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using Ostrabus.MiscBugFixes;
using Ostranauts.Ships;

static partial class Program {
    static JArray Assets(string path) => JArray.Parse(File.ReadAllText(Ostrabus.Development.InstallationPaths.Data + path));
    static DestCheck Rule(JToken asset, CondOwner co) {
        co.strName = (string)asset["strName"];
        var rule = new DestCheck();
        string command = asset["aUpdateCommands"].Values<string>().Single(c => c.StartsWith("Destructable,StatDamage,"));
        Check(rule.SetData(command.Split(','), co), "Native destruction command did not initialize: " + co.strName);
        return rule;
    }
    static void MeatChecks(Harmony h, DamageSystem system, CondOwner target, JArray attacks) {
        int startChecks = checks;
        var meat = Assets("condowners/condowners_meat.json");
        var loot = Assets("loot/loot.json");
        var modes = Assets("interactions/interactions_modeswitch.json");
        var looseAsset = meat.Single(a => (string)a["strName"] == "ItmMeat01Loose");
        var loose = Empty<CondOwner>();
        var nativeRule = Rule(looseAsset, loose);
        Check(nativeRule.strLootModeSwitch == "ACTMeat01Destroy", "Native recursive destruction no longer present");
        var meatLoot = loot.Single(a => (string)a["strName"] == nativeRule.strLootModeSwitch);
        Check(meatLoot["aCOs"].Values<string>().Single() == "MSDestroyMeat=1.0x1", "Unexpected native meat destruction interaction");
        Check((string)modes.Single(a => (string)a["strName"] == "MSDestroyMeat")["objLootModeSwitch"] == "ItmMeatDestroyed" &&
            loot.Single(a => (string)a["strName"] == "ItmMeatDestroyed")["aCOs"].Values<string>().Contains("ItmMeat01Loose=1.0x1-5"),
            "Native recipe did not reproduce loose-meat duplication");

        h.CreateClassProcessor(typeof(LooseMeatDestructionPatch)).Patch();
        foreach (var asset in meat.Where(a => a["aUpdateCommands"] != null && a["aUpdateCommands"].Values<string>().Any(c => c.StartsWith("Destructable,StatDamage,")))) {
            var co = Empty<CondOwner>();
            var initialized = Rule(asset, co);
            string native = asset["aUpdateCommands"].Values<string>().Single(c => c.StartsWith("Destructable,StatDamage,")).Split(',')[2];
            Check(initialized.strLootModeSwitch == (co.strName == "ItmMeat01Loose" ? "ACTDefaultDestroy" : native),
                co.strName + ": incorrect destruction recipe after initialization");
        }
        var corrected = Rule(looseAsset, loose);
        Check(corrected.strLootModeSwitch == "ACTDefaultDestroy", "Loose meat retained the recursive recipe");
        Check(loot.Single(a => (string)a["strName"] == corrected.strLootModeSwitch)["aCOs"].Values<string>().Single() == "MSDestroyDefault=1.0x1" &&
            (string)modes.Single(a => (string)a["strName"] == "MSDestroyDefault")["objLootModeSwitch"] == "ItmDefaultDestroyed" &&
            loot.Single(a => (string)a["strName"] == "ItmDefaultDestroyed")["aCOs"].Values<string>().Single() == "ItmScrapTrash=1.0x1",
            "Corrected native destruction path produces more meat");
        string[] custom = "Destructable,StatDamage,CustomRecipe,StatDamageMax,1.0".Split(',');
        var check = new DestCheck();
        Check(check.SetData(custom, loose) && check.strLootModeSwitch == "CustomRecipe", "Overrode another mod's destruction recipe");
        var evolve = new DestCheck();
        Check(evolve.SetData("Destructable,IsReadyEvolve,ACTMeat01Destroy,IsReadyEvolveMax,1.0".Split(','), loose) &&
            evolve.strLootModeSwitch == "ACTMeat01Destroy", "Changed a non-damage destruction check");
        var invalid = new DestCheck();
        Check(!invalid.SetData("Destructable,StatDamage,ACTMeat01Destroy,StatDamageMax,invalid".Split(','), loose) &&
            invalid.strLootModeSwitch == "ACTMeat01Destroy", "Modified a failed native initialization");
        var arguments = "Destructable,StatDamage,ACTMeat01Destroy,StatDamageMax,1.0".Split(',');
        new DestCheck().SetData(arguments, loose);
        Check(arguments[2] == "ACTMeat01Destroy", "Mutated a shared definition/command array");
        var reloadedAsset = JToken.Parse(looseAsset.ToString());
        Check(Rule(reloadedAsset, Empty<CondOwner>()).strLootModeSwitch == "ACTDefaultDestroy", "Reloaded definition bypassed correction");

        h.CreateClassProcessor(typeof(LooseMeatCoverPatch)).Patch();
        foreach (var asset in attacks.Where(a => (string)a["strType"] == "ranged" && (double?)a["fDmgEnv"] > 0 &&
            ((double?)a["fDmgBlunt"] ?? 0) + ((double?)a["fDmgCut"] ?? 0) > 0)) {
            var mode = asset.ToObject<JsonAttackMode>();
            Reset(Body(target)); Shot(system, mode, attacker);
            double expectedBlunt = blunt, expectedCut = cut;
            Reset(Body(attacker), Object(loose), Object(loose), Body(target)); Shot(system, mode, attacker);
            Check(conditionCalls == 0 && damageCalls == 1 && ReferenceEquals(woundReceiver, target) && blunt == expectedBlunt && cut == expectedCut,
                mode.strName + ": floor meat blocked, weakened or redirected a shot");
            Reset(Object(loose), Body(target)); Shot(system, mode, null);
            Check(conditionCalls == 0 && damageCalls == 1 && blunt == expectedBlunt && cut == expectedCut,
                mode.strName + ": floor scrap blocked an environmental ray");
        }
        var carbine = attacks.Single(a => (string)a["strName"] == "AModeRifle02").ToObject<JsonAttackMode>();
        foreach (string type in new[] { "ItmMeat01EyeLoose", "ItmMeat01HoofLoose", "ItmMeat01HornLoose" }) {
            var part = Empty<CondOwner>(); part.strName = type;
            Check(meat.Any(a => (string)a["strName"] == type), "Unknown native loose meat part: " + type);
            foreach (var mode in new[] { carbine, attacks.Single(a => (string)a["strName"] == "AModeShotgun01").ToObject<JsonAttackMode>() }) {
                Reset(Body(target)); Shot(system, mode, attacker);
                double expectedBlunt = blunt, expectedCut = cut;
                Reset(Object(part), Body(target)); Shot(system, mode, attacker);
                Check(conditionCalls == 0 && damageCalls == 1 && ReferenceEquals(woundReceiver, target) && blunt == expectedBlunt && cut == expectedCut,
                    type + ": loose part blocked or weakened " + mode.strName);
            }
            AccessTools.Field(typeof(CondOwner), "_objCOParent").SetValue(part, target);
            Reset(Object(part), Body(target)); Shot(system, carbine, attacker);
            Check(conditionCalls == 1 && damageCalls == 0, type + ": contained part was treated as floor clutter");
            AccessTools.Field(typeof(CondOwner), "_objCOParent").SetValue(part, null);
            part.slotNow = Empty<Slot>();
            Reset(Object(part), Body(target)); Shot(system, carbine, attacker);
            Check(conditionCalls == 1 && damageCalls == 0, type + ": slotted part was treated as floor clutter");
        }
        foreach (string type in new[] { "ItmMeat01", "ItmMeat01Eye", "ItmMeat01Hoof", "ItmMeat01Horn", "ItmPlanterMeat01Loose", "crate", "wall" }) {
            var cover = Empty<CondOwner>(); cover.strName = type;
            Reset(Object(cover), Body(target)); Shot(system, carbine, attacker);
            Check(conditionCalls == 1 && damageCalls == 0, type + ": ordinary cover was bypassed");
        }
        AccessTools.Field(typeof(CondOwner), "_objCOParent").SetValue(loose, target);
        Reset(Object(loose), Body(target)); Shot(system, carbine, attacker);
        Check(conditionCalls == 1 && damageCalls == 0, "Carried/container meat was treated as floor clutter");
        AccessTools.Field(typeof(CondOwner), "_objCOParent").SetValue(loose, null);
        loose.slotNow = Empty<Slot>();
        Reset(Object(loose), Body(target)); Shot(system, carbine, attacker);
        Check(conditionCalls == 1 && damageCalls == 0, "Slotted meat was treated as floor clutter");
        loose.slotNow = null;
        var objectHit = Object(loose).Equipment;
        Check(objectHit.DmgLeft("StatDamage") == 100 && LooseMeatCoverPatch.RemainingForRay(objectHit, "StatDamage") == 0,
            "Patch globally changed meat durability instead of its ranged-cover interpretation");
        Check(LooseMeatCoverPatch.RemainingForRay(objectHit, "OtherStat") == 100, "Patch modified another destruction check");
        for (int i = 0; i < 20; i++) LooseMeatCoverPatch.RemainingForRay(objectHit, "StatDamage");
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) LooseMeatCoverPatch.RemainingForRay(objectHit, "StatDamage");
        Check(GC.GetAllocatedBytesForCurrentThread() == allocated, "Floor scrap test allocates per collider");

        var nativeMethod = AccessTools.Method(typeof(DamageSystem), nameof(DamageSystem.DamageRay));
        var reader = typeof(PatchProcessor).GetMethods().Single(m => m.Name == "GetOriginalInstructions" && m.GetParameters().Length == 2 && !m.GetParameters()[1].ParameterType.IsByRef);
        var original = ((IEnumerable<CodeInstruction>)reader.Invoke(null, new object[] { nativeMethod, null })).ToList();
        var changed = LooseMeatCoverPatch.Transpiler(original.Select(i => new CodeInstruction(i))).ToList();
        var remaining = AccessTools.Method(typeof(Destructable), nameof(Destructable.DmgLeft));
        Check(changed.Count == original.Count, "Cover fix inserted unrelated instructions");
        for (int i = 0; i < original.Count; i++) {
            if (original[i].Calls(remaining)) {
                Check(changed[i].Calls(AccessTools.Method(typeof(LooseMeatCoverPatch), nameof(LooseMeatCoverPatch.RemainingForRay))), "Ray remaining-damage call was not replaced");
                continue;
            }
            Check(changed[i].opcode == original[i].opcode && Equals(changed[i].operand, original[i].operand), "Cover fix changed unrelated native IL");
        }
        int warnings = Ostrabus.OstrabusPlugin.Logger.Warnings;
        var unknown = new[] { new CodeInstruction(OpCodes.Ret) };
        var fallback = LooseMeatCoverPatch.Transpiler(unknown).ToArray();
        Check(Ostrabus.OstrabusPlugin.Logger.Warnings == warnings + 1 && ReferenceEquals(fallback[0], unknown[0]), "Unknown native shape did not warn and retain native behavior");
        Console.WriteLine((checks - startChecks) + " loose meat destruction/cover checks passed.");
    }
}
