using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace Ostrabus.ShipNavigation.Collisions;

// Only CheckCollisions' ordinary Ship/Ship branch is rewritten. In particular,
// GetCollisionDistanceAU, WillCollide, projectile scans and BodyOrbit remain native.
[HarmonyPatch(typeof(CollisionManager),nameof(CollisionManager.CheckCollisions))]
internal static class HullCollisionScanPatch {
    static readonly FieldInfo Labels=AccessTools.Field(typeof(CodeInstruction),"labels");
    static int LabelCount(CodeInstruction code)=>((ICollection)Labels.GetValue(code)).Count;
    static void Prefix(out PhysicalCollisions.ScanState __state)=>PhysicalCollisions.Begin(out __state);
    static Exception Finalizer(Exception __exception,PhysicalCollisions.ScanState __state){PhysicalCollisions.End(__state);return __exception;}
    internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions,MethodBase __originalMethod) {
        var code=instructions.ToList();var radius=AccessTools.Method(typeof(CollisionManager),nameof(CollisionManager.GetCollisionDistanceAU),new[]{typeof(Ship),typeof(Ship)});
        var locations=Enumerable.Range(0,code.Count).Where(i=>code[i].Calls(radius)).ToArray();
        if(locations.Length!=1)throw new InvalidOperationException("Expected one physical Ship/Ship radius branch; collision patch requires review.");
        int at=locations[0],start=at+2;
        if(at<8||start+2>=code.Count||!LoadShip(code[at-1],__originalMethod)||
            code[at-7].opcode!=OpCodes.Conv_R4||!code[at-8].Calls(AccessTools.Method(typeof(ShipSitu),nameof(ShipSitu.GetDistance),new[]{typeof(double),typeof(double)}))||
            code[start+2].opcode!=OpCodes.Bge_Un_S&&code[start+2].opcode!=OpCodes.Bge_Un||
            code[start].blocks.Count!=0||code[start+1].blocks.Count!=0||code[start+2].blocks.Count!=0||
            LabelCount(code[start+1])!=0||LabelCount(code[start+2])!=0)
            throw new InvalidOperationException("Physical overlap IL changed; refusing an unverified collision branch.");
        // Verify both comparison operands are the locals just computed, not a
        // superficially similar proximity/planet/projectile comparison.
        if(Local(code[start],false)!=Local(code[at-6],true)||Local(code[start+1],false)!=Local(code[at+1],true))
            throw new InvalidOperationException("Physical distance/radius locals changed.");
        for(int i=0;i<code.Count;i++) {
            if(i==start) {
                yield return new CodeInstruction(code[i]){opcode=OpCodes.Ldarg_0,operand=null};
                yield return new CodeInstruction(code[at-1].opcode,code[at-1].operand);
                yield return new CodeInstruction(code[i].opcode,code[i].operand);
                yield return new CodeInstruction(code[i+1].opcode,code[i+1].operand);
                yield return new CodeInstruction(OpCodes.Call,AccessTools.Method(typeof(PhysicalCollisions),nameof(PhysicalCollisions.Overlap)));
                yield return new CodeInstruction(OpCodes.Brfalse,code[i+2].operand);i+=2;
            } else {
                yield return code[i];
                if(i==at-7) {
                    yield return new CodeInstruction(OpCodes.Ldarg_0);
                    yield return new CodeInstruction(code[at-1].opcode,code[at-1].operand);
                    yield return new CodeInstruction(OpCodes.Call,AccessTools.Method(typeof(PhysicalCollisions),nameof(PhysicalCollisions.Nonzero)));
                }
            }
        }
    }
    static bool LoadShip(CodeInstruction code,MethodBase method) {
        int i=Local(code,false);var locals=method.GetMethodBody().LocalVariables;
        return i>=0&&i<locals.Count&&locals[i].LocalType==typeof(Ship);
    }
    static int Local(CodeInstruction code,bool store) {
        if(code.opcode==(store?OpCodes.Stloc_0:OpCodes.Ldloc_0))return 0;
        if(code.opcode==(store?OpCodes.Stloc_1:OpCodes.Ldloc_1))return 1;
        if(code.opcode==(store?OpCodes.Stloc_2:OpCodes.Ldloc_2))return 2;
        if(code.opcode==(store?OpCodes.Stloc_3:OpCodes.Ldloc_3))return 3;
        if(code.opcode!=(store?OpCodes.Stloc:OpCodes.Ldloc)&&code.opcode!=(store?OpCodes.Stloc_S:OpCodes.Ldloc_S))return -1;
        if(code.operand is LocalBuilder b)return b.LocalIndex;
        if(code.operand is LocalVariableInfo v)return v.LocalIndex;
        return Convert.ToInt32(code.operand);
    }
}

[HarmonyPatch(typeof(CollisionManager),"ProcessCollision",new[]{typeof(Ship),typeof(Ship),typeof(double)})]
internal static class HullCollisionResponsePatch {
    static void Prefix(Ship a,Ship b,ref double fOverlap,out PhysicalCollisions.ResponseState __state)=>PhysicalCollisions.EnterResponse(a,b,ref fOverlap,out __state);
    static Exception Finalizer(Exception __exception,Ship a,Ship b,PhysicalCollisions.ResponseState __state){PhysicalCollisions.LeaveResponse(a,b,__state);return __exception;}
    internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) {
        var code=instructions.ToList();var normalize=AccessTools.Method(typeof(MathUtils),nameof(MathUtils.SetLength),new[]{typeof(double).MakeByRefType(),typeof(double).MakeByRefType(),typeof(double)});
        int count=0;
        foreach(var instruction in code)if(instruction.Calls(normalize)){instruction.operand=AccessTools.Method(typeof(PhysicalCollisions),nameof(PhysicalCollisions.Normal));count++;}
        if(count!=1)throw new InvalidOperationException("Expected one native collision normal calculation; response patch requires review.");
        return code;
    }
}
