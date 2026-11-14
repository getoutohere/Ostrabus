using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Ostranauts.Core;
using Ostranauts.Ships.AIPilots;
using Ostranauts.Ships.AIPilots.Interfaces;
using Ostranauts.Ships.Commands;

namespace Ostrabus.ShipNavigation;

internal static class CommandQueuePersistence {
    const string MarkerName = "OstrabusOneShot";
    internal const string Wire = "ostrabus.oneshot.v1";
    static readonly AccessTools.FieldRef<OneShotWrapperCommand, ICommand> Wrapped = AccessTools.FieldRefAccess<OneShotWrapperCommand, ICommand>("_wrappedCommand");
    static readonly AccessTools.FieldRef<BaseCommand, string> Name = AccessTools.FieldRefAccess<BaseCommand, string>("_nodeName");
    static readonly AccessTools.FieldRef<SelectorCommand, List<ICommand>> Choices = AccessTools.FieldRefAccess<SelectorCommand, List<ICommand>>("_childCommands");
    static readonly AccessTools.FieldRef<SequenceCommand, List<ICommand>> Sequence = AccessTools.FieldRefAccess<SequenceCommand, List<ICommand>>("_childCommands");
    static readonly AccessTools.FieldRef<StickySequenceCommand, List<ICommand>> Sticky = AccessTools.FieldRefAccess<StickySequenceCommand, List<ICommand>>("_childCommands");
    // Only held during a native tree save; nested/exceptional calls restore it.
    [ThreadStatic] internal static IAICharacter Saving;

    static List<ICommand> Children(ICommand node) => node is SelectorCommand choice ? Choices(choice) :
        node is SequenceCommand sequence ? Sequence(sequence) : node is StickySequenceCommand sticky ? Sticky(sticky) : null;

    static string FindPath(ICommand node, ICommand target, string parent = "", int depth = 0) {
        if (!(node is BaseCommand command) || depth >= 64) return null;
        string name = Name(command);
        string path = parent + "/" + (string.IsNullOrEmpty(name) ? node.GetType().Name : name);
        if (ReferenceEquals(node, target)) return path;
        var children = Children(node);
        if (children != null) foreach (var child in children) {
            string found = FindPath(child, target, path, depth + 1);
            if (found != null) return found;
        }
        return null;
    }

    static ICommand FindNode(ICommand node, string wanted, string parent = "", int depth = 0) {
        if (!(node is BaseCommand command) || depth >= 64) return null;
        string name = Name(command);
        string path = parent + "/" + (string.IsNullOrEmpty(name) ? node.GetType().Name : name);
        if (path == wanted) return node;
        var children = Children(node);
        if (children != null) foreach (var child in children) {
            var found = FindNode(child, wanted, path, depth + 1);
            if (found != null) return found;
        }
        return null;
    }

    internal static bool Save(OneShotWrapperCommand wrapper, string path, ref List<JsonCommandPayload> result) {
        ICommand leaf = Wrapped(wrapper);
        // Native FFWD only wraps leaves. Leave unknown mod-owned nodes alone.
        if (leaf == null || leaf is ICommandNode || leaf.GetType().Assembly != typeof(AIShip).Assembly) return true;
        string command = leaf.GetType().Name;
        string leafPath = path + "/" + command;
        var saved = leaf.GetSaveData(path);
        if (saved != null && (saved.Count != 1 || saved[0].strPath != leafPath)) return true;
        bool absent = saved == null;
        if (absent) saved = new List<JsonCommandPayload> { new JsonCommandPayload { strPath = leafPath, aCommandPayload = Array.Empty<string>() } };
        // The native leaf and payload remain intact for loading without the mod.
        // Vanilla silently ignores the extra unknown command marker.
        string source = FindPath(Saving?.BehaviorTree, leaf) ?? "";
        saved.Add(new JsonCommandPayload { strPath = path + "/" + MarkerName, aCommandPayload = new[] { Wire, command, absent ? "null" : "array", source } });
        result = saved;
        return false;
    }

