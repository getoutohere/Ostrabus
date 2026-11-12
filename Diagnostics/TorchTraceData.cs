using System;
using System.Collections.Generic;
using System.Globalization;
using HarmonyLib;
using Ostranauts.ShipGUIs.Utilities;
using SolarSystem;

namespace Ostrabus.Diagnostics;

// Detached values only. Reading diagnostics must not call CatchUp, evaluate
// triggers, initialize a mass cache or attach/restore a corrected fuel profile.
internal static class TorchTraceData {
    internal const int MaxParts = 64, MaxTanks = 64, MaxScan = 4096, MaxPoints = 256;
    const double AU = 149597863936, G = 9.81;
    static readonly AccessTools.FieldRef<FusionIC, CondOwner> Core = AccessTools.FieldRefAccess<FusionIC, CondOwner>("co");
    static readonly AccessTools.FieldRef<Ship, List<CondOwner>> Cores = AccessTools.FieldRefAccess<Ship, List<CondOwner>>("aCores");
    static readonly AccessTools.FieldRef<Ship, List<CondOwner>> Navs = AccessTools.FieldRefAccess<Ship, List<CondOwner>>("aNavs");
    static readonly AccessTools.FieldRef<Ship, double> Mass = AccessTools.FieldRefAccess<Ship, double>("_mass");
    static readonly AccessTools.FieldRef<DockGroupMember, Ship> Member = AccessTools.FieldRefAccess<DockGroupMember, Ship>("_ship");
    static readonly AccessTools.FieldRef<CondOwner, string> Id = AccessTools.FieldRefAccess<CondOwner, string>("_strID");
    static readonly AccessTools.FieldRef<NavData, Ship> Owner = AccessTools.FieldRefAccess<NavData, Ship>("_shipUs");
    static readonly AccessTools.FieldRef<NavData, List<NavDataPoint>> Points = AccessTools.FieldRefAccess<NavData, List<NavDataPoint>>("_navPoints");
    static readonly AccessTools.FieldRef<NavData, double> AdvanceEpoch = AccessTools.FieldRefAccess<NavData, double>("fEpochLast");
    static readonly AccessTools.FieldRef<Ship, double> Pilot = AccessTools.FieldRefAccess<Ship, double>("fFuelEfficiencyMod");
    static readonly AccessTools.FieldRef<FusionIC, bool> Ignited = AccessTools.FieldRefAccess<FusionIC, bool>("bIgnition");
    static readonly AccessTools.FieldRef<FusionIC, double> Capacity = AccessTools.FieldRefAccess<FusionIC, double>("fPelletMax");
    static readonly string[] ModuleLists = { "aLasers", "aPellets", "aCryos", "aCaps", "aFuelRegs", "aCorePumps", "aCoils", "aMHDs" };
    static readonly AccessTools.FieldRef<FusionIC, List<CondOwner>>[] Modules = CreateModules();
    static AccessTools.FieldRef<FusionIC, List<CondOwner>>[] CreateModules() {
        var fields = new AccessTools.FieldRef<FusionIC, List<CondOwner>>[ModuleLists.Length];
        for (int i = 0; i < fields.Length; i++) fields[i] = AccessTools.FieldRefAccess<FusionIC, List<CondOwner>>(ModuleLists[i]);
        return fields;
    }

