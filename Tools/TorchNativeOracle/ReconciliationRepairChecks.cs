using System;
using System.Linq;
using System.IO;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using Ostrabus.MiscBugFixes;
using Ostrabus.TorchCalculator;
using Ostranauts.ShipGUIs.Utilities;

static partial class Program {
    static void RunReconciliationRepairChecks() {
        int before = checks;
        try {
            filterEmptyReactants = true;
            foreach (int pairs in new[] { 1, 2, 4 })
            foreach (string layout in new[] { "empty-spares", "no-spares", "water-full", "no-water", "no-helium", "overfilled", "partly-full" }) {
                var fit = new ReactorFit { Feeders = pairs, Lasers = pairs, Capacitors = 2, Regulators = 2 };
                var fusion = Make(fit,TorchModel.Ideal,.2,.5);
                Field(fusion,"fPelletMax",TorchModel.PelletMax(fit));
                ship.fFusionThrustMax = TorchModel.RatedThrust(TorchModel.PelletMax(fit));
                Values[d2o[0]]["StatVolume"] = 40.4;
                Values[d2o[0]]["StatLiqD2O"] = layout == "water-full" ? 40.4*1107 : layout == "overfilled" ? 50000 : 10000;
                Values[he3[0]]["StatVolume"] = 40.4;
                Values[he3[0]]["StatSolidHe3"] = 40.4*(double)129.11f;
                if (layout != "no-spares") for (int i=0;i<4;i++) {
                    var tank = Co(); Values[tank]["StatVolume"] = 40.4; he3.Add(tank);
                }
                if (layout == "partly-full") Values[he3[0]]["StatSolidHe3"] = 1000;
                if (layout == "no-water") d2o.Clear();
                if (layout == "no-helium") he3.Clear();
                double water = d2o.Sum(t=>Read(t,"StatLiqD2O")), helium = he3.Sum(t=>Read(t,"StatSolidHe3"));
                double usage = fusion.MassUsageMax;
                double available = Math.Min(water/(usage*TorchModel.D2ORatio),helium/usage);
                double target = available+4000;
                for (int i=0;i<80;i++) {
                    double oldD=d2o.Sum(t=>Read(t,"StatLiqD2O")), oldHe=he3.Sum(t=>Read(t,"StatSolidHe3"));
                    fusion.SetReactants(target);
                    double newD=d2o.Sum(t=>Read(t,"StatLiqD2O")), newHe=he3.Sum(t=>Read(t,"StatSolidHe3"));
                    Check(Math.Abs(newD-oldD-(newHe-oldHe)*TorchModel.D2ORatio)<1e-6,"Refund remains paired: "+layout);
                    Check(newD>=oldD-1e-6 && newHe>=oldHe-1e-6,"Refund unexpectedly debited a tank: "+layout);
                    if(i>0) Check(Math.Abs(newD-oldD)<1e-5 && Math.Abs(newHe-oldHe)<1e-5,"Repeated refund created fuel: "+layout);
                }
                double finalD=d2o.Sum(t=>Read(t,"StatLiqD2O")), finalHe=he3.Sum(t=>Read(t,"StatSolidHe3"));
                if(layout=="empty-spares" || layout=="partly-full") Check(finalHe>helium && finalD>water,"Empty receivers were not included");
                else Near(finalD,water,"Unavailable paired capacity leaves D2O unchanged",1e-9);
                double debitTarget = Math.Max(0, Math.Min(finalD/(usage*TorchModel.D2ORatio),finalHe/usage)-100);
                fusion.SetReactants(debitTarget);
                double afterD=d2o.Sum(t=>Read(t,"StatLiqD2O")), afterHe=he3.Sum(t=>Read(t,"StatSolidHe3"));
                Near(finalD-afterD,(finalHe-afterHe)*TorchModel.D2ORatio,"Ordinary expenditure keeps native ratio",1e-5);
            }
            var scoped=Make(new ReactorFit { Feeders=2,Lasers=2 },TorchModel.Ideal,.2,.5);
            Field(scoped,"fPelletMax",4d);ship.fFusionThrustMax=TorchModel.RatedThrust(4);
            foreach(bool helium in new[]{false,true}) {
                string condition=helium?"StatSolidHe3":"StatLiqD2O";
                var list=helium?he3:d2o;
                Values[list[0]][condition]=1000;Values[list[0]]["StatVolume"]=40.4;
                var foreign=Co();Values[foreign][condition]=1234;Values[foreign]["StatVolume"]=40.4;
                TankOwners[foreign]=Empty<Ship>();list.Insert(0,foreign);
                var destroyed=Co();Values[destroyed][condition]=5678;Values[destroyed]["StatVolume"]=40.4;
                destroyed.bDestroyed=true;list.Insert(0,destroyed);
            }
            scoped.SetReactants(0);
            Near(scoped.ReactantD,1000*(1-TorchModel.D2ORatio),"Owned excess D2O retained",1e-10);
            Check(scoped.ReactantHe3==0 && ship.fShallowFusionRemain==0,"Caches included foreign or destroyed tanks");
            foreach(bool helium in new[]{false,true}) {
                var list=helium?he3:d2o;string condition=helium?"StatSolidHe3":"StatLiqD2O";
                Check(Read(list[0],condition)==5678 && Read(list[1],condition)==1234,"Reconciliation crossed ship/destroyed ownership boundary");
            }
        } finally { filterEmptyReactants = false; }
        Console.WriteLine("Fusion reconciliation: "+(checks-before)+" paired/refund-capacity/empty-tank checks passed.");
    }

