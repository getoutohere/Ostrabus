using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Serialization;
using HarmonyLib;
using Ostrabus.Venus;

int checks = 0;
void Check(bool ok, string message) { checks++; if (!ok) throw new Exception(message); }
object Invoke(Type type, string method, params object[] args) { return type.GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, args)!; }
int venus = Enumerable.Range(0, 100).Count(i => VenusDerelicts.ChooseLoot("VORB", true, i / 100d) == VenusDerelicts.VenusLoot);
Check(venus == 50, "Wrong default 50/50 split");
Check(VenusDerelicts.ChooseLoot("VORB", true, .5) == "RandomShip", "Wrong threshold boundary");
Check(VenusDerelicts.ChooseLoot("OKLG", true, 0) == "RandomShip", "Other stations affected");
Check(VenusDerelicts.ChooseLoot("VORB", false, 0) == "RandomShip", "Missing Venus pool didn't fall back");
BodyOrbit Orbit(string name) { var body = (BodyOrbit)FormatterServices.GetUninitializedObject(typeof(BodyOrbit)); body.strName = name; return body; }
Check(VenusDerelicts.IsVorbOrbit(Orbit("VORB|Aux")), "Placeholder excluded");
Check(VenusDerelicts.IsVorbOrbit(Orbit("VORB")), "VORB orbit excluded");
Check(!VenusDerelicts.IsVorbOrbit(Orbit("Venus")) && !VenusDerelicts.IsVorbOrbit(Orbit("VORBISH")) && !VenusDerelicts.IsVorbOrbit(null), "Scope extends beyond VORB");
Check(!VenusDerelicts.Eligible(null), "Null wreck eligible");
var method = typeof(Ship).GetMethod(nameof(Ship.Undock), new[] { typeof(Ship) })!;
var readIL = typeof(PatchProcessor).GetMethods().Single(m => m.Name == "GetOriginalInstructions" && m.GetParameters().Length == 2 && !m.GetParameters()[1].ParameterType.IsByRef);
var input = ((IEnumerable<CodeInstruction>)readIL.Invoke(null, new object[] { method, null })!).ToList();
var nearest = AccessTools.Method(typeof(StarSystem), nameof(StarSystem.GetNearestBO), new[] { typeof(ShipSitu), typeof(double), typeof(bool) });
Check(input.Count(i => i.Calls(nearest)) == 1, "Unexpected original game IL");
int index = input.FindIndex(i => i.Calls(nearest));
var before = input.Select(i => (i.opcode, i.operand)).ToArray();
var output = ((IEnumerable<CodeInstruction>)Invoke(typeof(VorbUndockAnchorPatch), "Transpiler", input)).ToList();
Check(output.Count == input.Count + 1, "Unexpected IL size change");
Check(output[index].opcode == OpCodes.Ldarg_0 && output[index + 1].Calls(AccessTools.Method(typeof(VorbUndockAnchorPatch), "Reference")), "Wrong replacement stack order");
Check(output[index] == input[index], "Original branch target was discarded");
Check(Enumerable.Range(0, input.Count).Where(i => i != index).All(i => output[i < index ? i : i + 1].opcode == before[i].opcode && Equals(output[i < index ? i : i + 1].operand, before[i].operand)), "Vanilla speed/pilot/lock logic was altered");
Check(output.All(i => !i.Calls(nearest)), "Original lookup remains");
Check(output.Count(i => i.operand is MethodInfo m && m.Name == "LockToBO") == 1, "Vanilla lock call modified");
var empty = new List<CodeInstruction> { new CodeInstruction(OpCodes.Ret) };
Check(((IEnumerable<CodeInstruction>)Invoke(typeof(VorbUndockAnchorPatch), "Transpiler", empty)).Single() == empty[0], "Unknown IL changed");
VorbWreckSpawnPatch.CurrentLoot = VenusDerelicts.VenusLoot;
object[] scope = { "OKLG", null! };
Invoke(typeof(VorbWreckSpawnPatch), "Prefix", scope);
Check(VorbWreckSpawnPatch.CurrentLoot == null, "Nested non-VORB spawn inherited Venus selection");
var failure = new Exception("spawn failed");
Check(ReferenceEquals(Invoke(typeof(VorbWreckSpawnPatch), "Finalizer", failure, scope[1]), failure), "Spawn exception swallowed");
Check(VorbWreckSpawnPatch.CurrentLoot == VenusDerelicts.VenusLoot, "Outer spawn scope not restored");
object[] loot = { "RandomShip" };
Invoke(typeof(VorbWreckLootPatch), "Prefix", loot);
Check((string)loot[0] == VenusDerelicts.VenusLoot, "Vanilla wreck pool not replaced");
loot[0] = "OtherLoot";
Invoke(typeof(VorbWreckLootPatch), "Prefix", loot);
Check((string)loot[0] == "OtherLoot", "Explicit loot replaced");
Invoke(typeof(VorbWreckSpawnPatch), "Finalizer", null!, null!);
Check(VorbWreckSpawnPatch.CurrentLoot == null, "Finished spawn retained state");
Console.WriteLine($"Passed {checks} Venus fix checks using bundled Mono and real Undock IL.");

namespace Ostrabus {
    internal static class OstrabusPlugin { internal static StubLog Logger = new StubLog(); }
    internal class StubLog {
        public void LogWarning(string message) { Console.WriteLine(message); }
        public void LogInfo(string message) { Console.WriteLine(message); }
    }
}
