using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace ShipExecutivePrototype;

// Unity's computed normalized property is not saved state. Encode components.
static class CheckpointJson
{
    internal static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
    {
        TypeNameHandling = TypeNameHandling.None,
        Converters = { new VectorConverter() }
    };

    sealed class VectorConverter : JsonConverter
    {
        public override bool CanConvert(Type type) => type == typeof(Vector2);
        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            var v = (Vector2)value;
            writer.WriteStartArray();
            writer.WriteValue(v.x);
            writer.WriteValue(v.y);
            writer.WriteEndArray();
        }
        public override object ReadJson(JsonReader reader, Type type, object existing, JsonSerializer serializer)
        {
            var values = JArray.Load(reader);
            return new Vector2((float)values[0], (float)values[1]);
        }
    }
}
