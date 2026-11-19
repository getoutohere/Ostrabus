using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using HarmonyLib;
using Ostranauts.Ships;
using UnityEngine;

namespace Ostrabus.QoL.WreckSeverity;

// One synchronous initialization scope, never a ship registry or saved record.
internal static class WreckDamageLog {
    internal static bool Enabled;
    [ThreadStatic] internal static Capture Current;
    const int MaxRayDetails = 32;

    internal sealed class Capture {
        internal Capture Parent;
        internal string Id, Region, Name;
        internal int Columns, Rows;
        internal float Original, Adjusted, Reduction;
        internal int Holes, MinRadius = int.MaxValue, MaxRadius, HoleSelected, SalvageSelected, ForbiddenSelected;
        internal float HoleChance, SalvageChance, GeneralWear, SalvageWear;
        internal bool HasHoleChance, HasSalvageChance, HasGeneralWear, HasSalvageWear;
        internal string ForbiddenTrigger;
        internal int Rays, RaysWithRolls;
        internal double Env, Blunt, Cut;
        internal RayRoll Pending;
        internal readonly List<RayRoll> Details = new List<RayRoll>();
    }
    internal sealed class RayRoll {
        internal JsonAttackMode Mode;
        internal float Strength;
        internal int Draws;
        internal double Env, Blunt, Cut;
        internal bool Completed;
    }

    internal static Capture Begin(Ship ship, float original) {
        var capture = new Capture { Parent = Current, Id = ship.strRegID, Region = CollisionManager.strATCClosest,
            Name = ship.publicName, Columns = ship.nCols, Rows = ship.nRows,
            Original = original, Adjusted = ship.fBreakInMultiplier, Reduction = WreckSeverity.Reduction?.Value ?? 0 };
        Current = capture;
        return capture;
    }

    internal static void End(Ship ship, Capture capture, Exception failure) {
        if (capture == null) return;
        Current = capture.Parent;
        try {
            foreach (string line in Format(capture, ship?.fBreakInMultiplier ?? capture.Adjusted, failure)) {
                ConsoleToGUI.instance?.LogInfo(line);
                OstrabusPlugin.Logger?.LogInfo(line);
            }
        } catch (Exception error) {
            // A diagnostic formatting/console failure must not replace a native
            // exception or turn otherwise successful wreck generation into one.
            OstrabusPlugin.Logger?.LogWarning("Wreck damage report unavailable: " + error.GetType().Name);
        }
    }

    static int OnShip(List<CondOwner> objects, Ship ship) {
        int count = 0;
        foreach (var item in objects) if (item.ship == ship) count++;
        return count;
    }

    internal static JsonZone Hole(Ship ship, Vector3 position, int radius, bool shuffled, bool circle) {
        var result = TileUtils.GetZoneFromTileRadius(ship, position, radius, shuffled, circle);
        var capture = Current;
        if (capture != null) {
            capture.Holes++;
            capture.MinRadius = Math.Min(capture.MinRadius, radius);
            capture.MaxRadius = Math.Max(capture.MaxRadius, radius);
        }
        return result;
    }

    internal static List<CondOwner> HoleSelection(Ship ship, JsonZone zone, CondTrigger trigger, bool locked, bool docked) {
        var result = ship.GetCOsInZone(zone, trigger, locked, docked);
        var capture = Current;
        if (capture != null) {
            capture.HoleSelected += OnShip(result, ship);
            capture.HoleChance = trigger.fChance; capture.HasHoleChance = true;
        }
        return result;
    }

    internal static List<CondOwner> Selection(Ship ship, CondTrigger trigger, bool subObjects, bool docked, bool locked) {
        var result = ship.GetCOs(trigger, subObjects, docked, locked);
        var capture = Current;
        if (capture != null && trigger != null) {
            if (trigger.strName == "TIsDerelictSalvage") {
                capture.SalvageSelected += OnShip(result, ship);
                capture.SalvageChance = trigger.fChance; capture.HasSalvageChance = true;
            } else if (trigger.strName?.StartsWith("TIsDerelictForbid", StringComparison.Ordinal) == true) {
                capture.ForbiddenSelected += OnShip(result, ship); capture.ForbiddenTrigger = trigger.strName;
            }
        }
        return result;
    }

    internal static void Wear(Ship ship, float amount, bool multiple, CondTrigger trigger) {
        var capture = Current;
        if (capture != null) {
            if (trigger?.strName == "TIsDerelictSalvage") { capture.SalvageWear = amount; capture.HasSalvageWear = true; }
            else { capture.GeneralWear = amount; capture.HasGeneralWear = true; }
        }
        ship.DamageAllCOs(amount, multiple, trigger);
    }

