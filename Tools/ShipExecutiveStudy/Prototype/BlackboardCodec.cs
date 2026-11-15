using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using Ostranauts.Core;

namespace ShipExecutivePrototype;

sealed class BlackboardValue
{
    public string Kind;
    public JToken Value;
}

// Restricted tagged codec, never CLR type-name deserialization. Unsupported
// values are an explicit blocker rather than silently discarded state.
static class BlackboardCodec
{
    static Dictionary<string, object> Data(Blackboard board) =>
        (Dictionary<string, object>)AccessTools.Field(typeof(Blackboard), "_data").GetValue(board);

    internal static Dictionary<string, BlackboardValue> Capture(Blackboard board) =>
        Data(board).ToDictionary(p => p.Key, p => Encode(p.Value));

    static BlackboardValue Encode(object value)
    {
        if (value == null)
            return new BlackboardValue { Kind = "null", Value = JValue.CreateNull() };
        if (value is Ship ship)
            return new BlackboardValue { Kind = "ship", Value = new JValue(ship.strRegID) };
        if (value is List<Ship> ships)
            return new BlackboardValue { Kind = "ships", Value = new JArray(ships.Select(s => s?.strRegID)) };
        if (value is List<string> ids)
            return new BlackboardValue { Kind = "strings", Value = new JArray(ids) };
        if (value is Ostranauts.Ships.Commands.PilotSkill skill)
            return new BlackboardValue { Kind = "pilotSkill", Value = new JValue((int)skill) };
        if (value is Ostranauts.Core.Models.Tuple<double, ShipSitu> signal)
            return new BlackboardValue { Kind = "signal", Value = new JObject { ["epoch"] = signal.Item1, ["situ"] = JToken.FromObject(signal.Item2.GetJSON(), Newtonsoft.Json.JsonSerializer.Create(CheckpointJson.Settings)) } };
        var type = value.GetType();
        if (type == typeof(bool) || type == typeof(int) || type == typeof(double) || type == typeof(float) || type == typeof(string))
            return new BlackboardValue { Kind = type.Name, Value = new JValue(value) };
        throw new InvalidOperationException("Unsupported blackboard value: " + type.FullName);
    }

    internal static void Restore(Blackboard board, Dictionary<string, BlackboardValue> data, NativeWorld world)
    {
        var decoded = new Dictionary<string, object>();
        foreach (var pair in data)
            decoded.Add(pair.Key, Decode(pair.Value, world));
        board.Clear();
        foreach (var pair in decoded)
            board.Set(pair.Key, pair.Value);
    }

    static object Decode(BlackboardValue entry, NativeWorld world)
    {
        switch (entry.Kind)
        {
            case "null":
                return null;
            case "ship":
                return world.Resolve((string)entry.Value);
            case "ships":
                return entry.Value.Select(v => world.Resolve((string)v)).Where(s => s != null).ToList();
            case "strings":
                return entry.Value.Select(v => (string)v).ToList();
            case "pilotSkill":
                int skill = (int)entry.Value;
                if (!Enum.IsDefined(typeof(Ostranauts.Ships.Commands.PilotSkill), skill))
                    throw new InvalidOperationException("Unknown pilot skill value");
                return (Ostranauts.Ships.Commands.PilotSkill)skill;
            case "signal":
                return new Ostranauts.Core.Models.Tuple<double, ShipSitu>((double)entry.Value["epoch"], new ShipSitu(entry.Value["situ"].ToObject<JsonShipSitu>(Newtonsoft.Json.JsonSerializer.Create(CheckpointJson.Settings))));
            case "Boolean":
                return (bool)entry.Value;
            case "Int32":
                return (int)entry.Value;
            case "Double":
                return (double)entry.Value;
            case "Single":
                return (float)entry.Value;
            case "String":
                return (string)entry.Value;
            default:
                throw new InvalidOperationException("Unsupported blackboard tag: " + entry.Kind);
        }
    }
}
