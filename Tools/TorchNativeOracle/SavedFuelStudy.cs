using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using Ostrabus.MiscBugFixes;
using Ostranauts.ShipGUIs.Utilities;

static partial class Program {
    static Dictionary<string,double> SavedConditions(JToken row) {
        var result = new Dictionary<string,double>();
        foreach (string condition in row["conditions"] ?? new JArray()) {
            int equal = condition.IndexOf('='), split = condition.LastIndexOf('x');
            if (equal > 0 && split > equal && double.TryParse(condition.Substring(split+1), NumberStyles.Float, CultureInfo.InvariantCulture, out double amount))
                result[condition.Substring(0,equal)] = amount;
        }
        return result;
    }
    static void RunSavedFuelStudy(string path) {
        var saved = JObject.Parse(File.ReadAllText(path));
        var records = (JArray)saved["tanks"];
        var savedCore = records.First(r => ((string)r["type"] ?? "").StartsWith("ItmFusionReactorCore01", StringComparison.Ordinal));
        double begin = (double)savedCore["epoch"];
        foreach (bool filtered in new[] { false, true }) foreach (bool lagged in new[] { false, true }) {
            SetupCourse(attach:false,pairs:2);
            filterEmptyReactants = filtered;
            foreach (var pair in SavedConditions(savedCore)) Values[core][pair.Key] = pair.Value;
            double capacity = Read(core,"StatICPellMax");
            Field(playbackReactor,"fPelletMax",capacity);
            ship.fFusionThrustMax = Ostrabus.TorchCalculator.TorchModel.RatedThrust(capacity);
            d2o.Clear(); he3.Clear();
            foreach (var record in records) {
                string type = (string)record["type"];
                if(type != "ItmCanisterLH02" && type != "ItmCanisterLHe02")continue;
                var tank = Co();
                foreach(var pair in SavedConditions(record))Values[tank][pair.Key]=pair.Value;
                (type == "ItmCanisterLH02" ? d2o : he3).Add(tank);
            }
            CrewSim.objInstance.coDicts.COMapList["ItmCanisterLH02"] = d2o;
            CrewSim.objInstance.coDicts.COMapList["ItmCanisterLHe02"] = he3;
            fixtureEpoch=StarSystem.fEpoch=begin;
            ship.fShallowFusionRemain=(double)saved["shallowFuel"];
            ship.strRegID=(string)saved["ship"];
            CrewSim.system.dictShips.Clear(); CrewSim.system.dictShips.Add(ship.strRegID,ship);
            var nav = new NavData(saved["situation"]["jnd"].ToObject<JsonNavData>());
            ship.objSS.NavData=nav;
            Field(nav,"fEpochLast",begin);
            Field(playbackReactor,"fTimeLastRun",begin);
            Field(playbackReactor,"fTimeNextRun",begin+.27);
            bool attached=TorchCourseFuel.Attach(nav,true);
            Controls["slidCycle"] = Number(CourseCycleAt(nav,begin));
            Controls["slidFlow"] = Number(TorchCourseFuel.BalancedFlow(capacity,CourseCycleAt(nav,begin),Ostrabus.TorchCalculator.TorchModel.Ideal,.27));
            var freshCopy = new NavData(saved["situation"]["jnd"].ToObject<JsonNavData>());
            bool freshEligible = TorchCourseFuel.Attach(freshCopy,false);
            Console.WriteLine("Saved fuel: filtered="+filtered+" lagged="+lagged+" attach="+attached+" capacity="+capacity+" begin="+begin);
            Console.WriteLine("  freshPlanningEligible="+freshEligible+" warm="+Read(core,"StatICCoreTemp")+" running="+ship.bFusionReactorRunning);
            double initialD=d2o.Sum(t=>Read(t,"StatLiqD2O")),initialHe=he3.Sum(t=>Read(t,"StatSolidHe3"));
            int steps=0;
            while(StarSystem.fEpoch<nav.Destination.ArrivalTime && steps++<12) {
                double next=Math.Min(nav.Destination.ArrivalTime-.001,StarSystem.fEpoch+21600);
                if(next<=StarSystem.fEpoch)break;
                StarSystem.fEpoch=next;
                if(!lagged)fixtureEpoch=next;
                bool active=nav.TimeAdvance(ship.objSS);
                fixtureEpoch=next;
                playbackReactor.CatchUp();
                double water=d2o.Sum(t=>Read(t,"StatLiqD2O")),helium=he3.Sum(t=>Read(t,"StatSolidHe3"));
                var profile=TorchCourseFuel.Find(nav,false);
                Console.WriteLine("  seconds="+(next-begin)+" active="+active+" D2O="+water+" He3="+helium+" Dchange="+(initialD-water)+" HeChange="+(initialHe-helium)+" budget="+ship.fShallowFusionRemain+" adopted="+profile?.AdoptedNative+" invalid="+profile?.Invalid);
                if(!active || ship.objSS.NavData==null)break;
            }
        }
        RunRefundCapacityStudy();
        filterEmptyReactants=false;
    }

