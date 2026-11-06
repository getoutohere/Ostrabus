using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using HarmonyLib;
using Ostrabus.Inspection;

namespace Ostrabus.Diagnostics;

// Explicit registration: PatchAll must never install these diagnostic hooks.
internal static class WorkTrace {
    const int Capacity = 256;
    const string Owner = "com.ostranauts.template.diagnostics.work";
    static readonly object[] events = new object[Capacity];
    static readonly Harmony harmony = new Harmony(Owner);
    static readonly AccessTools.FieldRef<Interaction, CondOwner> StoredUs = AccessTools.FieldRefAccess<Interaction, CondOwner>("objUsTemp");
    static readonly AccessTools.FieldRef<Interaction, CondOwner> StoredThem = AccessTools.FieldRefAccess<Interaction, CondOwner>("objThemTemp");
    static readonly AccessTools.FieldRef<Interaction, string> StoredThemId = AccessTools.FieldRefAccess<Interaction, string>("strThemID");
    static long sequence;
    static int session;
    static long startedAt;
    internal static bool Enabled { get; private set; }

    static HarmonyMethod Hook(string name) => new HarmonyMethod(typeof(WorkTrace), name);
    internal static void SetEnabled(bool value) {
        if (value == Enabled) return;
        session++;
        Enabled = false;
        harmony.UnpatchSelf();
        if (!value) return;
        try {
            foreach (string method in new[] { "ValidateParticipants", "ValidateConditions", "ValidateMovementSync", "ValidateLedger",
                "ValidateLackingItems", "ValidateGiveUseRemoveContracts", "ValidateTakeContract", "ValidateRequiredInputsSync" })
                harmony.Patch(Required(typeof(Interaction), method), postfix: Hook(nameof(ValidationSync)));
            foreach (string method in new[] { "ValidateMovementDeferred", "ValidateRequiredInputsDeferred" })
                harmony.Patch(Required(typeof(Interaction), method), postfix: Hook(nameof(ValidationDeferred)));
            harmony.Patch(Required(typeof(WorkManager), "HandleSuitRequirement"), prefix: Hook(nameof(SuitRequested)), postfix: Hook(nameof(SuitRequestedDone)));
            harmony.Patch(Required(typeof(WorkManager), "ClaimTaskDirect"), prefix: Hook(nameof(DirectClaim)));
            harmony.Patch(Required(typeof(WorkManager), "FinalizeTask"), postfix: Hook(nameof(Claimed)));
            harmony.Patch(Required(typeof(Condowner.Utils.Scheduler), "StartAIInteractionSample"), prefix: Hook(nameof(InteractionStarted)));
            harmony.Patch(Required(typeof(Interaction), "ApplyEffects"), prefix: Hook(nameof(EffectsStarting)), postfix: Hook(nameof(EffectsReturned)));
            startedAt = Stopwatch.GetTimestamp();
            Enabled = true;
        } catch {
            harmony.UnpatchSelf();
            throw;
        }
    }
    static MethodInfo Required(Type type, string name) => AccessTools.Method(type, name) ?? throw new MissingMethodException(type.FullName, name);
    static bool Watched(CondOwner co) => co != null && (co == CrewSim.coPlayer || co.HasCond("IsPlayerCrew"));
    internal static bool Relevant(Interaction interaction) {
        if (interaction == null) return false;
        string name = interaction.strName;
        return (!string.IsNullOrEmpty(interaction.strDuty) && Array.IndexOf(JsonCompanyRules.aDutiesNew, interaction.strDuty) >= 0) ||
            (name != null && (name.IndexOf("Install", StringComparison.OrdinalIgnoreCase) >= 0 ||
            name.StartsWith("ACTFeedItem", StringComparison.Ordinal) || name.StartsWith("ACTReloadItem", StringComparison.Ordinal) ||
            name == "ACTHaulItem" || name == "EquipItem"));
    }
    static object Map(params object[] pairs) => SnapshotJson.Map(pairs);
    static void Record(string stage, CondOwner co, Interaction interaction, CondOwner target, bool surroundings = false) {
        if (!Enabled || !Relevant(interaction) || !Watched(co)) return;
        try {
            object environment = null;
            if (surroundings && co.ship != null) {
                var position = co.tf.position;
                var room = co.ship.GetRoomAtWorldCoords1(position, bAllowDocked: false);
                environment = Map("x", position.x, "y", position.y, "roomVoid", room?.Void,
                    "roomPressure", room?.CO == null ? null : (object)GasExchange.GetStatGasPressure(room.CO),
                    "targetX", target == null ? null : (object)target.tf.position.x,
                    "targetY", target == null ? null : (object)target.tf.position.y);
            }
            long number = ++sequence;
            events[(int)((number - 1) % Capacity)] = Map("sequence", number, "epoch", StarSystem.fEpoch,
                "crewEpoch", co.Clock?.Epoch, "wallMsSinceTraceStart", (Stopwatch.GetTimestamp() - startedAt) * 1000d / Stopwatch.Frequency,
                "stage", stage, "crewId", co.strID, "shipId", co.ship?.strRegID,
                "interactionId", interaction?.id.ToString(), "interaction", interaction?.strName,
                "manualInteraction", interaction?.bManual, "requiredInputs", interaction?.strLootItmInputs,
                "durationHours", interaction?.fDuration, "nativeStartEpoch", interaction?.fEpochAdded,
                "targetId", target == null ? StoredThemId(interaction) : target.strID,
                "targetType", target == null ? null : target.strName, "targetShip", target == null ? null : target.ship?.strRegID,
                "airlockBlocked", interaction?.bAirlockBlocked, "airtight", co.HasCond("IsAirtight"),
                "fakeAirtight", co.HasCond("IsAirtightFake"), "manualCrew", co.HasCond("IsAIManual"),
                "queuedSuitWork", co.GetGPMInfo("PledgeData", "WearSuitQueue"), "environment", environment);
        } catch (Exception error) {
            InspectionLog.Add("WorkTrace", "Warning", "Diagnostic capture failed: " + error.Message);
        }
    }
    static void ValidationSync(Interaction __instance, MethodBase __originalMethod, object[] __args, bool __result) {
        if (!__result) Record(__originalMethod.Name + ":failed", __args[0] as CondOwner, __instance, __args.Length > 1 ? __args[1] as CondOwner : null);
    }
    static void ValidationDeferred(Interaction __instance, MethodBase __originalMethod, object[] __args, ref IEnumerator __result) {
        var co = __args[1] as CondOwner;
        if (Enabled && Relevant(__instance) && Watched(co) && __result != null)
            __result = new ValidationIterator(__result, (IteratorResult<bool>)__args[0], co, __args[2] as CondOwner, __instance, __originalMethod.Name, session);
    }
    static void SuitRequested(CondOwner co, Task2 task, Interaction iact, CondOwner coThem) => Record("suit-request:" + task.strInteraction, co, iact, coThem, true);
    static void SuitRequestedDone(CondOwner co, Interaction iact, CondOwner coThem, Task2 __result) => Record(__result == null ? "suit-request:rejected" : "suit-request:queued", co, iact, coThem);
    static void DirectClaim(Interaction iact) {
        if (Relevant(iact)) Record("direct-claim", StoredUs(iact), iact, StoredThem(iact), true);
    }
    static void Claimed(CondOwner co, Interaction iact, CondOwner coThem, Task2 __result) {
        if (__result != null) Record("work-claimed", co, iact, coThem);
    }
    static void InteractionStarted(CondOwner co, Interaction interaction) {
        if (Relevant(interaction)) Record("interaction-start", co, interaction, StoredThem(interaction));
    }
    // CompleteTask runs before ApplyEffects performs the material transfer. Observe the return
    // from ApplyEffects instead; keep an entry marker as well if effects throw or destroy targets.
    static void EffectsStarting(Interaction __instance, bool isCancelIa) {
        if (!isCancelIa && Relevant(__instance)) Record("effects-begin", StoredUs(__instance), __instance, StoredThem(__instance));
    }
    static void EffectsReturned(Interaction __instance, bool isCancelIa) {
        if (!isCancelIa && Relevant(__instance)) Record("effects-returned", StoredUs(__instance), __instance, StoredThem(__instance));
    }
    internal static object Snapshot() {
        var items = new List<object>();
        for (long i = Math.Max(1, sequence - Capacity + 1); i <= sequence; i++) items.Add(events[(int)((i - 1) % Capacity)]);
        var co = CrewSim.coPlayer;
        return Map("enabled", Enabled, "latestSequence", sequence, "overwritten", Math.Max(0, sequence - Capacity), "items", items,
            "playerQueuedSuitWork", co == null ? null : co.GetGPMInfo("PledgeData", "WearSuitQueue"),
            "note", "Player/crew work and equipment only. interaction-start is the native start marker; effects-returned follows normal ApplyEffects return, not proof of a successful transfer. Compare crewEpoch for local game-time gaps; wallMs includes pauses and tracing overhead. Queued suit work can be stale. Off removes hooks; captured scalar data remains until clear/restart.");
    }
    internal static void Clear() { Array.Clear(events, 0, events.Length); sequence = 0; startedAt = Stopwatch.GetTimestamp(); }
    internal static void Stop() { SetEnabled(false); Clear(); }
    internal static void SceneUnloaded(UnityEngine.SceneManagement.Scene scene) { session++; Clear(); }

