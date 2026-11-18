using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Ostrabus.Orbital;
using Ostranauts.Utils.Models;

namespace Ostrabus.ShipNavigation;

internal static class AIShipAvoidance {
    const double KmPerAU = OrbitalMotion.KmPerAU;
    const double NativeAccelerationCap = 1.9016982118751132E-10;

    internal static bool FutureCollision(ShipSitu us, ShipSitu them, double seconds) =>
        AvoidancePrediction.Collision(us, them, seconds, AvoidanceQuery.Route, them.NavData, AvoidanceQuery.Owner?.objSS);

    internal static Point CurrentMotionEscape(Point original, Ship ship, ShipSitu obstacle) {
        ShipSitu current = ship?.objSS;
        if (current != null && !current.bGrounded && obstacle != null) {
            // Replacement thrust must not choose its side from the thrust it
            // replaces. Preserve the native perpendicular escape, selecting
            // the side already opened by relative lateral motion.
            double x = obstacle.vPosx - current.vPosx, y = obstacle.vPosy - current.vPosy;
            double length = Math.Sqrt(x * x + y * y);
            if (length > 0 && OrbitAcceleration.Finite(length)) {
                double cross = x * (obstacle.vVelY - current.vVelY) - y * (obstacle.vVelX - current.vVelX);
                if (!OrbitAcceleration.Finite(cross)) return original;
                double side = cross > 0 ? -1 : 1;
                original = new Point(current.vPosx - side * y / length * 3.342293553032505E-08,
                    current.vPosy + side * x / length * 3.342293553032505E-08);
            }
        }
        return original;
    }

    internal static Point SelectEscape(Point original, Ship ship, ShipSitu obstacle, float predictionTime) {
        var world = CrewSim.system;
        ShipSitu current = ship?.objSS;
        if (current == null || obstacle == null || world?.aBOs == null || current.bGrounded || !(predictionTime > 0) || !OrbitAcceleration.Finite(predictionTime)) return original;
        ShipSitu anchor = ship.HasDockGroup && ship.DockGroup.Anchor != null ? ship.DockGroup.Anchor : current;
        if (anchor.bGrounded) return original;
        DVector delta = new DVector(original.X - current.vPosx, original.Y - current.vPosy);
        double distance = delta.Length, acceleration = .5 * Math.Min(ship.RCSAccelMax, NativeAccelerationCap) * KmPerAU;
        if (!(distance > 0) || !delta.Finite || !(acceleration > 0) || !OrbitAcceleration.Finite(acceleration)) return original;
        // Native Line uses a 30 s prediction by default. Look through two such
        // intervals for a sustained dodge; never turn this into long-trip policy.
        double horizon = Math.Min(120, 2d * predictionTime);
        DVector thrust = delta * (acceleration / distance);
        DVector position = new DVector(anchor.vPosx, anchor.vPosy) * KmPerAU;
        DVector velocity = new DVector(anchor.vVelX, anchor.vVelY) * KmPerAU;
        DVector ambient = Ambient(anchor) * KmPerAU;
        double radius = anchor.GetRadiusAU() * KmPerAU;
        if (!position.Finite || !velocity.Finite || !ambient.Finite || !OrbitAcceleration.Finite(radius) || radius < 0) return original;
        if (BodySafe(position, velocity, ambient + thrust, radius, horizon)) return original;
        DVector opposite = thrust * -1;
        if (!BodySafe(position, velocity, ambient + opposite, radius, horizon)) return original;
        if (!ObstacleSafe(position, velocity, ambient, thrust, obstacle, radius, horizon)) return original;
        return new Point(2 * current.vPosx - original.X, 2 * current.vPosy - original.Y);
    }
    static DVector Ambient(ShipSitu situ) => new DVector(
        situ.vAccIn.x + situ.vAccEx.x + situ.vAccLift.x + situ.vAccDrag.x,
        situ.vAccIn.y + situ.vAccEx.y + situ.vAccLift.y + situ.vAccDrag.y);

