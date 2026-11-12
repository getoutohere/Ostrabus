using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using Ostrabus.Diagnostics;
using Ostrabus.Inspection;
using Ostranauts.ShipGUIs.Utilities;
using UnityEngine;

static class Program {
    static int checks, nextId;
    static Ship ship;
    static CondOwner core;
    static FusionIC reactor;
    static List<CondOwner> water, helium, cryogen;
    static bool fail, sff;
    static double thrust;
    static readonly Exception gameError = new InvalidOperationException("native-fixture-error");
    static T Empty<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    static void Field(object o, string name, object value) => AccessTools.Field(o.GetType(),name).SetValue(o,value);
    static void Check(bool value, string why) { checks++; if (!value) throw new Exception(why); }
    static bool Equal(UnityEngine.Object __0, UnityEngine.Object __1, ref bool __result) { __result = ReferenceEquals(__0,__1); return false; }
    static bool Unequal(UnityEngine.Object __0, UnityEngine.Object __1, ref bool __result) { __result = !ReferenceEquals(__0,__1); return false; }
    static bool Quiet() => false;
    static double Amount(CondOwner co, string id) => TorchTraceData.Amount(co,id);
    static void Put(CondOwner co, string id, double amount) {
        if (!co.mapConds.TryGetValue(id,out var cond)) co.mapConds[id] = cond = Empty<Condition>();
        cond.fCount = amount;
    }
    static bool Get(CondOwner __instance, string __0, ref double __result) { __result = Amount(__instance,__0); return false; }
    static bool Has(CondOwner __instance, string __0, ref bool __result) { __result = Amount(__instance,__0)>0; return false; }
    static bool Set(CondOwner __instance, string __0, double __1) { Put(__instance,__0,__1); return false; }
    static bool Add(CondOwner __instance, string __0, double __1) { Put(__instance,__0,Math.Max(0,Amount(__instance,__0)+__1)); return false; }
    static bool Zero(CondOwner __instance, string __0) { Put(__instance,__0,0); return false; }
    static bool Gpm(CondOwner __instance, string __0, string __1, ref string __result) {
        __result = __instance.mapGUIPropMaps.TryGetValue(__0,out var panel) && panel.TryGetValue(__1,out var value) ? value : "false";
        return false;
    }
    static bool Clock(ref double __result) { __result = StarSystem.fEpoch; return false; }
    static bool Trigger(CondTrigger __instance, ref bool __result) { __result = ReferenceEquals(__instance,reactor.CTFuse); return false; }
    static bool Tanks(string __1, ref List<CondOwner> __result) { __result = __1 == "ItmCanisterLH02" ? water : __1 == "ItmCanisterLHe02" ? helium : cryogen; return false; }
    static bool Component(ref FusionIC __result) { __result = reactor; return false; }
    static bool InSff(ref bool __result) { __result = sff; return false; }
    static bool Thrust(double fAmount) { thrust = fAmount; ship.objSS.vAccIn.Set(0,(float)(fAmount/27000/149597863936)); return false; }
    static void OtherControls() {
        if (fail) throw gameError;
        core.mapGUIPropMaps["Panel A"]["slidFlow"] = ".1";
    }
    static bool Playback(NavData __instance, ref bool __result) {
        reactor.SetReactants(1000);
        ship.objSS.vVelY += .000000001;
        __result = true;
        return false;
    }
    static CondOwner Co(string name = "part") {
        var co = Empty<CondOwner>();
        Field(co,"_strID",name + (++nextId)); Field(co,"_objShip",ship);
        co.strName = name; co.mapConds = new Dictionary<string,Condition>();
        co.mapGUIPropMaps = new Dictionary<string,Dictionary<string,string>>();
        co.Clock = Empty<Condowner.Utils.Clock>(); Put(co,"StatDamageMax",6);
        return co;
    }
    static List<CondOwner> Modules(int count, string name) {
        var parts = new List<CondOwner>();
        for (int i = 0; i < count; i++) parts.Add(Co(name));
        return parts;
    }
    static void Setup() {
        ship = Empty<Ship>(); ship.strRegID = "PLAYER";
        Field(ship,"nLoadState",Ship.Loaded.Full); Field(ship,"_mass",27000d);
        Field(ship,"<objSS>k__BackingField",new ShipSitu(false));
        CrewSim.system = Empty<StarSystem>(); CrewSim.system.dictShips = new Dictionary<string,Ship> { [ship.strRegID] = ship };
        CrewSim.objInstance = Empty<CrewSim>(); CrewSim.objInstance.coDicts = Empty<CODicts>();
        CrewSim.coPlayer = Co("player");
        core = Co("core"); Field(ship,"aCores",new List<CondOwner> { core });
        Put(core,"StatICCapA",1); Put(core,"StatICCoreTemp",.7250000238418579); Put(core,"StatICVe",70500000); Put(core,"IsTorchDrive",1);
        var power = Empty<Powered>(); power.jsonPI = new JsonPowerInfo(); Field(core,"pwr",power);
        var panel = new Dictionary<string,string>(); core.mapGUIPropMaps["Panel A"] = panel;
        foreach (string key in new[] { "chkIgnition", "chkPellet", "chkCryo", "chkCoilFwd", "chkAlign", "chkFuelReg" }) panel[key] = "true";
        panel["knobRatio"] = "1"; panel["knobBus"] = "2"; panel["knobPump"] = "0"; panel["slidCycle"] = ".3"; panel["slidFlow"] = ".8";
        reactor = Empty<FusionIC>(); Field(reactor,"co",core); Field(reactor,"bIgnition",true);
        Field(reactor,"fReactantD",1000000d); Field(reactor,"fReactantHe3",1000000d);
        Field(reactor,"aModules",new List<CondOwner>());
        foreach (string list in new[] { "aLasers", "aPellets", "aCaps", "aFuelRegs", "aCryos", "aCoils" }) Field(reactor,list,Modules(1,list));
        Field(reactor,"aCorePumps",Modules(0,"pump")); Field(reactor,"aMHDs",Modules(0,"mhd"));
        foreach (string ct in new[] { "_ctFuse", "_ctShutDown", "_ctReactantA", "_ctReactantB", "_ctCryo" }) Field(reactor,ct,Empty<CondTrigger>());
        Field(reactor,"aeAlarm",Empty<AudioEmitter>());
        water = Modules(1,"water"); helium = Modules(1,"helium"); cryogen = Modules(1,"cryo");
        Put(water[0],"StatLiqD2O",1000000); Put(helium[0],"StatSolidHe3",1000000);
        CrewSim.objInstance.coDicts.COMapList = new Dictionary<string,List<CondOwner>> { ["ItmCanisterLH02"] = water, ["ItmCanisterLHe02"] = helium };
        StarSystem.fEpoch = 1000;
    }
    static int Owned() => Harmony.GetAllPatchedMethods().Count(m => Harmony.GetPatchInfo(m).Owners.Contains(TorchTrace.Owner));
    static FlightJournalWriter Writer() {
        var session = AccessTools.Field(typeof(TorchTrace),"session").GetValue(null);
        return (FlightJournalWriter)AccessTools.Field(session.GetType(),"Writer").GetValue(session);
    }
    static void Drain() => Check(Writer().Completion.Wait(10000) && Writer().Error == null,"Trace writer did not drain: " + Writer().Error);
    static void Main() {
        string folder = Path.GetFullPath("obj/torch-trace-tests"); Directory.CreateDirectory(folder);
        var fixture = new Harmony("offline.torch-trace.boundaries");
        var other = new Harmony("offline.torch-trace.other-owner");
        void Patch(Type type, string name, string prefix) => fixture.Patch(AccessTools.Method(type,name),prefix:new HarmonyMethod(typeof(Program),prefix));
        try {
            Patch(typeof(UnityEngine.Object),"op_Equality",nameof(Equal)); Patch(typeof(UnityEngine.Object),"op_Inequality",nameof(Unequal));
            foreach (var method in AccessTools.GetDeclaredMethods(typeof(CondOwner))) {
                if (method.Name == "GetCondAmount") fixture.Patch(method,prefix:new HarmonyMethod(typeof(Program),nameof(Get)));
                if (method.Name == "HasCond") fixture.Patch(method,prefix:new HarmonyMethod(typeof(Program),nameof(Has)));
            }
            Patch(typeof(CondOwner),"SetCondAmount",nameof(Set)); Patch(typeof(CondOwner),"AddCondAmount",nameof(Add)); Patch(typeof(CondOwner),"ZeroCondAmount",nameof(Zero));
            Patch(typeof(CondOwner),"GetGPMInfo",nameof(Gpm)); Patch(typeof(Condowner.Utils.Clock),"get_Epoch",nameof(Clock));
            Patch(typeof(CODicts),"GetTriggeredCOListByType",nameof(Tanks)); Patch(typeof(CondTrigger),"Triggered",nameof(Trigger));
            Patch(typeof(FusionIC),"WearModule",nameof(Quiet)); Patch(typeof(Powered),"UserPowerExt",nameof(Quiet));
            Patch(typeof(Ship),"SetThrust",nameof(Thrust)); Patch(typeof(Ship),"UnlockFromOrbit",nameof(Quiet));
            Patch(typeof(TorchTrace),"Component",nameof(Component)); Patch(typeof(GUIFFWD),"get_Active",nameof(InSff));
            Patch(typeof(NavData),"TimeAdvance",nameof(Playback));
            var run = AccessTools.Method(typeof(FusionIC),"Run");
            other.Patch(run,prefix:new HarmonyMethod(typeof(Program),nameof(OtherControls)));
            Setup(); Check(!TorchTrace.Enabled && Owned() == 0,"Trace installed before enable");
            run.Invoke(reactor,new object[] { .27 });
            double expectedTemp = Amount(core,"StatICCoreTemp"), expectedFuel = Amount(helium[0],"StatSolidHe3"), expectedThrust = thrust;
            Setup();
            string path = Path.Combine(folder,Guid.NewGuid().ToString("N") + ".jsonl");
            TorchTrace.Start(path);
            Check(TorchTrace.Enabled && Owned() == 5,"Expected exactly five temporary observed methods");
            TorchTrace.Start(path); Check(Owned() == 5,"Repeated enable duplicates hooks");
            run.Invoke(reactor,new object[] { .27 });
            Check(Math.Abs(expectedTemp-Amount(core,"StatICCoreTemp")) < 1e-12,"Trace changed native heat balance");
            Check(Math.Abs(expectedFuel-Amount(helium[0],"StatSolidHe3")) < 1e-12 && thrust == expectedThrust,"Trace changed native fuel/thrust");
            var state = TorchTraceData.Read(ship,core,reactor);
            Check(state.PelletCapacity > 0 && state.FuelScanAvailable && state.He3Total == expectedFuel,"Snapshot does not reflect actual native/tank state");
            Field(ship,"_mass",0d);
            Check(!TorchTraceData.Read(ship,core,reactor).GroupMassComplete && (double)AccessTools.Field(typeof(Ship),"_mass").GetValue(ship) == 0,
                "Diagnostic read initialized or concealed an invalid native mass cache");
            Field(ship,"_mass",27000d);
            var tow = Empty<Ship>(); tow.strRegID = "TOW";
            Field(tow,"_mass",10000d); Field(tow,"<objSS>k__BackingField",new ShipSitu(false));
            var group = new SolarSystem.DockGroup();
            group.DockedShips.Add(new SolarSystem.DockGroupMember(ship,false));
            var member = new SolarSystem.DockGroupMember(tow,false); group.DockedShips.Add(member);
            Field(ship.objSS,"_dockGroup",group);
            Check(TorchTraceData.Read(ship,core,reactor).GroupMassCacheKg == 37000,"Towed mass not included in inspection");
            Field(member,"_ship",null); CrewSim.system.dictShips[tow.strRegID] = tow;
            Check(!TorchTraceData.Read(ship,core,reactor).GroupMassComplete && AccessTools.Field(member.GetType(),"_ship").GetValue(member) == null,
                "Inspection lazily repaired a stale dock-member reference");
            Field(ship.objSS,"_dockGroup",null);
            var deep = TorchTraceData.Deep(ship,core,reactor);
            Check(deep.Modules.Length == 6 && deep.Tanks.Length == 2 && deep.Internals.ContainsKey("nPellets"),"Deep module/tank inspection incomplete");
            var statusJson = JObject.Parse(SnapshotJson.Encode(TorchTrace.Snapshot()));
            Check(statusJson["active"].Value<bool>() && statusJson["latest"]["Before"]["ShipId"].Value<string>() == "PLAYER",
                "Active status cannot pass the actual restricted HTTP serializer");
            var inspectJson = JObject.Parse(SnapshotJson.Encode(TorchTrace.Inspect(ship.strRegID)));
            Check(inspectJson["resolved"].Value<bool>() && inspectJson["reactor"]["Modules"].Count() == 6,
                "Deep snapshot cannot pass the actual restricted HTTP serializer");
            deep.Modules[0].Damage = 99;
            Check(Amount(((List<CondOwner>)AccessTools.Field(typeof(FusionIC),"aLasers").GetValue(reactor))[0],"StatDamage") == 0,"Deep snapshot retains live mutable state");
            // Real SetReactants executes native tank debits; only scene/condition
            // boundaries are prescribed. The flight integrator is a fixture here.
            var nav = Empty<NavData>(); Field(nav,"_shipUs",ship);
            Field(nav,"_navPoints",new List<NavDataPoint> { new NavDataPoint(1000,new ShipSitu(false),100,0.3f,2000), new NavDataPoint(2000,new ShipSitu(false),100,0.3f,1000) });
            ship.objSS.NavData = nav; sff = true; StarSystem.fEpoch += 3600;
            Check(nav.TimeAdvance(ship.objSS),"Trace changed native return value");
            sff = false;
            fail = true; StarSystem.fEpoch += 60;
            try { run.Invoke(reactor,new object[] { .27 }); Check(false,"Expected original exception"); }
            catch (TargetInvocationException ex) { Check(ReferenceEquals(ex.InnerException,gameError),"Trace replaced the original exception"); }
            fail = false;
            TorchTrace.Stop(); Drain();
            Check(!TorchTrace.Enabled && Owned() == 0,"Disable left diagnostic hooks installed");
            Check(Harmony.GetPatchInfo(run).Owners.Contains(other.Id),"Trace removed another owner's hook");
            var rows = File.ReadLines(path).Select(JObject.Parse).ToArray();
            Check((string)rows[0]["format"] == "ostrabus-torch-trace","Incorrect trace format/units header");
            var records = rows.Where(r => ((string)r["Kind"])?.StartsWith("torch-") == true).ToArray();
            Check(rows.Last()["kind"]?.ToString() == "journal-end" && records.Length >= 4,"Trace file missing complete records/footer");
            var burn = records.First(r => (string)r["Kind"] == "torch-reactor-run")["Diagnostic"]["Observation"];
            Check((double)burn["Before"]["Flow"] == .8 && (double)burn["Applied"]["Flow"] == .1 && (bool)burn["AppliedCaptured"],"Trace missed applied controls after another prefix");
            Check((double)burn["Before"]["He3Total"] > (double)burn["After"]["He3Total"],"Trace missed native reactor fuel consumption");
            var writeback = records.First(r => (string)r["Kind"] == "torch-fuel-writeback")["Diagnostic"]["Observation"];
            Check((bool)writeback["FuelArgumentCaptured"] && (double)writeback["RequestedFuelBudget"] == 1000 && (bool)writeback["After"]["InSff"],"SFF writeback was conflated with reactor fuel burn");
            Check(records.Any(r => ((string)r["Diagnostic"]["Observation"]["Error"])?.Contains("native-fixture-error") == true),"Native exception not recorded");
            Check(JObject.FromObject(TorchTrace.Snapshot())["active"].Value<bool>() == false,"Stopped trace not inspectable");
            Check(JObject.Parse(SnapshotJson.Encode(TorchTrace.Snapshot()))["active"].Value<bool>() == false,
                "Stopped status cannot pass the actual restricted HTTP serializer");
            for (int i = 0; i < 3; i++) {
                TorchTrace.Start(Path.Combine(folder,Guid.NewGuid().ToString("N") + ".jsonl"));
                TorchTrace.Stop(); Drain(); Check(Owned() == 0,"Repeated toggle leaked hooks");
            }
            var many = helium[0];
            for (int i = 0; i < 70; i++) helium.Add(many);
            Check(TorchTraceData.Read(ship,core,reactor).FuelScanTruncated && TorchTraceData.Deep(ship,core,reactor).TanksTruncated,
                "Bounded tank capture failed to disclose truncation");
            helium.RemoveRange(1,helium.Count-1);
            TorchTrace.Start(Path.Combine(folder,Guid.NewGuid().ToString("N") + ".jsonl"));
            var oldWorld = CrewSim.system;
            CrewSim.system = Empty<StarSystem>(); CrewSim.system.dictShips = oldWorld.dictShips;
            run.Invoke(reactor,new object[] { .27 }); Drain();
            Check(!TorchTrace.Enabled && Owned() == 0,"Simulation replacement left diagnostic hooks/references alive");
            string blocked = Path.Combine(folder,Guid.NewGuid().ToString("N")); File.WriteAllText(blocked,"occupied");
            TorchTrace.Start(Path.Combine(blocked,"trace.jsonl"));
            Check(Writer().Completion.Wait(10000) && Writer().Error != null,"Writer error was hidden");
            run.Invoke(reactor,new object[] { .27 });
            Check(!TorchTrace.Enabled && Owned() == 0,"Writer failure did not stop trace hooks on the next observed call");
            Console.WriteLine(checks + " native torch trace checks passed.");
        } finally { TorchTrace.Stop(); fixture.UnpatchSelf(); other.UnpatchSelf(); }
    }
}
namespace Ostrabus.Orbital { internal static class OrbitalSimulation { internal static object Snapshot() => null; } }
