using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Ostranauts.Ships;
using Ostrabus.MiscBugFixes;
using UnityEngine;

namespace Ostrabus.Inspection;

// Explicit registration only: no HarmonyPatch attribute and no permanent hot-path hooks.
internal static class PersonnelCombatTraceHooks {
    const string Owner = "com.ostranauts.template.inspection.personnel";
    static readonly Harmony harmony = new Harmony(Owner);
    internal static bool Installed { get; private set; }
    static Dictionary<string, object> Map(params object[] pairs) => SnapshotJson.Map(pairs);
    static HarmonyMethod Hook(string name) => new HarmonyMethod(typeof(PersonnelCombatTraceHooks), name);
    internal static void Install() {
        if (Installed) return;
        try {
            harmony.Patch(AccessTools.Method(typeof(Interaction), nameof(Interaction.ApplyEffects)),
                prefix: Hook(nameof(EffectsBegin)), finalizer: Hook(nameof(EffectsEnd)));
            var rayTrace = Hook(nameof(TraceRay));
            rayTrace.priority = Priority.Last;
            harmony.Patch(AccessTools.Method(typeof(DamageSystem), nameof(DamageSystem.DamageRay)),
                prefix: Hook(nameof(RayBegin)), finalizer: Hook(nameof(RayEnd)), transpiler: rayTrace);
            Installed = true;
        } catch { harmony.UnpatchSelf(); throw; }
    }
    internal static void Remove() {
        if (!Installed) return;
        harmony.UnpatchSelf();
        Installed = false;
    }
    static void EffectsBegin(Interaction __instance, bool isCancelIa, out Dictionary<string, object> __state) {
        __state = PersonnelCombatInspection.Intent;
        if (isCancelIa) return;
        try { PersonnelCombatInspection.Intent = PersonnelCombatInspection.BeginIntent(__instance); }
        catch (Exception ex) { PersonnelCombatInspection.Fail(ex); }
    }
    static Exception EffectsEnd(Exception __exception, Dictionary<string, object> __state) {
        PersonnelCombatInspection.Intent = __state;
        return __exception;
    }
    sealed class RayScope {
        internal Dictionary<string, object> Previous, Current;
    }
    static void RayBegin(Vector3 vStart, Vector3 vDir, float fRadius, float fMult, JsonAttackMode jam, CondOwner coAttacker, bool bAllowDocked, out RayScope __state) {
        var previous = PersonnelCombatInspection.Ray;
        __state = previous == null ? null : new RayScope { Previous = previous };
        PersonnelCombatInspection.Ray = null;
        try {
            var current = PersonnelCombatInspection.BeginRay(vStart, vDir, fRadius, fMult, jam, coAttacker, bAllowDocked);
            if (current != null) {
                if (__state == null) __state = new RayScope();
                __state.Current = current;
            }
            PersonnelCombatInspection.Ray = current;
        } catch (Exception ex) { PersonnelCombatInspection.Fail(ex); PersonnelCombatInspection.Ray = previous; }
    }
    static Exception RayEnd(Exception __exception, RayScope __state) {
        try {
            if (__state?.Current != null) {
                __state.Current["completed"] = __exception == null;
                if (__exception != null) __state.Current["exception"] = __exception.GetType().Name;
            }
        } catch (Exception ex) { PersonnelCombatInspection.Fail(ex); }
        PersonnelCombatInspection.Ray = __state?.Previous;
        return __exception;
    }

