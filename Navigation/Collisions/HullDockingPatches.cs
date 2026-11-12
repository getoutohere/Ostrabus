using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Ostranauts.Ships.Commands;
using Ostranauts.ShipGUIs.NavStation;
using Ostranauts.ShipGUIs.MFD;

namespace Ostrabus.ShipNavigation.Collisions;

[HarmonyPatch]
internal static class HullDockingGeometryPatch {
    static IEnumerable<MethodBase> TargetMethods() {
        yield return AccessTools.Method(typeof(GUIDockSys),"CanDock");
        yield return AccessTools.Method(typeof(GUIDockSys),"ConvertShipToRing");
        yield return AccessTools.Method(typeof(GUIDockSys),"SetUI");
        yield return AccessTools.Method(typeof(Ship),nameof(Ship.PlaceAtAirlocks));
        yield return AccessTools.Method(typeof(ShipSitu),nameof(ShipSitu.PlaceOrbitPosition));
        yield return AccessTools.Method(typeof(NavModMooringControl),"CanMoor");
        yield return AccessTools.Method(typeof(NavModMooringControl),"UpdateSliders");
        yield return AccessTools.Constructor(typeof(MFDDockingClearance),new[]{typeof(string),typeof(int)});
        yield return AccessTools.Method(typeof(NavModTargetData),"SetStringsVRelShip");
        yield return AccessTools.Method(typeof(NavModTargetData),"SetPointOfReferenceUI");
        yield return AccessTools.Method(typeof(ShakeDown),"HandlePlayer");
    }
    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod) {
        var code = instructions.ToList();
        if (__originalMethod.DeclaringType == typeof(Ship) || __originalMethod.DeclaringType == typeof(ShipSitu)) {
            bool orbit=__originalMethod.DeclaringType == typeof(ShipSitu);
            var radius = AccessTools.Method(typeof(ShipSitu),nameof(ShipSitu.GetRadiusAU));
            var getters = Enumerable.Range(0,code.Count).Where(i => code[i].Calls(radius)).ToArray();
            if (getters.Length != 2 || getters[1]+1 >= code.Count || code[getters[1]+1].opcode != OpCodes.Add)
                throw new InvalidOperationException("Native airlock placement radius expression changed; review hull docking integration.");
            int at = getters[1]+1;
            var a = new CodeInstruction(OpCodes.Ldarg_0); code[at].MoveLabelsTo(a);
            code.InsertRange(at,new[] { a,new CodeInstruction(orbit ? OpCodes.Ldarg_1 : OpCodes.Ldarg_3) });
            code[at+2].opcode = OpCodes.Call;
            code[at+2].operand = AccessTools.Method(typeof(HullDocking),orbit ? nameof(HullDocking.OrbitPlacementRadius) : nameof(HullDocking.PlacementRadius));
            // An explicit native relocation is not a swept travelled segment,
            // including members whose docked flag was already true beforehand.
            for (int i=code.Count-1;i>=0;i--) if (code[i].opcode==OpCodes.Ret) {
                var instance=new CodeInstruction(OpCodes.Ldarg_0);code[i].MoveLabelsTo(instance);
                code.InsertRange(i,new[]{instance,new CodeInstruction(OpCodes.Call,AccessTools.Method(typeof(PhysicalCollisions),
                    orbit?nameof(PhysicalCollisions.DisplacedSitu):nameof(PhysicalCollisions.Displaced)))});
            }
            return code;
        }
        var distance = AccessTools.Method(typeof(CollisionManager),nameof(CollisionManager.GetCollisionDistanceAU),new[] { typeof(Ship),typeof(Ship) });
        var situDistance = AccessTools.Method(typeof(CollisionManager),nameof(CollisionManager.GetCollisionDistanceAU),new[] { typeof(ShipSitu),typeof(ShipSitu) });
        bool mooring=__originalMethod.DeclaringType==typeof(NavModMooringControl);
        bool inspection=__originalMethod.DeclaringType==typeof(ShakeDown);
        var max=AccessTools.Method(typeof(UnityEngine.Mathf),nameof(UnityEngine.Mathf.Max),new[]{typeof(float),typeof(float)});
        int lastMax=inspection?code.FindLastIndex(c=>c.Calls(max)):-1;
        if (inspection && code.Count(c=>c.Calls(max))!=2) throw new InvalidOperationException("Native inspection range expressions changed; review hull docking integration.");
        if (inspection) code.InsertRange(lastMax+1,new[]{new CodeInstruction(OpCodes.Ldarg_0),new CodeInstruction(OpCodes.Ldarg_1),
            new CodeInstruction(OpCodes.Call,AccessTools.Method(typeof(HullDocking),nameof(HullDocking.InspectionRange)))});
        int calls = 0, outer = 0;
        for (int i = 0; i < code.Count; i++) {
            if (mooring && code[i].Calls(situDistance)) {
                var self=new CodeInstruction(OpCodes.Ldarg_0);code[i].MoveLabelsTo(self);
                code.Insert(i,self);i++;
                code[i].opcode=OpCodes.Call;code[i].operand=AccessTools.Method(typeof(HullDocking),nameof(HullDocking.MooringDistance));calls++;
            } else if (code[i].Calls(distance)) {
                code[i].opcode = OpCodes.Call;
                code[i].operand = AccessTools.Method(typeof(HullDocking),nameof(HullDocking.DistanceAU));
                calls++;
            } else if (__originalMethod.Name == "ConvertShipToRing" && code[i].opcode == OpCodes.Ldc_R4 &&
                code[i].operand is float value && value == 6.684587E-08f) {
                code.InsertRange(i+1,new[] { new CodeInstruction(OpCodes.Ldarg_0),new CodeInstruction(OpCodes.Ldarg_1),
                    new CodeInstruction(OpCodes.Call,AccessTools.Method(typeof(HullDocking),nameof(HullDocking.RingOuterLimit))) });
                i += 3; outer++;
            }
        }
        if (calls != (inspection?2:1) || outer != (__originalMethod.Name == "ConvertShipToRing" ? 1 : 0))
            throw new InvalidOperationException("Native docking range/ring expression changed in " + __originalMethod.Name + "; review hull docking integration.");
        return code;
    }
}