    internal static void Prepare(QueuedCommandsNode queue, string path, ref JsonCommandPayload[] data) {
        if (data == null) return;
        string root = path + "/" + Name(queue);
        List<JsonCommandPayload> copy = null;
        for (int i = 0; i < data.Length; i++) {
            var marker = data[i];
            string markerPath = marker?.strPath;
            string[] tag = marker?.aCommandPayload;
            if (i > 0 && tag?.Length == 4 && tag[0] == Wire && !string.IsNullOrEmpty(tag[1]) && (tag[2] == "null" || tag[2] == "array") &&
                (markerPath == root + "/Queue/" + MarkerName || markerPath == root + "/CurrentCommand/" + MarkerName)) {
                var previous = data[i - 1];
                string parent = markerPath.Substring(0, markerPath.Length - MarkerName.Length);
                if (previous?.strPath == parent + tag[1] && tag[1].IndexOf('/') < 0) {
                    if (copy == null) { copy = new List<JsonCommandPayload>(data.Length); for (int j = 0; j < i; j++) copy.Add(data[j]); }
                    string[] payload = previous.aCommandPayload;
                    var encoded = new string[3 + (payload?.Length ?? 0)];
                    encoded[0] = Wire; encoded[1] = tag[2]; encoded[2] = tag[3];
                    if (payload != null) Array.Copy(payload, 0, encoded, 3, payload.Length);
                    copy[copy.Count - 1] = new JsonCommandPayload { strPath = previous.strPath, aCommandPayload = encoded };
                    continue;
                }
            }
            if (copy != null) copy.Add(marker);
        }
        if (copy != null) data = copy.ToArray();
    }

    internal static ICommand Create(string commandName, IAICharacter pilot, JsonCommandPayload payload) {
        string[] data = payload?.aCommandPayload;
        if (data == null || data.Length < 3 || data[0] != Wire || (data[1] != "null" && data[1] != "array")) return AIShip.CreateCommand(commandName, pilot);
        ICommand command = string.IsNullOrEmpty(data[2]) ? null : FindNode(pilot.BehaviorTree, data[2]);
        if (command == null || command.GetType().Name != commandName || command.GetType().Assembly != typeof(AIShip).Assembly) command = AIShip.CreateCommand(commandName, pilot);
        if (command == null) return null;
        string[] original = null;
        if (data[1] == "array") { original = new string[data.Length - 3]; Array.Copy(data, 3, original, 0, original.Length); }
        command.SaveData = original;
        return new OneShotWrapperCommand(command);
    }
}

[HarmonyPatch(typeof(AIShip), nameof(AIShip.GetBehaviorTreeSaveData))]
internal static class CommandTreeSaveContextPatch {
    [HarmonyPrefix]
    static void Prefix(AIShip __instance, out IAICharacter __state) {
        __state = CommandQueuePersistence.Saving;
        CommandQueuePersistence.Saving = __instance.AICharacter;
    }

    [HarmonyFinalizer]
    static Exception Finalizer(Exception __exception, IAICharacter __state) {
        CommandQueuePersistence.Saving = __state;
        return __exception;
    }
}

[HarmonyPatch(typeof(OneShotWrapperCommand), nameof(OneShotWrapperCommand.GetSaveData))]
internal static class OneShotPersistencePatch {
    [HarmonyPrefix]
    static bool Prefix(OneShotWrapperCommand __instance, string path, ref List<JsonCommandPayload> __result) => CommandQueuePersistence.Save(__instance, path, ref __result);
}

[HarmonyPatch(typeof(QueuedCommandsNode), nameof(QueuedCommandsNode.LoadSaveData))]
internal static class CommandQueueLoadPatch {
    [HarmonyPrefix]
    static void Prefix(QueuedCommandsNode __instance, string path, ref JsonCommandPayload[] saveData) => CommandQueuePersistence.Prepare(__instance, path, ref saveData);

    [HarmonyTranspiler]
    internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod) {
        var code = new List<CodeInstruction>(instructions);
        var locals = __originalMethod.GetMethodBody().LocalVariables.Where(v => v.LocalType == typeof(JsonCommandPayload)).ToArray();
        if (locals.Length != 1) throw new InvalidOperationException("Queue persistence: expected one native payload local.");
        int creates = 0, enqueues = 0;
        var factory = AccessTools.Method(typeof(AIShip), nameof(AIShip.CreateCommand));
        var urgent = AccessTools.Method(typeof(QueuedCommandsNode), nameof(QueuedCommandsNode.EnqueueUrgentCommand));
        foreach (var instruction in code) {
            if (instruction.Calls(factory)) {
                var load = new CodeInstruction(OpCodes.Ldloc, locals[0].LocalIndex);
                instruction.MoveLabelsTo(load); instruction.MoveBlocksTo(load);
                yield return load;
                yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(CommandQueuePersistence), nameof(CommandQueuePersistence.Create)));
                creates++;
            } else if (instruction.Calls(urgent)) {
                instruction.operand = AccessTools.Method(typeof(QueuedCommandsNode), nameof(QueuedCommandsNode.EnqueueCommand));
                yield return instruction;
                enqueues++;
            } else yield return instruction;
        }
        if (creates != 2 || enqueues != 1) throw new InvalidOperationException("Queue persistence: native factory/FIFO layout changed.");
    }
}
