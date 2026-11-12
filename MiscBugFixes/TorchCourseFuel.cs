using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Ostranauts.ShipGUIs.Utilities;
using Ostranauts.Ships.AIPilots;

namespace Ostrabus.MiscBugFixes;

internal static class TorchCourseFuel {
    internal const double Ideal = .7250000238418579;
    static readonly ConditionalWeakTable<NavData, Profile> Profiles = new ConditionalWeakTable<NavData, Profile>();
    static readonly ConditionalWeakTable<NavData, object> Ignored = new ConditionalWeakTable<NavData, object>();
    static readonly object Ignore = new object();
    static readonly AccessTools.FieldRef<NavData, Ship> Owner = AccessTools.FieldRefAccess<NavData, Ship>("_shipUs");
    static readonly AccessTools.FieldRef<Ship, double> Pilot = AccessTools.FieldRefAccess<Ship, double>("fFuelEfficiencyMod");
    static readonly List<CondOwner> NoTanks = new List<CondOwner>();
    internal static Profile Playing;
    internal sealed class Profile {
        internal Ship Ship;
        internal CondOwner Reactor;
        internal NavData Nav;
        internal double Capacity, Usage, PilotMod, Theory, Exhaust;
        internal CondOwner[] Tanks;
        internal double[] Volumes;
        internal Interval[] Intervals;
        internal bool Invalid;
        internal double ObservedD, ObservedHe;
    }
    internal struct Interval {
        internal double Start, End, Cycle, Fuel, Rate;
    }
    internal static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    static bool Player(Ship ship) => ship != null && !ship.bDestroyed && ship.LoadState == Ship.Loaded.Full &&
        !ship.IsAIShip && CrewSim.coPlayer != null && CrewSim.coPlayer.ship == ship;
    static double Idle(double capacity) => (MathfLerp((float)capacity, .001f, 1) / capacity);
    static float MathfLerp(float a, float b, float t) => a + (b-a) * Math.Max(0, Math.Min(1, t));
    internal static double BalancedFlow(double capacity, double cycle, double temperature, double elapsed) {
        double dt = Math.Min(1, Math.Max(.00001, elapsed));
        double minimum = MathfLerp((float)capacity, .001f, (float)Math.Min((temperature-cycle*.2*dt)/Ideal, 1));
        double required = (Ideal-temperature+cycle*.2*dt)/(.04*dt);
        return Math.Max(0, Math.Min(1, (required-minimum)/(capacity-minimum)));
    }
    internal static double Cycle(NavDataPoint a, NavDataPoint b) {
        double change = (b.ObjSS.vVel-a.ObjSS.vVel).magnitude;
        return !Finite(change) ? double.NaN : change < 1e-15 ? 0 : Math.Max(a.TorchCycle, b.TorchCycle);
    }
    static bool Warm(CondOwner core) => core != null && Math.Abs(core.GetCondAmount("StatICCoreTemp") / Ideal - 1) <= .001;
    static List<CondOwner> TankList(bool he3) => CrewSim.objInstance.coDicts.COMapList.TryGetValue(he3 ? "ItmCanisterLHe02" : "ItmCanisterLH02", out var list) ? list : NoTanks;
    static void CaptureTanks(Profile profile) {
        var tanks = new List<CondOwner>();
        foreach (bool he3 in new[] { false, true }) foreach (var tank in TankList(he3)) if (tank != null && tank.ship == profile.Ship) tanks.Add(tank);
        profile.Tanks = tanks.ToArray(); profile.Volumes = new double[tanks.Count];
        for (int i = 0; i < tanks.Count; i++) profile.Volumes[i] = tanks[i].GetCondAmount("StatVolume");
    }
    static bool TankSetValid(Profile profile) {
        int count = 0;
        for (int type = 0; type < 2; type++) foreach (var tank in TankList(type == 1)) {
            if (tank == null || tank.ship != profile.Ship) continue;
            bool found = false;
            for (int i = 0; i < profile.Tanks.Length; i++) if (ReferenceEquals(profile.Tanks[i], tank) && profile.Volumes[i] == tank.GetCondAmount("StatVolume")) { found = true; break; }
            if (!found) return false;
            count++;
        }
        return count == profile.Tanks.Length;
    }
    internal static void Observe(Profile profile) {
        profile.ObservedD = Total(profile, false);
        profile.ObservedHe = Total(profile, true);
    }
    static double Total(Profile profile, bool he3) {
        double result = 0;
        foreach (var tank in TankList(he3)) if (tank != null && tank.ship == profile.Ship)
            result += Math.Max(0, tank.GetCondAmount(he3 ? FusionIC.aReactantNames[1] : FusionIC.aReactantNames[0]));
        return result;
    }
    static bool InventoryUnchanged(Profile profile) =>
        Math.Abs(Total(profile, false)-profile.ObservedD) <= 1e-6 && Math.Abs(Total(profile, true)-profile.ObservedHe) <= 1e-6;
    internal static bool Attach(NavData nav, bool restore) {
        if (nav == null || Profiles.TryGetValue(nav, out _)) return false;
        var ship = Owner(nav);
        if (!Player(ship) || !ship.bFusionReactorRunning || !Warm(ship.Reactor) || CrewSim.objInstance?.coDicts == null) return false;
        var core = ship.Reactor;
        // Plotting predicts a future torch burn; it need not switch a running
        // power-mode reactor first. Execution still requires mode 1 in Valid.
        string ratio = ship.GetReactorGPMValue("knobRatio");
        if (ratio != "0" && ratio != "1") return false;
        double capacity = core.GetCondAmount("StatICPellMax"), theory = core.GetCondAmount("StatICPellMaxTheory"), cooling = core.GetCondAmount("StatICCryoMult");
        double pilot = Pilot(ship), usage = 2 * ship.fFusionThrustMax / (.949999988079071 * 70500000);
        if (!Finite(capacity) || capacity <= .001 || !Finite(usage) || usage <= 0 || cooling <= 0 ||
            (pilot != 1 && pilot != .75) || core.GetCondAmount("IsTorchDrive") <= 0) return false;
        var points = nav.GetNavDataPoints();
        if (points == null || points.Count < 2 || nav.InAtmo) return false;
        if (restore) {
            for (int i = 1; i < points.Count; i++) {
                double cycle = Cycle(points[i-1], points[i]);
                if (cycle <= 0 || !Finite(cycle)) continue;
                double savedRate = (points[i-1].TorchFuelLevel-points[i].TorchFuelLevel)/(points[i].ArrivalTime-points[i-1].ArrivalTime);
                double originalCapacity = 5*cycle*pilot/savedRate;
                if (!Finite(originalCapacity) || originalCapacity <= .001 || Math.Abs(originalCapacity/capacity-1) > .01) return false;
                usage *= originalCapacity/capacity;
                capacity = originalCapacity;
                break;
            }
        }
        var intervals = new Interval[points.Count-1];
        double fuel = points[0].TorchFuelLevel;
        bool powered = false; double maxCycle = 0;
        for (int i = 1; i < points.Count; i++) {
            var a = points[i-1]; var b = points[i];
            double duration = b.ArrivalTime-a.ArrivalTime, cycle = Cycle(a, b);
            if (!Finite(duration) || duration <= 0 || !Finite(cycle) || cycle < 0 || cycle > Math.Min(1, capacity*.2) + 1e-6 ||
                !Finite(a.TorchFuelLevel) || !Finite(b.TorchFuelLevel)) return false;
            powered |= cycle > 0;
            maxCycle = Math.Max(maxCycle, cycle);
            double rate = Math.Max(Idle(capacity), 5*cycle/capacity) * pilot;
            double cost = duration * rate;
            if (restore && Math.Abs((a.TorchFuelLevel-b.TorchFuelLevel)-cost) > Math.Max(.00001, cost*.0001)) return false;
            intervals[i-1] = new Interval { Start = a.ArrivalTime, End = b.ArrivalTime, Cycle = cycle, Fuel = fuel, Rate = rate };
            fuel -= cost;
        }
        if (!powered) return false;
        double plotFlow = BalancedFlow(capacity, maxCycle, Ideal, .27000001072883606);
        // This existing field already stores the FLOW used when plotting. Preserve
        // that meaning, and require it when recognizing a corrected saved course.
        if (restore && Math.Abs(nav.fFlowMultPlot-plotFlow) > .00001) return false;
        var profile = new Profile { Ship = ship, Reactor = core, Nav = nav, Capacity = capacity, Usage = usage,
            PilotMod = pilot, Theory = theory, Exhaust = core.GetCondAmount("StatICVe"), Intervals = intervals };
        CaptureTanks(profile);
        if (profile.Tanks.Length == 0) return false;
        Observe(profile);
        if (restore && Math.Min(profile.ObservedD/(usage*FusionIC.aReactantAmounts[0]), profile.ObservedHe/(usage*FusionIC.aReactantAmounts[1])) < Budget(profile, StarSystem.fEpoch)-1) return false;
        if (!restore) for (int i = 1; i < points.Count; i++) {
            var interval = intervals[i-1];
            points[i].TorchFuelLevel = interval.Fuel - (interval.End-interval.Start) * interval.Rate;
        }
        if (!restore) nav.SetFlowMultPlot(plotFlow);
        Profiles.Add(nav, profile);
        return true;
    }
    internal static Profile Find(NavData nav, bool restore = true) {
        if (nav == null) return null;
        if (!Profiles.TryGetValue(nav, out var profile) && restore && !Ignored.TryGetValue(nav, out _)) {
            if (!Attach(nav, true)) Ignored.Add(nav, Ignore);
            Profiles.TryGetValue(nav, out profile);
        }
        return profile;
    }
    internal static bool Valid(Profile profile, bool inventory = true) {
        var ship = profile.Ship; var core = profile.Reactor;
        return !profile.Invalid && ship != null && !ship.bDestroyed && ship.LoadState == Ship.Loaded.Full && !ship.IsAIShip &&
            ReferenceEquals(Owner(profile.Nav), ship) && ReferenceEquals(ship.objSS?.NavData, profile.Nav) && ship.Reactor == core && ship.bFusionReactorRunning &&
            Math.Abs(core.GetCondAmount("StatICPellMax") / profile.Capacity-1) <= .01 &&
            core.GetCondAmount("StatICPellMaxTheory") == profile.Theory && core.GetCondAmount("StatICCryoMult") > 0 &&
            core.GetCondAmount("StatICVe") == profile.Exhaust && ship.GetReactorGPMValue("knobRatio") == "1" &&
            Math.Abs(core.GetCondAmount("StatICCoreTemp") / Ideal-1) <= .2 && Pilot(ship) == profile.PilotMod && TankSetValid(profile) &&
            (!inventory || InventoryUnchanged(profile));
    }
    internal static void Abort(Profile profile) {
        bool first = !profile.Invalid;
        profile.Invalid = true;
        var current = profile.Ship.objSS?.NavData;
        if (current != null && !ReferenceEquals(current, profile.Nav)) return;
        if (current != null) {
            profile.Ship.objSS.ResetNavData();
            profile.Ship.ClearShipTarget();
            var controller = AIShipManager.GetAIShipByRegID(profile.Ship.strRegID);
            if (controller != null && controller.ActiveCommandName == "FlyToAutoPilot") AIShipManager.UnregisterShip(profile.Ship);
        }
        profile.Ship.SetReactorGPMValue("slidCycle", "0");
        profile.Ship.SetReactorGPMValue("slidFlow", "0");
        profile.Ship.SetThrust(0);
        if (first) {
            profile.Ship.LogAdd("Autopilot disengaged: torch reactor or tank configuration changed.", StarSystem.fEpoch, true);
            UnityEngine.Debug.LogWarning("[Ostrabus] Torch autopilot disengaged: reactor or tank configuration changed.");
        }
    }
    static Interval Current(Profile profile, double epoch) {
        foreach (var interval in profile.Intervals) if (epoch <= interval.End) return interval;
        return profile.Intervals[profile.Intervals.Length-1];
    }
    internal static double Budget(Profile profile, double epoch) {
        var interval = Current(profile, epoch);
        return interval.Fuel-Math.Max(0, Math.Min(interval.End-interval.Start, epoch-interval.Start))*interval.Rate;
    }
    internal static double CycleAt(Profile profile, double epoch) => epoch >= profile.Intervals[profile.Intervals.Length-1].End ? 0 : Current(profile, epoch).Cycle;
    internal static void WritePlaybackControl(Ship ship, string key, string value) {
        var profile = Playing;
        // This replaces only TimeAdvance's ordinary computed FLOW/CYCLE writes.
        // Run owns those settings for corrected playback. Native literal-zero
        // abort/arrival writes remain direct, including before the course detaches.
        if (profile != null && !profile.Invalid && ReferenceEquals(profile.Ship,ship) &&
            ReferenceEquals(ship.objSS?.NavData,profile.Nav)) return;
        ship.SetReactorGPMValue(key,value);
    }
    internal static void Control(FusionIC reactor, double elapsed) {
        var ship = reactor.COSelf?.ship;
        var profile = Find(ship?.objSS?.NavData);
        if (profile == null || !ReferenceEquals(profile.Reactor, reactor.COSelf)) return;
        if (!Valid(profile)) { Abort(profile); return; }
        double cycle = CycleAt(profile, StarSystem.fEpoch);
        double capacity = profile.Reactor.GetCondAmount("StatICPellMax"), temp = profile.Reactor.GetCondAmount("StatICCoreTemp");
        double flow = BalancedFlow(capacity, cycle, temp, elapsed);
        ship.SetReactorGPMValue("slidCycle", cycle.ToString());
        ship.SetReactorGPMValue("slidFlow", flow.ToString());
    }
}
