using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using HarmonyLib;
using UnityEngine;

namespace Ostrabus.Inspection;

// All access happens on Unity's main thread. History contains detached values only.
internal static class PersonnelCombatInspection {
    internal const int Capacity = 64, MaxHits = 32, MaxEvents = 64;
    static readonly object[] rays = new object[Capacity];
    static readonly AccessTools.FieldRef<Interaction, string> UsId = AccessTools.FieldRefAccess<Interaction, string>("strUsID");
    static readonly AccessTools.FieldRef<Interaction, string> ThemId = AccessTools.FieldRefAccess<Interaction, string>("strThemID");
    static readonly AccessTools.FieldRef<Interaction, string> ThirdId = AccessTools.FieldRefAccess<Interaction, string>("str3rdID");
    static readonly AccessTools.FieldRef<CondOwner, CondOwner> Parent = AccessTools.FieldRefAccess<CondOwner, CondOwner>("_objCOParent");
    static readonly System.Reflection.FieldInfo SlotsField = AccessTools.Field(typeof(Slots), "aSlots");
    static long deadline, sequence, attackSequence;
    static string actorId, stopReason = "not started", error;
    internal static bool Active { get; private set; }
    internal static Dictionary<string, object> Intent, Ray;

    static Dictionary<string, object> Map(params object[] pairs) => SnapshotJson.Map(pairs);
    internal static double Amount(CondOwner co, string name) => co != null && co.mapConds != null && co.mapConds.TryGetValue(name, out var cond) && cond != null ? cond.fCount : 0;
    internal static object Vector(Vector3 v) => Map("x", v.x, "y", v.y, "z", v.z);
    internal static object ObjectState(CondOwner co) {
        if (co == null) return null;
        return Map("id", co.strID, "type", co.strName, "name", co.strNameFriendly, "shipId", co.ship?.strRegID,
            "parentId", Parent(co)?.strID, "position", co.tf == null ? null : Vector(co.tf.position),
            "damage", Amount(co, "StatDamage"), "damageMax", Amount(co, "StatDamageMax"),
            "defense", Amount(co, "StatDefense"), "armorBlunt", Amount(co, "StatArmorBlunt"), "armorCut", Amount(co, "StatArmorCut"));
    }
    internal static object Mode(JsonAttackMode mode) => mode == null ? null : Map("id", mode.strName, "type", mode.strType,
        "range", mode.fRange, "blunt", mode.fDmgBlunt, "cut", mode.fDmgCut, "environment", mode.fDmgEnv,
        "penetration", mode.fPenetration, "spreadDegrees", mode.fSpread, "extraRays", mode.nExtraRays,
        "targetDefenseModifier", mode.fTargetDefenseMod);
    static CondOwner Resolve(string id) => id != null && DataHandler.mapCOs != null && DataHandler.mapCOs.TryGetValue(id, out var co) ? co : null;

    internal static object Snapshot(CondOwner co) {
        var queue = new List<object>();
        if (co.aQueue != null) {
            foreach (var ia in co.aQueue) {
                if (queue.Count == 8) break;
                if (ia == null) continue;
                var target = Resolve(ThemId(ia));
                queue.Add(Map("id", ia.id.ToString(), "name", ia.strName, "targetId", ThemId(ia), "target", ObjectState(target),
                    "mode", Mode(ia.attackMode), "nativeAccuracyChance", ia.attackMode == null || target == null ? null :
                    (object)Math.Max(.05, Math.Min(1, 50 - (Amount(target, "StatDefense") + ia.attackMode.fTargetDefenseMod)))));
            }
        }
        var equipment = new List<object>();
        int scanned = 0, scannedItems = 0;
        bool truncated = false;
        if (co.compSlots != null && SlotsField.GetValue(co.compSlots) is IEnumerable slots) {
            foreach (Slot slot in slots) {
                if (++scanned > 64) { truncated = true; break; }
                if (slot?.aCOs == null) continue;
                foreach (var item in slot.aCOs) {
                    if (++scannedItems > 128) { truncated = true; break; }
                    if (equipment.Count >= 32) { truncated = true; break; }
                    if (item == null) continue;
                    equipment.Add(Map("slot", slot.strName, "held", slot.bHoldSlot, "object", ObjectState(item)));
                }
                if (truncated) break;
            }
        }
        return Map("actor", ObjectState(co), "interactions", queue, "queueTruncated", (co.aQueue?.Count ?? 0) > 8,
            "equipment", equipment, "equipmentTruncated", truncated,
            "note", "Stored queue, direct slot contents and raw conditions only; no attacks, hit rolls, pathfinding or trigger evaluation. Positions are world transforms. Accuracy is the native pre-ray formula, not a prediction of collider hits.");
    }