    static bool BodySafe(DVector position, DVector velocity, DVector acceleration, double hullRadius, double seconds) {
        double epoch = StarSystem.fEpoch;
        foreach (var body in CrewSim.system.aBOs.Values) {
            if (body == null || body.IsPlaceholder() || body.IsAsteroidField || !(body.fRadius > 0)) continue;
            if (!NativeEphemeris.State(body, epoch, out DVector center, out DVector bodyVelocity) ||
                !OrbitAcceleration.TryFrame(body, epoch, out OrbitAcceleration.Acceleration frame, out _)) return false;
            DVector p = position - center * KmPerAU, v = velocity - bodyVelocity * KmPerAU;
            DVector a = acceleration - new DVector(frame.X, frame.Y) * KmPerAU;
            double radius = body.fRadius * KmPerAU + hullRadius;
            // A distant body cannot enter this bounded maneuver envelope.
            double reach = v.Length * seconds + .5 * a.Length * seconds * seconds;
            if (p.Length > radius + reach) continue;
            double minimum = ConstantAccelerationApproach.MinimumSquared(p, v, a, seconds);
            if (!OrbitAcceleration.Finite(minimum) || minimum <= radius * radius) return false;
        }
        return true;
    }
    static bool ObstacleSafe(DVector position, DVector velocity, DVector ambient, DVector thrust, ShipSitu obstacle, double hullRadius, double seconds) {
        DVector p = position - new DVector(obstacle.vPosx, obstacle.vPosy) * KmPerAU;
        DVector v = velocity - new DVector(obstacle.vVelX, obstacle.vVelY) * KmPerAU;
        DVector a = ambient - (Ambient(obstacle) + new DVector(obstacle.vAccRCS.x, obstacle.vAccRCS.y)) * KmPerAU;
        if (!p.Finite || !v.Finite || !a.Finite) return false;
        double radius = 2 * hullRadius + obstacle.GetRadiusAU() * KmPerAU;
        double alternative = ConstantAccelerationApproach.MinimumSquared(p, v, a - thrust, seconds);
        if (p.Dot(p) > radius * radius) return alternative > radius * radius;
        // An enclosing docking-group circle can already overlap empty space.
        // Keep a way out, but never choose a deeper penetration than native.
        double original = ConstantAccelerationApproach.MinimumSquared(p, v, a + thrust, seconds);
        return alternative + 1e-9 * Math.Max(1, original) >= original;
    }
}

