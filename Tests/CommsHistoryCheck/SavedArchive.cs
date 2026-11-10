using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using LitJson;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using JsonToken = Newtonsoft.Json.JsonToken;
using Ostrabus.Optimizations;
using Ostranauts.Ships.Comms;

static partial class Program {
    static byte[] Hash(string path) { using (var stream = File.OpenRead(path)) using (var hash = SHA256.Create()) return hash.ComputeHash(stream); }
    static ShipMessage FromSave(JsonShipMessage message) => new ShipMessage {
        SenderRegId = message.strSenderRegId, ReceiverRegId = message.strRecieverRegId, AvailableTime = message.dAvailableTime,
        Read = message.bRead, MessageText = message.strMessageText, Interaction = new Interaction { strName = message.iaMessageInteraction?.strName }
    };
    static void SavedArchive(string path) {
        byte[] hash = Hash(path);
        var saved = new List<KeyValuePair<string, JsonCommData>>();
        var inFlight = new List<JsonShipMessage>(); double epoch = double.NaN;
        using (var zip = Ionic.Zip.ZipFile.Read(path)) foreach (var entry in zip.Entries.Where(e => e.FileName.EndsWith(".json") && Path.GetFileName(e.FileName) != "saveInfo.json")) {
            using (var text = new StreamReader(entry.OpenReader())) using (var reader = new JsonTextReader(text)) {
                var ids = new Dictionary<int, string>();
                while (reader.Read()) {
                    if (reader.TokenType != JsonToken.PropertyName) continue;
                    string name = (string)reader.Value; int depth = reader.Depth;
                    if (name == "strRegID") { reader.Read(); if (reader.TokenType == JsonToken.String) ids[depth] = (string)reader.Value; }
                    else if (name == "dfEpoch") { reader.Read(); epoch = Convert.ToDouble(reader.Value); }
                    else if (name == "aShipMessages") {
                        reader.Read(); if (reader.TokenType == JsonToken.StartArray) inFlight.AddRange(JsonMapper.ToObject<JsonShipMessage[]>(JArray.Load(reader).ToString()));
                    } else if (name == "commData") {
                        reader.Read(); if (reader.TokenType != JsonToken.StartObject) continue;
                        string json = JObject.Load(reader).ToString();
                        Check(ids.TryGetValue(depth, out string id), "Saved communication owner could not be resolved");
                        saved.Add(new KeyValuePair<string, JsonCommData>(id, JsonMapper.ToObject<JsonCommData>(json)));
                    } else if (name == "aCOs" || name == "aItems") { reader.Read(); reader.Skip(); }
                }
            }
        }
        Check(!double.IsNaN(epoch) && saved.Count > 0, "Save fixture did not contain epoch/comms data");
        var world = Empty<StarSystem>(); world.dictShips = new Dictionary<string, Ship>();
        var queue = new Dictionary<string, ShipMessage>();
        foreach (var message in inFlight.Select(FromSave)) queue[message.ID] = message;
        Field(world, "_messagesEnRoute", queue);
        var before = new Dictionary<string, List<ShipMessage>>();
        var metadata = new Dictionary<string, string>();
        int total = 0;
        foreach (var pair in saved) {
            var ship = Hull(pair.Key, world);
            var restored = new Comms(ship, pair.Value); listeners.Add(restored);
            HarmonyLib.AccessTools.Property(typeof(Ship), "Comms").SetValue(ship, restored);
            var log = Log(restored); before[pair.Key] = new List<ShipMessage>(log); total += log.Count;
            var data = restored.GetJson(); data.aMessages = null; metadata[pair.Key] = JsonMapper.ToJson(data);
        }
        int removed = CommsHistory.Sweep(world, epoch);
        foreach (var pair in before) {
            var ship = world.dictShips[pair.Key]; var log = Log(ship.Comms);
            Check(pair.Value.Where(m => m.AvailableTime >= epoch - 600).SequenceEqual(log.Where(m => m.AvailableTime >= epoch - 600)), "Save cleanup changed AI/hail recent-message windows");
            Check(pair.Value.Where(m => !m.Read && m.ReceiverRegId == pair.Key).SequenceEqual(ship.Comms.GetUnreadMessages()), "Save cleanup changed native unread inbox");
            var peers = pair.Value.Select(m => m.SenderRegId == pair.Key ? m.ReceiverRegId : m.SenderRegId).Distinct();
            foreach (string peer in peers) Check(ReferenceEquals(pair.Value.Last(m => m.SenderRegId == peer || m.ReceiverRegId == peer), ship.Comms.GetMessages(peer).Last()), "Latest reply context was removed");
            JsonCommData after = ship.Comms.GetJson(); after.aMessages = null;
            Check(JsonMapper.ToJson(after) == metadata[pair.Key], "Native clearance/request metadata changed");
        }
        Check(CommsHistory.Sweep(world, epoch) == 0, "Real-save cleanup was not idempotent");
        Check(queue.Count == inFlight.Count, "Real-save in-flight queue was altered");
        Check(Hash(path).SequenceEqual(hash), "Source save was modified by read-only offline validation");
        Console.WriteLine("Read-only saved-archive replay: " + saved.Count + " logs, " + total + " messages, " + removed + " removed; recent decisions, unread, latest replies, queue and clearance preserved.");
    }
}
