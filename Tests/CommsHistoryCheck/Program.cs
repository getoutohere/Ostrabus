using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using HarmonyLib;
using LitJson;
using Mono.Cecil;
using Ostrabus.MiscBugFixes;
using Ostrabus.Optimizations;
using Ostranauts.Core;
using Ostranauts.Ships.Comms;
using Ostranauts.ShipGUIs.NavStation;

namespace Ostrabus {
    static class OstrabusPlugin { internal static BepInEx.Logging.ManualLogSource Logger = null; }
}

static partial class Program {
    static int checks;
    const double Now = 10000000;
    static readonly List<ShipMessage> drawn = new List<ShipMessage>();
    static readonly List<Comms> listeners = new List<Comms>();
    static List<ShipMessage> saveObserved;
    static T Empty<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    static void Field(object obj, string name, object value) => AccessTools.Field(obj.GetType(), name).SetValue(obj, value);
    static void Check(bool ok, string reason) { checks++; if (!ok) throw new Exception(reason); }
    static bool Equal(UnityEngine.Object __0, UnityEngine.Object __1, ref bool __result) { __result = ReferenceEquals(__0, __1); return false; }
    static bool Unequal(UnityEngine.Object __0, UnityEngine.Object __1, ref bool __result) { __result = !ReferenceEquals(__0, __1); return false; }
    static bool Prepare() => false;
    static bool Capture(ShipMessage mfdMessage) { drawn.Add(mfdMessage); return false; }
    static bool RestoreInteraction(string strName, ref Interaction __result) { __result = new Interaction { strName = strName }; return false; }
    static bool SaveBoundary() {
        Check(saveObserved.Count == 1, "Save snapshot body began before pruning");
        return false; // Unity/world snapshot body is outside this fixture.
    }
    static ShipMessage Message(string sender, string receiver, double time, bool read = true, string ia = "SHIPStatus") => new ShipMessage {
        SenderRegId = sender, ReceiverRegId = receiver, AvailableTime = time, Read = read, MessageText = "unchanged text",
        Interaction = new Interaction { strName = ia }
    };
    static List<ShipMessage> Log(Comms comms) => (List<ShipMessage>)AccessTools.Field(typeof(Comms), "_messageLog").GetValue(comms);
    static Ship Hull(string id, StarSystem world) {
        var ship = Empty<Ship>(); ship.strRegID = id;
        var comms = new Comms(ship); listeners.Add(comms);
        AccessTools.Property(typeof(Ship), "Comms").SetValue(ship, comms); world.dictShips.Add(id, ship); return ship;
    }
    static int Prune(List<ShipMessage> log, double now = Now, params ShipMessage[] queue) => CommsHistory.Prune(log, "US", now,
        new HashSet<CommsHistory.MessageKey>(queue.Select(m => new CommsHistory.MessageKey(m))), new Dictionary<string, int>());
    static void ProtectionChecks() {
        double old = Now - CommsHistory.RetentionSeconds - 100;
        var stale = Message("US", "A", old, false); // Outgoing unread flag is not inbox state.
        var unread = Message("B", "US", old, false);
        var queued = Message("US", "C", old, true, "SHIPSosStranded");
        var pendingCopy = Message("US", "C", old, true, "SHIPSosStranded");
        var last = Message("US", "A", old - 100); // Native dialogue uses last STORED entry.
        var exact = Message("US", "D", Now - CommsHistory.RetentionSeconds);
        var future = Message("US", "D", Now + 100);
        var recent = Message("US", "D", Now - 600, true, "SHIPHail");
        var invalid = Message("US", "D", double.NaN);
        var unknown = Message("OTHER", "ELSE", old);
        var log = new List<ShipMessage> { stale, unread, queued, exact, future, recent, invalid, unknown, null,
            Message("US", "B", Now), Message("C", "US", Now), last, Message("US", "D", Now) };
        var expected = log.Skip(1).ToList();
        Check(Prune(log, Now, pendingCopy) == 1, "Protection rules removed the wrong number");
        Check(log.SequenceEqual(expected), "Compaction changed order/identity or removed protected records");
        Check(log.Contains(queued) && !ReferenceEquals(queued, pendingCopy), "Restored queued message was matched by object identity");
        Check(Prune(log, Now, pendingCopy) == 0, "Pruning is not idempotent");
        Check(Prune(log, double.NaN, pendingCopy) == 0 && log.SequenceEqual(expected), "Invalid clock changed history");
        Check(unread.MessageText == "unchanged text" && !unread.Read && queued.Interaction.strName == "SHIPSosStranded", "Retention mutated messages/interactions");
        Check(!CommsHistory.MessageKey.Equals(new CommsHistory.MessageKey(queued), new CommsHistory.MessageKey(Message("US", "C", old, true, "DIFFERENT"))), "Pending key ignored interaction identity");
        var boundary = new List<ShipMessage> { Message("US", "D", Now - CommsHistory.RetentionSeconds - .001), exact, Message("US", "D", Now) };
        Check(Prune(boundary) == 1 && boundary.Contains(exact), "Seven-day boundary must be inclusive");
    }
    static void DisplayChecks(Harmony environment) {
        environment.Patch(AccessTools.Method(typeof(GUIMessageDisplay), "PreSetup"), prefix:new HarmonyMethod(typeof(Program), nameof(Prepare)));
        environment.Patch(AccessTools.Method(typeof(GUIMessageDisplay), "AddMessage"), prefix:new HarmonyMethod(typeof(Program), nameof(Capture)));
        var ui = Empty<GUIMessageDisplay>();
        var log = Enumerable.Range(0, 773).Select(n => Message("S", "R", n)).Reverse().ToList();
        var original = log.ToArray();
        ui.ShowPanel("ATC", log);
        Check(drawn.Count == 100 && drawn.First().AvailableTime == 0 && drawn.Last().AvailableTime == 99, "Native oldest-100 reproduction changed");
        var production = new Harmony("offline.comms.display.production");
        try {
            production.CreateClassProcessor(typeof(CommsDisplayPatch)).Patch();
            foreach (int count in new[] { 0, 1, 99, 100, 101, 773 }) {
                var sample = log.Take(count).ToList(); drawn.Clear(); ui.ShowPanel("ATC", sample);
                var expected = sample.OrderBy(m => m.AvailableTime).Skip(Math.Max(0, count - 100));
                Check(drawn.SequenceEqual(expected), "Patched display did not retain newest 100 in chronological order: " + count);
            }
            Check(log.SequenceEqual(original), "Display selection edited stored history");
        } finally { production.UnpatchSelf(); }
    }
    static void LifecycleChecks() {
        var world = Empty<StarSystem>(); world.dictShips = new Dictionary<string, Ship>();
        var queue = new Dictionary<string, ShipMessage>(); Field(world, "_messagesEnRoute", queue);
        var ship = Hull("US", world); var log = Log(ship.Comms);
        var retained = Message("US", "A", Now); var stale = Message("US", "A", Now - 9 * 86400);
        log.Add(stale); log.Add(retained);
        var sim = Empty<CrewSim>(); CrewSim.objInstance = sim; CrewSim.system = world; StarSystem.fEpoch = Now;
        Field(sim, "_finishedLoading", false); CommsHistory.Run("loading");
        Check(log.Count == 2, "Cleanup ran during incomplete loading");
        var iterator = CommsHistory.AfterLoad(sim, (int)AccessTools.Field(typeof(CommsHistory), "loadGeneration").GetValue(null));
        Check(iterator.MoveNext() && log.Count == 2, "Post-load callback did not wait for complete loading");
        Field(sim, "_finishedLoading", true);
        Check(!iterator.MoveNext() && log.SequenceEqual(new[] { retained }), "Post-load sweep did not run exactly at ready boundary");
        log.Insert(0, stale); Check(!iterator.MoveNext() && log.Count == 2, "Completed load callback ran again");
        var production = new Harmony("offline.comms.save.production");
        var boundary = new Harmony("offline.comms.save.boundary");
        var method = AccessTools.Method(typeof(LoadManager), "SaveGameData");
        try {
            production.CreateClassProcessor(typeof(CommsHistorySavePatch)).Patch();
            saveObserved = log;
            boundary.Patch(method, prefix:new HarmonyMethod(typeof(Program), nameof(SaveBoundary)) { priority = Priority.Last });
            method.Invoke(Empty<LoadManager>(), new object[] { "unused" });
            Check(log.Count == 1, "Pre-save snapshot hook did not compact runtime history");
        } finally { production.UnpatchSelf(); boundary.UnpatchSelf(); saveObserved = null; }
        // Native serialization/restoration and clearance metadata remain usable without the mod.
        var clearance = new Clearance { TargetRegId = "PORT", DockID = "D2", Squak = "1234", ClearanceType = "DOCK", IssueTimestamp = 500, SquawkID = true };
        ship.Comms.Clearance = clearance;
        JsonCommData saved = JsonMapper.ToObject<JsonCommData>(JsonMapper.ToJson(ship.Comms.GetJson()));
        var restored = new Comms(ship, saved); listeners.Add(restored);
        Check(restored.GetMessages().Count == 1 && restored.GetMessages()[0].MessageText == retained.MessageText, "Native comms save/reload failed");
        Check(restored.Clearance.TargetRegId == "PORT" && restored.Clearance.DockID == "D2" && restored.Clearance.Squak == "1234" && restored.Clearance.IssueTimestamp == 500 && restored.Clearance.SquawkID && restored.Clearance.ClearanceType == "DOCK", "Pruning disturbed native docking clearance");
        var queued = Message("US", "A", stale.AvailableTime); queue.Add(queued.ID, queued); log.Insert(0, stale);
        Check(CommsHistory.Sweep(world, Now) == 0 && log.Contains(stale) && queue.Count == 1 && ReferenceEquals(queue.Values.First(), queued), "Overdue in-flight message or native delivery queue was changed");
        queue.Clear(); queue.Add("unknown", null);
        Check(CommsHistory.Sweep(world, Now) == 0 && log.Count == 2, "Unresolvable pending queue must refuse cleanup");
        Check(((System.Collections.ICollection)AccessTools.Field(typeof(CommsHistory), "latest").GetValue(null)).Count == 0, "Cleanup retained latest-message context");
        var pending = (HashSet<CommsHistory.MessageKey>)AccessTools.Field(typeof(CommsHistory), "pending").GetValue(null);
        Check(pending.Count == 0, "Cleanup retained in-flight identifiers"); queue.Clear();
        Field(sim, "_finishedLoading", false);
        iterator = CommsHistory.AfterLoad(sim, (int)AccessTools.Field(typeof(CommsHistory), "loadGeneration").GetValue(null));
        Check(iterator.MoveNext(), "Cancellation fixture did not wait"); CommsHistory.CancelLoad(); Field(sim, "_finishedLoading", true);
        Check(!iterator.MoveNext() && log.Count == 2, "Cancelled/plugin-unload callback pruned history");
        Field(sim, "_finishedLoading", false);
        iterator = CommsHistory.AfterLoad(sim, (int)AccessTools.Field(typeof(CommsHistory), "loadGeneration").GetValue(null));
        Check(iterator.MoveNext(), "World-replacement fixture did not wait"); CrewSim.objInstance = Empty<CrewSim>();
        Check(!iterator.MoveNext() && log.Count == 2, "Old load callback acted on a replacement world");
    }
    static void SaveOrderChecks() {
        using (var assembly = AssemblyDefinition.ReadAssembly(Ostrabus.Development.InstallationPaths.Managed + "/Assembly-CSharp.dll")) {
            var type = assembly.MainModule.GetType("Ostranauts.Core.LoadManager");
            var method = type.Methods.Single(m => m.Name == "SaveGame");
            var calls = method.Body.Instructions.Select(i => i.Operand).OfType<MethodReference>().ToList();
            int cancel = calls.FindIndex(m => m.Name == "CancelRunningThreads"), snapshot = calls.FindIndex(m => m.Name == "SaveGameData");
            Check(cancel >= 0 && snapshot > cancel, "Native save no longer joins previous writer before snapshot");
            foreach (string start in new[] { "Start", "StartWithoutThreading" }) {
                int writer = calls.FindIndex(m => m.Name == start && m.DeclaringType.Name.Contains("SavingJob"));
                Check(writer > snapshot, "Native save now starts its writer before pruning/snapshot: " + start);
            }
        }
    }
    static int Main(string[] args) {
        var previousSim = CrewSim.objInstance; var previousWorld = CrewSim.system; double previousEpoch = StarSystem.fEpoch;
        var previousMessages = StarSystem.OnNewShipCommsMessage;
        StarSystem.OnNewShipCommsMessage = new Ostranauts.Events.OnNewShipCommsMessageEvent();
        var h = new Harmony("offline.comms.environment");
        try {
            h.Patch(AccessTools.Method(typeof(UnityEngine.Object), "op_Equality"), prefix:new HarmonyMethod(typeof(Program), nameof(Equal)));
            h.Patch(AccessTools.Method(typeof(UnityEngine.Object), "op_Inequality"), prefix:new HarmonyMethod(typeof(Program), nameof(Unequal)));
            h.Patch(AccessTools.Method(typeof(DataHandler), "GetInteraction", new[] { typeof(string), typeof(JsonInteractionSave), typeof(bool) }), prefix:new HarmonyMethod(typeof(Program), nameof(RestoreInteraction)));
            ProtectionChecks(); DisplayChecks(h); LifecycleChecks(); SaveOrderChecks();
            if (args.Length > 0) SavedArchive(args[0]);
            Console.WriteLine(checks + " comms retention/display/native lifecycle and serialization checks passed.");
            return 0;
        } finally {
            foreach (var comms in listeners) comms.Destroy(); listeners.Clear(); h.UnpatchSelf();
            CrewSim.objInstance = previousSim; CrewSim.system = previousWorld; StarSystem.fEpoch = previousEpoch;
            StarSystem.OnNewShipCommsMessage = previousMessages;
        }
    }
}