[HarmonyPatch(typeof(AIShipManager), nameof(AIShipManager.IsOnCollisionCourse))]
internal static class AIShipAvoidancePatch {
    static void Prefix(Ship shipUs,out AvoidanceQuery.State __state) => __state=AvoidanceQuery.BeginQuery(shipUs);
    static Exception Finalizer(Exception __exception,AvoidanceQuery.State __state) {AvoidanceQuery.EndQuery(__state);return __exception;}
    internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod) {
        var code = new List<CodeInstruction>(instructions);
        var collision = AccessTools.Method(typeof(CollisionManager), nameof(CollisionManager.WillCollide));
        var escape = AccessTools.Method(typeof(AIShipManager), "GetEscapePoint");
        var hidden = AccessTools.PropertyGetter(typeof(Ship), nameof(Ship.HideFromSystem));
        RemoveDistanceGate(code, __originalMethod);
        CodeInstruction obstacle = null;
        int predictions = 0, escapes = 0, hiddenChecks = 0;
        for (int i = 0; i < code.Count; i++) {
            if (code[i].Calls(collision)) {
                predictions++;
                if (i < 2 || !LoadLocal(code[i - 2], __originalMethod, typeof(ShipSitu)))
                    throw new InvalidOperationException("AI collision obstacle local changed; avoidance patch requires review.");
                obstacle = code[i - 2];
            }
            if (code[i].Calls(escape)) escapes++;
            if (code[i].Calls(hidden)) hiddenChecks++;
        }
        if (predictions != 1 || escapes != 1 || hiddenChecks != 1) throw new InvalidOperationException("Expected one AI prediction, evasion choice and obstacle visibility check; refusing an unknown game version.");
        foreach (var instruction in code) {
            if (instruction.Calls(hidden)) {
                yield return new CodeInstruction(instruction) { opcode = OpCodes.Call,
                    operand = AccessTools.Method(typeof(StationDockedCollisions), nameof(StationDockedCollisions.IgnoreAsObstacle)) };
            } else if (instruction.Calls(collision)) {
                var replacement = new CodeInstruction(instruction) { operand = AccessTools.Method(typeof(AIShipAvoidance), nameof(AIShipAvoidance.FutureCollision)) };
                yield return replacement;
            } else {
                yield return instruction;
                if (!instruction.Calls(escape)) continue;
                yield return new CodeInstruction(OpCodes.Ldarg_0);
                yield return new CodeInstruction(obstacle.opcode, obstacle.operand);
                yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(AIShipAvoidance), nameof(AIShipAvoidance.CurrentMotionEscape)));
                yield return new CodeInstruction(OpCodes.Ldarg_0);
                yield return new CodeInstruction(obstacle.opcode, obstacle.operand);
                yield return new CodeInstruction(OpCodes.Ldarg_2);
                yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(AIShipAvoidance), nameof(AIShipAvoidance.SelectEscape)));
            }
        }
    }
    static void RemoveDistanceGate(List<CodeInstruction> code, MethodBase method) {
        var stopping = AccessTools.Method(typeof(MathUtils), nameof(MathUtils.GetStoppingDistance));
        var distance = AccessTools.Method(typeof(ShipSitu), nameof(ShipSitu.GetDistance), new[] { typeof(ShipSitu) });
        var situation = AccessTools.PropertyGetter(typeof(Ship), nameof(Ship.objSS));
        int at = -1, count = 0;
        for (int i = 0; i < code.Count; i++) if (code[i].Calls(stopping)) { at = i; count++; }
        // Release IL leaves the distance on the evaluation stack while it
        // calculates/stores stopping distance. Remove both sides of that one
        // comparison; preserve labels/blocks at their existing locations.
        if (count != 1 || at < 15 || at + 5 >= code.Count ||
            code[at - 15].opcode != OpCodes.Ldarg_0 || !code[at - 14].Calls(situation) ||
            !LoadLocal(code[at - 13], method, typeof(Ship)) || !code[at - 12].Calls(situation) || !code[at - 11].Calls(distance) ||
            !LoadLocal(code[at - 2], method, typeof(double)) || !LoadLocal(code[at - 1], method, typeof(double)) ||
            code[at + 3].opcode != OpCodes.Ldc_R8 || !(code[at + 3].operand is double two) || two != 2 ||
            code[at + 4].opcode != OpCodes.Mul || code[at + 5].opcode != OpCodes.Bgt && code[at + 5].opcode != OpCodes.Bgt_S)
            throw new InvalidOperationException("AI avoidance braking-distance filter changed; prediction admission requires review.");
        void Nop(int i) { code[i].opcode = OpCodes.Nop; code[i].operand = null; }
        for (int i = at - 15; i <= at - 11; i++) Nop(i);
        for (int i = at - 2; i <= at + 5; i++) Nop(i);
    }
    static bool LoadLocal(CodeInstruction instruction, MethodBase method, Type type) {
        int index;
        if (instruction.opcode == OpCodes.Ldloc_0) index = 0;
        else if (instruction.opcode == OpCodes.Ldloc_1) index = 1;
        else if (instruction.opcode == OpCodes.Ldloc_2) index = 2;
        else if (instruction.opcode == OpCodes.Ldloc_3) index = 3;
        else if (instruction.opcode == OpCodes.Ldloc || instruction.opcode == OpCodes.Ldloc_S) {
            if (instruction.operand is LocalBuilder builder) index = builder.LocalIndex;
            else if (instruction.operand is LocalVariableInfo variable) index = variable.LocalIndex;
            else index = Convert.ToInt32(instruction.operand);
        } else return false;
        var locals = method.GetMethodBody()?.LocalVariables;
        return locals != null && index >= 0 && index < locals.Count && locals[index].LocalType == type;
    }
}
