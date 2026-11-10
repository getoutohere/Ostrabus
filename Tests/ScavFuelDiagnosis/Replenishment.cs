using System;
using System.Collections.Generic;
using HarmonyLib;
using Ostranauts.ShipGUIs.Utilities;

static partial class Program {
    static Action<Ship> refillSideEffect;
    static bool LoadedFill(Ship __instance, float amount, ref float __result) {
        if (__instance.LoadState <= Ship.Loaded.Shallow) return true;
        double accepted = Math.Min(amount, 100 - physicalFuel);
        physicalFuel += accepted; __result = (float)(amount - accepted); refillSideEffect?.Invoke(__instance); return false;
    }
    static bool LoadedRemoval(Ship __instance, float fMassNeeded, ref float __result) {
        if (__instance.LoadState <= Ship.Loaded.Shallow) return true;
        __result = (float)Math.Min(fMassNeeded, physicalFuel); physicalFuel -= __result; return false;
    }
    static bool NotPlayerManned(ref bool __result) { __result = false; return false; }
    static Ship RefillShip(double fuel = 100) {
        var ship = MakeShip(fuel); ship.IsAIShip = true; ship.fShallowRCSRemassMax = 120;
        ((Dictionary<string, Ship>)AccessTools.Field(typeof(Ship), "aDocked").GetValue(ship)).Clear();
        return ship;
    }
    static void CheckNativeReplenishment(Harmony boundary) {
        boundary.Patch(AccessTools.Method(typeof(ShipSitu), nameof(ShipSitu.UpdateTime)), prefix: new HarmonyMethod(typeof(Program), nameof(Quiet)));
        var ship = RefillShip(); var plan = ScopeRoute(ship);
        StarSystem.fEpoch = 1000; ship.AIRefuel();
        Check(ship.GetRCSRemain() == 120, "Native refill failed to reach fixture capacity");
        Check(plan.Origin.FuelLevel == 100 && plan.Destination.FuelLevel == 50, "Native refill unexpectedly translated plan budgets");
        StarSystem.fEpoch = 1050; plan.TimeAdvance(ship.objSS);
        Check(ship.GetRCSRemain() == 75, "Native plan no longer reproduces refill erasure");
        Console.WriteLine(checks + " native refill reproduction checks passed: 100 + 20 - 25 leaves 75 kg; 20 kg refill erased. No production patches installed.");
    }
    static void CheckReplenishment(Harmony boundary) {
        var production = new Harmony("offline.rcs-replenishment.production");
        CrewSim.coPlayer = Empty<CondOwner>(); Set(CrewSim.coPlayer, "_strID", "PLAYER"); CrewSim.coPlayer.ship = MakeShip(0);
        boundary.Patch(AccessTools.Method(typeof(ShipSitu), nameof(ShipSitu.UpdateTime)), prefix: new HarmonyMethod(typeof(Program), nameof(Quiet)));
        boundary.Patch(AccessTools.PropertyGetter(typeof(Ship), nameof(Ship.NavPlayerManned)), prefix: new HarmonyMethod(typeof(Program), nameof(NotPlayerManned)));
        try {
            foreach (string name in new[] { "Ostrabus.RefuelRCSPatch", "Ostrabus.ShipNavigation.RcsAutomaticReplenishmentPatch", "Ostrabus.ShipNavigation.RcsPhysicalReplenishmentPatch" })
                production.CreateClassProcessor(typeof(Ostrabus.OstrabusPlugin).Assembly.GetType(name, true)).Patch();
            foreach (bool automatic in new[] { false, true }) {
                var ship = RefillShip(); var plan = ScopeRoute(ship); StarSystem.fEpoch = 1000;
                float overflow = automatic ? 0 : ship.RefuelRCS(40);
                if (automatic) ship.AIRefuel();
                Check(automatic || overflow == 20, "Partial capacity fill changed native overflow contract");
                Check(ship.GetRCSRemain() == 120 && plan.Origin.FuelLevel == 120 && plan.Destination.FuelLevel == 70, "Actual accepted fuel was not rebased exactly once");
                double endpoint = plan.Destination.FuelLevel;
                if (automatic) ship.AIRefuel(); else ship.RefuelRCS(10);
                Check(plan.Destination.FuelLevel == endpoint, "No-op refill shifted the plan");
                StarSystem.fEpoch = 1050; plan.TimeAdvance(ship.objSS); plan.TimeAdvance(ship.objSS);
                Check(Math.Abs(ship.GetRCSRemain() - 95) < .001, "Repeated same-epoch route advance erased refill or double-charged");
                ship.RefuelRCS(10); StarSystem.fEpoch = 1100; plan.TimeAdvance(ship.objSS);
                Check(Math.Abs(ship.GetRCSRemain() - 80) < .001, "Mid-course refill changed remaining planned expenditure");
            }
            var invalid = RefillShip(); var invalidPlan = ScopeRoute(invalid); invalidPlan.Destination.FuelLevel = double.NaN;
            invalid.AIRefuel(); Check(invalidPlan.Origin.FuelLevel == 100, "Invalid plan caused partial writes");
            var foreign = RefillShip(); var foreignPlan = ScopeRoute(RefillShip()); foreign.objSS.NavData = foreignPlan;
            foreign.AIRefuel(); Check(foreignPlan.Origin.FuelLevel == 100, "Refill rebased a different ship's route");
            var player = RefillShip(); var playerPlan = ScopeRoute(player); var previousPlayer = CrewSim.coPlayer.ship; CrewSim.coPlayer.ship = player;
            player.AIRefuel(); Check(playerPlan.Origin.FuelLevel == 100, "Player course was modified"); CrewSim.coPlayer.ship = previousPlayer;
            var owned = RefillShip(); var ownedPlan = ScopeRoute(owned);
            var owners = (Dictionary<string, string>)AccessTools.Field(typeof(StarSystem), "dictShipOwners").GetValue(CrewSim.system);
            owners[owned.strRegID] = "PLAYER"; owned.AIRefuel();
            Check(ownedPlan.Origin.FuelLevel == 100, "Inactive player-owned hull was modified"); owners[owned.strRegID] = "OKLGScav";

            // Loaded physical tanks are explicit boundaries; SyncFuel and route
            // reconciliation are native, preserving the prior 10 kg shallow debit.
            boundary.Patch(AccessTools.Method(typeof(Ship), nameof(Ship.GetRCSRemain)), prefix: new HarmonyMethod(typeof(Program), nameof(PhysicalFuel)));
            boundary.Patch(AccessTools.Method(typeof(Ship), nameof(Ship.GetRCSMax)), prefix: new HarmonyMethod(typeof(Program), nameof(PhysicalCapacity)));
            boundary.Patch(AccessTools.Method(typeof(Ship), nameof(Ship.RefuelRCS)), prefix: new HarmonyMethod(typeof(Program), nameof(LoadedFill)));
            boundary.Patch(AccessTools.Method(typeof(Ship), nameof(Ship.RemoveGasMass)), prefix: new HarmonyMethod(typeof(Program), nameof(LoadedRemoval)));
            var loaded = RefillShip(40); Set(loaded, "nLoadState", Ship.Loaded.Full); physicalFuel = 50;
            var loadedPlan = new NavData(loaded); loadedPlan.AddNavDataPoint(new NavDataPoint(1000, Empty<ShipSitu>(), 50, 0, 0));
            loadedPlan.AddNavDataPoint(new NavDataPoint(1100, Empty<ShipSitu>(), 30, 0, 0)); loaded.objSS.NavData = loadedPlan;
            StarSystem.fEpoch = 1000; loaded.RefuelRCS(100);
            Check(physicalFuel == 100 && loaded.fShallowRCSRemass == 90, "Physical refill erased pending shallow debit or omitted addition");
            Check(loadedPlan.Origin.FuelLevel == 100 && loadedPlan.Destination.FuelLevel == 80, "Physical plan rebase used request rather than accepted gas");
            loaded.SyncFuel(); Check(physicalFuel == 90, "Delayed native SyncFuel erased new gas or forgave prior spending");
            StarSystem.fEpoch = 1050; loadedPlan.TimeAdvance(loaded.objSS); Check(physicalFuel == 90, "Physical plan charged pending debit twice");
            loaded.AIRefuel(); Check(physicalFuel == 90 && loadedPlan.Origin.FuelLevel == 100, "Counter-only Full AIRefuel invented physical gas or shifted plan");
            refillSideEffect = s => ScopeRoute(s); physicalFuel = 50;
            var abandoned = ScopeRoute(loaded); loaded.RefuelRCS(10);
            Check(abandoned.Origin.FuelLevel == 100 && loaded.objSS.NavData.Origin.FuelLevel == 100, "Refill mutated replaced/new route budgets");
            refillSideEffect = null;
            var noPlan = RefillShip(); noPlan.objSS.NavData = null;
            for (int i = 0; i < 100; i++) noPlan.AIRefuel();
            long before = GC.GetAllocatedBytesForCurrentThread(); for (int i = 0; i < 1000; i++) noPlan.AIRefuel();
            Check(GC.GetAllocatedBytesForCurrentThread() - before == 0, "No-plan shallow replenishment observer allocated after warmup");
            Console.WriteLine(checks + " RCS replenishment regressions passed. Loaded tank I/O is stubbed; shallow filling, native plan accounting and SyncFuel execute.");
        } finally { refillSideEffect = null; production.UnpatchSelf(); }
    }
}
