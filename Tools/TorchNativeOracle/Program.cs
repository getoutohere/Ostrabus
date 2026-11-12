using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using Ostranauts.ShipGUIs.Utilities;
using Ostranauts.Ships.Commands;
using Ostrabus.TorchCalculator;
using UnityEngine;

static partial class Program {
    static int checks;
    static CondOwner core;
    static Ship ship;
    static double mass;
    static readonly Dictionary<CondOwner, Dictionary<string,double>> Values = new Dictionary<CondOwner,Dictionary<string,double>>();
    static readonly Dictionary<string,string> Controls = new Dictionary<string,string>();
    static List<CondOwner> d2o, he3, cryogen;
    static T Empty<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    static void Field(object obj, string name, object value) => AccessTools.Field(obj.GetType(), name).SetValue(obj, value);
    static void Check(bool ok, string why) { checks++; if (!ok) throw new Exception(why); }
    static void Near(double actual, double expected, string why, double relative = 3e-6) => Check(Math.Abs(actual - expected) <= relative * Math.Max(1e-8, Math.Abs(expected)), why + ": " + actual + " != " + expected);
    static bool Equal(UnityEngine.Object __0, UnityEngine.Object __1, ref bool __result) { __result = ReferenceEquals(__0, __1); return false; }
    static bool Unequal(UnityEngine.Object __0, UnityEngine.Object __1, ref bool __result) { __result = !ReferenceEquals(__0, __1); return false; }
    static bool Skip() => false;
    static bool Self(ref CondOwner __result) { __result = core; return false; }
    static bool OwnerShip(ref Ship __result) { __result = ship; return false; }
    static bool Reactor(ref CondOwner __result) { __result = core; return false; }
    static bool Mass(ref double __result) { __result = mass; return false; }
    static double fixtureEpoch = 100;
    static bool Clock(ref double __result) { __result = fixtureEpoch; return false; }
    static bool Get(CondOwner __instance, string __0, ref double __result) { __result = Values[__instance].TryGetValue(__0, out double v) ? v : 0; return false; }
    static bool Has(CondOwner __instance, string __0, ref bool __result) { __result = Values[__instance].TryGetValue(__0, out double v) && v > 0; return false; }
    static bool Set(CondOwner __instance, string __0, double __1) { Values[__instance][__0] = __1; return false; }
    static bool Add(CondOwner __instance, string __0, double __1) { Values[__instance][__0] = Math.Max(0, Read(__instance, __0) + __1); return false; }
    static bool Zero(CondOwner __instance, string __0) { Values[__instance][__0] = 0; return false; }
    static double Read(CondOwner co, string key) => Values[co].TryGetValue(key, out double value) ? value : 0;
    static bool Gpm(string __1, ref string __result) { __result = Controls.TryGetValue(__1, out string value) ? value : "false"; return false; }
    static bool Trigger(CondTrigger __instance, ref bool __result) { __result = playbackReactor != null && ReferenceEquals(__instance, playbackReactor.CTFuse); return false; }
    static bool Tanks(string __1, ref List<CondOwner> __result) { __result = __1 == "ItmCanisterLH02" ? d2o : __1 == "ItmCanisterLHe02" ? he3 : cryogen; return false; }
    static bool NotPlaying(ref bool __result) { __result = false; return false; }
    static CondOwner Co() {
        var co = Empty<CondOwner>(); Values[co] = new Dictionary<string,double> { ["StatDamageMax"] = 6 };
        co.Clock = Empty<Condowner.Utils.Clock>(); return co;
    }
    static List<CondOwner> Modules(int count, double health = 1) {
        var result = new List<CondOwner>();
        for (int i = 0; i < count; i++) { var co = Co(); Values[co]["StatDamage"] = (1 - health) * 6; result.Add(co); }
        return result;
    }
    static FusionIC Make(ReactorFit fit, double temperature, double cycle, double flow) {
        Values.Clear(); core = Co(); mass = fit.MassKg; ship = Empty<Ship>();
        Field(ship, "<objSS>k__BackingField", new ShipSitu(false));
        var powered = Empty<Powered>(); powered.jsonPI = new JsonPowerInfo(); Field(core, "pwr", powered);
        Values[core]["StatICCapA"] = 1; Values[core]["StatICCoreTemp"] = temperature; Values[core]["StatICVe"] = fit.ExhaustVelocity;
        Controls.Clear();
        foreach (string key in new[] { "chkIgnition", "chkPellet", "chkCryo", "chkCoilFwd", "chkAlign", "chkFuelReg" }) Controls[key] = "true";
        Controls["knobRatio"] = "1"; Controls["knobBus"] = "2"; Controls["knobPump"] = "0";
        Controls["slidCycle"] = cycle.ToString(CultureInfo.InvariantCulture); Controls["slidFlow"] = flow.ToString(CultureInfo.InvariantCulture);
        var fusion = Empty<FusionIC>();
        Field(fusion, "co", core);
        Field(fusion, "bIgnition", true); Field(fusion, "fReactantD", 1e9); Field(fusion, "fReactantHe3", 1e9);
        Field(fusion, "aModules", new List<CondOwner>());
        Field(fusion, "aLasers", Modules(fit.Lasers, fit.LaserHealth)); Field(fusion, "aPellets", Modules(fit.Feeders, fit.FeederHealth));
        Field(fusion, "aCaps", Modules(fit.Capacitors)); Field(fusion, "aFuelRegs", Modules(fit.Regulators));
        Field(fusion, "aCryos", Modules(fit.Cryopumps, fit.CryoHealth)); Field(fusion, "aCoils", Modules(1));
        Field(fusion, "aCorePumps", Modules(0)); Field(fusion, "aMHDs", Modules(0));
        Field(fusion, "_ctFuse", Empty<CondTrigger>()); Field(fusion, "_ctShutDown", Empty<CondTrigger>());
        Field(fusion, "_ctReactantA", Empty<CondTrigger>()); Field(fusion, "_ctReactantB", Empty<CondTrigger>()); Field(fusion, "_ctCryo", Empty<CondTrigger>());
        Field(fusion, "aeAlarm", Empty<AudioEmitter>());
        d2o = Modules(1); he3 = Modules(1); cryogen = Modules(1);
        CrewSim.objInstance.coDicts.COMapList = new Dictionary<string, List<CondOwner>> {
            ["ItmCanisterLH02"] = d2o, ["ItmCanisterLHe02"] = he3
        };
        Values[d2o[0]]["StatLiqD2O"] = 1e9; Values[he3[0]]["StatSolidHe3"] = 1e9;
        return fusion;
    }
    static void Main(string[] args) {
        var h = new Harmony("offline.torch.boundaries");
        void Patch(MethodBase method, string name) => h.Patch(method, prefix: new HarmonyMethod(typeof(Program), name));
        Patch(AccessTools.Method(typeof(UnityEngine.Object), "op_Equality"), nameof(Equal));
        Patch(AccessTools.Method(typeof(UnityEngine.Object), "op_Inequality"), nameof(Unequal));
        Patch(AccessTools.PropertyGetter(typeof(FusionIC), "COSelf"), nameof(Self));
        Patch(AccessTools.PropertyGetter(typeof(CondOwner), "ship"), nameof(OwnerShip));
        Patch(AccessTools.PropertyGetter(typeof(Ship), "Reactor"), nameof(Reactor));
        Patch(AccessTools.PropertyGetter(typeof(Ship), "Mass"), nameof(Mass));
        Patch(AccessTools.PropertyGetter(typeof(Condowner.Utils.Clock), "Epoch"), nameof(Clock));
        foreach (var method in AccessTools.GetDeclaredMethods(typeof(CondOwner))) {
            if (method.Name == "GetCondAmount") Patch(method, nameof(Get));
            if (method.Name == "HasCond") Patch(method, nameof(Has));
        }
        Patch(AccessTools.Method(typeof(CondOwner), "SetCondAmount"), nameof(Set));
        Patch(AccessTools.Method(typeof(CondOwner), "AddCondAmount"), nameof(Add));
        Patch(AccessTools.Method(typeof(CondOwner), "ZeroCondAmount"), nameof(Zero));
        Patch(AccessTools.Method(typeof(CondOwner), "GetGPMInfo"), nameof(Gpm));
        Patch(AccessTools.Method(typeof(CODicts), "GetTriggeredCOListByType"), nameof(Tanks));
        Patch(AccessTools.Method(typeof(CondTrigger), "Triggered"), nameof(Trigger));
        Patch(AccessTools.Method(typeof(FusionIC), "WearModule"), nameof(Skip));
        Patch(AccessTools.Method(typeof(Powered), "UserPowerExt"), nameof(Skip));
        Patch(AccessTools.Method(typeof(Ship), "SetThrust"), nameof(ThrustBoundary));
        Patch(AccessTools.PropertyGetter(typeof(AudioEmitter), "PlayingSteady"), nameof(NotPlaying));
        Patch(AccessTools.Method(typeof(Debug), "LogWarning", new[] { typeof(object) }), nameof(Skip));
        CrewSim.objInstance = Empty<CrewSim>(); CrewSim.objInstance.coDicts = Empty<CODicts>();
        try {
            if (args.Length == 1 && args[0] == "transfer-playback") {
                h.CreateClassProcessor(typeof(Ostrabus.MiscBugFixes.TorchFuelCachePatch)).Patch();
                RunPlaybackStudy(transferChecksOnly:true); return;
            }
            if (args.Length == 1 && args[0] == "sff-clock") {
                h.CreateClassProcessor(typeof(Ostrabus.MiscBugFixes.TorchFuelCachePatch)).Patch();
                RunPlaybackStudy(clockChecksOnly:true);return;
            }
            if (args.Length == 1 && args[0] == "transfer-planner") { RunTransferPlannerChecks(h); return; }
            if (args.Length == 1 && args[0] == "transfer-solver") { RunTransferSolverChecks(); return; }
            if (args.Length == 1 && args[0] == "initial-velocity") { RunInitialVelocityStudy(); return; }
            if (args.Length == 1 && args[0] == "barter-reactor") { RunBarterReactorStudy(h); return; }
            if (args.Length == 1 && args[0] == "control-handoff") {
                h.CreateClassProcessor(typeof(Ostrabus.MiscBugFixes.TorchFuelCachePatch)).Patch();
                RunPlaybackStudy(true);
                return;
            }
            var run = AccessTools.Method(typeof(FusionIC), "Run"); var burn = AccessTools.Method(typeof(FusionIC), "Fusion");
            foreach (int pairs in new[] { 1, 2, 3, 4 }) foreach (double health in new[] { 1d, .6 }) foreach (double cycle in new[] { 0d, .2, .4, .8, 1 }) foreach (double temp in new[] { .6, TorchModel.Ideal, .74 }) {
                var fit = new ReactorFit { Feeders = pairs, Lasers = pairs, Capacitors = 2, Regulators = 2, FeederHealth = health, LaserHealth = health };
                double p = TorchModel.PelletMax(fit), flow = .75, dt = TorchModel.TickSeconds;
                var fusion = Make(fit, temp, cycle, flow);
                run.Invoke(fusion, new object[] { dt });
                var predicted = TorchModel.Step(temp, cycle, flow, p, (double).6f, dt);
                Near(Read(core, "StatICPellMax"), p, "Native module capacity");
                Near(Read(core, "StatICCoreTemp"), predicted.Temperature, "Native thermal step");
                Near(Read(core, "StatICPellRate"), predicted.PelletRate, "Native pellet rate");
                burn.Invoke(fusion, new object[] { dt, new JsonPowerInfo(), 0d });
                Near(ship.fFusionThrustMax, TorchModel.RatedThrust(p), "Native rated thrust");
                Near(fusion.MassUsageMax, TorchModel.MassUsageMax(ship.fFusionThrustMax), "Native rated fuel rate");
                Near(Read(core, "StatICHe3Rate"), TorchModel.ManualHe3Rate(predicted.Temperature, predicted.PelletRate), "Native manual He-3 rate");
                Near(Read(core, "StatICDRate"), Read(core, "StatICHe3Rate") * TorchModel.D2ORatio, "Native D2O ratio");
                foreach (double m in new[] { 16000d, 32000, 100000 }) {
                    mass = m;
                    Near(ship.GetMaxTorchThrust((float)cycle), TorchModel.AccelerationAu(m, p, ship.fFusionThrustMax, cycle), "Native thrust law", 2e-7);
                }
                if (cycle > 0) foreach (double efficiency in new[] { 1d, .75 }) {
                    Field(ship, "fFuelEfficiencyMod", efficiency);
                    double deltaV = ship.GetMaxTorchThrust((float)cycle) * 3600;
                    Near(ship.CalculateTorchFuelConsumption(deltaV, (float)cycle), 3600 * efficiency, "Native plotted burn seconds");
                }
            }
            foreach (var config in new[] { new ReactorFit { Feeders = 4, Lasers = 4 }, new ReactorFit { Feeders = 0 }, new ReactorFit { Lasers = 0 } }) {
                var fusion = Make(config, TorchModel.Ideal, .4, 1);
                run.Invoke(fusion, new object[] { TorchModel.TickSeconds });
                Near(Read(core, "StatICPellMax"), TorchModel.PelletMax(config), "Native support/missing-module cap");
            }
            // The tank conversion is real native SetReactants/RemoveFromTanks, not just the displayed rate.
            foreach (int pairs in new[] { 1, 2, 4 }) {
                var fit = new ReactorFit { Feeders = pairs, Lasers = pairs, Capacitors = 2, Regulators = 2 };
                var fusion = Make(fit, TorchModel.Ideal, .3, 1);
                Field(fusion, "fPelletMax", TorchModel.PelletMax(fit));
                ship.fFusionThrustMax = TorchModel.RatedThrust(TorchModel.PelletMax(fit));
                double usage = fusion.MassUsageMax;
                Values[d2o[0]]["StatLiqD2O"] = usage * TorchModel.D2ORatio * 100000;
                Values[he3[0]]["StatSolidHe3"] = usage * 100000;
                fusion.SetReactants(96400);
                Near(Read(he3[0], "StatSolidHe3"), usage * 96400, "Native plotted He-3 tank debit", 1e-10);
                Near(Read(d2o[0], "StatLiqD2O"), usage * TorchModel.D2ORatio * 96400, "Native plotted D2O tank debit", 1e-10);
            }
            // Reproduce the native playback boundary: Fusion refreshes the ship cache,
            // then NavData.TimeAdvance calls SetReactants to reconcile physical tanks.
            // These are defect-characterization checks, not a proposed gameplay patch.
            foreach (double target in new[] { 4000d, 24000d }) {
                var fit = new ReactorFit { Feeders = 1, Lasers = 1 };
                var fusion = Make(fit, TorchModel.Ideal, .3, 1);
                Field(fusion, "fPelletMax", TorchModel.PelletMax(fit));
                ship.fFusionThrustMax = TorchModel.RatedThrust(TorchModel.PelletMax(fit));
                double usage = fusion.MassUsageMax;
                Values[d2o[0]]["StatVolume"] = 40.4;
                Values[he3[0]]["StatVolume"] = 40.4;
                Values[d2o[0]]["StatLiqD2O"] = usage * TorchModel.D2ORatio * 30000;
                Values[he3[0]]["StatSolidHe3"] = usage * 30000;
                Values[core]["StatICPellRate"] = 2;
                Values[core]["StatICPwrTotal"] = (double).35f;
                Values[core]["IsTorchDrive"] = 1;
                burn.Invoke(fusion, new object[] { 21600d, new JsonPowerInfo(), 0d });
                Near(ship.fShallowFusionRemain, 8400, "Six-hour reactor catch-up establishes low cache", 1e-6);
                fusion.SetReactants(target);
                double tankSeconds = Math.Min(Read(he3[0], "StatSolidHe3") / usage,
                    Read(d2o[0], "StatLiqD2O") / (usage * TorchModel.D2ORatio));
                Near(tankSeconds, target, "Playback reconciles actual tanks", 1e-10);
                Near(ship.fShallowFusionRemain, 8400, "BUG: playback leaves cached remaining fuel stale", 1e-6);
                if (target == 24000) {
                    Check(ship.fShallowFusionRemain < target / 2, "Stale cache rejects affordable waypoint");
                    Check(tankSeconds >= target / 2, "Physical tanks pass the same native guard");
                    StarSystem.fEpoch = 100;
                    var nav = new NavData(ship);
                    nav.AddNavDataPoint(new NavDataPoint(99, new ShipSitu(false), 0, .3, 25000));
                    nav.AddNavDataPoint(new NavDataPoint(101, new ShipSitu(false), 0, .3, target));
                    ship.objSS.NavData = nav;
                    var autopilot = Empty<FlyToAutoPilot>();
                    Field(autopilot, "_shipUs", ship);
                    var requirements = AccessTools.Method(typeof(FlyToAutoPilot), "RequirementsMet");
                    Check(!(bool)requirements.Invoke(autopilot, null), "Native autopilot rejects stale cache");
                    Check(!ship.objSS.HasNavData(), "Native rejection clears plotted course");
                    burn.Invoke(fusion, new object[] { 0d, new JsonPowerInfo(), 0d });
                    ship.objSS.NavData = nav;
                    Check((bool)requirements.Invoke(autopilot, null), "Same native guard accepts refreshed cache and unchanged tanks");
                }
                burn.Invoke(fusion, new object[] { 0d, new JsonPowerInfo(), 0d });
                Near(ship.fShallowFusionRemain, target, "Next Fusion refresh repairs cache", 1e-6);
            }
            h.CreateClassProcessor(typeof(Ostrabus.MiscBugFixes.TorchFuelCachePatch)).Patch();
            foreach (int pairs in new[] { 1, 2, 4 }) foreach (double available in new[] { 0d, 4000, 30000 })
                foreach (double target in new[] { 0d, 2000, 24000, 100000 }) foreach (double ratio in new[] { .25, 1d, 4 }) {
                var fit = new ReactorFit { Feeders = pairs, Lasers = pairs, Capacitors = 2, Regulators = 2 };
                var fusion = Make(fit, TorchModel.Ideal, .3, 1);
                Field(fusion, "fPelletMax", TorchModel.PelletMax(fit));
                ship.fFusionThrustMax = TorchModel.RatedThrust(TorchModel.PelletMax(fit));
                double usage = fusion.MassUsageMax;
                Values[d2o[0]]["StatVolume"] = 40.4;
                Values[he3[0]]["StatVolume"] = 40.4;
                Values[d2o[0]]["StatLiqD2O"] = usage * TorchModel.D2ORatio * available * ratio;
                Values[he3[0]]["StatSolidHe3"] = usage * available;
                ship.fShallowFusionRemain = -123;
                fusion.SetReactants(target);
                double actualD = Read(d2o[0], "StatLiqD2O"), actualHe = Read(he3[0], "StatSolidHe3");
                double actualSeconds = Math.Min(actualD / (usage * TorchModel.D2ORatio), actualHe / usage);
                Near(ship.fShallowFusionRemain, actualSeconds, "Patched cache uses physical limiting reactant", 1e-10);
                Near(fusion.ReactantD, actualD, "Patched D2O cache", 1e-10);
                Near(fusion.ReactantHe3, actualHe, "Patched He-3 cache", 1e-10);
                StarSystem.fEpoch = 100;
                var nav = new NavData(ship);
                nav.AddNavDataPoint(new NavDataPoint(99, new ShipSitu(false), 0, .3, target + 1000));
                nav.AddNavDataPoint(new NavDataPoint(101, new ShipSitu(false), 0, .3, target));
                ship.objSS.NavData = nav;
                ship.bFusionReactorRunning = true;
                var autopilot = Empty<FlyToAutoPilot>(); Field(autopilot, "_shipUs", ship);
                bool accepted = (bool)AccessTools.Method(typeof(FlyToAutoPilot), "RequirementsMet").Invoke(autopilot, null);
                Check(accepted == (actualSeconds >= target / 2), "Native guard reflects physical inventory, including capacity-limited shortages");
                Check(ship.objSS.HasNavData() == accepted, "Affordable course preserved; shortage still clears course");
            }
            RunFuelStudy();
            RunPlaybackStudy();
            Console.WriteLine(checks + " native reactor, thermal, thrust, fuel and tank-debit checks passed.");
        } finally { h.UnpatchSelf(); }
    }
}
