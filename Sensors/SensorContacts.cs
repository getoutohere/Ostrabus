using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using Ostrabus.Sensors.Model;
using Ostranauts.Ships.Sensors;
using Ostranauts.ShipGUIs.Utilities;
using Ostranauts.Utils.Models;
using UnityEngine.SceneManagement;

namespace Ostrabus.Sensors;

// Display observations only. Never write ShipSitu, native detection, or navigation plans.
internal static class SensorContacts {
    const double AuM = 149597863936, Interval = 2;
    const int Capacity = 2048;
    internal static ConfigEntry<bool> Flag;
    static readonly Harmony harmony = new Harmony("com.ostranauts.template.sensors");
    static readonly AccessTools.FieldRef<ShipDraw,Point> NativeOffset = AccessTools.FieldRefAccess<ShipDraw,Point>("_posOffset");
    static readonly AccessTools.FieldRef<ShipDraw,double> NativeTime = AccessTools.FieldRefAccess<ShipDraw,double>("_posTimeStamp");
    static readonly Dictionary<ShipDraw,Track> tracks = new Dictionary<ShipDraw,Track>();
    static Ship observer;
    static StarSystem system;
    static SensorSpec[] package = Array.Empty<SensorSpec>();
    static double packageAt = double.NegativeInfinity;
    static bool hasSensor;
    internal static bool Enabled { get; private set; }
    sealed class Track {
        internal string TargetId;
        internal double Sample, Next, RadialNoise, CrossNoise;
        internal int Cols, Rows;
        internal Point Start, End;
        internal bool Initialized, Attempted;
    }
    internal static void Initialize(ConfigFile config) {
        Flag = config.Bind("Sensors", "Enabled", false, "Smooth navigation contact position uncertainty using optical bearing, active ranging and acquired transponder timestamps. Toggle live in PDA Mod Settings; native motion, weapons and ship-target courses are preserved.");
        SetEnabled(Flag.Value);
    }
    internal static void SetEnabled(bool value) {
        if (Enabled != value) {
            Stop();
            if (value) {
                try {
                    harmony.Patch(AccessTools.Method(typeof(ShipDraw),nameof(ShipDraw.GetPositionOffset),new[]{typeof(double)}),prefix: Hook(nameof(Absolute)));
                    harmony.Patch(AccessTools.Method(typeof(ShipDraw),nameof(ShipDraw.GetPositionOffset),Type.EmptyTypes),prefix: Hook(nameof(Relative)));
                    harmony.Patch(AccessTools.Method(typeof(ShipDraw),nameof(ShipDraw.Destroy)),prefix: Hook(nameof(Remove)));
                    harmony.Patch(AccessTools.Method(typeof(GUIOrbitDraw),"OnDestroy"),prefix: Hook(nameof(NavDestroyed)));
                    SceneManager.sceneUnloaded += SceneUnloaded;
                    Enabled = true;
                } catch { Stop(); throw; }
            }
        }
        if (Flag != null) Flag.Value = value;
    }
    static HarmonyMethod Hook(string name) => new HarmonyMethod(typeof(SensorContacts),name);
    internal static void Stop() {
        harmony.UnpatchSelf();
        SceneManager.sceneUnloaded -= SceneUnloaded;
        Enabled = false;
        Reset();
    }
    static void SceneUnloaded(Scene scene) => Reset();
    static void NavDestroyed() => Reset();
    static void Remove(ShipDraw __instance) { tracks.Remove(__instance); }
    internal static void Reset() {
        foreach (var pair in tracks) { NativeOffset(pair.Key) = default; NativeTime(pair.Key) = 0; }
        tracks.Clear(); observer = null; system = null; package = Array.Empty<SensorSpec>(); packageAt = double.NegativeInfinity; hasSensor = false;
        if (GUIOrbitDraw.CrossHairTarget != null) GUIOrbitDraw.CrossHairTarget.SensorOffset = default;
    }
    static bool Absolute(ShipDraw __instance, ref Point __result) {
        if (!TryOffset(__instance,out Point offset)) return true;
        __result = __instance.ship.objSS.vPos + offset;
        return false;
    }
    static bool Relative(ShipDraw __instance, ref Point __result) {
        if (!TryOffset(__instance,out Point offset)) return true;
        __result = offset;
        return false;
    }
    static bool TryOffset(ShipDraw draw, out Point offset) {
        offset = default;
        var nav = GUIOrbitDraw.Instance;
        if (nav == null || CrewSim.system == null) return false;
        if (!ReferenceEquals(system,CrewSim.system)) { Reset(); system = CrewSim.system; }
        var shipUs = nav.COSelf?.ship;
        return TryOffset(draw,shipUs,StarSystem.fEpoch,out offset);
    }
    // Explicit observer/time inputs also allow offline fixtures to exercise the actual adapter.
    internal static bool TryOffset(ShipDraw draw, Ship shipUs, double now, out Point offset) {
        offset = default;
        Ship target = draw?.ship;
        if (target?.objSS == null || target.bDestroyed || shipUs?.objSS == null || shipUs.bDestroyed || shipUs.ElectronicSystems == null) return false;
        if (!ReferenceEquals(observer,shipUs)) { Reset(); observer = shipUs; system = CrewSim.system; }
        if (draw.SignalVisibility == SignalVisibility.None || target.HideFromSystem ||
            target.Classification != Ship.TypeClassification.Default) { Forget(draw); return false; }
        double dx = target.objSS.vPosx - shipUs.objSS.vPosx, dy = target.objSS.vPosy - shipUs.objSS.vPosy;
        double distanceM = Math.Sqrt(dx*dx+dy*dy)*AuM;
        if (!Finite(distanceM) || !Finite(now)) return false;
        bool exact = target == shipUs || target.IsDockedWith(shipUs) || shipUs.IsDockedWith(target) ||
            (target.IsStation(bIgnoreDocks:true) && draw.shipInfo?.Known == true);
        RefreshPackage(shipUs,now);
        if (!exact && !hasSensor) { Forget(draw); return false; }
        if (exact || distanceM <= ModelSettings.DefaultCombatRangeKm*1000) {
            Forget(draw); NativeOffset(draw) = default; NativeTime(draw) = 0;
            return true;
        }
        bool found = tracks.TryGetValue(draw,out Track track);
        if (found && track.TargetId != target.strRegID) { Forget(draw); found = false; }
        if (!found) {
            if (tracks.Count >= Capacity) return false;
            var dims = target.ParseColsRows();
            track = new Track { TargetId = target.strRegID, Cols = Math.Max(1,dims.x), Rows = Math.Max(1,dims.y) };
            uint seed = Seed(shipUs.strRegID,target.strRegID);
            track.RadialNoise = Noise(ref seed); track.CrossNoise = Noise(ref seed);
            tracks.Add(draw,track);
        }
        if (!track.Attempted || now >= track.Next || now < track.Sample) {
            var result = Measure(draw,shipUs,track,dx,dy,distanceM);
            track.Attempted = true;
            if (!result.HasEstimate) {
                // Release a previous modeled offset once. Rejected first samples
                // must leave vanilla's existing offset/timer untouched; clearing
                // them on every redraw rerolls native noise even while paused.
                if (track.Initialized) { NativeOffset(draw) = default; NativeTime(draw) = 0; }
                track.Initialized = false; track.Sample = now; track.Next = now+Interval;
                return false;
            }
            // Bounded independent uniform components each have unit variance. No Unity RNG is consumed.
            double r = Math.Max(1,distanceM), ux = dx*AuM/r, uy = dy*AuM/r;
            double radial = track.RadialNoise*result.RangeSigmaM/AuM, cross = track.CrossNoise*result.CrossRangeSigmaM/AuM;
            Point next = new Point(ux*radial-uy*cross,uy*radial+ux*cross);
            Point current = Interpolate(track,now);
            bool snap = !track.Initialized || now-track.Sample > Interval*4 || now < track.Sample;
            track.Start = snap ? next : current; track.End = next;
            track.Sample = now; track.Next = now+Interval; track.Initialized = true;
        }
        if (!track.Initialized) return false; // Cache rejection until the next simulation-time sample.
        offset = Interpolate(track,now);
        NativeOffset(draw) = offset;
        return true;
    }
    static void Forget(ShipDraw draw) {
        if (tracks.Remove(draw)) { NativeOffset(draw) = default; NativeTime(draw) = 0; }
    }
    static Point Interpolate(Track track, double now) {
        double t = Math.Max(0,Math.Min(1,(now-track.Sample)/Interval));
        return new Point(track.Start.X+(track.End.X-track.Start.X)*t,track.Start.Y+(track.End.Y-track.Start.Y)*t);
    }
    static void RefreshPackage(Ship ship, double now) {
        if (now >= packageAt && now < packageAt+Interval) return;
        var list = ship.ElectronicSystems.aElectronicSystems;
        int count = list?.Count ?? 0;
        if (package.Length != count) package = new SensorSpec[count];
        hasSensor = false;
        for (int i=0;i<count;i++) {
            var sensor = list[i];
            SensorKind kind;
            if (sensor is Radar) kind = SensorKind.Radar;
            else if (sensor is Lidar) kind = SensorKind.Lidar;
            else if (sensor is Optical) kind = SensorKind.Optical;
            else if (sensor is IR) kind = SensorKind.IR;
            else if (sensor is EM) kind = SensorKind.EM;
            else { package[i] = default; continue; }
            package[i] = new SensorSpec(kind,sensor.RangeKm,sensor.SensorStrength,sensor.On);
            hasSensor |= sensor.On;
        }
        packageAt = now;
    }
    static ContactResolution Measure(ShipDraw draw, Ship shipUs, Track track, double dx, double dy, double distanceM) {
        var target = draw.ship;
        var direction = target.objSS.GetDirectionVector(false);
        double length = Math.Max(distanceM/AuM,1e-30);
        double cos = Math.Min(1,Math.Abs((dx*direction.X+dy*direction.Y)/length));
        double sin = Math.Sqrt(Math.Max(0,1-cos*cos));
        double projectedTiles = track.Cols*cos+track.Rows*sin;
        double ax = target.objSS.vAccRCS.x, ay = target.objSS.vAccRCS.y;
        double heat = (Math.Sqrt(ax*ax+ay*ay)*AuM*target.RCSCount+(target.bFusionReactorRunning?100:0))*.015;
        bool broadcasting = !target.IsFlyingDark();
        double em = broadcasting ? TransponderReception.BroadcastEmission : 0;
        var electronics = target.ElectronicSystems?.aElectronicSystems;
        if (electronics != null) for (int i=0;i<electronics.Count;i++) if (electronics[i] != null) em += electronics[i].EMEmission;
        bool knownHull = draw.shipInfo != null && !string.IsNullOrEmpty(draw.shipInfo.dimensions) && draw.shipInfo.dimensions != "?";
        var observation = new Observation(distanceM/1000,crossSection:projectedTiles/20,projectedSizeM:Math.Max(.32,projectedTiles*.32),
            heatSignature:heat,emEmission:em,knownHull:knownHull,transponderBroadcasting:broadcasting);
        var settings = new ModelSettings(true,shipUs.ElectronicSystems.DetectionThreshold,EMPropagation.EmPropagation.Enabled);
        return ContactModel.Evaluate(package,observation,settings);
    }
    static bool Finite(double x) => !double.IsNaN(x) && !double.IsInfinity(x);
    static uint Seed(string a, string b) {
        uint seed = 2166136261;
        if (a != null) foreach (char c in a) seed = unchecked((seed^c)*16777619);
        seed = unchecked((seed^0xff)*16777619);
        if (b != null) foreach (char c in b) seed = unchecked((seed^c)*16777619);
        return seed == 0 ? 1u : seed;
    }
    static double Noise(ref uint seed) {
        seed ^= seed<<13; seed ^= seed>>17; seed ^= seed<<5;
        return (seed/(double)uint.MaxValue*2-1)*1.7320508075688772;
    }
}
