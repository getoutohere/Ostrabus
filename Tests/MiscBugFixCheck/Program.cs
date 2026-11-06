using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Serialization;
using HarmonyLib;
using Ostrabus.MiscBugFixes;

static class Program {
    static int checks;
    static Ship active;
    static void Check(bool ok, string message) { checks++; if (!ok) throw new Exception(message); }
    static bool Current(ref Ship __result) { __result = active; return false; }
    public static void Main() {
        new Harmony("offline.misc.rotation").Patch(AccessTools.PropertyGetter(typeof(CrewSim), nameof(CrewSim.shipCurrentLoaded)), prefix: new HarmonyMethod(typeof(Program), nameof(Current)));
        var reader = typeof(PatchProcessor).GetMethods().Single(m => m.Name == "GetOriginalInstructions" && m.GetParameters().Length == 2 && !m.GetParameters()[1].ParameterType.IsByRef);
        var helper = AccessTools.Method(typeof(ShipRotationPatch), nameof(ShipRotationPatch.ActiveShipIsDocked));
        foreach (var type in new[] { typeof(Ostranauts.InputControl.CommandRotateCW), typeof(Ostranauts.InputControl.CommandRotateCCW) }) {
            var method = AccessTools.Method(type, "Execute");
            var il = ((IEnumerable<CodeInstruction>)reader.Invoke(null, new object[] { method, null })).ToList();
            var before = il.Select(i => (i.opcode, i.operand, labels: ShipRotationPatch.Labels(i).Cast<object>().ToArray(), blocks: i.blocks.ToArray())).ToArray();
            var output = ShipRotationPatch.Transpiler(il, method).ToList();
            int start = output.FindIndex(i => i.Calls(helper));
            Check(start >= 0 && output.Count == before.Length, "Guard not replaced: " + type.Name);
            Check(output.Count(i => i.Calls(helper)) == 1, "Duplicate guard");
            for (int i = 0; i < output.Count; i++) {
                Check(ReferenceEquals(output[i], il[i]) && ShipRotationPatch.Labels(output[i]).Cast<object>().SequenceEqual(before[i].labels) && output[i].blocks.SequenceEqual(before[i].blocks), "Branch metadata altered");
                if (i < start || i > start + 10) Check(output[i].opcode == before[i].opcode && Equals(output[i].operand, before[i].operand), "Surrounding vanilla behavior altered");
                else if (i > start) Check(output[i].opcode == OpCodes.Nop, "Original captain guard remains");
            }
            var dynamicGuard = new DynamicMethod("RotationGuard", typeof(bool), Type.EmptyTypes, typeof(Program).Module, true);
            var generator = dynamicGuard.GetILGenerator();
            generator.Emit(OpCodes.Call, helper); generator.Emit(OpCodes.Ret);
            var guard = (Func<bool>)dynamicGuard.CreateDelegate(typeof(Func<bool>));
            active = null; Check(!guard(), "Null ship rejected incorrectly");
            active = (Ship)FormatterServices.GetUninitializedObject(typeof(Ship));
            var links = new Dictionary<string, Ship>(); AccessTools.Field(typeof(Ship), "aDocked").SetValue(active, links);
            Check(!guard(), "Free active ship blocked");
            links["port"] = active; Check(guard(), "Docked active ship permitted");
            links.Clear(); links["MP|tether"] = active; Check(guard(), "Moored active ship permitted");
            links.Clear(); Check(!guard(), "Undocking not reflected immediately");
            var bad = new List<CodeInstruction> { new CodeInstruction(OpCodes.Ret) };
            Check(ShipRotationPatch.Transpiler(bad, method).Single() == bad[0], "Unknown IL modified");
        }
        new Harmony("offline.misc.rotation").Patch(AccessTools.PropertyGetter(typeof(CrewSim), nameof(CrewSim.shipCurrentLoaded)), prefix: new HarmonyMethod(typeof(Program), nameof(Current)));
        active = (Ship)FormatterServices.GetUninitializedObject(typeof(Ship));
        AccessTools.Field(typeof(Ship), "aDocked").SetValue(active, new Dictionary<string, Ship>());
        CrewSim.coPlayer = null;
        Check(!ShipRotationPatch.ActiveShipIsDocked(), "Captain independence failed");
        AccessTools.Field(typeof(Ship), "aDocked").SetValue(active, new Dictionary<string, Ship> { ["port"] = active });
        Check(ShipRotationPatch.ActiveShipIsDocked(), "Missing captain bypasses docking guard");
        var harmony = new Harmony("offline.misc.rotation.commands");
        foreach (var type in new[] { typeof(Ostranauts.InputControl.CommandRotateCW), typeof(Ostranauts.InputControl.CommandRotateCCW) }) {
            var command = AccessTools.Method(type, "Execute");
            harmony.Patch(command, transpiler: new HarmonyMethod(typeof(ShipRotationPatch), nameof(ShipRotationPatch.Transpiler)));
            Check(Harmony.GetPatchInfo(command).Transpilers.Count == 1, "Native command patch failed");
        }
        Console.WriteLine($"Passed {checks} rotation checks against both native command methods.");
    }
}
namespace Ostrabus {
    internal static class OstrabusPlugin { internal static StubLog Logger = new StubLog(); }
    internal class StubLog { public void LogWarning(string message) { Console.WriteLine(message); } }
}



