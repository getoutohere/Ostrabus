using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Ostranauts.Core;
using Ostranauts.Ships.Commands;

namespace ShipExecutivePrototype;

enum NodeKind
{
    Leaf, Selector, ReactiveSequence, StickySequence, InputQueue
}

sealed class PolicyNode
{
    internal string Id;
    internal ICommand Native;
    internal NodeKind Kind;
    internal PolicyNode[] Children = Array.Empty<PolicyNode>();
    internal int Cursor;
}

// Import the constructor's actual objects/order. This is not a hand-written
// substitute Navy tree. Nested native command controllers remain abilities.
sealed class Policy
{
    internal readonly PolicyNode Root;
    internal readonly Dictionary<string, PolicyNode> Nodes = new Dictionary<string, PolicyNode>();
    internal readonly CommandGraph Commands;

    internal Policy(ICommand native)
    {
        Root = Compile(native, "root");
        Commands = new CommandGraph(native);
    }

    PolicyNode Compile(ICommand native, string id)
    {
        var type = native.GetType();
        NodeKind kind = type == typeof(SelectorCommand) ? NodeKind.Selector :
            type == typeof(SequenceCommand) ? NodeKind.ReactiveSequence :
            type == typeof(StickySequenceCommand) ? NodeKind.StickySequence :
            type == typeof(QueuedCommandsNode) ? NodeKind.InputQueue : NodeKind.Leaf;
        var node = new PolicyNode { Id = id, Native = native, Kind = kind };
        Nodes.Add(id, node);
        if (kind != NodeKind.Leaf && kind != NodeKind.InputQueue)
        {
            var children = (List<ICommand>)AccessTools.Field(type, "_childCommands").GetValue(native);
            node.Children = children.Select((child, i) => Compile(child, id + "/" + i + ":" + child.GetType().Name)).ToArray();
        }
        return node;
    }

    internal void ExportCursors()
    {
        foreach (var node in Nodes.Values)
            if (node.Kind == NodeKind.StickySequence)
                node.Native.SaveData = new[] { node.Cursor.ToString() };
    }
}

sealed class CommandState
{
    public string[] Payload;
    public Dictionary<string, object> Scalars = new Dictionary<string, object>();
    public Dictionary<string, string> Links = new Dictionary<string, string>();
}

// Known native command graph, including private subordinate controllers. Service
// caches and constructor-bound pilot/hull references are deliberately excluded.
sealed class CommandGraph
{
    internal readonly Dictionary<string, ICommand> ById = new Dictionary<string, ICommand>();
    internal readonly Dictionary<ICommand, string> Ids = new Dictionary<ICommand, string>();

    internal CommandGraph(ICommand root)
    {
        Register(root, "native");
    }

    internal static IEnumerable<FieldInfo> Fields(Type type)
    {
        for (var current = type; current != null && current != typeof(object); current = current.BaseType)
            foreach (var field in current.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly).OrderBy(f => f.Name))
                yield return field;
    }

    internal void Register(ICommand command, string id)
    {
        if (command == null || Ids.ContainsKey(command))
            return;
        Ids.Add(command, id);
        ById.Add(id, command);
        if (command is QueuedCommandsNode)
            return;
        foreach (var field in Fields(command.GetType()))
        {
            if (field.Name == "_activeCommand")
                continue; // alias, not definition
            var value = field.GetValue(command);
            if (value is ICommand child)
                Register(child, id + "/" + field.Name);
            else if (value is IEnumerable<ICommand> children)
            {
                int index = 0;
                foreach (var item in children)
                    Register(item, id + "/" + field.Name + "/" + index++);
            }
        }
    }

    internal void ForgetTransient(string id)
    {
        if (!id.StartsWith("directive/", StringComparison.Ordinal))
            return;
        foreach (string key in ById.Keys.Where(k => k == id || k.StartsWith(id + "/", StringComparison.Ordinal)).ToArray())
        {
            Ids.Remove(ById[key]);
            ById.Remove(key);
        }
    }

    internal Dictionary<string, CommandState> Capture()
    {
        var result = new Dictionary<string, CommandState>();
        foreach (var pair in ById)
        {
            var state = new CommandState { Payload = pair.Value.SaveData };
            foreach (var field in Fields(pair.Value.GetType()))
            {
                if (field.IsInitOnly || field.Name == "_nodeName")
                    continue;
                var value = field.GetValue(pair.Value);
                string key = field.DeclaringType.FullName + "::" + field.Name;
                if (field.FieldType.IsPrimitive || field.FieldType.IsEnum)
                    state.Scalars[key] = value;
                else if (value is ICommand link && Ids.TryGetValue(link, out var target))
                    state.Links[key] = target;
            }
            result.Add(pair.Key, state);
        }
        return result;
    }

    internal void Restore(Dictionary<string, CommandState> states)
    {
        foreach (var pair in states)
        {
            if (!ById.TryGetValue(pair.Key, out var command))
                throw new InvalidOperationException("Native command binding changed: " + pair.Key);
            command.SaveData = pair.Value.Payload;
            foreach (var field in Fields(command.GetType()))
            {
                string key = field.DeclaringType.FullName + "::" + field.Name;
                if (pair.Value.Scalars.TryGetValue(key, out var value))
                {
                    object converted = field.FieldType.IsEnum ? Enum.ToObject(field.FieldType, Convert.ToInt32(value)) : Convert.ChangeType(value, field.FieldType);
                    field.SetValue(command, converted);
                }
                if (pair.Value.Links.TryGetValue(key, out var target))
                    field.SetValue(command, ById[target]);
            }
        }
    }
}
