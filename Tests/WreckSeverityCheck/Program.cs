using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Serialization;
using BepInEx.Configuration;
using HarmonyLib;
using Ostrabus.QoL.WreckSeverity;

internal static class Program {
    private static int checks;
    private static void Check(bool valid, string message) {
        ++checks;
        if (!valid) throw new InvalidOperationException(message);
    }

    private static void Near(float actual, float expected, string message) =>
        Check(Math.Abs(actual - expected) < 0.000001f, message + ": " + actual + " instead of " + expected);

    private static int Main() {
        CurveChecks();
        InitialDamageChecks();
        NativeContractChecks();
        DamageLogChecks.Run(Check);
        BonusLogChecks.Run(Check);
        Console.WriteLine("Wreck severity: " + checks + " curve, scope, native contract and configuration assertions passed.");
        return 0;
    }

    private static void CurveChecks() {
        foreach (var pair in new[] {
            (0f, 0f), (0.1f, 0.09022556f), (0.2f, 0.16666667f), (0.4f, 0.3f),
            (0.5f, 0.36363636f), (0.6f, 0.42857143f), (0.8f, 0.57142857f), (1f, 0.75f)
        }) Near(WreckSeverity.Adjust(pair.Item1, 0.25f), pair.Item2, "Agreed curve anchor");

        foreach (float r in new[] { 0f, 0.01f, 0.25f, 0.5f, 0.9f, 0.99f, 0.99999994f }) {
            float previous = 0;
            for (int i = 0; i <= 10000; ++i) {
                float s = i / 10000f;
                float y = WreckSeverity.Adjust(s, r);
                Check(y >= previous && y <= s && (s == 0 ? y == 0 : y > 0), "Curve reverses roll order, increases severity or erases a positive roll");
                if (r == 0) Check(y == s, "Zero setting must be an exact native identity");
                previous = y;
            }
            Near(WreckSeverity.Adjust(1, r), 1 - r, "Maximum severity follows setting");
            Check(WreckSeverity.Adjust(float.Epsilon, r) > 0, "Smallest positive severity was erased");
        }
        foreach (float r in new[] { -0.25f, 1f, 2f, float.NaN, float.PositiveInfinity, float.NegativeInfinity })
            Check(WreckSeverity.Adjust(0.6f, r) == 0.6f, "Invalid setting must preserve native severity");
        foreach (float s in new[] { -1f, 1.1f, float.PositiveInfinity, float.NegativeInfinity })
            Check(WreckSeverity.Adjust(s, 0.25f) == s, "Unexpected native severity must be retained");
        Check(float.IsNaN(WreckSeverity.Adjust(float.NaN, 0.25f)), "NaN native severity must not be rewritten");
    }

