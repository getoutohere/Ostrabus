using System;
using System.Globalization;
using System.Linq;
using System.Runtime.Serialization;
using System.Collections.Generic;
using HarmonyLib;
using Ostrabus.QoL.WreckSeverity;
using Ostranauts.Ships;

static class DamageLogChecks {
    static int draws;
    static bool throwRay, skipRay;
    static double[] returned;
    static bool Random(ref double __result) { __result = (++draws % 3 + 1) * .2; return false; }
    static bool Ray(JsonAttackMode jam, float fMult) {
        if (throwRay) throw new InvalidOperationException("fixture failure");
        if (!skipRay) returned = new[] { jam.GetDmgAmount(null), jam.GetDmgAmount(null), jam.GetDmgAmount(null) };
        return false;
    }

    internal static void Run(Action<bool, string> check) {
        var fixture = new Harmony("ostrabus.tests.wrecklog.scene");
        var observer = new Harmony("ostrabus.tests.wrecklog.observer");
        var binding = new Harmony("ostrabus.tests.wrecklog.binding");
        var rollMethod = AccessTools.Method(typeof(JsonAttackMode), nameof(JsonAttackMode.GetDmgAmount));
        var ship = (Ship)FormatterServices.GetUninitializedObject(typeof(Ship));
        ship.strRegID = "O-LOGTEST"; ship.DMGStatus = Ship.Damage.Derelict;
        var mode = new JsonAttackMode { strName = "AModeDerelictBreakIn", strType = "ranged", fDmgEnv = 100, fDmgBlunt = 20, fDmgCut = 80 };
        var system = (DamageSystem)FormatterServices.GetUninitializedObject(typeof(DamageSystem));
        var culture = CultureInfo.CurrentCulture;
        try {
            WreckDamageLog.Enabled = false;
            observer.CreateClassProcessor(typeof(WreckRayRollPatch)).Patch();
            check(Harmony.GetPatchInfo(rollMethod)?.Postfixes.Any(p => p.owner == observer.Id) != true,
                "Disabled logging installed the damage-roll observer");
            WreckSeverityPatch.Prefix(ship, true, out var disabled);
            check(disabled == null && WreckDamageLog.Current == null, "Disabled logging allocated a scope");

            fixture.Patch(AccessTools.Method(typeof(MathUtils), nameof(MathUtils.Rand), new[] { typeof(double), typeof(double), typeof(MathUtils.RandType), typeof(string) }),
                prefix: new HarmonyMethod(typeof(DamageLogChecks), nameof(Random)));
            fixture.Patch(AccessTools.Method(typeof(DamageSystem), nameof(DamageSystem.DamageRayRandom)), prefix: new HarmonyMethod(typeof(DamageLogChecks), nameof(Ray)));
            draws = 0;
            WreckDamageLog.Ray(system, mode, .5625f, null, false, 0);
            var baseline = returned.ToArray();
            check(draws == 3, "Native ray-roll fixture must draw three independent amounts");

            WreckDamageLog.Enabled = true;
            observer.CreateClassProcessor(typeof(WreckRayRollPatch)).Patch();
            check(Harmony.GetPatchInfo(rollMethod).Postfixes.Any(p => p.owner == observer.Id), "Enabled observer did not bind");
            binding.CreateClassProcessor(typeof(WreckSeverityPatch)).Patch();
            check(WreckDamageLog.Enabled, "Reviewed native initialization call-site contract failed");
            // Native body is not executed: the test binds the actual rewritten
            // initialization method and exercises the real GetDmgAmount below.
            ship.fBreakInMultiplier = 1;
            WreckSeverityPatch.Prefix(ship, true, out var capture);
            check(capture != null && capture.Original == 1 && capture.Adjusted == .75f, "Original/curve rolls were not captured");
            ship.fBreakInMultiplier *= .75f; // The native bonus stays in BreakIn.
            draws = 0;
            WreckDamageLog.Ray(system, mode, ship.fBreakInMultiplier, null, false, 0);
            check(draws == 3 && returned.SequenceEqual(baseline), "Logging changed native random calls/results");
            check(capture.Rays == 1 && capture.RaysWithRolls == 1 && capture.Details[0].Completed, "Actual ray attempt/count missing");
            check(Math.Abs(capture.Env - baseline[0] * 100 * .5625) < 1e-9 &&
                Math.Abs(capture.Blunt - baseline[1] * 20 * .5625) < 1e-9 &&
                Math.Abs(capture.Cut - baseline[2] * 80 * .5625) < 1e-9, "Randomized channel budgets are incorrect");

            // Other modes and already-complete rays must not contaminate totals.
            var unrelated = new JsonAttackMode { strType = "ranged", fDmgEnv = 900 };
            unrelated.GetDmgAmount(null); mode.GetDmgAmount(null);
            check(capture.Details[0].Draws == 3, "Unrelated/non-ray rolls contaminated the capture");
            skipRay = true;
            WreckDamageLog.Ray(system, mode, .5625f, null, false, 0);
            skipRay = false;
            check(capture.Rays == 2 && capture.RaysWithRolls == 1 && capture.Details[1].Draws == 0,
                "A native early-return ray must be labeled unavailable, not fabricated zero damage");

            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            string message = string.Join("\n", WreckDamageLog.Format(capture, ship.fBreakInMultiplier, null));
            check(message.Contains("original=1 -> curve=0.75 -> native-final=0.5625") &&
                message.Contains("rays=2 with damage rolls=1") && message.Contains("not final hull damage") &&
                message.Contains("unavailable (0/3 rolls)"), "Diagnostic format lost stage/count/measurement distinctions");
            check(message.All(ch => ch <= 127), "Report introduced non-ASCII glyphs");

            var inner = WreckDamageLog.Begin(ship, .75f);
            throwRay = true;
            try { WreckDamageLog.Ray(system, mode, .5625f, null, false, 0); check(false, "Native ray exception was swallowed"); }
            catch (InvalidOperationException error) { check(error.Message == "fixture failure", "Native exception was replaced"); }
            finally { throwRay = false; }
            check(inner.Pending == null && inner.Rays == 1 && !inner.Details[0].Completed, "Interrupted ray scope leaked");
            WreckSeverityPatch.Finalizer(ship, inner, new InvalidOperationException());
            check(ReferenceEquals(WreckDamageLog.Current, capture), "Nested scope did not restore its parent");
            WreckSeverityPatch.Finalizer(ship, capture, null);
            check(WreckDamageLog.Current == null, "Finished initialization retained its capture");
            ship.DMGStatus = Ship.Damage.Used;
            WreckSeverityPatch.Prefix(ship, true, out var excluded);
            check(excluded == null, "Operational ship initialized a wreck diagnostic");

            var reader = typeof(PatchProcessor).GetMethods().Single(method => method.Name == "GetOriginalInstructions" &&
                method.GetParameters().Length == 2 && !method.GetParameters()[1].ParameterType.IsByRef);
            var original = ((IEnumerable<CodeInstruction>)reader.Invoke(null,
                new object[] { AccessTools.Method(typeof(Ship), "BreakIn"), null })).ToList();
            WreckDamageLog.Enabled = false;
            check(ReferenceEquals(WreckSeverityPatch.Transpiler(original), original), "Disabled diagnostics rewrote native initialization");
            WreckDamageLog.Enabled = true;
            var randomRay = AccessTools.Method(typeof(DamageSystem), nameof(DamageSystem.DamageRayRandom));
            var unfamiliar = original.Where(instruction => !instruction.Calls(randomRay)).ToList();
            var retained = WreckSeverityPatch.Transpiler(unfamiliar).ToList();
            check(!WreckDamageLog.Enabled && retained.Count == unfamiliar.Count &&
                retained.Select((instruction, i) => instruction.opcode == unfamiliar[i].opcode && Equals(instruction.operand, unfamiliar[i].operand)).All(same => same),
                "Unfamiliar call sites must disable diagnostics and retain the original IL");
        } finally {
            CultureInfo.CurrentCulture = culture;
            WreckDamageLog.Current = null; WreckDamageLog.Enabled = false;
            binding.UnpatchSelf(); observer.UnpatchSelf(); fixture.UnpatchSelf();
        }
    }
}
