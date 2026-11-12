using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Ostranauts.Ships.AIPilots;

static partial class Program {
    static bool NoInspectionNavigation(ref object __result) { __result=null;return false; }
    static bool ForbiddenControllerGetter() => throw new Exception("Inspection used repairing native AIShip.Ship getter");
    static JObject ControllerRequest(string path, out int status, int offset=0, int limit=50) {
        var inspection=typeof(Ostrabus.OstrabusPlugin).Assembly.GetType("Ostrabus.Inspection.InspectionSnapshots",true);
        var reply=AccessTools.Method(inspection,"Query").Invoke(null,new object[]{path,offset,limit,long.MaxValue,500d});
        status=(int)AccessTools.Field(reply.GetType(),"Status").GetValue(reply);
        return JObject.Parse(JsonConvert.SerializeObject(AccessTools.Field(reply.GetType(),"Body").GetValue(reply)));
    }
    static void CheckControllerInspection() {
        var boundary=new Harmony("offline.controller.inspection.boundaries");
        try {
            var assembly=typeof(Ostrabus.OstrabusPlugin).Assembly;
            // Only nested navigation/combat materialization is outside this
            // routing test. Controller selection and response building run real.
            foreach (var method in new[]{
                AccessTools.Method(assembly.GetType("Ostrabus.Inspection.NavigationInspection",true),"ShipRoute"),
                AccessTools.Method(assembly.GetType("Ostrabus.Inspection.CombatInspection",true),"AI")
            }) boundary.Patch(method,prefix:new HarmonyMethod(typeof(Program),nameof(NoInspectionNavigation)));
            boundary.Patch(AccessTools.PropertyGetter(typeof(AIShip),nameof(AIShip.Ship)),prefix:new HarmonyMethod(typeof(Program),nameof(ForbiddenControllerGetter)));
            var ship=MakeShip("O-TEST");
            CrewSim.system=Empty<StarSystem>();CrewSim.system.dictShips=new Dictionary<string,Ship>{{ship.strRegID,ship}};
            var a=Controller(ship);var b=Controller(ship);
            Back(a,"HomeStation","HOME");Back(b,"HomeStation","LOCAL");
            var replacement=MakeShip(ship.strRegID);var old=Controller(replacement);
            AIShipManager.dictAIs=new Dictionary<string,List<AIShip>>{{"OLD",new List<AIShip>{old}},{"INTERREGIONAL",new List<AIShip>{a}},{"LOCAL",new List<AIShip>{b}}};
            var data=ControllerRequest("/ai/controllers",out int status);
            Check(status==200 && data["items"].Count()==3,"Global controller route collapsed duplicate/stale hull entries");
            Check(!(bool)data["items"][0]["hullMatchesRegistry"] && (bool)data["items"][1]["hullMatchesRegistry"],"Global route hid replacement-object identity");
            data=ControllerRequest("/ships/O-TEST/controllers",out status,0,1);
            Check(status==200 && data["items"].Count()==1 && (int)data["nextOffset"]==1 && (bool)data["summary"]["multipleControllers"],"Hull controller route lost pagination or ambiguity");
            data=ControllerRequest("/ships/O-TEST/ai",out status);
            Check(status==200 && data["registrations"].Count()==3 && (bool)data["multipleControllers"],"Existing ship AI response still conceals competing controllers");
            Check(data["homeStation"].Type==JTokenType.Null && data["region"].Type==JTokenType.Null && data["combat"].Type==JTokenType.Null,
                "Existing AI response invents authoritative state for competing controllers");
            AIShipManager.dictAIs["LOCAL"].Clear();
            data=ControllerRequest("/ships/O-TEST/ai",out status);
            Check((string)data["region"]=="INTERREGIONAL" && (string)data["homeStation"]=="HOME" && !(bool)data["multipleControllers"],
                "Singular compatibility fields selected a stale same-ID hull instead of the exact current hull");
            CrewSim.system.dictShips.Clear();
            data=ControllerRequest("/ships/O-TEST/controllers",out status);
            Check(status==200 && data["items"].Count()==2 && (int)data["summary"]["staleHullReferences"]==2,"Missing hull blocks inspection of its stale controllers");
            data=ControllerRequest("/ships/ABSENT/controllers",out status);
            Check(status==200 && data["items"].Count()==0 && !(bool)data["summary"]["registered"],"Empty controller result is ambiguous despite a complete census");
            ControllerRequest("/ships/controllers",out status);Check(status==400,"Malformed controller route did not reject missing ID");
            ControllerRequest("/ships//controllers",out status);Check(status==400,"Empty controller ID did not return 400");
            Check(ReferenceEquals(AccessTools.Field(typeof(AIShip),"_shipUs").GetValue(old),replacement),"Inspection rebound a stale native hull pointer");
            var indexType=assembly.GetType("Ostrabus.Inspection.ControllerInspection",true);
            Check(!indexType.GetFields(BindingFlags.Static|BindingFlags.NonPublic|BindingFlags.Public).Any(f=>
                typeof(System.Collections.IEnumerable).IsAssignableFrom(f.FieldType) || f.FieldType==typeof(Ship) || f.FieldType==typeof(AIShip)),
                "Controller inspector retains a static game object collection");
            Check(indexType.GetCustomAttributes(typeof(HarmonyPatch),false).Length==0,"Controller inspection adds a gameplay hook");
            Console.WriteLine(checks+" controller inspection routing/identity checks passed.");
        } finally { boundary.UnpatchSelf(); }
    }
}
