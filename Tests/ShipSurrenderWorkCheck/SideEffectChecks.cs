using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using HarmonyLib;
using Newtonsoft.Json.Linq;

internal static partial class Program {
    static bool Equal(UnityEngine.Object __0, UnityEngine.Object __1, ref bool __result) {
        __result = ReferenceEquals(__0, __1);
        return false;
    }
    static bool NoParentValidation() => false;

    static bool FlagEquation(string value) => Loot.ParseCondEquation(value).strName == "InSocialCombat";
    static JObject WithoutFlag(JObject original) {
        var copy = (JObject)original.DeepClone();
        copy["aCOs"] = new JArray(((JArray)copy["aCOs"]).Values<string>().Where(value => !FlagEquation(value)));
        return copy;
    }

    static void CheckSideEffects(string data, string interactionsFile, string lootFile,
        CondOwner owner, Condition flag, Action<string> apply, Harmony fixture) {
        string[] replies = {
            "STKShipSurrender_PlayerDemand_Intro_Allow_DcSecurityBad",
            "STKShipSurrender_PlayerDemand_Gambit_DemandBoarding_Allow"
        };
        var interactions = JArray.Parse(File.ReadAllText(interactionsFile)).OfType<JObject>().ToArray();
        var loot = JArray.Parse(File.ReadAllText(lootFile)).OfType<JObject>().ToDictionary(item => (string)item["strName"]);
        var terminal = interactions.Single(item => (string)item["strName"] == replies[1]);
        Check((string)terminal["LootCTsUs"] == "CMDComeHere", "Boarding no longer uses the separate native ship command");
        Check(terminal["strPledgeAdd"] == null && terminal["strPledgeRemove"] == null,
            "Boarding now uses a character pledge that requires separate examination");
        var command = Definition(Path.Combine(data, "loot", "loot.json"), "CMDComeHere");
        Check((string)command["strType"] == "text" && (string)command["aCOs"][0] == "ComeHere=1x1",
            "Boarding command wiring changed");

        foreach (string role in new[] { "Us", "Them" }) {
            string key = "LootConds" + role;
            string id = (string)terminal[key];
            Check(interactions.Count(ia => (string)ia[key] == id) == 1,
                "Proposed terminal loot is shared by another interaction");
            var original = loot[id];
            var simple = (JObject)original.DeepClone();
            var edits = (JArray)simple["aCOs"];
            for (int i = 0; i < edits.Count; i++) if (FlagEquation((string)edits[i])) edits[i] = "-InSocialCombat=1.0x1";
            Check(JToken.DeepEquals(WithoutFlag(original), WithoutFlag(simple)),
                "Simple terminal correction changes other condition effects");

            // Counterexample: one flag belongs to a simultaneous face-to-face
            // conversation. Native remove-all arithmetic cannot distinguish it.
            foreach (int unrelatedFlags in new[] { 0, 1, 3 }) {
                flag.fCount = 0;
                owner.mapConds.Remove(flag.strName);
                if (unrelatedFlags > 0) flag.AddAmount(owner, unrelatedFlags);
                apply("InSocialCombat=1.0x1"); // radio intro
                apply("-InSocialCombat=1.0x1"); // proposed simple terminal fix
                Check(!owner.HasCond(flag.strName), "Remove-all behavior changed");
                Check(flag.fCount == 0, "Simple fix did not erase unrelated conversation flags");

                // Alternative model: radio never writes the on-foot flag.
                flag.fCount = 0;
                owner.mapConds.Remove(flag.strName);
                if (unrelatedFlags > 0) flag.AddAmount(owner, unrelatedFlags);
                foreach (string reply in replies) {
                    var ia = interactions.Single(item => (string)item["strName"] == reply);
                    JObject isolated = WithoutFlag(loot[(string)ia[key]]);
                    Check(!((JArray)isolated["aCOs"]).Values<string>().Any(FlagEquation),
                        "Isolated radio model still changes the shared flag");
                }
                Check(flag.fCount == unrelatedFlags && owner.HasCond(flag.strName) == (unrelatedFlags > 0),
                    "Isolated radio model alters another conversation's lock");
            }
        }

        fixture.Patch(AccessTools.Method(typeof(UnityEngine.Object), "op_Equality"),
            prefix: new HarmonyMethod(typeof(Program), nameof(Equal)));
        fixture.Patch(AccessTools.Method(typeof(CondOwner), "ValidateParent"),
            prefix: new HarmonyMethod(typeof(Program), nameof(NoParentValidation)));
        var strictJson = Definition(Path.Combine(data, "condtrigs", "condtrigs.json"), "TIsValidSocialTargetStrict");
        var strict = new CondTrigger {
            strName = (string)strictJson["strName"], fChance = (float)strictJson["fChance"],
            bAND = (bool)strictJson["bAND"],
            aReqs = ((JArray)strictJson["aReqs"]).Values<string>().ToArray(),
            aForbids = ((JArray)strictJson["aForbids"]).Values<string>().ToArray()
        };
        owner.mapConds["IsHuman"] = new Condition(null) { strName = "IsHuman", fCount = 1 };
        flag.fCount = 1;
        owner.mapConds[flag.strName] = flag;
        Check(strict.Triggered(owner, logOutcome: false),
            "Native captain selection now excludes actors in a social conversation");
        owner.mapConds["IsInCombat"] = new Condition(null) { strName = "IsInCombat", fCount = 1 };
        Check(!strict.Triggered(owner, logOutcome: false), "Trigger fixture failed its negative control");
        CheckContextSideEffects();
        Console.WriteLine("Side-effect counterexample confirmed: terminal cleanup clears an unrelated conversation lock; radio-only omission preserves it.");
    }

