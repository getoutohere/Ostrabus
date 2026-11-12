using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using Condowner.Utils;
using HarmonyLib;
using Ostranauts.Electrical;
using UnityEngine;

static partial class Program {
    static int checks, serial;
    static readonly Dictionary<string, CondOwner> registry = new Dictionary<string, CondOwner>();
    static readonly Dictionary<string, HashSet<string>> conditions = new Dictionary<string, HashSet<string>>();
    static readonly List<string> findings = new List<string>();
    static T Empty<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    static void Set(object obj, string name, object value) => AccessTools.Field(obj.GetType(), name).SetValue(obj, value);
    static T Get<T>(object obj, string name) => (T)AccessTools.Field(obj.GetType(), name).GetValue(obj);
    static void Check(bool value, string why) { checks++; if (!value) throw new Exception(why); }
    static void Finding(string text) { findings.Add(text); Console.WriteLine("CONFIRMED: " + text); }
    static bool Equal(UnityEngine.Object __0, UnityEngine.Object __1, ref bool __result) { __result = ReferenceEquals(__0, __1); return false; }
    static bool Unequal(UnityEngine.Object __0, UnityEngine.Object __1, ref bool __result) { __result = !ReferenceEquals(__0, __1); return false; }
    static bool Has(CondOwner __instance, string strName, ref bool __result) { __result = conditions[__instance.strID].Contains(strName); return false; }
    static bool Add(CondOwner __instance, string strName) { conditions[__instance.strID].Add(strName); return false; }
    static bool Zero(CondOwner __instance, string strName) { conditions[__instance.strID].Remove(strName); return false; }
    static bool Lookup(string strID, ref CondOwner __result) { lookupCalls++; registry.TryGetValue(strID, out __result); return false; }
    static bool Epoch(ref double __result) { __result = StarSystem.fEpoch; return false; }
    static bool Unscaled(ref float __result) { __result = 0; return false; }
    static bool Skip() => false;
    static Dictionary<string, string> Gpm(Electrical e) => Get<Dictionary<string, string>>(e, "mapGPM");
    static List<ElectricalSignal> Queue(Electrical e, string field) => Get<List<ElectricalSignal>>(e, field);
    static Electrical Device() {
        var co = Empty<CondOwner>(); Set(co, "_strID", "device-" + ++serial);
        co.Clock = Empty<Clock>(); co.mapGUIPropMaps = new Dictionary<string, Dictionary<string, string>>();
        var ship = Empty<Ship>(); Set(co, "_objShip", ship);
        var e = Empty<Electrical>(); Set(co, "elec", e); e.coSelf = co; e.coShip = ship;
        e.inputConnections = new Dictionary<string, ElectricalConnection>();
        e.outputConnections = new Dictionary<string, ElectricalConnection>();
        Set(e, "signalQueue", new List<ElectricalSignal>()); Set(e, "sendQueue", new List<ElectricalSignal>()); Set(e, "outputRemoves", new List<string>());
        Set(e, "strGPMKey", "Electrical"); Set(e, "sliderMax", 60f); Set(e, "bNeedsCheck", false);
        Set(e, "mapGPM", new Dictionary<string, string> { ["status"] = "true", ["gate"] = "0", ["delay"] = "0", ["override"] = "true" });
        co.mapGUIPropMaps["Electrical"] = Gpm(e);
        e.status = e.bOverride = true; e.isInitialising = false; e.fTimeToNextAudit = 10000;
        registry[co.strID] = co; conditions[co.strID] = new HashSet<string> { "IsPowered" };
        if (productionTesting) {
            co.mapConds = new Dictionary<string, Condition>();
            ship.strRegID = "production-ship-" + serial;
            Set(ship, "nLoadState", Ship.Loaded.Full);
            CompletedElectricalLoad(ship) = true;
            CrewSim.system.dictShips[ship.strRegID] = ship;
        }
        return e;
    }
    static void Input(Electrical e, string origin, bool on) {
        e.inputConnections[origin] = new ElectricalConnection(origin, on ? SignalType.On : SignalType.Off);
        if (productionTesting && registry.TryGetValue(origin, out var source) && source.Electrical != null)
            source.Electrical.outputConnections[e.coSelf.strID] = new ElectricalConnection(e.coSelf.strID, SignalType.On);
    }
    static void Tick(Electrical e) { StarSystem.fEpoch += 1; e.ResolveSignalQueue(); }
    static void Init(Electrical e) => AccessTools.Method(typeof(Electrical), "InitGPMValues").Invoke(e, null);
    public static void Main() {
        StarSystem.fEpoch = 1000;
        var h = new Harmony("offline.signalbox.engine.fixtures");
        h.Patch(AccessTools.Method(typeof(UnityEngine.Object), "op_Equality"), prefix: new HarmonyMethod(typeof(Program), nameof(Equal)));
        h.Patch(AccessTools.Method(typeof(UnityEngine.Object), "op_Inequality"), prefix: new HarmonyMethod(typeof(Program), nameof(Unequal)));
        h.Patch(AccessTools.Method(typeof(CondOwner), "HasCond", new[] { typeof(string) }), prefix: new HarmonyMethod(typeof(Program), nameof(Has)));
        h.Patch(AccessTools.Method(typeof(CondOwner), "AddCondAmount"), prefix: new HarmonyMethod(typeof(Program), nameof(Add)));
        h.Patch(AccessTools.Method(typeof(CondOwner), "ZeroCondAmount"), prefix: new HarmonyMethod(typeof(Program), nameof(Zero)));
        h.Patch(AccessTools.Method(typeof(Ship), "GetCOByID"), prefix: new HarmonyMethod(typeof(Program), nameof(Lookup)));
        h.Patch(AccessTools.PropertyGetter(typeof(Clock), "Epoch"), prefix: new HarmonyMethod(typeof(Program), nameof(Epoch)));
        h.Patch(AccessTools.PropertyGetter(typeof(Time), "unscaledDeltaTime"), prefix: new HarmonyMethod(typeof(Program), nameof(Unscaled)));
        ConsoleToGUI.instance = Empty<ConsoleToGUI>();
        h.Patch(AccessTools.Method(typeof(ConsoleToGUI), "LogInfo"), prefix: new HarmonyMethod(typeof(Program), nameof(VisibleInfo)));
        h.Patch(AccessTools.Method(typeof(Debug), "LogWarning", new[] { typeof(object) }), prefix: new HarmonyMethod(typeof(Program), nameof(Skip)));

        // Baseline: gate calculations themselves match the four truth tables.
        foreach (GateMode gate in Enum.GetValues(typeof(GateMode))) for (int mask = 0; mask < 4; mask++) {
            var e = Device(); e.gateMode = gate; Input(e, "a", (mask & 1) != 0); Input(e, "b", (mask & 2) != 0);
            Tick(e);
            bool any = mask != 0, all = mask == 3;
            bool expected = gate == GateMode.OR ? any : gate == GateMode.AND ? all : gate == GateMode.NOR ? !any : !all;
            Check(e.status == expected, "Baseline truth table failed: " + gate + " " + mask);
        }
        var switched = Device(); Input(switched, "a", true); Input(switched, "b", false); Tick(switched);
        switched.SetGateMode((int)GateMode.AND);
        Check(Gpm(switched)["gate"] == "1", "Native knob setter should serialize its integer argument");
        Check(!Get<bool>(switched, "bNeedsCheck"), "Gate knob unexpectedly scheduled reevaluation");
        switched.UpdateManual();
        Check(switched.status, "Gate change unexpectedly updated output without an input event");
        bool formatError = false;
        try { Init(switched); } catch (TargetInvocationException ex) when (ex.InnerException is FormatException) { formatError = true; }
        Check(!formatError, "Numeric gate serialization unexpectedly failed native reload parsing");
        Finding("Gate knob leaves the old output active without scheduling reevaluation; gate serialization itself is numeric and reloads correctly.");
        switched.SetGPM(); Init(switched); Tick(switched);
        Check(!switched.status && Gpm(switched)["gate"] == "1", "Numeric serialization/evaluation control failed");

        var source = Device(); var target = Device(); var other = Device();
        Input(target, other.coSelf.strID, false);
        source.SetUpConnection(target.coSelf); Tick(source); Tick(target);
        Check(target.inputConnections.ContainsKey(source.coSelf.strID), "Native connection handshake failed");
        source.RemoveConnection(target.coSelf.strID); Tick(source); Tick(target);
        Check(!source.outputConnections.ContainsKey(target.coSelf.strID) && target.inputConnections.ContainsKey(source.coSelf.strID), "Disconnect did not reproduce stale receiver input");
        Check(target.status, "Ghost high input did not keep OR output high despite the other input being off");
        target.AuditConnections();
        Check(target.inputConnections.ContainsKey(source.coSelf.strID), "Audit unexpectedly pruned an existing source with no reciprocal output");
        Finding("Disconnect is dropped because RemoveConnection deletes the output before SendSignal checks it; receiver input survives connection audits.");

        var sender = Device(); var receiver = Device(); sender.SetUpConnection(receiver.coSelf); Tick(sender); Tick(receiver);
        sender.QueueSignal(receiver.coSelf.strID, SignalType.Off); Tick(sender); Tick(receiver);
        Check(Queue(sender, "sendQueue").Count == 0 && Gpm(sender)["sendQueue"].Length > 0, "Expected stale serialized outgoing queue");
        string pending = Gpm(sender)["sendQueue"];
        Init(sender);
        Check(Queue(sender, "sendQueue").Count == 1 && Queue(sender, "sendQueue")[0].signalType == SignalType.Off, "Processed outgoing signal did not return after native state reinitialization");
        Finding("Already-sent outgoing commands remain in the saved GPM and replay on reload.");

        var gateOff = Device(); var consumer = Device(); Input(gateOff, "upstream", false); Tick(gateOff);
        Check(!gateOff.status, "Low-source control failed");
        gateOff.SetUpConnection(consumer.coSelf); Tick(gateOff); Tick(consumer);
        Check(!gateOff.status && consumer.inputConnections[gateOff.coSelf.strID].signalType == SignalType.On, "New connection did not reproduce incorrect initial high signal");
        gateOff.ToggleConnection(consumer.coSelf.strID); Tick(gateOff); Tick(consumer);
        gateOff.ToggleConnection(consumer.coSelf.strID); Tick(gateOff); Tick(consumer);
        Check(!gateOff.status && consumer.inputConnections[gateOff.coSelf.strID].signalType == SignalType.On, "Reenabled output unexpectedly respected source low state");
        Finding("New/re-enabled outputs send On even when the source gate output is Off (tested while signal delivery is powered).");

        var audited = Device(); var high = Device(); var low = Device();
        audited.gateMode = GateMode.AND; Input(audited, high.coSelf.strID, true); Input(audited, low.coSelf.strID, false); Tick(audited);
        Check(!audited.status, "Audit precondition failed"); registry.Remove(low.coSelf.strID);
        audited.AuditConnections(); audited.UpdateManual();
        Check(audited.inputConnections.Count == 1 && !audited.status && !Get<bool>(audited, "bNeedsCheck"), "Input audit unexpectedly reevaluated gate output");
        Tick(audited); Check(audited.status, "Manual reevaluation control failed");
        Finding("Connection audit changes inputs without scheduling gate reevaluation; status can remain stale indefinitely.");

        var partial = Device(); var savedEndpoint = Device();
        string savedId = savedEndpoint.coSelf.strID;
        partial.coShip.json = new JsonShip { aItems = new[] { new JsonItem { strID = savedId, strName = "ItmElectricalBox01" } } };
        Input(partial, savedId, true); partial.SetGPM();
        registry.Remove(savedId); // Endpoint remains in ship JSON but is absent from materialized lookup.
        partial.AuditConnections();
        Check(partial.coShip.json.aItems.Any(x => x.strID == savedId) && !partial.inputConnections.ContainsKey(savedId), "Audit did not reproduce deletion of a still-saved endpoint");
        Check(Gpm(partial)["inputConnections"] == "", "Runtime-only deletion was not persisted to connection GPM");
        registry[savedId] = savedEndpoint.coSelf;
        partial.AuditConnections();
        Check(!partial.inputConnections.ContainsKey(savedId), "Rematerializing endpoint unexpectedly restored the removed relationship");
        Finding("Audit deletes and saves a connection whose endpoint still exists in ship JSON but is absent from runtime lookup; rematerialization does not restore it.");

        var named = Device(); named.outputConnections["destination"] = new ElectricalConnection("destination", SignalType.On, "Fuel#backup"); named.SetGPM();
        string saved = Gpm(named)["outputConnections"]; named.outputConnections.Clear(); Init(named);
        Check(named.outputConnections["destination"].nickName == "Fuel", "Hash nickname truncation did not reproduce");
        named.outputConnections["destination"] = new ElectricalConnection("destination", SignalType.On, "Fuel, backup"); named.SetGPM(); named.outputConnections.Clear(); Init(named);
        Check(named.outputConnections.ContainsKey("") && named.outputConnections["destination"].nickName == "Fuel", "Comma nickname corruption did not reproduce");
        Finding("Connection nicknames containing '#' truncate; commas produce phantom malformed connection entries after reload.");
        CheckAuditLogging();
        CheckOffscreenPersistence();
        CheckCandidateFixes();
        CheckCandidateFixes(production: true);
        h.UnpatchSelf();
        Console.WriteLine("Signal-box audit: " + checks + " checks; " + findings.Count + " reproduced findings; logging routing verified. No live game or save changed.");
    }
}
