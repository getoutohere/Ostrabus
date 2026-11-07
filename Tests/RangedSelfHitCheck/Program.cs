using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Serialization;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using Ostranauts.Ships;
using UnityEngine;
using Ostrabus.MiscBugFixes;

static partial class Program {
    static Transform transform;
    static CondOwner hitPerson, attacker, woundReceiver, damageSource;
    static Wound wound;
    static Destructable hitEquipment;
    sealed class ReportedHit {
        internal Transform Transform = Empty<Transform>();
        internal CondOwner Person;
        internal Destructable Equipment;
    }
    static ReportedHit[] reportedHits;
    static CondOwner damagedObject;
    static bool dragging;
    static Exception nativeWoundError;
    static int damageCalls, conditionCalls, checks;
    static double blunt, cut;
    static T Empty<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    static void Check(bool ok, string message) { checks++; if (!ok) throw new Exception(message); }
    static bool Equal(UnityEngine.Object __0, UnityEngine.Object __1, ref bool __result) { __result = ReferenceEquals(__0, __1); return false; }
    static bool Unequal(UnityEngine.Object __0, UnityEngine.Object __1, ref bool __result) { __result = !ReferenceEquals(__0, __1); return false; }
    static bool Yes(ref bool __result) { __result = true; return false; }
    static bool No() => false;
    static bool NotDragged(ref bool __result) { __result = dragging; return false; }
    static bool Amount(ref double __result) { __result = 1; return false; }
    static bool WoundOf(CondOwner __instance, ref Wound __result) { woundReceiver = __instance; __result = wound; return false; }
    static bool Remaining(ref double __result) { __result = 100; return false; }
    static bool Injure(double fBluntAmount, double fCutAmount, CondOwner coSource, ref Vector2 __result) {
        if (nativeWoundError != null) throw nativeWoundError;
        damageCalls++; blunt += fBluntAmount; cut += fCutAmount; damageSource = coSource;
        __result = default; return false;
    }
    static bool Condition(CondOwner __instance) { conditionCalls++; damagedObject = __instance; return false; }
    static RaycastHit[] Hits(Ray ray, float radius, int mask) {
        if (reportedHits == null) return new[] { default(RaycastHit) };
        var hits = new RaycastHit[reportedHits.Length];
        for (int i = 0; i < hits.Length; i++) hits[i].distance = i + 1;
        return hits;
    }
    static Transform HitTransform(ref RaycastHit hit) => reportedHits == null ? transform : reportedHits[(int)hit.distance - 1].Transform;
    static Destructable Equipment(Component component) {
        if (reportedHits == null) return hitEquipment;
        foreach (var hit in reportedHits) if (ReferenceEquals(hit.Transform, component)) return hit.Equipment;
        throw new Exception("Unknown fixture transform");
    }
    static CondOwner Person(Component component) {
        if (reportedHits == null) return hitPerson;
        foreach (var hit in reportedHits) if (ReferenceEquals(hit.Transform, component)) return hit.Person;
        throw new Exception("Unknown fixture transform");
    }
    static IEnumerable<CodeInstruction> Fixture(IEnumerable<CodeInstruction> source) {
        int physics = 0, transforms = 0, equipment = 0, people = 0;
        foreach (var instruction in source) {
            if (instruction.operand is MethodInfo method) {
                string replacement = null;
                if (method.DeclaringType == typeof(Physics) && method.Name == "RaycastAll") { replacement = nameof(Hits); physics++; }
                else if (method.DeclaringType == typeof(RaycastHit) && method.Name == "get_transform") { replacement = nameof(HitTransform); transforms++; }
                else if (method.Name == "GetComponent" && method.IsGenericMethod) {
                    Type t = method.GetGenericArguments()[0];
                    if (t == typeof(Destructable)) { replacement = nameof(Equipment); equipment++; }
                    if (t == typeof(CondOwner)) { replacement = nameof(Person); people++; }
                }
                if (replacement != null) { instruction.opcode = OpCodes.Call; instruction.operand = AccessTools.Method(typeof(Program), replacement); }
            }
            yield return instruction;
        }
        Check(physics == 1 && transforms == 2 && equipment == 1 && people == 1, "Unexpected native ray/hit API shape: " + physics + "/" + transforms + "/" + equipment + "/" + people);
    }
    static void Prefix(Harmony h, Type type, string method, string prefix, Type[] args = null) =>
        h.Patch(AccessTools.Method(type, method, args), prefix: new HarmonyMethod(typeof(Program), prefix));
    static void Shot(DamageSystem damage, JsonAttackMode mode, CondOwner source, bool allowDocked = true) =>
        damage.DamageRay(new Vector3(0, 0, -.25f), new Vector3(1, 0, 0), mode.fRange, 1, mode, source, allowDocked);
    static void Reset(params ReportedHit[] hits) {
        reportedHits = hits; damageCalls = conditionCalls = 0; blunt = cut = 0;
        woundReceiver = damageSource = damagedObject = null;
    }
    static ReportedHit Body(CondOwner person) => new ReportedHit { Person = person };
    static ReportedHit Object(CondOwner co) {
        var destructable = Empty<Destructable>();
        AccessTools.Field(typeof(Destructable), "co").SetValue(destructable, co);
        return new ReportedHit { Equipment = destructable };
    }
    static void PatchedChecks(Harmony h, DamageSystem damage, Ship ship, CondOwner target, CondOwner suit, JArray attacks) {
        h.CreateClassProcessor(typeof(RangedShooterExclusionPatch)).Patch();
        Check(Ostrabus.OstrabusPlugin.Logger.Warnings == 0, "Production patch failed to bind");
        var child = Empty<CondOwner>();
        AccessTools.Field(typeof(CondOwner), "_objCOParent").SetValue(child, attacker);
        var nested = Empty<CondOwner>();
        AccessTools.Field(typeof(CondOwner), "_objCOParent").SetValue(nested, child);
        var targetPart = Empty<CondOwner>();
        AccessTools.Field(typeof(CondOwner), "_objCOParent").SetValue(targetPart, target);
        foreach (var asset in attacks.Where(a => (string)a["strType"] == "ranged" && (double?)a["fDmgEnv"] > 0 &&
            ((double?)a["fDmgBlunt"] ?? 0) + ((double?)a["fDmgCut"] ?? 0) > 0)) {
            var mode = asset.ToObject<JsonAttackMode>();
            Reset(Body(target)); Shot(damage, mode, attacker);
            Check(damageCalls == 1 && ReferenceEquals(woundReceiver, target), mode.strName + ": intended target not damaged");
            double expectedBlunt = blunt, expectedCut = cut;
            Reset(Body(attacker)); Shot(damage, mode, attacker);
            Check(damageCalls == 0 && woundReceiver == null && conditionCalls == 0, mode.strName + ": shooter still damaged");
            Reset(Body(attacker), Body(target)); Shot(damage, mode, attacker);
            Check(damageCalls == 1 && ReferenceEquals(woundReceiver, target) && blunt == expectedBlunt && cut == expectedCut,
                mode.strName + ": own collider blocked or weakened intended hit");
            Reset(Body(target), Body(attacker)); Shot(damage, mode, attacker);
            Check(damageCalls == 1 && ReferenceEquals(woundReceiver, target), mode.strName + ": target-first traversal changed");
            Reset(Body(nested), Body(target)); Shot(damage, mode, attacker);
            Check(damageCalls == 1 && ReferenceEquals(woundReceiver, target) && blunt == expectedBlunt && cut == expectedCut,
                mode.strName + ": own nested collider blocked intended hit");
            Reset(Body(targetPart)); Shot(damage, mode, attacker);
            Check(damageCalls == 1 && ReferenceEquals(woundReceiver, targetPart), mode.strName + ": another character's part became immune");
            Reset(Object(suit), Body(target)); Shot(damage, mode, attacker);
            Check(conditionCalls == 0 && damageCalls == 1 && ReferenceEquals(woundReceiver, target) && blunt == expectedBlunt && cut == expectedCut,
                mode.strName + ": existing worn-equipment exclusion changed");
            Reset(Body(attacker)); Shot(damage, mode, null);
            Check(damageCalls == 1 && damageSource == null, mode.strName + ": environmental damage became immune");
            Reset(Body(attacker)); Shot(damage, mode, target);
            Check(damageCalls == 1 && ReferenceEquals(damageSource, target), mode.strName + ": other shooter cannot hit player");
            Reset(Body(target), Body(nested)); Shot(damage, mode, attacker);
            Check(damageCalls == 1 && ReferenceEquals(woundReceiver, target), mode.strName + ": trailing owned part took damage");
        }
        var shotgun = attacks.Single(a => (string)a["strName"] == "AModeShotgun01").ToObject<JsonAttackMode>();
        Reset(Body(attacker), Body(target));
        for (int i = 0; i <= shotgun.nExtraRays; i++) Shot(damage, shotgun, attacker);
        Check(damageCalls == 10 && ReferenceEquals(woundReceiver, target) && blunt == 50 && cut == 150,
            "Full ten-ray shotgun blast lost damage after skipping shooter colliders");
        Reset(); Shot(damage, shotgun, attacker);
        Check(damageCalls == 0 && conditionCalls == 0, "Miss created a hit");
        var crate = Empty<CondOwner>();
        AccessTools.Field(typeof(CondOwner), "_objShip").SetValue(crate, ship);
        Reset(Object(crate)); Shot(damage, shotgun, attacker);
        Check(conditionCalls == 1 && ReferenceEquals(damagedObject, crate), "Unattached object on shooter's ship became immune");
        Reset(Object(crate), Body(target)); Shot(damage, shotgun, attacker);
        Check(conditionCalls == 1 && damageCalls == 0, "Native solid-object absorption/cover changed");
        AccessTools.Field(typeof(CondOwner), "_objShip").SetValue(target, ship);
        Reset(Body(attacker), Body(target)); Shot(damage, shotgun, attacker, false);
        Check(damageCalls == 1 && ReferenceEquals(woundReceiver, target), "Same-ship target blocked when docked hits forbidden");
        AccessTools.Field(typeof(CondOwner), "_objShip").SetValue(target, Empty<Ship>());
        Reset(Body(target)); Shot(damage, shotgun, attacker, false);
        Check(damageCalls == 0, "Cross-ship damage restriction changed");
        Reset(Body(target)); Shot(damage, shotgun, attacker, true);
        Check(damageCalls == 1, "Allowed docked-ship target blocked");
        dragging = true;
        Reset(Body(nested)); Shot(damage, shotgun, attacker);
        Check(damageCalls == 1, "Native dragged-root exception changed");
        dragging = false;
        // Direct shooter identity requires neither a parent walk nor allocation.
        for (int i = 0; i < 10; i++) RangedShooterExclusionPatch.WoundForHit(attacker, true, true, null, attacker);
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) RangedShooterExclusionPatch.WoundForHit(attacker, true, true, null, attacker);
        Check(GC.GetAllocatedBytesForCurrentThread() == allocated, "Shooter exclusion allocated memory");
        Console.WriteLine("Patched native rays: shooter skipped; intended/friendly targets, worn equipment, cover, cross-ship policy and environmental hits preserved.");
    }
    static void InstructionChecks() {
        var method = AccessTools.Method(typeof(DamageSystem), nameof(DamageSystem.DamageRay));
        var reader = typeof(PatchProcessor).GetMethods().Single(m => m.Name == "GetOriginalInstructions" && m.GetParameters().Length == 2 && !m.GetParameters()[1].ParameterType.IsByRef);
        var original = ((IEnumerable<CodeInstruction>)reader.Invoke(null, new object[] { method, null })).ToList();
        var patched = RangedShooterExclusionPatch.Transpiler(original.Select(i => new CodeInstruction(i))).ToList();
        Check(patched.Count == original.Count + 1, "Unexpected number of native instruction changes");
        var wound = AccessTools.Method(typeof(CondOwner), nameof(CondOwner.GetWoundLocation));
        int index = original.FindIndex(i => i.Calls(wound));
        Check(index >= 0 && patched[index].opcode == OpCodes.Ldarg_S && (byte)patched[index].operand == 6 &&
            patched[index + 1].Calls(AccessTools.Method(typeof(RangedShooterExclusionPatch), nameof(RangedShooterExclusionPatch.WoundForHit))),
            "Shooter argument/helper call not inserted at native wound selection");
        for (int i = 0; i < original.Count; i++) {
            if (i == index) continue;
            var copy = patched[i > index ? i + 1 : i];
            Check(copy.opcode == original[i].opcode && Equals(copy.operand, original[i].operand), "Unrelated native damage/raycast instruction changed");
        }
        var unfamiliar = original.Where(i => !i.Calls(wound)).Select(i => new CodeInstruction(i)).ToList();
        int warnings = Ostrabus.OstrabusPlugin.Logger.Warnings;
        var fallback = RangedShooterExclusionPatch.Transpiler(unfamiliar).ToList();
        Check(Ostrabus.OstrabusPlugin.Logger.Warnings == warnings + 1 && fallback.Count == unfamiliar.Count &&
            fallback.Select((instruction, i) => instruction.opcode == unfamiliar[i].opcode && Equals(instruction.operand, unfamiliar[i].operand)).All(same => same),
            "Unfamiliar native code did not warn and retain original behavior");
    }
    static int Main() {
        var h = new Harmony("offline.shotgun.selfhit.diagnosis");
        try {
            Prefix(h, typeof(UnityEngine.Object), "op_Equality", nameof(Equal));
            Prefix(h, typeof(UnityEngine.Object), "op_Inequality", nameof(Unequal));
            h.Patch(AccessTools.PropertyGetter(typeof(GameObject), "activeInHierarchy"), prefix: new HarmonyMethod(typeof(Program), nameof(Yes)));
            Prefix(h, typeof(JsonAttackMode), "GetDmgAmount", nameof(Amount));
            Prefix(h, typeof(CondOwner), "GetWoundLocation", nameof(WoundOf));
            Prefix(h, typeof(CondOwner), "AreWeGettingDragged", nameof(NotDragged));
            Prefix(h, typeof(Wound), "DamageLeft", nameof(Remaining));
            Prefix(h, typeof(Wound), "Damage", nameof(Injure), new[] { typeof(double), typeof(double), typeof(double), typeof(CondOwner), typeof(string), typeof(bool), typeof(string), typeof(bool) });
            Prefix(h, typeof(CondOwner), "PlayHitAnim", nameof(No));
            Prefix(h, typeof(CondOwner), "ApplyFactionReps", nameof(No));
            Prefix(h, typeof(CondOwner), "AddCondAmount", nameof(Condition));
            Prefix(h, typeof(Destructable), "DmgLeft", nameof(Remaining));
            Prefix(h, typeof(Destructable), "DamageCheck", nameof(No));
            Prefix(h, typeof(CondOwner), "EndTurn", nameof(No));
            h.Patch(AccessTools.Method(typeof(DamageSystem), "DamageRay"), transpiler: new HarmonyMethod(typeof(Program), nameof(Fixture)));
            transform = Empty<Transform>(); wound = Empty<Wound>(); attacker = Empty<CondOwner>();
            var target = Empty<CondOwner>(); var suit = Empty<CondOwner>();
            AccessTools.Field(typeof(CondOwner), "_objCOParent").SetValue(suit, attacker);
            var ship = Empty<Ship>(); ship.gameObject = Empty<GameObject>(); ship.strRegID = "offline";
            var system = Empty<DamageSystem>(); AccessTools.Field(typeof(DamageSystem), "_ship").SetValue(system, ship);
            var attacks = JArray.Parse(File.ReadAllText(Ostrabus.Development.InstallationPaths.Data + "attackmodes/coAttacks/attackmodes.json"));
            var mode = attacks.Single(a => (string)a["strName"] == "AModeShotgun01").ToObject<JsonAttackMode>();
            Check(mode.nExtraRays == 9 && mode.fSpread == 5, "Shotgun asset differs from observed attack");
            // Same one-hit input, same coordinates, only reported collider ownership changes.
            hitPerson = attacker;
            Shot(system, mode, attacker);
            Check(damageCalls == 1 && ReferenceEquals(woundReceiver, attacker) && ReferenceEquals(damageSource, attacker), "Native character branch did not reproduce self-hit");
            Check(blunt > 0 && cut > 0, "Self-hit did not carry damaging values");
            Console.WriteLine("Native DamageRay routes shooter-body hit to its own Wound.Damage: blunt=" + blunt + ", cut=" + cut);
            hitPerson = target; Shot(system, mode, attacker);
            Check(damageCalls == 2 && ReferenceEquals(woundReceiver, target), "Legitimate target control failed");
            hitEquipment = Empty<Destructable>(); AccessTools.Field(typeof(Destructable), "co").SetValue(hitEquipment, suit);
            Shot(system, mode, attacker);
            Check(damageCalls == 2 && conditionCalls == 0, "Native equipment branch failed to skip shooter's worn suit");
            Console.WriteLine("Same native method skips shooter's own destructible suit, but not shooter-body collider.");
            hitEquipment = null; hitPerson = attacker;
            Shot(system, mode, null);
            Check(damageCalls == 3 && damageSource == null, "Environmental damage control failed");
            PatchedChecks(h, system, ship, target, suit, attacks);
            InstructionChecks();
            Console.WriteLine(checks + " ranged self-hit/native damage regression checks passed.");
            MeatChecks(h, system, target, attacks);
            PersonnelChecks(h, system, ship, target, attacks);
            return 0;
        } finally { h.UnpatchSelf(); }
    }
}