    internal struct State {
        public string ShipId, ReactorId, TargetId, ReferenceBody;
        public double Epoch, ReactorLastRunEpoch, NavLastAdvanceEpoch, Flow, Cycle, Ratio, Bus;
        public double TemperatureMeV, Pressure, PelletCapacity, TheoryCapacity, InternalCapacity, PelletRate, Throttle;
        public double Cooling, ExhaustVelocity, PowerTotal, PowerFusion, PowerThrust, PowerLoad, WallDamage;
        public double OwnMassCacheKg, GroupMassCacheKg, RatedThrustN, ModelThrustG, InputAccelerationG, AnchorAccelerationG;
        public double XAU, YAU, VxAUPerSecond, VyAUPerSecond, InputAxAUPerSecond2, InputAyAUPerSecond2;
        public double PilotFuelModifier, ShallowFuelBudget, ReactorDCache, ReactorHeCache, D2OTotal, He3Total, D2ORate, He3Rate;
        public int TankCount, GroupCount, PlanIdentity, PlanPointCount, SegmentIndex;
        public bool FuelScanAvailable, FuelScanTruncated, GroupMassComplete, Ignition, Running, HasPlan, PlanAttached, Torching, InSff;
        public double PlanFlow, SegmentStart, SegmentEnd, StartCycle, EndCycle, StartFuel, EndFuel, PlannedG, PlannedFuelRate;
    }
    internal sealed class Part {
        public string Id, Type, ShipId, Role;
        public bool Destroyed, Off, OverrideOff;
        public double Damage, MaxDamage, Power;
    }
    internal sealed class Tank {
        public string Id, Type, ShipId, Reactant;
        public double Amount, Volume;
        public bool Destroyed;
    }
    internal sealed class Details {
        public string ShipId, ReactorId;
        public double Epoch;
        public Part[] Modules;
        public Tank[] Tanks;
        public Dictionary<string,string> Controls;
        public Dictionary<string,Dictionary<string,string>> NavControls;
        public Dictionary<string,double> Conditions;
        public Dictionary<string,object> Internals;
        public bool ModulesTruncated, TanksTruncated, ControlsTruncated, ConditionsTruncated, NavControlsTruncated;
        public string Note = "Raw module lists, flags, damage and tank quantities; no trigger/availability evaluation. Amounts use the game's StatLiqD2O/StatSolidHe3 units, not assumed SI fuel mass. Mass fields are native caches; completeness is explicit.";
    }

