using System;
using System.Linq;
using HarmonyLib;
using Ostrabus.Orbital;
using Ostrabus.Orbital.Economy;

// Compare explicit engine reuse and the actual deployed PASS bridge while
// keeping the native GUI flag false.
static partial class EconomyChecks {
    static bool PassGuiInactive(ref bool __result) { __result = false; return false; }
    static int passRoleCallbacks;
    static bool requirePassEconomy;
    static bool CountPassRole() { passRoleCallbacks++; return false; }
    static void PassReuseChecks(Action<bool, string> check, Harmony fixture) {
        var active = AccessTools.PropertyGetter(typeof(GUIFFWD), nameof(GUIFFWD.Active));
        var callbacks = new Harmony("offline.pass-reuse.role-count");
        string stage = "reference";
        try {
            callbacks.Patch(AccessTools.Method(typeof(Ostranauts.Ships.AIPilots.AIShip), "FFWD"),
                prefix: new HarmonyMethod(typeof(EconomyChecks), nameof(CountPassRole)));
            string State() => string.Join(";", markets.OrderBy(p => p.Key).Select(p => p.Key + ":" +
                p.Value.GetStockCountForCategory("Metal") + ":" + p.Value.GetStockCountForCategory("Product"))) +
                "|plans=" + planCalls + "|dispatchClock=" + AccessTools.Field(typeof(AIShipManager), "fTimeOfNextTradeCheck").GetValue(null) +
                "|cargoX=" + (hulls.TryGetValue("CARGO1", out var cargo) ? cargo.objSS.vPosx : 0);
            void Start() {
                Setup(); passRoleCallbacks = 0;
                AccessTools.Field(typeof(AIShipManager), "fTimeOfNextTradeCheck").SetValue(null, start + 600);
            }
            Start();
            CrewSim.system.Update(1500); // Current corrected GUI-SFF reference.
            string reference = State();
            check(passRoleCallbacks == 0 && planCalls == 1, "Sub-hour GUI SFF economic work requires native role callbacks");

            fixture.Unpatch(active, HarmonyPatchType.Prefix, fixture.Id);
            fixture.Patch(active, prefix: new HarmonyMethod(typeof(EconomyChecks), nameof(PassGuiInactive)));
            Start();
            OrbitalStepObservationPatch.Prefix(CrewSim.system, 1500, out var passBoundary);
            check(!GUIFFWD.Active && passBoundary == null && SffEconomy.Current == null && planCalls == 0,
                "PASS control unexpectedly acquired the bulk freight timeline");

            Start();
            // Match the production prefix's observation-before-capture order.
            stage = "explicit engine reuse";
            OrbitalSimulation.ObserveUpdate(CrewSim.system, 1500);
            using (var economy = SffEconomy.Begin(CrewSim.system, 1500, true))
            using (var motion = SffSimulation.Begin(CrewSim.system, 1500, true)) {
                check(economy != null && !GUIFFWD.Active, "Existing economic engine cannot enter independently of GUI SFF");
                CrewSim.system.Update(1500);
                check(State() == reference && passRoleCallbacks == 0 && !GUIFFWD.Active,
                    "Shared engine scopes changed cargo, production, timing or native sub-hour role eligibility");
            }
            check(SffEconomy.Current == null && SffSimulation.Current == null && FreightPlanningScope.Current == null,
                "PASS reuse experiment leaked a runtime scope");
            Start();
            stage = "native callback threshold";
            var callbackPilot = Empty<Ostranauts.Ships.AIPilots.AIShip>();
            Field(callbackPilot, "_shipUs", Hull("callback-probe"));
            AIShipManager.dictAIs["INTERREGIONAL"].Add(callbackPilot);
            AIShipManager.FFWD(3600);
            check(passRoleCallbacks == 1, "Native one-hour callback threshold did not execute");
            passRoleCallbacks = 0;
            AIShipManager.FFWD(300); AIShipManager.FFWD(3300);
            check(passRoleCallbacks == 0, "Splitting a one-hour jump did not demonstrate lost native role callback eligibility");
            callbacks.CreateClassProcessor(typeof(SffBeforeCatchupPatch)).Patch();
            callbacks.CreateClassProcessor(typeof(SffCatchupPatch)).Patch();
            foreach (double duration in new[] { 1500d, 21600d }) {
                stage = "adapter reference " + duration;
                Start();
                OrbitalSimulation.ObserveUpdate(CrewSim.system, duration);
                using (var economy = SffEconomy.Begin(CrewSim.system, duration, true))
                using (var motion = SffSimulation.Begin(CrewSim.system, duration, true)) CrewSim.system.Update(duration);
                string expected = State();
                stage = "adapter " + duration;
                Start(); requirePassEconomy = true;
                // A nonconsumed station/member route must not cause microscopic
                // retries, and forces a sub-minute economic window here.
                var source = hulls["SOURCE"];
                var route = new Ostranauts.ShipGUIs.Utilities.NavData(source);
                route.AddNavDataPoint(new Ostranauts.ShipGUIs.Utilities.NavDataPoint(start, new ShipSitu(source.objSS, false), 100, 0, 0, 0));
                route.AddNavDataPoint(new Ostranauts.ShipGUIs.Utilities.NavDataPoint(start + 20, new ShipSitu(source.objSS, false), 100, 0, 0, 0));
                source.objSS.NavData = route;
                SffAdvance.Pass(CrewSim.system, duration);
                check(State() == expected && StarSystem.fEpoch == start + duration,
                    "Actual PASS windows changed production, cargo, delivery, dispatch clock or flight position");
                var report = (System.Collections.Generic.Dictionary<string, object>)SffAdvance.Snapshot();
                check(!(bool)report["budgetFallback"] && (int)report["worldSteps"] < 10,
                    "Unconsumed native course caused a substep retry storm");
                requirePassEconomy = false;
            }
            Start(); requirePassEconomy = true;
            stage = "short adapter";
            SffAdvance.Pass(CrewSim.system, 30);
            check(StarSystem.fEpoch == start + 30 && SffEconomy.Current == null && FreightPlanningScope.Current == null,
                "Short PASS economic work lost time or leaked scopes");
            Console.WriteLine("PASS economy: actual 30s/1,500s/6h adapters enter the shared timeline; forced short and course-completion windows preserve stock, dispatch, freight delivery and accounting without retry storms.");
        } catch (Exception error) { throw new Exception("PASS economic test failed at " + stage, error); }
        finally { requirePassEconomy = false; callbacks.UnpatchSelf(); }
    }
}
