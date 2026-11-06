using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Ostrabus.Optimizations;

System.Threading.Thread.CurrentThread.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
foreach (string name in new[] { "dictNamesFirst", "dictNamesFull", "dictNamesLast", "dictNamesRobots", "mapCOs" }) {
    var field = typeof(DataHandler).GetField(name)!;
    field.SetValue(null, Activator.CreateInstance(field.FieldType));
}
if (args.Length == 3 && args[0] == "validate") {
    Environment.ExitCode = Benchmark.Validate(args[1], args[2]);
    return;
}

int checks = 0;
void Check(bool condition, string message) { checks++; if (!condition) throw new Exception(message); }
var patchType = typeof(SavedJsonLoadingPatch);
var targets = ((IEnumerable<MethodBase>)patchType.GetMethod("TargetMethods", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, null)!).ToArray();
Check(targets.Length == 2 && targets.All(m => !m.ContainsGenericParameters), "Hook targets contain an open generic method");
foreach (var target in targets) {
var input = ((IEnumerable<CodeInstruction>)typeof(PatchProcessor).GetMethods().Single(m => m.Name == "GetOriginalInstructions" && m.GetParameters().Length == 2 && !m.GetParameters()[1].ParameterType.IsByRef).Invoke(null, new object[] { target, null })!).ToList();
var output = ((IEnumerable<CodeInstruction>)patchType.GetMethod("Transpiler", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, new object[] { input })!).ToList();
Check(output.Count == input.Count, "Instruction count changed");
Check(output.Count(i => i.opcode == OpCodes.Call && i.operand is MethodInfo m && m.DeclaringType == typeof(SavedJsonLoading)) == 1, "Actual game loader call was not redirected exactly once");
Check(output.All(i => !(i.operand is MethodInfo m && m.DeclaringType == typeof(DataHandler) && m.Name == "JsonToData")), "Vanilla call left in rewritten caller");
}
var noMatch = new List<CodeInstruction> { new CodeInstruction(OpCodes.Ret) };
var unchanged = ((IEnumerable<CodeInstruction>)patchType.GetMethod("Transpiler", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, new object[] { noMatch })!).ToList();
Check(unchanged.Count == 1 && unchanged[0] == noMatch[0], "Unrecognized IL was modified");
SavedJsonLoading.Enabled = false;
Check(SavedJsonLoading.Deserialize<JsonShip[]>("[{\"strName\":\"test\"}]")[0].strName == "test", "Disabled path failed");
Check(LoadProfiling.Calls == 0, "Disabled path used adapter");
SavedJsonLoading.Enabled = true;
Check(SavedJsonLoading.Deserialize<JsonShip[]>("[{\"strName\":\"test\",\"fShallowMass\":9E99}]")[0].fShallowMass == double.PositiveInfinity, "Enabled path lost infinity");
Check(LoadProfiling.Calls == 1, "Enabled path missing telemetry");
Check(SavedJsonLoading.Deserialize<int[]>("[1,2]")[1] == 2 && LoadProfiling.Calls == 1, "Unrelated types entered adapter");
LitJson.JsonMapper.RegisterImporter<int, double>(value => value + 7d);
try {
    Check(SavedJsonLoading.Deserialize<JsonShip[]>("[{\"fShallowMass\":42}]")[0].fShallowMass == 49d, "Custom importer fallback failed");
    Check(LoadProfiling.Calls == 1, "Custom importer entered adapter");
} finally { LitJson.JsonMapper.UnregisterImporters(); }
bool failed = false;
try { SavedJsonLoading.Deserialize<JsonShip[]>("[{\"fShallowMass\":null}]"); } catch (LitJson.JsonException) { failed = true; }
Check(failed && LoadProfiling.Errors == 1, "Adapter failure swallowed or absent from telemetry");
Console.WriteLine($"Passed {checks} loader integration checks");
foreach (string culture in new[] { "", "fr-FR" }) {
    System.Threading.Thread.CurrentThread.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo(culture);
    if (Benchmark.CompatibilityCases() != 0) throw new Exception("Compatibility cases failed for " + culture);
}

namespace Ostrabus.Optimizations {
    internal static class ShipFileProfilePatch {
        static void ReadMemory(string path, Dictionary<string, JsonShip> output, Dictionary<string, byte[]> files) { DataHandler.JsonToData(path, output, files); }
    }
    internal static class LoadProfiling {
        internal struct Sample {}
        internal static int Calls, Errors;
        internal static Sample Enter(string name) { Calls++; return new Sample(); }
        internal static void Exit(Sample sample, Exception error) { if (error != null) Errors++; }
    }
}
namespace Ostrabus {
    internal static class OstrabusPlugin { internal static StubLog Logger = new StubLog(); }
    internal class StubLog {
        public void LogWarning(string message) { Console.WriteLine(message); }
        public void LogInfo(string message) { Console.WriteLine(message); }
    }
}