[HarmonyPatch]
internal static class HullSafePlacementPatch {
    static IEnumerable<MethodBase> TargetMethods() {
        yield return AccessTools.Method(typeof(StarSystem),nameof(StarSystem.SetSituToSafeCoords));
        yield return AccessTools.Method(typeof(StarSystem),nameof(StarSystem.SetSituToRandomSafeCoords));
        yield return AccessTools.Method(typeof(StarSystem),nameof(StarSystem.SetSituToRandomSafeCoordsWithinBelt));
    }
    static void Prefix(ShipSitu objSS, out HullPlacement.State __state) => HullPlacement.Begin(objSS,out __state);
    static Exception Finalizer(Exception __exception, HullPlacement.State __state) {HullPlacement.End(__state);return __exception;}
    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod) {
        var code=instructions.ToList();int pairs=0;
        var native=AccessTools.Method(typeof(ShipSitu),nameof(ShipSitu.GetRadiusAU));
        int count=code.Count(c=>c.Calls(native));
        int expected=__originalMethod.Name==nameof(StarSystem.SetSituToSafeCoords)?4:__originalMethod.Name==nameof(StarSystem.SetSituToRandomSafeCoords)?2:3;
        if (count!=expected) throw new InvalidOperationException("Native safe-coordinate radius expressions changed in "+__originalMethod.Name+"; review hull placement integration.");
        bool belt=__originalMethod.Name==nameof(StarSystem.SetSituToRandomSafeCoordsWithinBelt);
        for (int i=code.Count-1;i>=0;i--) if (code[i].Calls(native)) {
            int add=belt?i+2:i+3;
            if (add>=code.Count || code[add].opcode!=OpCodes.Add || (!belt && !code[i+2].Calls(native))) continue;
            var duplicate=new CodeInstruction(OpCodes.Dup);code[i].MoveLabelsTo(duplicate);
            code.Insert(i,duplicate);add++;
            code[add].opcode=OpCodes.Call;code[add].operand=AccessTools.Method(typeof(HullPlacement),nameof(HullPlacement.RadiusSum));pairs++;
        }
        if (pairs!=(belt?2:1)) throw new InvalidOperationException("Native safe-coordinate ship pair changed in "+__originalMethod.Name+"; review hull placement integration.");
        return code;
    }
}