    internal static InspectionApi.Reply Start(string id, int seconds) {
        if (seconds < 1 || seconds > 300) return new InspectionApi.Reply(400, Map("error", "seconds must be an integer from 1 to 300."));
        var co = id == null ? CrewSim.GetSelectedCrew() : Resolve(id);
        if (co == null || string.IsNullOrEmpty(co.strID)) return new InspectionApi.Reply(404, Map("error", "Shooter not found; specify coId or select a crew member."));
        if (Active || PersonnelCombatTraceHooks.Installed) return new InspectionApi.Reply(409, Map("error", "A personnel trace is already active; stop it before starting another."));
        Reset();
        actorId = co.strID;
        try {
            PersonnelCombatTraceHooks.Install();
            deadline = Stopwatch.GetTimestamp() + Stopwatch.Frequency * seconds;
            stopReason = null;
            Active = true;
        } catch (Exception ex) {
            Fail(ex);
            Stop("hook installation failed");
            return new InspectionApi.Reply(500, Map("error", "Personnel trace hooks could not be installed.", "detail", error));
        }
        return new InspectionApi.Reply(200, Read(0, 25));
    }
    internal static void Stop(string reason = "requested") {
        Active = false;
        stopReason = reason;
        PersonnelCombatTraceHooks.Remove();
        Intent = Ray = null;
    }
    internal static void Tick() {
        if (Active && Stopwatch.GetTimestamp() >= deadline) { Active = false; stopReason = "timeout"; }
        if (!Active && PersonnelCombatTraceHooks.Installed) Stop(stopReason ?? "stopped");
    }
    internal static void Reset() {
        Stop("reset");
        Array.Clear(rays, 0, rays.Length);
        actorId = error = null;
        sequence = attackSequence = deadline = 0;
    }
    internal static void Fail(Exception ex) {
        error = ex.GetType().Name + ": " + ex.Message;
        Active = false;
        stopReason = "diagnostic error";
    }
    internal static Dictionary<string, object> BeginIntent(Interaction ia) {
        if (!Active || ia.attackMode == null || UsId(ia) != actorId) return Intent;
        return Map("attackSequence", ++attackSequence, "interaction", ia.strName, "interactionId", ia.id.ToString(),
            "shooterId", UsId(ia), "targetId", ThemId(ia), "target", ObjectState(Resolve(ThemId(ia))),
            "thirdId", ThirdId(ia), "third", ObjectState(Resolve(ThirdId(ia))), "mode", Mode(ia.attackMode));
    }
    internal static Dictionary<string, object> BeginRay(Vector3 origin, Vector3 direction, float range, float multiplier, JsonAttackMode mode, CondOwner attacker, bool allowDocked) {
        if (!Active || attacker == null || attacker.strID != actorId) return null;
        var ray = Map("sequence", ++sequence, "epoch", StarSystem.fEpoch, "utc", DateTime.UtcNow.ToString("O"),
            "intent", Intent, "targetAtFire", Intent == null ? null : ObjectState(Resolve(Intent["targetId"] as string)),
            "shooter", ObjectState(attacker), "mode", Mode(mode), "origin", Vector(origin), "direction", Vector(direction),
            "range", range, "multiplier", multiplier, "allowDocked", allowDocked, "completed", false, "physicsCalled", false,
            "events", new List<object>(), "eventsDropped", 0);
        rays[(int)(sequence - 1)] = ray;
        // Stop recording at the limit rather than overwrite part of an encounter.
        if (sequence == Capacity) { Active = false; stopReason = "ray limit"; }
        return ray;
    }
    internal static void Event(object data) {
        if (Ray == null) return;
        var events = (List<object>)Ray["events"];
        if (events.Count < MaxEvents) events.Add(data);
        else Ray["eventsDropped"] = (int)Ray["eventsDropped"] + 1;
    }
    internal static bool WantsEvent {
        get {
            if (Ray == null) return false;
            if (((List<object>)Ray["events"]).Count < MaxEvents) return true;
            Ray["eventsDropped"] = (int)Ray["eventsDropped"] + 1;
            return false;
        }
    }
    internal static object Read(long since, int limit) {
        var items = new List<object>();
        long next = since;
        for (long i = since >= sequence ? sequence : since; i < sequence && items.Count < limit; i++) {
            items.Add(rays[(int)i]); next = i + 1;
        }
        return Map("active", Active, "hooksInstalled", PersonnelCombatTraceHooks.Installed, "shooterId", actorId,
            "remainingWallSeconds", Active ? Math.Max(0, (deadline - Stopwatch.GetTimestamp()) / (double)Stopwatch.Frequency) : 0,
            "stopReason", stopReason, "error", error, "latestSequence", sequence, "nextSince", next, "capacity", Capacity, "items", items,
            "note", "Opt-in observation of native attacks. No extra physics queries or random draws. Up to 32 raw candidate hits and 64 processing events per ray; truncation is reported. A candidate collider is not proof of damage. Stop retains primitive records; start or scene change clears them.");
    }
}
