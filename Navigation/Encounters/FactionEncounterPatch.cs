using System;
using System.Collections.Generic;
using System.Collections;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Ostranauts.Ships.AIPilots;

namespace Ostrabus.ShipNavigation.Encounters;

[HarmonyPatch]
internal static class FactionEncounterPatch {
    static readonly AccessTools.FieldRef<float> Roll = AccessTools.StaticFieldRefAccess<float>(AccessTools.Field(typeof(BeatManager), "fRoll"));
    static readonly AccessTools.FieldRef<Dictionary<string, float>> Chances = AccessTools.StaticFieldRefAccess<Dictionary<string, float>>(AccessTools.Field(typeof(BeatManager), "dictEventChances"));
    [HarmonyTargetMethods]
    internal static IEnumerable<MethodBase> Targets() {
        yield return AccessTools.Method(typeof(BeatManager), "FactionShip");
        yield return AccessTools.Method(typeof(BeatManager), "FactionBattle");
    }
    [HarmonyPrefix]
    static bool Prefix(MethodBase __originalMethod, ref bool __result) {
        if (FactionEncounter.CanStage()) return true;
        if (__originalMethod.Name == "FactionShip" && Chances() != null && Chances().TryGetValue("tension_factionship", out float chance)) Roll() -= chance;
        __result = false;
        return false;
    }
    [HarmonyTranspiler]
    internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator, MethodBase __originalMethod) {
        var code = new List<CodeInstruction>(instructions);
        bool ordinary = __originalMethod.Name == "FactionShip";
        var stations = AccessTools.Method(typeof(AIShipManager), nameof(AIShipManager.GetAllStationsInATCRegion));
        var add = AccessTools.Method(typeof(List<AIShip>), nameof(List<AIShip>.Add));
        int stationSites = 0, candidateSites = 0;
        if (ordinary) foreach (var instruction in code) {
            if (instruction.Calls(stations)) { instruction.operand = AccessTools.Method(typeof(FactionEncounter), nameof(FactionEncounter.Stations)); stationSites++; }
            if (instruction.Calls(add)) { instruction.opcode = OpCodes.Call; instruction.operand = AccessTools.Method(typeof(FactionEncounter), nameof(FactionEncounter.Consider)); candidateSites++; }
        }
        if (ordinary && (stationSites != 1 || candidateSites != 1)) throw new InvalidOperationException("Faction staging: native depot/candidate layout changed.");
        var distance = AccessTools.Method(typeof(ShipSitu), nameof(ShipSitu.GetDistance), new[] { typeof(ShipSitu) });
        var ship = AccessTools.PropertyGetter(typeof(AIShip), nameof(AIShip.Ship));
        var situ = AccessTools.PropertyGetter(typeof(Ship), nameof(Ship.objSS));
        var rotation = AccessTools.Field(typeof(ShipSitu), nameof(ShipSitu.fRot));
        int found = -1, sites = 0;
        for (int i = 0; i < code.Count; i++) if (code[i].Calls(distance)) { found = i; sites++; }
        int begin = found - 5, end = -1;
        if (sites != 1 || begin < 0 || !code[begin + 1].Calls(ship) || !code[begin + 2].Calls(situ) || !code[begin + 4].Calls(situ))
            throw new InvalidOperationException("Faction staging: expected native ship/player distance operands.");
        for (int i = found + 1; i < code.Count && i < found + 110; i++) if (code[i].StoresField(rotation)) { end = i + 1; break; }
        if (end < 0 || end >= code.Count) throw new InvalidOperationException("Faction staging: native placement/velocity/rotation block changed.");
        var retain = generator.DefineLabel();
        ((IList)AccessTools.Field(typeof(CodeInstruction), "labels").GetValue(code[end])).Add(retain);
        var load = new CodeInstruction(code[begin].opcode, code[begin].operand);
        code[begin].MoveLabelsTo(load); code[begin].MoveBlocksTo(load);
        code.InsertRange(begin, new[] { load, new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(FactionEncounter), nameof(FactionEncounter.Fresh))), new CodeInstruction(OpCodes.Brfalse, retain) });
        // Existing actors retain native physical state. Fresh/reserve hulls still
        // execute the original placement code; no movement algorithm is copied.
        return code;
    }
}