    static void CheckContextSideEffects() {
        // Run the native context setter with non-player actors so its Unity
        // tooltip event is outside the fixture. Relationship context writes
        // themselves execute unchanged in Interaction.UpdateStakes.
        T Empty<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
        CondOwner oldPlayer = CrewSim.coPlayer;
        var oldOwners = DataHandler.mapCOs;
        try {
            var us = Empty<CondOwner>();
            var them = Empty<CondOwner>();
            AccessTools.Field(typeof(CondOwner), "_strID").SetValue(us, "radio-captain-a");
            AccessTools.Field(typeof(CondOwner), "_strID").SetValue(them, "radio-captain-b");
            us.tf = Empty<UnityEngine.Transform>();
            them.tf = Empty<UnityEngine.Transform>();
            DataHandler.mapCOs = new Dictionary<string, CondOwner> { [us.strID] = us, [them.strID] = them };
            CrewSim.coPlayer = Empty<CondOwner>();
            var interaction = Empty<Interaction>();
            interaction.objUs = us;
            interaction.objThem = them;
            var relUs = Empty<Relationship>();
            var relThem = Empty<Relationship>();
            var update = AccessTools.Method(typeof(Interaction), "UpdateStakes");

            relUs.strContext = "ACTHireCrewUs";
            relThem.strContext = "ACTHireCrewThem";
            interaction.strLootContextUs = interaction.strLootContextThem = "Default";
            update.Invoke(interaction, new object[] { relUs, relThem });
            Check(relUs.IsContextDefault && relThem.IsContextDefault,
                "Simple terminal context reset no longer overwrites another negotiation");

            relUs.strContext = "ACTHireCrewUs";
            relThem.strContext = "ACTHireCrewThem";
            interaction.strLootContextUs = interaction.strLootContextThem = null;
            update.Invoke(interaction, new object[] { relUs, relThem });
            Check(relUs.strContext == "ACTHireCrewUs" && relThem.strContext == "ACTHireCrewThem",
                "Omitting radio context edits did not preserve the independent on-foot negotiation");
        } finally {
            CrewSim.coPlayer = oldPlayer;
            DataHandler.mapCOs = oldOwners;
        }
    }
}
