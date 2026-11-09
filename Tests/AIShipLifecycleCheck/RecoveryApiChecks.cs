using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Ostranauts.Ships.AIPilots;

static partial class Program {
    static int restoredFactories, inspectionCleanups;
    static bool Factory(Ship ship, AIType at, string atcLast, ref AIShip __result) {
        Check(at == AIType.Police && atcLast == "HQCH", "Recovery selects verified native police role/home");
        restoredFactories++;
        var ai = Controller(ship, Empty<PolicePilot>());
        Back(ai, "HomeStation", atcLast); AIShipManager.AddAIToDict(ai, atcLast); __result = ai; return false;
    }
    static bool InspectionCleanup(CondOwner coUs) {
        inspectionCleanups++;
        conditions[coUs].Remove("IsShakedownModeActive");
        return false;
    }
    static (int status, Dictionary<string, object> body) Recovery(string id, bool apply) {
        var type = typeof(Ostrabus.OstrabusPlugin).Assembly.GetType("Ostrabus.Inspection.ShipAIRecovery", true);
        var reply = type.GetMethod("Run", BindingFlags.Static|BindingFlags.NonPublic).Invoke(null, new object[] { "/ships/"+id+"/ai/restore", apply });
        return ((int)reply.GetType().GetField("Status", BindingFlags.Instance|BindingFlags.NonPublic).GetValue(reply),
            (Dictionary<string,object>)reply.GetType().GetField("Body", BindingFlags.Instance|BindingFlags.NonPublic).GetValue(reply));
    }
    static void CheckRecoveryApi(Harmony boundary) {
        var fixture = new Harmony("offline.police-recovery.factory");
        var oldLoot = DataHandler.dictLoot;
        try {
            fixture.Patch(AccessTools.Method(typeof(AIShipManager), nameof(AIShipManager.AddAIToShip), new[] { typeof(Ship), typeof(AIType), typeof(string) }), prefix: new HarmonyMethod(typeof(Program), nameof(Factory)));
            fixture.Patch(AccessTools.Method(typeof(Loot), nameof(Loot.ApplyCondLoot)), prefix: new HarmonyMethod(typeof(Program), nameof(InspectionCleanup)));
            DataHandler.dictLoot = new Dictionary<string,Loot> { ["CONDRemoveLEOAccuseOKLG"] = Empty<Loot>() };
            AIShipManager.strATCLast = "HQCH";
            AIShipManager.dictAIs = new Dictionary<string,List<AIShip>> { ["HQCH"] = new List<AIShip>() };
            var home = MakeShip("HQCH"); home.objSS.bIsBO = true; Set(home, "aDockingPorts", new List<string> { "dock" });
            var patrol = MakeShip("RECOVER-POLICE"); patrol.IsLocalAuthority = true; Set(patrol, "bAIShip", true);
            var officer = Person(patrol, "Returned Officer"); conditions[officer] = new HashSet<string> { "IsShakedownModeActive" };
            patrol.fShallowRCSRemass = 1234; patrol.objSS.vPosx = 1;
            Set(CrewSim.coPlayer, "_strID", "PLAYER");
            CrewSim.system.dictShips = new Dictionary<string,Ship> { [home.strRegID] = home, [patrol.strRegID] = patrol };
            Set(CrewSim.system, "dictShipOwners", new Dictionary<string,string> { [patrol.strRegID] = "HQCHLEO" });
            restoredFactories = inspectionCleanups = 0;
            var preview = Recovery(patrol.strRegID, false);
            Check(preview.status == 200 && (bool)preview.body["eligible"] && !(bool)preview.body["restored"] && Registered == 0 && restoredFactories == 0 && inspectionCleanups == 0,
                "Recovery GET preview has no controller or condition writes");
            var applied = Recovery(patrol.strRegID, true);
            Check(applied.status == 200 && (bool)applied.body["restored"] && Registered == 1 && restoredFactories == 1 && inspectionCleanups == 1, "Recovery POST registers exactly one verified patrol controller");
            Check(officer.HasCond("IsPledgeUndockDone") && !officer.HasCond("IsShakedownModeActive") && officer.ship == patrol, "Recovery completes native inspection/undock state without crew movement");
            Check(patrol.fShallowRCSRemass == 1234 && patrol.objSS.vPosx == 1, "Recovery does not refuel or teleport");
            Check(Recovery(patrol.strRegID, true).status == 409 && restoredFactories == 1, "Existing controller is never replaced or duplicated");
            AIShipManager.UnregisterShip(patrol); conditions[patrol.ShipCO].Remove("IsStale"); conditions[officer].Add("IsShakedownModeActive");
            Connect(patrol, home);
            Check(!(bool)Recovery(patrol.strRegID, false).body["eligible"], "Connected encounter cannot be recovered prematurely");
            ((Dictionary<string,Ship>)AccessTools.Field(typeof(Ship), "aDocked").GetValue(patrol)).Clear();
            conditions[officer].Add("Unconscious"); Check(Recovery(patrol.strRegID, true).status == 409, "Incapacitated officer cannot authorize recovery"); conditions[officer].Remove("Unconscious");
            conditions[patrol.ShipCO].Add("IsStale"); Check(Recovery(patrol.strRegID, true).status == 409, "Retired patrol is protected"); conditions[patrol.ShipCO].Remove("IsStale");
            patrol.objSS.vAccRCS = new UnityEngine.Vector2(1, 0); Check(Recovery(patrol.strRegID, true).status == 409, "Existing thrust is protected"); patrol.objSS.vAccRCS = default;
            patrol.IsLocalAuthority = false; Check(Recovery(patrol.strRegID, true).status == 409, "Haulers/unknown roles cannot be guessed into a police role");
            Check(restoredFactories == 1, "Refused recoveries never reach the factory");
        } finally { fixture.UnpatchSelf(); DataHandler.dictLoot = oldLoot; }
    }
}
