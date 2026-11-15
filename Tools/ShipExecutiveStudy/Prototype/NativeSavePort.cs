using System;
using System.IO;
using System.Linq;
using System.Text;
using MissionContracts;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Ostranauts.Core.Models;

namespace ShipExecutivePrototype;

sealed class ExecutiveSaveRecord
{
    public int Schema = 1;
    public double Epoch;
    public string NativeBinding, NativeDigest;
    public ReturnCheckpoint Actor;
    public bool PreparedBeforeNativeCall;
}

// Offline candidate integration: one synchronous capture, embedded in native
// JsonGameSave, then detached before the existing save worker receives it.
// This is deliberately single-actor; no runtime module or new save writer.
sealed class NativeSavePort
{
    internal const string Key = "Ostrabus.MissionExecutive=";
    readonly NativeExecutionFence fence;
    readonly NativeSaveFixtures native;
    internal NativeNavyActor Actor;
    internal NativeArrivalObservation Arrival;

    internal NativeSavePort(NativeExecutionFence fence, NativeSaveFixtures native, NativeNavyActor actor)
    {
        this.fence = fence;
        this.native = native;
        Bind(actor);
    }
    void Bind(NativeNavyActor actor)
    {
        Actor = actor;
        Arrival = new NativeArrivalObservation(actor.Travel);
        // A synchronous effect has no interleaving save window. Keep one intent
        // in memory; actual game saves capture it with native world state.
        actor.Travel.RegisterArrivalIntent = fence.CertifyInvocationBoundary;
    }
    internal SaveDto Capture(string folder)
    {
        using (fence.Capture())
        {
            double epoch = StarSystem.fEpoch;
            var ids = native.World.Ships.Keys.OrderBy(id => id).ToArray();
            var dto = native.Collect(folder);
            if (epoch != StarSystem.fEpoch || dto.jGameSave.Item2.objSystem.dfEpoch != epoch || !ids.SequenceEqual(dto.dictShips.Values.Select(s => s.strRegID).OrderBy(id => id)))
                throw new InvalidOperationException("Native save capture omitted a hull or advanced world time");
            dto.jGameSave.Item2.aCustomInfos = (dto.jGameSave.Item2.aCustomInfos ?? Array.Empty<string>()).Where(s => !s.StartsWith(Key, StringComparison.Ordinal)).ToArray();
            if (Actor.Machine.State.Authority == "Executive")
            {
                Arrival.CaptureWitnesses();
                Actor.Machine.SettleWorldTime(epoch);
                var record = new ExecutiveSaveRecord
                {
                    Epoch = epoch,
                    NativeBinding = Executive.RuntimeBinding,
                    Actor = Actor.Travel.CaptureState(),
                    PreparedBeforeNativeCall = Actor.Machine.State.Active?.Phase == "ArrivalPrepared"
                };
                record.NativeDigest = Digest(dto);
                string item = Key + Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(record, CheckpointJson.Settings)));
                dto.jGameSave.Item2.aCustomInfos = dto.jGameSave.Item2.aCustomInfos.Concat(new[] { item }).ToArray();
            }
            // Native worker can serialize later without referencing executive
            // objects or live provider-owned DTO collections.
            return JsonConvert.DeserializeObject<SaveDto>(JsonConvert.SerializeObject(dto, CheckpointJson.Settings), CheckpointJson.Settings);
        }
    }
    internal NativeNavyActor Restore(Definition model, SaveDto dto)
        => Restore(model, dto, Digest(dto));

    internal NativeNavyActor Restore(Definition model, NativeArchiveImage archive)
        => Restore(model, archive.Read(), DigestContent(archive.Content()));

    NativeNavyActor Restore(Definition model, SaveDto dto, string nativeDigest)
    {
        bool prepared;
        using (fence.Capture())
        {
            string[] payloads = (dto.jGameSave.Item2.aCustomInfos ?? Array.Empty<string>()).Where(s => s.StartsWith(Key, StringComparison.Ordinal)).ToArray();
            if (payloads.Length != 1)
                throw new InvalidOperationException("Expected one executive save record; native-only saves are not adopted implicitly");
            var record = JsonConvert.DeserializeObject<ExecutiveSaveRecord>(Encoding.UTF8.GetString(Convert.FromBase64String(payloads[0].Substring(Key.Length))), CheckpointJson.Settings);
            if (record.Schema != 1 || record.NativeBinding != Executive.RuntimeBinding || record.Epoch != dto.jGameSave.Item2.objSystem.dfEpoch || record.NativeDigest != nativeDigest || record.Actor.World != null)
                throw new InvalidOperationException("Executive record does not match the native snapshot");
            NativeNavyActor.ValidateProfile(record.Actor);
            Machine.Restore(model, record.Actor.Executive, "SUBJECT", "fixture-activation", Executive.RuntimeBinding);
            bool savedPrepared = (string)JObject.Parse(record.Actor.Executive)["Active"]?["Phase"] == "ArrivalPrepared";
            if (record.PreparedBeforeNativeCall != savedPrepared)
                throw new InvalidOperationException("Pre-invocation proof disagrees with saved native operation phase");
            var world = native.LoadWorld(dto);
            var actor = NativeNavyActor.RestoreBound(model, record.Actor, world);
            Bind(actor);
            if (actor.Machine.State.ActorDisposition == "Retired" && Arrival.Read() == null)
                throw new InvalidOperationException("Retired executive lacks matching native crew/hull outcomes");
            prepared = record.PreparedBeforeNativeCall;
            if (prepared)
            {
                if (actor.Machine.State.PendingArrival == null || actor.Machine.State.Active.Phase != "ArrivalUncertain")
                    throw new InvalidOperationException("Prepared record disagrees with restored mission");
            }
        }
        if (prepared)
            Actor.Travel.Execute(Actor.Travel.Signal("NoWorldEffectIssued", proof: "native-call-did-not-enter-world-effect"));
        return Actor;
    }
    internal static string Digest(SaveDto dto)
        => DigestContent(Content(dto));

    internal static string DigestContent(JObject snapshot)
    {
        using (var sha = System.Security.Cryptography.SHA256.Create())
            return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(Canonical(snapshot).ToString(Formatting.None)))).Replace("-", "").ToLowerInvariant();
    }
    internal static JObject Content(SaveDto dto)
    {
        return Content(JObject.Parse(LitJson.JsonMapper.ToJson(dto.jGameSave.Item2)),
            JArray.Parse(LitJson.JsonMapper.ToJson(dto.dictShips.Values.OrderBy(s => s.strRegID).ToArray())));
    }
    internal static JObject Content(JObject game, JArray ships)
    {
        game["aCustomInfos"] = new JArray((game["aCustomInfos"] as JArray ?? new JArray()).Where(s => !((string)s).StartsWith(Key, StringComparison.Ordinal)).ToArray());
        return new JObject { ["game"] = game, ["ships"] = new JArray(ships.OrderBy(s => (string)s["strRegID"])) };
    }
    static JToken Canonical(JToken token)
    {
        if (token is JObject obj)
            return new JObject(obj.Properties().OrderBy(p => p.Name, StringComparer.Ordinal).Select(p => new JProperty(p.Name, Canonical(p.Value))));
        if (token is JArray array)
            return new JArray(array.Select(Canonical));
        return token.DeepClone();
    }
}