    internal static void Ray(DamageSystem system, JsonAttackMode mode, float strength, CondOwner attacker, bool docked, float angle) {
        var capture = Current;
        if (capture == null) { system.DamageRayRandom(mode, strength, attacker, docked, angle); return; }
        var ray = new RayRoll { Mode = mode, Strength = strength };
        var previous = capture.Pending;
        capture.Pending = ray;
        capture.Rays++;
        try {
            system.DamageRayRandom(mode, strength, attacker, docked, angle);
            ray.Completed = true;
        } finally {
            capture.Pending = previous;
            if (ray.Draws == 3) {
                capture.RaysWithRolls++;
                capture.Env += ray.Env; capture.Blunt += ray.Blunt; capture.Cut += ray.Cut;
            }
            if (capture.Details.Count < MaxRayDetails) capture.Details.Add(ray);
        }
    }

    internal static void ObserveRoll(JsonAttackMode mode, CondOwner attacker, double result) {
        var ray = Current?.Pending;
        if (ray == null || !ReferenceEquals(ray.Mode, mode) || !ReferenceEquals(attacker, null) || ray.Draws >= 3) return;
        // Native DamageRay makes three independent GetDmgAmount calls, in this
        // order. Observe their returned values; never call the RNG again.
        switch (ray.Draws++) {
            case 0: ray.Env = mode.fDmgEnv * result * ray.Strength; break;
            case 1: ray.Blunt = mode.fDmgBlunt * result * ray.Strength; break;
            case 2: ray.Cut = mode.fDmgCut * result * ray.Strength; break;
        }
    }

    static string N(double value) => value.ToString("0.####", CultureInfo.InvariantCulture);
    internal static bool NativeRayLayoutSupported() {
        var target = AccessTools.Method(typeof(DamageSystem), nameof(DamageSystem.DamageRay));
        var roll = AccessTools.Method(typeof(JsonAttackMode), nameof(JsonAttackMode.GetDmgAmount));
        var reader = typeof(PatchProcessor).GetMethods().Single(method => method.Name == "GetOriginalInstructions" &&
            method.GetParameters().Length == 2 && !method.GetParameters()[1].ParameterType.IsByRef);
        var code = ((IEnumerable<CodeInstruction>)reader.Invoke(null, new object[] { target, null })).ToList();
        var calls = code.Select((instruction, index) => (instruction, index)).Where(row => row.instruction.Calls(roll)).ToArray();
        if (calls.Length != 3) return false;
        string[] coefficients = { nameof(JsonAttackMode.fDmgEnv), nameof(JsonAttackMode.fDmgBlunt), nameof(JsonAttackMode.fDmgCut) };
        for (int i = 0; i < calls.Length; i++) {
            int start = i == 0 ? 0 : calls[i - 1].index + 1;
            var getter = AccessTools.PropertyGetter(typeof(JsonAttackMode), coefficients[i]);
            if (!code.Skip(start).Take(calls[i].index - start).Any(instruction => instruction.Calls(getter))) return false;
        }
        return true;
    }

    internal static IEnumerable<string> Format(Capture c, float finalSeverity, Exception failure) {
        string prefix = "[WreckDamage " + c.Id + "] ";
        yield return prefix + "severity original=" + N(c.Original) + " -> curve=" + N(c.Adjusted) +
            " -> native-final=" + N(finalSeverity) + " (reduction=" + N(c.Reduction) + ", region=" + c.Region +
            ", grid=" + c.Columns + "x" + c.Rows + ", name=" + c.Name + ")" +
            (failure == null ? "" : "; initialization FAILED: " + failure.GetType().Name);
        yield return prefix + "holes=" + c.Holes + " radii=" + (c.Holes == 0 ? "n/a" : c.MinRadius + "-" + c.MaxRadius + " tiles") +
            " selected=" + c.HoleSelected + " selectionChance=" + (c.HasHoleChance ? N(c.HoleChance) : "n/a") +
            "; high-value cull selected=" + c.SalvageSelected + " chance=" + (c.HasSalvageChance ? N(c.SalvageChance) : "n/a") +
            "; regional cull selected=" + c.ForbiddenSelected + " trigger=" + (c.ForbiddenTrigger ?? "none");
        yield return prefix + "wear maxima general=" + (c.HasGeneralWear ? N(c.GeneralWear) : "n/a") +
            " high-value=" + (c.HasSalvageWear ? N(c.SalvageWear) : "n/a") +
            "; rays=" + c.Rays + " with damage rolls=" + c.RaysWithRolls +
            "; incoming budget totals env/blunt/cut=" + N(c.Env) + "/" + N(c.Blunt) + "/" + N(c.Cut);
        var rays = new StringBuilder(prefix + "ray budgets env/blunt/cut:");
        for (int i = 0; i < c.Details.Count; i++) {
            var r = c.Details[i];
            rays.Append(" #").Append(i + 1).Append(" strength=").Append(N(r.Strength)).Append(' ');
            if (r.Draws == 3) rays.Append(N(r.Env)).Append('/').Append(N(r.Blunt)).Append('/').Append(N(r.Cut));
            else rays.Append("unavailable (").Append(r.Draws).Append("/3 rolls)");
            if (!r.Completed) rays.Append(" interrupted");
        }
        if (c.Rays > c.Details.Count) rays.Append("; further ray details omitted");
        rays.Append(". Incoming budgets precede ray intersections/absorption; they are not final hull damage.");
        yield return rays.ToString();
    }
}