[HarmonyPatch(typeof(AIShipManager),nameof(AIShipManager.IsOnCollisionCourse))]
internal static class HullAvoidancePatch {
    static void Prefix(out HullAvoidance.State __state) => HullAvoidance.Begin(out __state);
    static Exception Finalizer(Exception __exception, HullAvoidance.State __state) {HullAvoidance.End(__state);return __exception;}
    [HarmonyPriority(Priority.Last)]
    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod) {
        var code=instructions.ToList();
        var native=AccessTools.Method(typeof(CollisionManager),nameof(CollisionManager.WillCollide));
        var prediction=Enumerable.Range(0,code.Count).Where(i=>code[i].Calls(native) ||
            code[i].operand is MethodInfo method && method.DeclaringType?.FullName=="Ostrabus.ShipNavigation.AIShipAvoidance" && method.Name=="FutureCollision").ToArray();
        if (prediction.Length!=1) throw new InvalidOperationException("AI prediction caller changed; review hull avoidance integration.");
        int at=prediction[0], obstacleLocal=at>=2?Local(code[at-2],false):-1;
        int store=code.FindLastIndex(at,c=>Local(c,true)==obstacleLocal);
        if (obstacleLocal<0 || store<3 || code[store-1].opcode!=OpCodes.Ldfld ||
            !Equals(code[store-1].operand,AccessTools.Field(typeof(SolarSystem.DockGroup),nameof(SolarSystem.DockGroup.Anchor))) ||
            !code[store-2].Calls(AccessTools.PropertyGetter(typeof(Ship),nameof(Ship.DockGroup))))
            throw new InvalidOperationException("AI obstacle situation assignment changed; review hull avoidance integration.");
        var obstacle=code[store-3];int shipLocal=Local(obstacle,false);
        if (shipLocal<0 || __originalMethod.GetMethodBody().LocalVariables[shipLocal].LocalType!=typeof(Ship))
            throw new InvalidOperationException("AI obstacle ship local changed; review hull avoidance integration.");
        string helper=code[at].Calls(native)?nameof(HullAvoidance.PredictNative):nameof(HullAvoidance.PredictFuture);
        var first=new CodeInstruction(OpCodes.Ldarg_0);code[at].MoveLabelsTo(first);
        code.InsertRange(at,new[]{first,new CodeInstruction(obstacle.opcode,obstacle.operand)});
        code[at+2].opcode=OpCodes.Call;code[at+2].operand=AccessTools.Method(typeof(HullAvoidance),helper);
        return code;
    }
    static int Local(CodeInstruction code, bool store) {
        var op=code.opcode;
        if (op==(store?OpCodes.Stloc_0:OpCodes.Ldloc_0)) return 0;
        if (op==(store?OpCodes.Stloc_1:OpCodes.Ldloc_1)) return 1;
        if (op==(store?OpCodes.Stloc_2:OpCodes.Ldloc_2)) return 2;
        if (op==(store?OpCodes.Stloc_3:OpCodes.Ldloc_3)) return 3;
        if (op!=(store?OpCodes.Stloc:OpCodes.Ldloc) && op!=(store?OpCodes.Stloc_S:OpCodes.Ldloc_S)) return -1;
        if (code.operand is LocalBuilder builder) return builder.LocalIndex;
        if (code.operand is LocalVariableInfo variable) return variable.LocalIndex;
        return Convert.ToInt32(code.operand);
    }
}

// Existing planet-safe evasion must evaluate its alternate direction against
// the same bounds as the prediction which selected that obstacle. Optional in
// selective builds: no shared game/combat/body radius helper is hooked.
[HarmonyPatch]
internal static class HullAvoidanceClearancePatch {
    static Type AvoidanceType => typeof(HullAvoidance).Assembly.GetType("Ostrabus.ShipNavigation.AIShipAvoidance");
    static bool Prepare() => AvoidanceType != null;
    static IEnumerable<MethodBase> TargetMethods() {
        yield return AccessTools.Method(AvoidanceType,"SelectEscape");
        yield return AccessTools.Method(AvoidanceType,"ObstacleSafe");
    }
    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) {
        var code=instructions.ToList();int count=0;
        foreach (var instruction in code) if (instruction.Calls(AccessTools.Method(typeof(ShipSitu),nameof(ShipSitu.GetRadiusAU)))) {
            instruction.opcode=OpCodes.Call;instruction.operand=AccessTools.Method(typeof(HullAvoidance),nameof(HullAvoidance.EscapeRadius));count++;
        }
        if (count!=1) throw new InvalidOperationException("Planet-safe AI escape radius expression changed; review hull avoidance integration.");
        return code;
    }
}

[HarmonyPatch(typeof(BaseCommand),"GetHandOffDistance")]
internal static class HullDockingHandoffPatch {
    static void Prefix(BaseCommand __instance, Ship target, ref double fCollisionDistance) {
        if (HullDocking.Applies(__instance.ShipUs,target))
            fCollisionDistance = HullDocking.RadiusSum(__instance.ShipUs,target,(float)fCollisionDistance);
    }
}