    sealed class ValidationIterator : IEnumerator, IDisposable {
        readonly IEnumerator inner;
        readonly IteratorResult<bool> result;
        readonly CondOwner co, target;
        readonly Interaction interaction;
        readonly string stage;
        readonly int capturedSession;
        bool done;
        internal ValidationIterator(IEnumerator inner, IteratorResult<bool> result, CondOwner co, CondOwner target, Interaction interaction, string stage, int capturedSession) {
            this.inner = inner; this.result = result; this.co = co; this.target = target;
            this.interaction = interaction; this.stage = stage; this.capturedSession = capturedSession;
        }
        public object Current => inner.Current;
        public bool MoveNext() {
            bool more = inner.MoveNext();
            if (!more && !done) {
                done = true;
                if (Enabled && session == capturedSession && !result.Value) Record(stage + ":failed", co, interaction, target);
            }
            return more;
        }
        public void Reset() => throw new NotSupportedException();
        public void Dispose() { done = true; (inner as IDisposable)?.Dispose(); }
    }
}

// This console-only hook stays installed; no simulation methods are hooked while tracing is off.
[HarmonyPatch(typeof(ConsoleResolver), nameof(ConsoleResolver.ResolveString))]
internal static class WorkTraceConsolePatch {
    [HarmonyPrefix]
    static bool Prefix(string strInput) {
        if (strInput == null || !strInput.TrimStart().StartsWith("worktrace", StringComparison.OrdinalIgnoreCase)) return true;
        string command = strInput.Trim().ToLowerInvariant();
        if (command != "worktrace" && !command.StartsWith("worktrace ", StringComparison.Ordinal)) return true;
        string argument = command.Length > 9 ? command.Substring(9).Trim() : "status";
        try {
            if (argument == "on") WorkTrace.SetEnabled(true);
            else if (argument == "off") WorkTrace.SetEnabled(false);
            else if (argument == "clear") WorkTrace.Clear();
            else if (argument != "status") { ConsoleToGUI.instance?.LogInfo("Usage: worktrace on | off | clear | status"); return false; }
            ConsoleToGUI.instance?.LogInfo($"[WorkTrace] {(WorkTrace.Enabled ? "ON" : "OFF")}; GET /workorders/trace. Off removes simulation hooks; clear discards captured events.");
        } catch (Exception error) { ConsoleToGUI.instance?.LogInfo("[WorkTrace] Could not change diagnostic hooks: " + error.Message); }
        return false;
    }
}