    private static void InitialDamageChecks() {
        string configPath = Path.Combine(Directory.GetCurrentDirectory(), "obj", "WreckSeverityCheck", "test.cfg");
        AccessTools.Field(typeof(BepInEx.Paths), "<BepInExConfigPath>k__BackingField").SetValue(null, configPath + ".core");
        var config = new ConfigFile(configPath, false);
        config.SaveOnConfigSet = false;
        WreckSeverity.Initialize(config);
        Check(config.Count == 2 && WreckSeverity.Reduction.Value == 0.25f && !WreckSeverity.LogDamageRolls.Value && !WreckDamageLog.Enabled,
            "Feature must expose the severity setting and default-off damage logging");
        Check(WreckSeverity.Reduction.Definition.Section == "QoL.WreckSeverity" &&
            WreckSeverity.Reduction.Definition.Key == "SeverityReduction", "CFG contract changed");

        var ship = (Ship)FormatterServices.GetUninitializedObject(typeof(Ship));
        foreach (Ship.Damage damage in Enum.GetValues(typeof(Ship.Damage))) {
            foreach (Ship.TypeClassification classification in Enum.GetValues(typeof(Ship.TypeClassification))) {
                foreach (bool prefill in new[] { false, true }) {
                    ship.DMGStatus = damage;
                    ship.Classification = classification;
                    ship.fBreakInMultiplier = 0.4f;
                    WreckSeverityPatch.Prefix(ship, prefill, out var state);
                    Check(state == null, "Default-off logging created a capture");
                    bool applies = prefill && damage == Ship.Damage.Derelict && classification != Ship.TypeClassification.Asteroid;
                    Near(ship.fBreakInMultiplier, applies ? 0.3f : 0.4f, "Wrong initial-damage scope");
                }
            }
        }
        ship.DMGStatus = Ship.Damage.Derelict;
        ship.Classification = Ship.TypeClassification.Default;
        ship.fBreakInMultiplier = 0.4f;
        WreckSeverityPatch.Prefix(ship, true, out _);
        WreckSeverityPatch.Prefix(ship, false, out _);
        Near(ship.fBreakInMultiplier, 0.3f, "A completed initialization must not be adjusted on reload or developer re-damage");

        WreckSeverity.Reduction.Value = 0;
        ship.fBreakInMultiplier = 0.4f;
        WreckSeverityPatch.Prefix(ship, true, out _);
        Check(ship.fBreakInMultiplier == 0.4f, "CFG zero did not disable the production prefix");
        WreckSeverity.Reduction.Value = float.NaN;
        WreckSeverityPatch.Prefix(ship, true, out _);
        Check(ship.fBreakInMultiplier == 0.4f, "Invalid CFG did not preserve native behavior");
        WreckSeverityPatch.Prefix(null, true, out _);
        WreckSeverity.Reduction.Value = 0.25f;
    }

    private static void NativeContractChecks() {
        Check((bool)AccessTools.Method(typeof(WreckSeverityPatch), "Prepare").Invoke(null, null), "Native target no longer binds");
        var target = AccessTools.DeclaredMethod(typeof(Ship), "BreakIn", Type.EmptyTypes);
        var prefill = AccessTools.DeclaredField(typeof(Ship), "bPrefill");
        var init = AccessTools.Method(typeof(Ship), nameof(Ship.InitShip));
        var reader = typeof(PatchProcessor).GetMethods().Single(method => method.Name == "GetOriginalInstructions" &&
            method.GetParameters().Length == 2 && !method.GetParameters()[1].ParameterType.IsByRef);
        var code = (List<CodeInstruction>)reader.Invoke(null, new object[] { init, null });
        int call = code.FindIndex(i => i.operand is MethodInfo method && method == target);
        Check(call > 0 && code.Take(call).Any(i => i.opcode == OpCodes.Ldfld && Equals(i.operand, prefill)),
            "Native initializer no longer gates its damage pass on prefill");
        Check(code.Skip(call + 1).Any(i => i.opcode == OpCodes.Stfld && Equals(i.operand, prefill)),
            "Native initializer no longer clears prefill after damage");
        Check(typeof(JsonShip).GetProperty("bPrefill")?.PropertyType == typeof(bool) &&
            typeof(JsonShip).GetProperty("fBreakInMultiplier")?.PropertyType == typeof(float),
            "Native persistence fields no longer match the one-time initialization contract");

        // Bind the actual production prefix to the actual native target offline.
        // The target body is never invoked: damage generation requires Unity.
        var harmony = new Harmony("ostrabus.tests.wreckseverity");
        try {
            harmony.CreateClassProcessor(typeof(WreckSeverityPatch)).Patch();
            Check(Harmony.GetPatchInfo(target).Prefixes.Any(p => p.owner == harmony.Id), "Production prefix did not bind");
        } finally { harmony.UnpatchSelf(); }
    }
}

namespace Ostrabus {
    // Production logging target; the test performs no plugin initialization.
    internal static class OstrabusPlugin {
        internal static BepInEx.Logging.ManualLogSource Logger = null;
    }
}