    // A controlled mechanism reproduction, not a historical replay: one full
    // He-3 tank, empty spare tanks and a partly filled D2O tank. Native reactant
    // selection requires a positive reactant condition and omits the empties.
    static void RunRefundCapacityStudy() {
        var cacheTarget = AccessTools.Method(typeof(FusionIC),nameof(FusionIC.SetReactants));
        var cachePostfix = AccessTools.Method(typeof(TorchFuelCachePatch),"Postfix");
        var cacheOwner = new Harmony("offline.saved-fuel.cache-control");
        cacheOwner.Unpatch(AccessTools.Method(typeof(FusionIC),"RemoveFromTanks"),AccessTools.Method(typeof(TorchFuelRefundRemainderPatch),"Transpiler"));
        foreach(string method in new[]{"Prefix","Postfix","Transpiler"})
            cacheOwner.Unpatch(cacheTarget,AccessTools.Method(typeof(TorchFuelCachePatch),method));
        foreach(bool cache in new[]{false,true}) {
            if(cache)cacheOwner.Patch(cacheTarget,postfix:new HarmonyMethod(cachePostfix));
            foreach(bool filtered in new[]{false,true}) {
                filterEmptyReactants=filtered;
                foreach(var tank in he3)Values[tank]["StatSolidHe3"]=0;
                Values[he3[he3.Count-1]]["StatSolidHe3"]=Read(he3[he3.Count-1],"StatVolume")*(double)129.11f;
                Values[d2o[0]]["StatLiqD2O"]=10000;
                double beforeHe=he3.Sum(t=>Read(t,"StatSolidHe3"));
                double target=beforeHe/playbackReactor.MassUsageMax+4000;
                for(int i=0;i<80;i++)playbackReactor.SetReactants(target);
                double water=d2o.Sum(t=>Read(t,"StatLiqD2O")),helium=he3.Sum(t=>Read(t,"StatSolidHe3"));
                Console.WriteLine("Refund control: cache="+cache+" nativeEmptyFilter="+filtered+" waterBefore=10000 waterAfter="+water+" heliumBefore="+beforeHe+" heliumAfter="+helium);
                if(filtered) {
                    Near(water,Read(d2o[0],"StatVolume")*1107,"Native reconciliation filled D2O without a transfer",1e-9);
                    Near(helium,beforeHe,"Full He-3 tank did not accept the matching refund",1e-9);
                } else Check(helium>beforeHe && water<20000,"Empty-tank selection control did not distinguish the refund loop");
            }
        }
        cacheOwner.UnpatchSelf();
        new Harmony("offline.torch.boundaries").CreateClassProcessor(typeof(TorchFuelCachePatch)).Patch();
        new Harmony("offline.torch.boundaries").CreateClassProcessor(typeof(TorchFuelRefundRemainderPatch)).Patch();
    }
}