    // Each replacement calls the original once with unchanged arguments. Diagnostic
    // exceptions are isolated; exceptions thrown by the native call still propagate.
    internal static RaycastHit[] ObserveRaycast(Ray ray, float distance, int mask) {
        var hits = Physics.RaycastAll(ray, distance, mask);
        if (PersonnelCombatInspection.Ray == null) return hits;
        try {
            PersonnelCombatInspection.Ray["physicsCalled"] = true;
            var data = new List<object>(Math.Min(hits.Length, PersonnelCombatInspection.MaxHits));
            for (int i = 0; i < hits.Length && i < PersonnelCombatInspection.MaxHits; i++) {
                var hit = hits[i];
                var collider = hit.collider;
                var transform = hit.transform;
                var destructible = transform == null ? null : transform.GetComponent<Destructable>();
                var co = destructible != null ? destructible.CO : transform == null ? null : transform.GetComponent<CondOwner>();
                data.Add(Map("distance", hit.distance, "point", PersonnelCombatInspection.Vector(hit.point),
                    "normal", PersonnelCombatInspection.Vector(hit.normal), "object", PersonnelCombatInspection.ObjectState(co),
                    "destructible", destructible != null, "colliderId", collider == null ? null : (object)collider.GetInstanceID(),
                    "colliderType", collider?.GetType().Name, "colliderName", collider == null ? null : collider.name,
                    "isTrigger", collider == null ? null : (object)collider.isTrigger,
                    "layer", collider == null ? null : (object)collider.gameObject.layer,
                    "boundsMin", collider == null ? null : PersonnelCombatInspection.Vector(collider.bounds.min),
                    "boundsMax", collider == null ? null : PersonnelCombatInspection.Vector(collider.bounds.max)));
            }
            PersonnelCombatInspection.Ray["candidateHits"] = data;
            PersonnelCombatInspection.Ray["candidateCount"] = hits.Length;
            PersonnelCombatInspection.Ray["candidatesTruncated"] = hits.Length > data.Count;
            PersonnelCombatInspection.Ray["layerMask"] = mask;
        } catch (Exception ex) { PersonnelCombatInspection.Fail(ex); }
        return hits;
    }
    internal static double DamageRoll(JsonAttackMode mode, CondOwner attacker) {
        double value = mode.GetDmgAmount(attacker);
        if (PersonnelCombatInspection.WantsEvent) {
            try { PersonnelCombatInspection.Event(Map("stage", "damageRoll", "value", value)); }
            catch (Exception ex) { PersonnelCombatInspection.Fail(ex); }
        }
        return value;
    }
    internal static Destructable ObjectComponent(Component component) {
        var result = component.GetComponent<Destructable>();
        if (PersonnelCombatInspection.WantsEvent) {
            try { PersonnelCombatInspection.Event(Map("stage", "visitObject", "object", PersonnelCombatInspection.ObjectState(result == null ? null : result.CO))); }
            catch (Exception ex) { PersonnelCombatInspection.Fail(ex); }
        }
        return result;
    }
    internal static CondOwner CharacterComponent(Component component) {
        var result = component.GetComponent<CondOwner>();
        if (PersonnelCombatInspection.WantsEvent) {
            try { PersonnelCombatInspection.Event(Map("stage", "visitCharacter", "object", PersonnelCombatInspection.ObjectState(result))); }
            catch (Exception ex) { PersonnelCombatInspection.Fail(ex); }
        }
        return result;
    }
    internal static double Remaining(Destructable component, string stat) {
        double result = LooseMeatCoverPatch.RemainingForRay(component, stat);
        if (PersonnelCombatInspection.WantsEvent) {
            try { PersonnelCombatInspection.Event(Map("stage", "objectDamageRemaining", "id", component.CO?.strID, "stat", stat,
                "value", result, "ignoredFloorMeat", stat == "StatDamage" && LooseMeatCoverPatch.IsFloorScrap(component))); }
            catch (Exception ex) { PersonnelCombatInspection.Fail(ex); }
        }
        return result;
    }
    internal static void ObjectDamage(CondOwner co, string stat, double amount, double age, float track) {
        Dictionary<string, object> entry = null;
        if (PersonnelCombatInspection.WantsEvent) {
            try {
                entry = Map("stage", "objectDamage", "id", co.strID, "stat", stat, "amount", amount,
                    "before", PersonnelCombatInspection.Amount(co, stat), "returned", false);
                PersonnelCombatInspection.Event(entry);
            } catch (Exception ex) { PersonnelCombatInspection.Fail(ex); }
        }
        co.AddCondAmount(stat, amount, age, track);
        if (entry != null) {
            try { entry["after"] = PersonnelCombatInspection.Amount(co, stat); entry["returned"] = true; }
            catch (Exception ex) { PersonnelCombatInspection.Fail(ex); }
        }
    }
    internal static Vector2 WoundDamage(Wound wound, double blunt, double cut, double penetration, CondOwner source, string weapon, bool unsocket, string attacker, bool skipArmor) {
        Dictionary<string, object> entry = null;
        if (PersonnelCombatInspection.WantsEvent) {
            try {
                entry = Map("stage", "woundDamage", "id", wound.coUs?.strID, "wound", wound.strName,
                    "blunt", blunt, "cut", cut, "penetration", penetration, "weapon", weapon, "returned", false);
                PersonnelCombatInspection.Event(entry);
            } catch (Exception ex) { PersonnelCombatInspection.Fail(ex); }
        }
        var result = wound.Damage(blunt, cut, penetration, source, weapon, unsocket, attacker, skipArmor);
        if (entry != null) {
            try { entry["returnedBlunt"] = result.x; entry["returnedCut"] = result.y; entry["returned"] = true; }
            catch (Exception ex) { PersonnelCombatInspection.Fail(ex); }
        }
        return result;
    }
    internal static IEnumerable<CodeInstruction> TraceRay(IEnumerable<CodeInstruction> instructions) {
        var code = instructions.ToList();
        var counts = new Dictionary<string, int>();
        foreach (var instruction in code) {
            if (!(instruction.operand is MethodInfo method)) continue;
            string replacement = null;
            if (method == AccessTools.Method(typeof(Physics), nameof(Physics.RaycastAll), new[] { typeof(Ray), typeof(float), typeof(int) })) replacement = nameof(ObserveRaycast);
            else if (method == AccessTools.Method(typeof(JsonAttackMode), nameof(JsonAttackMode.GetDmgAmount))) replacement = nameof(DamageRoll);
            else if (method == AccessTools.Method(typeof(Destructable), nameof(Destructable.DmgLeft)) ||
                method == AccessTools.Method(typeof(LooseMeatCoverPatch), nameof(LooseMeatCoverPatch.RemainingForRay))) replacement = nameof(Remaining);
            else if (method == AccessTools.Method(typeof(CondOwner), nameof(CondOwner.AddCondAmount))) replacement = nameof(ObjectDamage);
            else if (method == AccessTools.Method(typeof(Wound), nameof(Wound.Damage), new[] { typeof(double), typeof(double), typeof(double), typeof(CondOwner), typeof(string), typeof(bool), typeof(string), typeof(bool) })) replacement = nameof(WoundDamage);
            else if (method.Name == "GetComponent" && method.DeclaringType == typeof(Component) && method.IsGenericMethod) {
                var type = method.GetGenericArguments()[0];
                if (type == typeof(Destructable)) replacement = nameof(ObjectComponent);
                if (type == typeof(CondOwner)) replacement = nameof(CharacterComponent);
            }
            if (replacement == null) continue;
            counts.TryGetValue(replacement, out int count); counts[replacement] = count + 1;
            instruction.opcode = OpCodes.Call;
            instruction.operand = AccessTools.Method(typeof(PersonnelCombatTraceHooks), replacement);
        }
        foreach (string name in new[] { nameof(ObserveRaycast), nameof(Remaining), nameof(ObjectDamage), nameof(WoundDamage), nameof(ObjectComponent), nameof(CharacterComponent), nameof(DamageRoll) }) {
            if (!counts.TryGetValue(name, out int count) || count != (name == nameof(DamageRoll) ? 3 : 1))
                throw new InvalidOperationException("Native DamageRay changed; personnel trace cannot bind " + name + ".");
        }
        return code;
    }
}
