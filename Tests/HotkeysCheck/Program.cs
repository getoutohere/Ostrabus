using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using BepInEx.Configuration;
using HarmonyLib;
using Ostrabus.Hotkeys;

static partial class Program {
    static int checks, messages, nativeSets;
    static CondOwner messageCrew;
    static string messageText, messageColor, messageOwner;
    static readonly HashSet<string> crewConditions = new HashSet<string>();
    static void Check(bool ok, string message) { checks++; if (!ok) throw new Exception(message); }
    static T Empty<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    static void Field(object obj, string name, object value) => AccessTools.Field(obj.GetType(), name).SetValue(obj, value);
    static bool Equal(UnityEngine.Object __0, UnityEngine.Object __1, ref bool __result) { __result = ReferenceEquals(__0, __1); return false; }
    static bool Unequal(UnityEngine.Object __0, UnityEngine.Object __1, ref bool __result) { __result = !ReferenceEquals(__0, __1); return false; }
    static bool Has(CondOwner __instance, string strName, ref bool __result) { __result = strName == "IsPlayerCrew" && crewConditions.Contains(__instance.strID) || equipmentConditions.TryGetValue(__instance, out var conditions) && conditions.Contains(strName); return false; }
    static bool Message(CondOwner __instance, string strMsg, string strColor, string strOwner) {
        messages++; messageCrew = __instance; messageText = strMsg; messageColor = strColor; messageOwner = strOwner; return false;
    }
    static void NativeSet() { nativeSets++; }
    static CondOwner Actor(string id, JsonCompany company, Ship ship) {
        var co = Empty<CondOwner>();
        Field(co, "_strID", id); Field(co, "objCompany", company); Field(co, "_objShip", ship); Field(co, "_isHumanOrRobot", true);
        co.bAlive = true; return co;
    }
    static void Rejected(CondOwner co, JsonCompanyRules rules) {
        int before = messages, sets = nativeSets;
        bool allowed = rules.bAirlockPermission;
        bool shore = rules.bShoreLeave;
        Check(!FunctionalHotkeys.ToggleAirlock(co), "Invalid selection accepted");
        Check(!FunctionalHotkeys.ToggleDisembark(co), "Invalid disembark selection accepted");
        Check(rules.bAirlockPermission == allowed && rules.bShoreLeave == shore && messages == before && nativeSets == sets, "Rejected hotkey changed permissions or emitted feedback");
    }
    static int Main() {
        var fixture = new Harmony("hotkeys.offline.check");
        var oldPlayer = CrewSim.coPlayer;
        var oldSelection = CrewSim.aSelected;
        try {
            fixture.Patch(AccessTools.Method(typeof(UnityEngine.Object), "op_Equality"), prefix: new HarmonyMethod(typeof(Program), nameof(Equal)));
            fixture.Patch(AccessTools.Method(typeof(UnityEngine.Object), "op_Inequality"), prefix: new HarmonyMethod(typeof(Program), nameof(Unequal)));
            fixture.Patch(AccessTools.Method(typeof(CondOwner), "HasCond", new[] { typeof(string) }), prefix: new HarmonyMethod(typeof(Program), nameof(Has)));
            fixture.Patch(AccessTools.Method(typeof(CondOwner), "LogMessage"), prefix: new HarmonyMethod(typeof(Program), nameof(Message)));
            // Run the actual native roster setter and clone; only observe setter calls.
            fixture.Patch(AccessTools.Method(typeof(JsonCompany), "SetPermissionAirlock"), postfix: new HarmonyMethod(typeof(Program), nameof(NativeSet)));
            fixture.Patch(AccessTools.Method(typeof(JsonCompany), "SetPermissionShore"), postfix: new HarmonyMethod(typeof(Program), nameof(NativeSet)));
            FunctionalHotkeys.Flag = Empty<ConfigEntry<bool>>(); Field(FunctionalHotkeys.Flag, "_typedValue", true);
            var company = new JsonCompany { mapRoster = new Dictionary<string, JsonCompanyRules>() };
            var ship = Empty<Ship>(); Field(ship, "nLoadState", Ship.Loaded.Full);
            var player = Actor("captain", company, ship);
            var crew = Actor("crewmate", company, ship); crewConditions.Add(crew.strID);
            var playerRules = new JsonCompanyRules { bAirlockPermission = false };
            var crewRules = new JsonCompanyRules { bAirlockPermission = false, bShoreLeave = false, bRestorePermission = true };
            company.mapRoster[player.strID] = playerRules; company.mapRoster[crew.strID] = crewRules;
            CrewSim.coPlayer = player;
            CrewSim.aSelected = new List<CondOwner> { crew };
            Check(ReferenceEquals(FunctionalHotkeys.SelectedCrew(), crew), "Selected crew did not receive hotkey");
            Check(FunctionalHotkeys.ToggleAirlock(FunctionalHotkeys.SelectedCrew()) && crewRules.bAirlockPermission, "Native permission was not enabled");
            Check(nativeSets == 1 && messages == 1 && ReferenceEquals(messageCrew, crew) && messageOwner == crew.strID && messageColor == "Neutral" && messageText == "Open airlock permission: ALLOWED.", "Feedback went to the wrong character/message channel");
            Check(!playerRules.bAirlockPermission && !crewRules.bShoreLeave && crewRules.bRestorePermission, "Hotkey changed another character or permission");
            Check(company.Clone().mapRoster[crew.strID].bAirlockPermission, "Permission did not survive native company/rule cloning");
            Check(FunctionalHotkeys.ToggleAirlock(crew) && !crewRules.bAirlockPermission && messageText == "Open airlock permission: DISALLOWED.", "Second activation did not disable permission");
            int beforeShore = nativeSets;
            Check(FunctionalHotkeys.ToggleDisembark(FunctionalHotkeys.SelectedCrew()) && crewRules.bShoreLeave, "Native disembark permission was not enabled");
            Check(nativeSets == beforeShore + 1 && ReferenceEquals(messageCrew, crew) && messageOwner == crew.strID && messageColor == "Neutral" && messageText == "Disembark permission: ALLOWED.", "Disembark feedback used the wrong character or message channel");
            Check(!crewRules.bAirlockPermission && crewRules.bRestorePermission && !playerRules.bShoreLeave, "Disembark hotkey changed another permission or character");
            Check(company.Clone().mapRoster[crew.strID].bShoreLeave, "Disembark permission did not survive native cloning");
            Check(FunctionalHotkeys.ToggleDisembark(crew) && !crewRules.bShoreLeave && messageText == "Disembark permission: DISALLOWED.", "Second activation did not disable disembark");
            CrewSim.aSelected.Clear();
            Check(ReferenceEquals(FunctionalHotkeys.SelectedCrew(), player), "No-selection default differs from native crew controls");
            Check(FunctionalHotkeys.ToggleAirlock(player) && playerRules.bAirlockPermission, "Captain could not change own roster permission");
            Check(FunctionalHotkeys.ToggleDisembark(FunctionalHotkeys.SelectedCrew()) && playerRules.bShoreLeave && !crewRules.bShoreLeave, "Captain's disembark hotkey affected the wrong roster entry");
            var npc = Actor("npc", company, ship); company.mapRoster[npc.strID] = new JsonCompanyRules();
            CrewSim.aSelected.Add(npc);
            Check(ReferenceEquals(FunctionalHotkeys.SelectedCrew(), npc), "NPC selection silently fell back to captain"); Rejected(FunctionalHotkeys.SelectedCrew(), playerRules);
            CrewSim.aSelected.Add(crew);
            Check(FunctionalHotkeys.SelectedCrew() == null, "Ambiguous multiple selection accepted"); Rejected(FunctionalHotkeys.SelectedCrew(), crewRules);
            Rejected(null, crewRules);
            crew.bAlive = false; Rejected(crew, crewRules); crew.bAlive = true;
            crew.bDestroyed = true; Rejected(crew, crewRules); crew.bDestroyed = false;
            Field(crew, "_isHumanOrRobot", false); Rejected(crew, crewRules); Field(crew, "_isHumanOrRobot", true);
            Field(ship, "nLoadState", Ship.Loaded.Shallow); Rejected(crew, crewRules); Field(ship, "nLoadState", Ship.Loaded.Full);
            Field(crew, "_objShip", null); Rejected(crew, crewRules); Field(crew, "_objShip", ship);
            Field(crew, "objCompany", new JsonCompany { mapRoster = new Dictionary<string, JsonCompanyRules> { [crew.strID] = crewRules } });
            Rejected(crew, crewRules); Field(crew, "objCompany", company);
            company.mapRoster.Remove(crew.strID); Rejected(crew, crewRules); company.mapRoster[crew.strID] = crewRules;
            company.mapRoster[crew.strID] = null; Rejected(crew, crewRules); company.mapRoster[crew.strID] = crewRules;
            Field(FunctionalHotkeys.Flag, "_typedValue", false); Rejected(crew, crewRules);
            EquipmentChecks();
            Console.WriteLine(checks + " hotkey permission/selection/equipment checks passed.");
            return 0;
        } finally {
            CrewSim.coPlayer = oldPlayer; CrewSim.aSelected = oldSelection;
            FunctionalHotkeys.Flag = null; crewConditions.Clear(); messageCrew = null;
            fixture.UnpatchSelf();
        }
    }
}
