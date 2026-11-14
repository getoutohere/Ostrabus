using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using HarmonyLib;
using Newtonsoft.Json.Linq;

internal static partial class Program {
    static int checks;
    static void Check(bool value, string message) {
        checks++;
        if (!value) throw new Exception(message);
    }

    // Replace only Unity face/UI notifications. Condition arithmetic and HasCond
    // execute in the shipped assembly; no work or conversation behavior is patched.
    static bool Records(CondOwner __instance, Condition objCond) {
        if (objCond.fCount <= 0) __instance.mapConds.Remove(objCond.strName);
        else __instance.mapConds[objCond.strName] = objCond;
        return false;
    }

    static JObject Definition(string file, string name) =>
        JArray.Parse(File.ReadAllText(file)).OfType<JObject>().Single(item => (string)item["strName"] == name);

    static int Main(string[] args) {
        if (args.Length > 0 && args[0] == "combat-loop") return CheckCombatLoop();
        if (args.Length > 0 && args[0] == "patched-combat") return CheckPatchedCombat();
        if (args.Length > 0 && args[0] == "patched-radio") return CheckPatchedRadio();
        if (args.Length > 0 && args[0] == "neutralization") return CheckNeutralization();
        if (args.Length > 0 && args[0] == "patched-neutralization") return CheckNeutralization(true);
        string data = Path.Combine(Ostrabus.Development.InstallationPaths.Game, "Ostranauts_Data", "StreamingAssets", "data");
        string stakes = Path.Combine(data, "tsv", "output", "stakes");
        string interactions = Path.Combine(stakes, "interactions", "interactions_STKShipSurrender_PlayerDemand.json");
        string loot = Path.Combine(stakes, "loot", "loot_STKShipSurrender_PlayerDemand.json");
        var definition = Definition(Path.Combine(data, "conditions", "conditions.json"), "InSocialCombat");
        Check((bool)definition["bRemoveOnLoad"], "Reload does not clear the observed blocker in this version");

        var harmony = new Harmony("offline.ship-surrender-work.fixture");
        harmony.Patch(AccessTools.Method(typeof(CondOwner), "UpdateCondRecords"),
            prefix: new HarmonyMethod(typeof(Program), nameof(Records)));
        try {
            var owner = (CondOwner)FormatterServices.GetUninitializedObject(typeof(CondOwner));
            owner.mapConds = new Dictionary<string, Condition>();
            owner.bFreezeCondRules = true;
            var condition = new Condition(null) {
                strName = "InSocialCombat", bRemoveAll = (bool)definition["bRemoveAll"],
                fClampMax = float.PositiveInfinity
            };
            void ApplyFlag(string equation) {
                LootUnit unit = Loot.ParseCondEquation(equation);
                Check(unit.strName == condition.strName && unit.fMin == unit.fMax,
                    "Fixture must use an exact native condition equation");
                condition.AddAmount(owner, unit.bPositive ? unit.fMin : -unit.fMin);
            }
            void ApplyRadioReply(string id) {
                var interaction = Definition(interactions, id);
                foreach (string field in new[] { "LootCondsUs", "LootCondsThem" }) {
                    var effect = Definition(loot, (string)interaction[field]);
                    string[] equations = ((JArray)effect["aCOs"]).Values<string>()
                        .Where(value => Loot.ParseCondEquation(value).strName == "InSocialCombat").ToArray();
                    Check(equations.Length == 1, "Expected one conversation flag edit on each participant");
                    // Model one participant. Verify the other has the same flag edit.
                    if (field == "LootCondsUs") ApplyFlag(equations[0]);
                    else Check(equations[0].StartsWith("InSocialCombat="), "Other participant differs from baseline");
                }
            }

            ApplyRadioReply("STKShipSurrender_PlayerDemand_Intro_Allow_DcSecurityBad");
            Check(owner.HasCond("InSocialCombat") && condition.fCount == 1, "Intro did not block work");
            ApplyRadioReply("STKShipSurrender_PlayerDemand_Gambit_DemandBoarding_Allow");
            Check(owner.HasCond("InSocialCombat") && condition.fCount == 2,
                "Native boarding acceptance did not reproduce the saved stuck flag");
            var terminal = Definition(interactions, "STKShipSurrender_PlayerDemand_Gambit_DemandBoarding_Allow");
            Check(terminal["aInverse"] == null || terminal["aInverse"].Type == JTokenType.Null,
                "Boarding acceptance has a follow-up that needs examination");
            ApplyFlag("-InSocialCombat=1.0x1");
            Check(!owner.HasCond("InSocialCombat") && condition.fCount == 0,
                "Native remove-all semantics did not clear the accumulated flag");
            ApplyFlag("-InSocialCombat=1.0x1");
            Check(!owner.HasCond("InSocialCombat"), "Cleanup is not idempotent");
            CheckSideEffects(data, interactions, loot, owner, condition, ApplyFlag, harmony);
        } finally {
            harmony.UnpatchSelf();
        }
        Console.WriteLine(checks + " native ship-surrender condition checks passed.");
        return 0;
    }
}
