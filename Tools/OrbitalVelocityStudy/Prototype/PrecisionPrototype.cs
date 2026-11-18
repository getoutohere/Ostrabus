using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace OrbitalVelocityStudy;

// Separate experimental variant, not an implicit widening of the velocity fix.
// Native's 1e-6 early exit can return E=M near periapsis, whose local numerical
// derivative differs from the intended Kepler solution even for e=.0067.
internal static class PrecisionPrototype
{
    static bool Solve(double meanAnomaly, double eccentricity, ref double __result, bool __runOriginal)
    {
        if (!__runOriginal)
            return false;
        double e = eccentricity < .8 ? meanAnomaly : Math.PI;
        for (int i = 0; i < 32; i++)
        {
            double error = e - eccentricity * Math.Sin(e) - meanAnomaly;
            if (Math.Abs(error) < 1e-13)
                break;
            e -= error / (1 - eccentricity * Math.Cos(e));
        }
        double turns = Math.Floor(meanAnomaly / (2 * Math.PI));
        e %= 2 * Math.PI;
        if (e < 0)
            e += 2 * Math.PI;
        __result = e + 2 * Math.PI * turns;
        return false;
    }
    internal static void Install(Harmony patch) => patch.Patch(AccessTools.Method(typeof(BodyOrbit), "SolveBigE"),
        prefix: new HarmonyMethod(typeof(PrecisionPrototype), nameof(Solve)));

    // Offline composition can expose the exact scope of the needed convention
    // change without editing any existing SFF/coast/avoidance source file.
    internal static void AdaptDetachedHelpers(Harmony patch, Assembly suite)
    {
        var assemblies = new List<Assembly> { suite };
        foreach (var name in suite.GetReferencedAssemblies())
            if (name.Name == "Ostrabus")
                assemblies.Add(Assembly.Load(name));
        foreach (var assembly in assemblies)
        {
            var type = assembly.GetType("Ostrabus.Orbital.NativeEphemeris");
            if (type == null)
                continue;
            patch.Patch(AccessTools.Method(type, "Evaluate"), transpiler: new HarmonyMethod(typeof(PrecisionPrototype), nameof(Tighten)));
            Console.WriteLine("PRECISION ADAPTATION: detached ephemeris in " + assembly.GetName().Name);
        }
    }
    static IEnumerable<CodeInstruction> Tighten(IEnumerable<CodeInstruction> instructions)
    {
        int tolerance = 0, iterations = 0;
        foreach (var i in instructions)
        {
            if (i.opcode == OpCodes.Ldc_R8 && Equals(i.operand, 1e-6))
            {
                i.operand = 1e-13;
                tolerance++;
            }
            if ((i.opcode == OpCodes.Ldc_I4_S || i.opcode == OpCodes.Ldc_I4) && Convert.ToInt32(i.operand) == 10)
            {
                i.opcode = OpCodes.Ldc_I4;
                i.operand = 32;
                iterations++;
            }
            yield return i;
        }
        if (tolerance != 1 || iterations != 1)
            throw new InvalidOperationException("Detached precision prototype shape changed: " + tolerance + "/" + iterations);
    }
}