    static void RunNativeProfileRecoveryChecks() {
        foreach(bool lagged in new[]{false,true}) {
            var nav=SetupCourse(attach:false,pairs:2);
            double begin=fixtureEpoch, water=Read(d2o[0],"StatLiqD2O"), helium=Read(he3[0],"StatSolidHe3");
            // A temporary cold or power-only read must not blacklist this route.
            Values[core]["StatICCoreTemp"]=TorchModel.Ideal*.9;
            Check(TorchCourseFuel.Find(nav)==null,"Cold native course unexpectedly adopted");
            Values[core]["StatICCoreTemp"]=TorchModel.Ideal;
            Controls["knobRatio"]="0";
            Check(TorchCourseFuel.Find(nav)==null,"Power-only native course unexpectedly adopted");
            Controls["knobRatio"]="1";
            Controls["slidFlow"]=Number(TorchCourseFuel.BalancedFlow(Read(core,"StatICPellMax"),.2,TorchModel.Ideal,.27));
            StarSystem.fEpoch=begin+600;
            if(!lagged)fixtureEpoch=StarSystem.fEpoch;
            Check(nav.TimeAdvance(ship.objSS),"Eligible native course did not continue");
            var profile=TorchCourseFuel.Find(nav,false);
            Check(profile?.AdoptedNative==true && !profile.Invalid,"Native course remained outside corrected accounting");
            Check(Read(d2o[0],"StatLiqD2O")<=water && Read(he3[0],"StatSolidHe3")<=helium,"Adoption recreated past fuel");
            double settledD=Read(d2o[0],"StatLiqD2O"), settledHe=Read(he3[0],"StatSolidHe3");
            fixtureEpoch=StarSystem.fEpoch; playbackReactor.CatchUp();
            Near(Read(d2o[0],"StatLiqD2O"),settledD,"Late clock catch-up double charged adopted D2O",1e-7);
            Near(Read(he3[0],"StatSolidHe3"),settledHe,"Late clock catch-up double charged adopted He-3",1e-7);
            Check(TorchCourseFuel.Playing==null && TorchCourseFuel.PlayingNav==null,"Adoption leaked its playback scope");
        }
        // A genuinely unaffordable remainder aborts rather than being funded by
        // a rebase. Unknown budget formats retain native handling.
        var unfunded=SetupCourse(attach:false,pairs:2);
        Controls["slidFlow"]=Number(TorchCourseFuel.BalancedFlow(Read(core,"StatICPellMax"),.2,TorchModel.Ideal,.27));
        Values[d2o[0]]["StatLiqD2O"]=1;Values[he3[0]]["StatSolidHe3"]=1;
        Check(!AdvanceCourse(unfunded,101) && ship.objSS.NavData==null,"Unfunded native remainder was allowed to continue");
        var unknown=SetupCourse(attach:false,pairs:2);
        unknown.Destination.TorchFuelLevel-=123;
        TorchCourseFuel.Find(unknown);
        Check(AdvanceCourse(unknown,101) && TorchCourseFuel.Find(unknown,false)==null,"Unknown budget format was automatically rewritten");
        RunNativeBudgetFixtureChecks();
    }

