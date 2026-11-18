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
    static readonly object NativeFormat = new object();
    static readonly AccessTools.FieldRef<NavData, Ship> Owner = AccessTools.FieldRefAccess<NavData, Ship>("_shipUs");
    static readonly AccessTools.FieldRef<Ship, double> Pilot = AccessTools.FieldRefAccess<Ship, double>("fFuelEfficiencyMod");
    static readonly List<CondOwner> NoTanks = new List<CondOwner>();
    internal static Profile Playing;
    internal static NavData PlayingNav;
    internal struct PlaybackScope {
        internal Profile Profile;
        internal NavData Nav;
    }
    internal sealed class Profile {
        internal Ship Ship;
        internal CondOwner Reactor;
        internal NavData Nav;
        internal double Capacity, Usage, PilotMod, Theory, Exhaust;
        internal CondOwner[] Tanks;
        internal double[] Volumes;
        internal Interval[] Intervals;
        internal bool Invalid;
        internal bool AdoptedNative;
        internal double ObservedD, ObservedHe;
    }
    internal struct Interval {
        internal double Start, End, Cycle, Fuel, Rate;
        internal double InputX, InputY;
        internal bool InputKnown;
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
    internal static bool Attach(NavData nav, bool restore, bool remainingOnly = false, double inventoryEpoch = 0) {
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
        foreach (var point in points) if (point == null || point.ObjSS == null) return false;
        if (remainingOnly && (!ReferenceEquals(ship.objSS?.NavData, nav) || ratio != "1" ||
            StarSystem.fEpoch < points[0].ArrivalTime || StarSystem.fEpoch >= points[points.Count-1].ArrivalTime)) return false;
        if (restore && !remainingOnly) {
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
        double fuel = remainingOnly ? 0 : points[0].TorchFuelLevel;
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
            double savedCost = a.TorchFuelLevel-b.TorchFuelLevel;
            if (remainingOnly) {
                // Native PlotBurnLoop bills one budget second per powered
                // second (times pilot efficiency), and zero during coast.
                // Recognize that format explicitly; do not adopt arbitrary or
                // mixed propulsion budgets as a corrected torch course.
                double nativeCost = duration * (cycle > 0 ? pilot : 0);
                if (Math.Abs(savedCost-nativeCost) > Math.Max(.00001, nativeCost*.0001)) return false;
            } else if (restore && Math.Abs(savedCost-cost) > Math.Max(.00001, cost*.0001)) return false;
            intervals[i-1] = new Interval { Start = a.ArrivalTime, End = b.ArrivalTime, Cycle = cycle, Fuel = fuel, Rate = rate,
                InputX = (b.ObjSS.vVelX-a.ObjSS.vVelX)/duration, InputY = (b.ObjSS.vVelY-a.ObjSS.vVelY)/duration,
                // Analytical/body-attached waypoints can change velocity from
                // orbital following; do not report that as onboard thrust.
                InputKnown = !a.ObjSS.bIsBO && !a.ObjSS.bBOLocked && !a.ObjSS.bOrbitLocked &&
                    !b.ObjSS.bIsBO && !b.ObjSS.bBOLocked && !b.ObjSS.bOrbitLocked &&
                    Math.Abs(a.FuelLevel-b.FuelLevel) <= .001 &&
                    a.ObjSS.vAccRCS.x == 0 && a.ObjSS.vAccRCS.y == 0 && b.ObjSS.vAccRCS.x == 0 && b.ObjSS.vAccRCS.y == 0 };
            fuel -= cost;
        }
        if (!powered) return false;
        double plotFlow = BalancedFlow(capacity, maxCycle, Ideal, .27000001072883606);
        // This existing field already stores the FLOW used when plotting. Preserve
        // that meaning, and require it when recognizing a corrected saved course.
        if (restore && !remainingOnly && Math.Abs(nav.fFlowMultPlot-plotFlow) > .00001) return false;
        var profile = new Profile { Ship = ship, Reactor = core, Nav = nav, Capacity = capacity, Usage = usage,
            PilotMod = pilot, Theory = theory, Exhaust = core.GetCondAmount("StatICVe"), Intervals = intervals,
            AdoptedNative = remainingOnly };
        CaptureTanks(profile);
        if (profile.Tanks.Length == 0) return false;
        Observe(profile);
        double actualBudget = Math.Min(profile.ObservedD/(usage*FusionIC.aReactantAmounts[0]), profile.ObservedHe/(usage*FusionIC.aReactantAmounts[1]));
        if (remainingOnly) {
            if (!Finite(actualBudget) || actualBudget < 0) return false;
            // Only future costs are adopted. Shift the ledger to the physical
            // inventory now; never recreate earlier fuel or charge past burns.
            double anchor = Math.Max(points[0].ArrivalTime, Math.Min(StarSystem.fEpoch, inventoryEpoch));
            double shift = actualBudget-Budget(profile, anchor);
            for (int i = 0; i < intervals.Length; i++) intervals[i].Fuel += shift;
            points[0].TorchFuelLevel = intervals[0].Fuel;
            profile.Invalid = Budget(profile, intervals[intervals.Length-1].End) < -1e-6;
        } else if (restore && actualBudget < Budget(profile, StarSystem.fEpoch)-1) return false;
        if (!restore || remainingOnly) for (int i = 1; i < points.Count; i++) {
            var interval = intervals[i-1];
            points[i].TorchFuelLevel = interval.Fuel - (interval.End-interval.Start) * interval.Rate;
        }
        if (!restore || remainingOnly) nav.SetFlowMultPlot(plotFlow);
        Profiles.Add(nav, profile);
        return true;
    }
    internal static Profile Find(NavData nav, bool restore = true) {
        if (nav == null) return null;
        if (!Profiles.TryGetValue(nav, out var profile) && restore && !Ignored.TryGetValue(nav, out _)) {
            var ship = Owner(nav);
            // Cold cores and temporarily unavailable player/control state are
            // not permanent evidence about a route's format. Retry when ready.
            if (!Player(ship) || !ship.bFusionReactorRunning || CrewSim.objInstance?.coDicts == null ||
                !ReferenceEquals(ship.objSS?.NavData, nav)) return null;
            var core = ship.Reactor;
            if (!Warm(core)) return null;
            string ratio = ship.GetReactorGPMValue("knobRatio");
            if (ratio != "0" && ratio != "1") return null;
            if (!Attach(nav, true)) {
                Ignored.Add(nav, NativeBudget(nav, Pilot(ship)) ? NativeFormat : Ignore);
            }
            Profiles.TryGetValue(nav, out profile);
        }
        return profile;
    }

    static bool NativeBudget(NavData nav, double pilot) {
        var points = nav.GetNavDataPoints();
        if (points == null || points.Count < 2 || nav.InAtmo || (pilot != 1 && pilot != .75)) return false;
        foreach (var point in points) if (point == null || point.ObjSS == null) return false;
        bool powered = false;
        for (int i = 1; i < points.Count; i++) {
            var a = points[i-1]; var b = points[i];
            double duration = b.ArrivalTime-a.ArrivalTime, cycle = Cycle(a,b);
            if (!Finite(duration) || duration <= 0 || !Finite(cycle) || cycle < 0 || cycle > 1 ||
                !Finite(a.TorchFuelLevel) || !Finite(b.TorchFuelLevel)) return false;
            powered |= cycle > 0;
            double expected = duration*(cycle > 0 ? pilot : 0);
            if (Math.Abs(a.TorchFuelLevel-b.TorchFuelLevel-expected) > Math.Max(.00001, expected*.0001)) return false;
        }
        return powered;
    }

    internal static Profile AdoptNative(FusionIC reactor) {
        var nav = PlayingNav;
        if (nav == null || reactor?.COSelf == null || !ReferenceEquals(Owner(nav), reactor.COSelf.ship)) return null;
        // Called from SetReactants, after native CatchUp has paid whatever its
        // own CO clock already simulated. Anchor to that inventory epoch, not
        // to an earlier planned balance or an unadvanced global-clock guess.
        var existing = Find(nav);
        if (existing != null) { Playing = existing; return existing; }
        if (!Ignored.TryGetValue(nav, out var format) || !ReferenceEquals(format, NativeFormat) ||
            !Finite(reactor.TimeLastRun) || !Attach(nav, true, remainingOnly: true, inventoryEpoch: reactor.TimeLastRun)) return null;
        Profiles.TryGetValue(nav, out var profile);
        Playing = profile;
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
    internal static void PlaybackInput(Profile profile, ShipSitu situation) {
        if (!ReferenceEquals(situation,profile.Ship.objSS) || !ReferenceEquals(situation.NavData,profile.Nav) ||
            StarSystem.fEpoch < profile.Intervals[0].Start) return;
        var interval = Current(profile,StarSystem.fEpoch);
        if (!interval.InputKnown || !Finite(interval.InputX) || !Finite(interval.InputY)) return;
        bool arrived = StarSystem.fEpoch >= profile.Intervals[profile.Intervals.Length-1].End;
        // Native playback linearly interpolates velocity, but also interpolates
        // the prior waypoint's cached force into the next interval. During coast
        // that resurrects thrust after Fusion has set it to zero. Report the
        // derivative of the actual played velocity instead. Do this after native
        // reactor catch-up, before the group/crew gravity consumer runs.
        situation.vAccIn.Set(arrived ? 0 : (float)interval.InputX, arrived ? 0 : (float)interval.InputY);
        profile.Ship.UpdateDockedShips(situation);
    }
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
