using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("BoneyardTurnoverCheck")]

namespace Ostrabus.BoneyardTurnover
{
    internal sealed class FieldState
    {
        [JsonProperty(Required = Required.Always)] public string Key;
        [JsonProperty(Required = Required.Always)] public string Region;
        [JsonProperty(Required = Required.Always)] public string Definition;
        [JsonProperty(Required = Required.Always)] public bool Away;
        [JsonProperty(Required = Required.Always)] public double Departure;
        [JsonProperty(Required = Required.Always)] public double HalfLife;
        [JsonProperty(Required = Required.Always)] public double Shape;
        [JsonProperty(Required = Required.Always)] public int Seed;
        [JsonProperty(Required = Required.Always)] public Dictionary<string, double> Ages = new Dictionary<string, double>(StringComparer.Ordinal);
    }

    internal sealed class TurnoverState
    {
        [JsonProperty(Required = Required.Always)] public int Schema = 1;
        [JsonProperty(Required = Required.Always)] public string World;
        [JsonProperty(Required = Required.AllowNull)] public string Player;
        [JsonProperty(Required = Required.AllowNull)] public string Region;
        [JsonProperty(Required = Required.Always)] public double Epoch;
        [JsonProperty(Required = Required.Always)] public double StartEpoch;
        [JsonProperty(Required = Required.Always)] public List<FieldState> Fields = new List<FieldState>();
    }

    internal static class StateCodec
    {
        internal const string Entry = "boneyard-turnover.state.json";
        internal const int MaxBytes = 1024 * 1024;

        internal static byte[] Write(TurnoverState state) => Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(state));

        internal static TurnoverState Read(byte[] bytes, JsonGameSave game, string seed)
        {
            if (bytes == null || bytes.Length > MaxBytes || game?.objSystem == null || string.IsNullOrEmpty(seed)) return null;
            try
            {
                using var reader = new JsonTextReader(new StringReader(Encoding.UTF8.GetString(bytes)))
                    { MaxDepth = 16, DateParseHandling = DateParseHandling.None };
                var state = JsonSerializer.CreateDefault(new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.None })
                    .Deserialize<TurnoverState>(reader);
                if (reader.Read()) return null;
                if (state == null || state.Schema != 1 || state.World != seed || state.Player != game.strPlayerCO ||
                    !SameNativeTime(state.Epoch, game.objSystem.dfEpoch) || !SameNativeTime(state.StartEpoch, game.objSystem.dfEpochGameStart) ||
                    state.Region != game.objAIShipManager?.strATCLast || !Curve.Finite(state.Epoch) ||
                    !Curve.Finite(state.StartEpoch) || state.Fields == null || state.Fields.Count > 32) return null;
                var keys = new HashSet<string>(StringComparer.Ordinal);
                foreach (var field in state.Fields)
                {
                    if (field == null || !Id(field.Key) || !Id(field.Region) || !Id(field.Definition) || !keys.Add(field.Key) ||
                        !Curve.Finite(field.Departure) ||
                        (field.Departure > state.Epoch && !SameNativeTime(field.Departure, state.Epoch)) ||
                        !Curve.Positive(field.HalfLife) || !Curve.Positive(field.Shape) ||
                        field.Ages == null || field.Ages.Count > 4096 ||
                        field.Ages.Any(x => !Id(x.Key) || !Curve.Finite(x.Value) || x.Value < 0)) return null;
                }
                return state;
            }
            catch (Exception error) when (error is JsonException || error is ArgumentException || error is InvalidOperationException || error is OverflowException)
            {
                return null;
            }
        }

        // Native SavingJob writes doubles through LitJson.JsonWriter, which can
        // retain fewer digits than our Newtonsoft metadata. Match that exact
        // native representation instead of inventing a time tolerance. Keeping
        // the metadata format unchanged also accepts existing 0.1.0 saves.
        internal static bool SameNativeTime(double left, double right) =>
            Curve.Finite(left) && Curve.Finite(right) && NativeTime(left) == NativeTime(right);

        private static string NativeTime(double value)
        {
            var writer = new LitJson.JsonWriter();
            LitJson.JsonMapper.ToJson(new[] { value }, writer);
            return writer.ToString();
        }

        private static bool Id(string value) => !string.IsNullOrEmpty(value) && value.Length <= 512;
    }
}