    internal static CondOwner RawCore(FusionIC reactor) => reactor == null ? null : Core(reactor);
    internal static CondOwner RawCore(Ship ship) {
        var cores = ship == null ? null : Cores(ship);
        return cores != null && cores.Count > 0 ? cores[0] : null;
    }
    internal static Ship CourseShip(NavData nav) => nav == null ? null : Owner(nav);
    internal static string RawId(CondOwner co) => co == null ? null : Id(co);
    internal static double Amount(CondOwner co, string name) => co?.mapConds != null &&
        co.mapConds.TryGetValue(name, out var value) && value != null ? value.fCount : 0;
    static double Number(CondOwner co, string key) {
        if (co?.mapGUIPropMaps == null || !co.mapGUIPropMaps.TryGetValue("Panel A", out var panel) ||
            panel == null || !panel.TryGetValue(key, out var text)) return double.NaN;
        if (bool.TryParse(text,out bool boolean)) return boolean ? 1 : 0;
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double result) ||
            double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out result) ? result : double.NaN;
    }
    static double Acceleration(double x, double y) => Math.Sqrt(x*x + y*y)*AU/G;
    internal static State Read(Ship ship, CondOwner co, FusionIC reactor = null, NavData nav = null) {
        var s = new State { ShipId = ship.strRegID, ReactorId = RawId(co), Epoch = StarSystem.fEpoch,
            ReactorLastRunEpoch = reactor == null ? double.NaN : reactor.TimeLastRun,
            TargetId = ship.targetRegID, ReferenceBody = ship.objSS?.strBOPORShip,
            Flow = Number(co,"slidFlow"), Cycle = Number(co,"slidCycle"), Ratio = Number(co,"knobRatio"),
            Bus = Number(co,"knobBus"),
            TemperatureMeV = Amount(co,"StatICCoreTemp"), Pressure = Amount(co,"StatICPressureA"),
            PelletCapacity = Amount(co,"StatICPellMax"), TheoryCapacity = Amount(co,"StatICPellMaxTheory"),
            InternalCapacity = reactor == null ? double.NaN : Capacity(reactor), PelletRate = Amount(co,"StatICPellRate"),
            Throttle = Amount(co,"StatICThrustThrottle"), Cooling = Amount(co,"StatICCryoMult"), ExhaustVelocity = Amount(co,"StatICVe"),
            PowerTotal = Amount(co,"StatICPwrTotal"), PowerFusion = Amount(co,"StatICPwrFus"),
            PowerThrust = Amount(co,"StatICPwrThrust"), PowerLoad = Amount(co,"StatICPwrLoad"), WallDamage = Amount(co,"StatICABLWall"),
            OwnMassCacheKg = Mass(ship), RatedThrustN = ship.fFusionThrustMax, PilotFuelModifier = Pilot(ship),
            ShallowFuelBudget = ship.fShallowFusionRemain, ReactorDCache = reactor == null ? double.NaN : reactor.ReactantD,
            ReactorHeCache = reactor == null ? double.NaN : reactor.ReactantHe3, D2ORate = Amount(co,"StatICDRate"),
            He3Rate = Amount(co,"StatICHe3Rate"), Ignition = reactor != null && Ignited(reactor), Running = ship.bFusionReactorRunning,
            SegmentIndex = -1, PlannedG = double.NaN, PlannedFuelRate = double.NaN, InSff = GUIFFWD.Active };
        s.GroupMassCacheKg = s.OwnMassCacheKg;
        s.GroupMassComplete = s.OwnMassCacheKg > 0;
        s.GroupCount = 1;
        var group = ship.DockGroup;
        if (group?.DockedShips != null) {
            s.GroupCount = group.DockedShips.Count;
            if (s.GroupCount > MaxParts) s.GroupMassComplete = false;
            for (int i = 0; i < group.DockedShips.Count && i < MaxParts; i++) {
                var member = group.DockedShips[i];
                var other = member == null ? null : Member(member);
                if (ReferenceEquals(other,ship)) continue;
                if (other == null || other.bDestroyed || !(Mass(other) > 0)) { s.GroupMassComplete = false; continue; }
                s.GroupMassCacheKg += Mass(other);
            }
            if (group.Anchor != null) s.AnchorAccelerationG = Acceleration(group.Anchor.vAccIn.x,group.Anchor.vAccIn.y);
        }
        var situ = ship.objSS;
        if (situ != null) {
            s.XAU = situ.vPosx; s.YAU = situ.vPosy; s.VxAUPerSecond = situ.vVelX; s.VyAUPerSecond = situ.vVelY;
            s.InputAxAUPerSecond2 = situ.vAccIn.x; s.InputAyAUPerSecond2 = situ.vAccIn.y;
            s.InputAccelerationG = Acceleration(situ.vAccIn.x,situ.vAccIn.y);
        }
        double p = s.PelletCapacity, c = Math.Max(0,Math.Min(1,s.Cycle));
        s.ModelThrustG = p > 0 && s.GroupMassComplete ? (1+(p-1)*c)/p*c*s.RatedThrustN/s.GroupMassCacheKg/G *
            s.TemperatureMeV/.7250000238418579 : double.NaN;
        ReadFuel(ship, ref s, null);
        nav = nav ?? situ?.NavData;
        if (nav != null) {
            s.HasPlan = true; s.Torching = nav.IsTorching;
            s.PlanAttached = ReferenceEquals(situ?.NavData,nav);
            s.NavLastAdvanceEpoch = AdvanceEpoch(nav);
            s.PlanIdentity = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(nav); s.PlanFlow = nav.fFlowMultPlot;
            var points = Points(nav);
            s.PlanPointCount = points?.Count ?? 0;
            if (points != null) for (int i = 1; i < points.Count && i < MaxPoints; i++) {
                var a = points[i-1]; var b = points[i];
                if (a == null || b == null || s.Epoch > b.ArrivalTime) continue;
                double dt = b.ArrivalTime-a.ArrivalTime;
                s.SegmentIndex = i-1; s.SegmentStart = a.ArrivalTime; s.SegmentEnd = b.ArrivalTime;
                s.StartCycle = a.TorchCycle; s.EndCycle = b.TorchCycle; s.StartFuel = a.TorchFuelLevel; s.EndFuel = b.TorchFuelLevel;
                if (dt > 0) {
                    s.PlannedG = Acceleration(b.VelX-a.VelX,b.VelY-a.VelY)/dt;
                    s.PlannedFuelRate = (a.TorchFuelLevel-b.TorchFuelLevel)/dt;
                }
                break;
            }
        }
        return s;
    }
    static void ReadFuel(Ship ship, ref State state, List<Tank> details) {
        var map = CrewSim.objInstance?.coDicts?.COMapList;
        state.FuelScanAvailable = map != null;
        if (map == null) return;
        for (int type = 0; type < 2; type++) {
            string kind = type == 0 ? "ItmCanisterLH02" : "ItmCanisterLHe02";
            string reactant = type == 0 ? "StatLiqD2O" : "StatSolidHe3";
            if (!map.TryGetValue(kind,out var tanks) || tanks == null) continue;
            if (tanks.Count > MaxScan) state.FuelScanTruncated = true;
            for (int i = 0; i < tanks.Count && i < MaxScan; i++) {
                var tank = tanks[i];
                if (tank == null || tank.bDestroyed || !ReferenceEquals(tank.ship,ship)) continue;
                if (state.TankCount++ >= MaxTanks) { state.FuelScanTruncated = true; break; }
                double amount = Amount(tank,reactant);
                if (type == 0) state.D2OTotal += amount; else state.He3Total += amount;
                details?.Add(new Tank { Id = RawId(tank), Type = kind, ShipId = ship.strRegID, Reactant = reactant,
                    Amount = amount, Volume = Amount(tank,"StatVolume"), Destroyed = tank.bDestroyed });
            }
        }
    }
    internal static Details Deep(Ship ship, CondOwner co, FusionIC reactor) {
        var result = new Details { ShipId = ship.strRegID, ReactorId = RawId(co), Epoch = StarSystem.fEpoch };
        result.Controls = new Dictionary<string,string>();
        if (co?.mapGUIPropMaps != null && co.mapGUIPropMaps.TryGetValue("Panel A",out var panel) && panel != null)
            foreach (var pair in panel) {
                if (result.Controls.Count == 64) { result.ControlsTruncated = true; break; }
                result.Controls[pair.Key] = pair.Value == null || pair.Value.Length <= 256 ? pair.Value : pair.Value.Substring(0,256);
            }
        result.Conditions = new Dictionary<string,double>();
        if (co?.mapConds != null) foreach (var pair in co.mapConds) {
            if (result.Conditions.Count == 128) { result.ConditionsTruncated = true; break; }
            if (pair.Value != null) result.Conditions[pair.Key] = pair.Value.fCount;
        }
        result.NavControls = new Dictionary<string,Dictionary<string,string>>();
        var navs = Navs(ship);
        if (navs != null) for (int i = 0; i < navs.Count && i < 16; i++) {
            var nav = navs[i];
            if (nav?.mapGUIPropMaps == null || !nav.mapGUIPropMaps.TryGetValue("Panel A",out var controls) || controls == null) continue;
            var copy = new Dictionary<string,string>();
            foreach (var pair in controls) {
                if (copy.Count == 64) { result.NavControlsTruncated = true; break; }
                copy[pair.Key] = pair.Value == null || pair.Value.Length <= 256 ? pair.Value : pair.Value.Substring(0,256);
            }
            result.NavControls[RawId(nav) ?? ("nav#"+i)] = copy;
        }
        result.NavControlsTruncated |= navs != null && navs.Count > 16;
        result.Internals = new Dictionary<string,object>();
        if (reactor != null) foreach (string name in new[] { "bIgnition", "bCryoState", "bCoilFwdState", "bCoilRearState", "bMHDOn",
            "nKnobStateBus", "nPellets", "nLasers", "nFuelRegs", "nCoils", "nCryos", "nCryoTanks", "fTimeLastRun", "fTimeNextRun" })
            result.Internals[name] = AccessTools.Field(typeof(FusionIC),name).GetValue(reactor);
        var parts = new List<Part>();
        if (reactor != null) for (int category = 0; category < Modules.Length; category++) {
            var list = Modules[category](reactor);
            if (list == null) continue;
            if (list.Count > MaxParts) result.ModulesTruncated = true;
            for (int i = 0; i < list.Count && i < MaxParts; i++) {
                var part = list[i];
                if (part == null) continue;
                if (parts.Count == MaxParts) { result.ModulesTruncated = true; break; }
                parts.Add(new Part { Id = RawId(part), Type = part.strName, ShipId = part.ship?.strRegID, Role = ModuleLists[category],
                    Destroyed = part.bDestroyed, Off = Amount(part,"IsOff") > 0, OverrideOff = Amount(part,"IsOverrideOff") > 0,
                    Damage = Amount(part,"StatDamage"), MaxDamage = Amount(part,"StatDamageMax"), Power = Amount(part,"StatPower") });
            }
        }
        var state = default(State);
        var tanks = new List<Tank>();
        ReadFuel(ship,ref state,tanks);
        result.TanksTruncated = state.FuelScanTruncated || !state.FuelScanAvailable;
        result.Modules = parts.ToArray(); result.Tanks = tanks.ToArray();
        return result;
    }
}
