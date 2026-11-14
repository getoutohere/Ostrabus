using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Ostrabus.Development;

static partial class Program {
    const string AssetHack = "OstrabusHackPDASkill";
    static bool NoPathfinder(ref Pathfinder __result) { __result = null; return false; }
    static bool NoRecovery(ref Ostranauts.UI.Quickbar.Models.AvailableActionDTO __result) { __result = null; return false; }
    static void NativeChecks(Harmony fixture) {
        Check(Harmony.GetPatchInfo(AccessTools.Method(typeof(CrewSim), nameof(CrewSim.GetAvailActionsForCO))) == null,
            "Asset proof must run the native quick-action builder without a menu hook.");
        string root = Path.GetFullPath(Path.Combine("QoL", "PdaHacking", "Assets"));
        var allCOs = JArray.Parse(File.ReadAllText(Path.Combine(InstallationPaths.Data, "condowners", "condowners.json")));
        var nativePda = allCOs.OfType<JObject>().Single(x => (string)x["strName"] == "ItmWristPDA01Locked");
        var changedPda = JArray.Parse(File.ReadAllText(Path.Combine(root, "data/condowners/quick_pda.json"))).OfType<JObject>().Single();
        var metadata = new Dictionary<string, JsonModInfo>();
        DataHandler.JsonToData(Path.Combine(root, "mod_info.json"), metadata);
        Check(metadata.Count == 1 && metadata.Values.Single().strGameVersion == "1.0.1.10", "Native metadata missing or targets another game version.");
        Check(Directory.GetFiles(Path.GetDirectoryName(root), "*.cs", SearchOption.AllDirectories).Length == 0, "PDA hacking must contain no runtime C# source.");
        var cos = new Dictionary<string, JsonCondOwner>();
        var overrides = new Dictionary<string, JsonInteractionOverride>();
        DataHandler.JsonToData(Path.Combine(root, "data/condowners/quick_pda.json"), cos);
        DataHandler.JsonToData(Path.Combine(root, "data/condtrigs/quick_pda.json"), DataHandler.dictCTs);
        DataHandler.JsonToData(Path.Combine(root, "data/interaction_overrides/quick_pda.json"), overrides);
        foreach (var entry in overrides.Values) Check(entry.Generate() != null, "Native override generation failed.");
        DataHandler.dictCTs["TOstrabusPdaHackUs"].PostInit();
        DataHandler.dictCTs["TOstrabusPdaDialogUs"].PostInit();
        DataHandler.dictCTs["TOstrabusPdaNotHacking"].PostInit();
        var differences = changedPda.Properties().Where(x => x.Name != "aInteractions" && !JToken.DeepEquals(x.Value, nativePda[x.Name])).ToArray();
        Check(differences.Length == 0 && changedPda.Count == nativePda.Count, "Asset replaced PDA attributes beyond the action list.");
        Check(cos["ItmWristPDA01Locked"].aInteractions.SequenceEqual(new[] { AssetHack }.Concat(nativePda["aInteractions"].Values<string>())), "Asset changed another offered PDA action.");
        Check(typeof(JsonCondOwnerSave).GetProperty("aInteractions") == null, "Saved item now overrides the definition's action list; revisit existing-PDA coverage.");
        var quick = DataHandler.GetInteraction(AssetHack);
        var native = DataHandler.dictInteractions["ENCHackPDASkillFinished"];
        Check(quick.objLootModeSwitchThem.strName == native.objLootModeSwitchThem && quick.LootCondsUs.strName == native.LootCondsUs,
            "Native override lost the authored unlock or rewards.");
        Check(quick.fDuration == native.fDuration && quick.fDuration == 0, "Native override changed duration.");
        Check(quick.strRaiseUI == null && !quick.bPause && quick.aInverse.Length == 0, "Native override failed to remove encounter presentation.");
        Check(native.strRaiseUI == "SocialCombat" && native.aInverse.Length == 1, "Override mutated its success template.");

        // Execute the complete native menu builder. Scene path components and its
        // unrelated recovery indicator are fixture boundaries, not a supplied list.
        fixture.Patch(AccessTools.PropertyGetter(typeof(CondOwner), nameof(CondOwner.Pathfinder)),
            prefix:new HarmonyMethod(typeof(Program), nameof(NoPathfinder)));
        fixture.Patch(AccessTools.PropertyGetter(typeof(CondOwner), nameof(CondOwner.Crew)),
            prefix:new HarmonyMethod(typeof(Program), nameof(Quiet)));
        fixture.Patch(AccessTools.Method(typeof(CrewSim), "InjectRecoveryIa"),
            prefix:new HarmonyMethod(typeof(Program), nameof(NoRecovery)));
        CrewSim.objInstance = Empty<CrewSim>();
        CrewSim.objInstance.workManager = Empty<WorkManager>();
        GigManager.aJobs = new List<JsonJobSave>();
        DataHandler.dictLoot["TXTPlayerCombatInteractions"] = new Loot();
        var actor = CO("asset-actor"); var target = CO("asset-pda"); var other = CO("asset-other");
        actor.aAttackIAs = new List<string>(); actor.aReplies = new List<ReplyThread>();
        target.aInteractions = new List<string>(cos["ItmWristPDA01Locked"].aInteractions);
        // Pickup/drop definitions are outside the hacking fixture; native still
        // enumerates their identifiers through the same PDA list.
        foreach (string name in new[] { "PickupItem", "DropItem" })
            DataHandler.dictInteractions[name] = new JsonInteraction { strName = name, strTitle = name, strThemType = "Other" };
        foreach (bool skill in new[] { false, true }) foreach (string incapacity in new[] { null, "Unconscious", "IsDead", "Stunned", "Recovering" })
            foreach (bool locked in new[] { false, true }) foreach (bool computer in new[] { false, true }) foreach (bool pda in new[] { false, true }) {
            selected = actor;
            Flags(actor, "IsHuman", skill ? "SkillHacking" : null, incapacity);
            Flags(target, locked ? "IsLocked" : null, computer ? "IsComputer" : null, pda ? "IsPDA" : null);
            var menu = CrewSim.GetAvailActionsForCO(actor, target);
            bool valid = incapacity == null && locked && computer && pda;
            Check(menu.Any(x => x.Ia.strName == AssetHack) == (valid && skill), "Pure asset shortcut eligibility failed in native menu.");
            Check(menu.Any(x => x.Ia.strName == "ENCHackPDAStart") == (valid && !skill),
                "Pure asset fallback dialog eligibility failed: skill=" + skill + ", state=" + incapacity +
                ", locked=" + locked + ", computer=" + computer + ", pda=" + pda + "; menu=" + string.Join(",", menu.Select(x => x.Ia.strName)));
        }
        selected = other;
        Flags(actor, "IsHuman", "SkillHacking"); Flags(target, "IsLocked", "IsComputer", "IsPDA");
        Check(!CrewSim.GetAvailActionsForCO(actor, target).Any(x => x.Ia.strName == AssetHack), "Asset shortcut escaped controlled-character gating.");
        selected = actor;
        quick = CrewSim.GetAvailActionsForCO(actor, target).Single(x => x.Ia.strName == AssetHack).Ia;
        Check(quick.strActionGroup == "Use" && quick.strTitle == "Hack PDA", "Native menu did not expose normal Use action.");
        actor.mapConds.Remove("SkillHacking");
        Check(!quick.Triggered(actor, target, bIgnoreItems:true), "Native asset click-time gate ignored lost skill.");
        Flags(actor, "IsHuman", "SkillHacking"); target.mapConds.Remove("IsLocked");
        Check(!quick.Triggered(actor, target, bIgnoreItems:true), "Native asset click-time gate ignored unlocked target.");
        Console.WriteLine("Assets-only PDA hacking: " + checks + " native loader/override/menu/gating checks passed; no game-runtime patch required.");
    }
}