    static void RunNativeBudgetFixtureChecks() {
        var saved=JObject.Parse(File.ReadAllText("Tests/Fixtures/Torch/NativeBudgetCoast.json"));
        foreach(bool lagged in new[]{false,true}) {
            SetupCourse(attach:false,pairs:2);
            double begin=(double)saved["epoch"], capacity=(double)saved["capacity"], volume=(double)saved["volume"];
            Values[core]["StatICPellMax"]=capacity;
            Field(playbackReactor,"fPelletMax",capacity);
            foreach(string field in new[]{"aLasers","aPellets"})
                foreach(var module in (System.Collections.Generic.List<CondOwner>)AccessTools.Field(typeof(FusionIC),field).GetValue(playbackReactor))
                    Values[module]["StatDamage"]=(1-capacity/4)*6;
            ship.fFusionThrustMax=TorchModel.RatedThrust(capacity);
            Values[d2o[0]]["StatLiqD2O"]=(double)saved["water"];
            Values[d2o[0]]["StatVolume"]=volume;
            he3.Clear();
            foreach(double amount in saved["helium"]) {
                var tank=Co();Values[tank]["StatSolidHe3"]=amount;Values[tank]["StatVolume"]=volume;he3.Add(tank);
            }
            var nav=new NavData(ship) { IsTorching=true };
            foreach(var point in saved["points"]) {
                var situation=new ShipSitu(false);
                situation.vVelX=(double)point["vx"];situation.vVelY=(double)point["vy"];
                nav.AddNavDataPoint(new NavDataPoint((double)point["time"],situation,0,(double)point["cycle"],(double)point["fuel"],rcsCount:0F));
            }
            nav.SetFlowMultPlot((double)saved["flow"]);
            ship.objSS.NavData=nav;
            fixtureEpoch=StarSystem.fEpoch=begin;
            Field(nav,"fEpochLast",begin);Field(playbackReactor,"fTimeLastRun",begin);
            Field(playbackReactor,"fTimeNextRun",begin+.27);
            Controls["slidCycle"]="0";Controls["slidFlow"]="0";
            double initialD=Read(d2o[0],"StatLiqD2O"),initialHe=he3.Sum(t=>Read(t,"StatSolidHe3"));
            double expectedSeconds=0;
            var points=nav.GetNavDataPoints();
            for(int i=1;i<points.Count;i++) {
                double seconds=Math.Max(0,points[i].ArrivalTime-Math.Max(begin,points[i-1].ArrivalTime));
                double cycle=IntervalCycle(points[i-1],points[i]);
                expectedSeconds+=SegmentFuel(cycle,cycle,seconds,capacity);
            }
            double through=nav.Destination.ArrivalTime;
            while(StarSystem.fEpoch<through) {
                double next=Math.Min(through,StarSystem.fEpoch+21600);
                StarSystem.fEpoch=next;if(!lagged)fixtureEpoch=next;
                Check(nav.TimeAdvance(ship.objSS),"Saved native-budget fixture failed playback");
                fixtureEpoch=next;playbackReactor.CatchUp();
                Check(TorchCourseFuel.Find(nav,false)?.AdoptedNative==true,"Saved native budget was not adopted at "+(next-begin)+" lagged="+lagged+" temp="+Read(core,"StatICCoreTemp"));
                Check(Read(d2o[0],"StatLiqD2O")<=initialD && he3.Sum(t=>Read(t,"StatSolidHe3"))<=initialHe,"Saved course gained physical fuel");
            }
            double heSpent=initialHe-he3.Sum(t=>Read(t,"StatSolidHe3")),dSpent=initialD-Read(d2o[0],"StatLiqD2O");
            // The first aligned CatchUp includes native idle expenditure before
            // adoption. Both paths must match the remaining analytic integral
            // to milligram precision, without refunding that earlier tiny cost.
            Check(Math.Abs(heSpent-expectedSeconds*playbackReactor.MassUsageMax)<.001,"Saved course He-3 disagrees with remaining burn/coast integral: "+heSpent+" expected="+(expectedSeconds*playbackReactor.MassUsageMax)+" lagged="+lagged);
            Check(Math.Abs(dSpent-heSpent*TorchModel.D2ORatio)<1e-6,"Saved course spent unpaired reactants");
            var restored=new NavData(LitJson.JsonMapper.ToObject<JsonNavData>(LitJson.JsonMapper.ToJson(nav.GetJSON())));
            ship.objSS.NavData=restored;
            Check(TorchCourseFuel.Find(restored)!=null,"Adopted route no longer recognizes its native JSON representation");
            ship.objSS.ResetNavData();
        }
    }
}
