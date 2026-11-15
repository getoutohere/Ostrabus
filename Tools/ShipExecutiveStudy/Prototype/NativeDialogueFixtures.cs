using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Newtonsoft.Json;
using Ostrabus.Development;
using Ostranauts.Ships.Comms;

namespace ShipExecutivePrototype;

// Registry contents come from shipped JSON. Native Interaction construction,
// reply order, role swapping, condition/ship-spec tests and Comms command loot
// execute. Personality scores, presentation, transport and crew effect execution
// are explicit environmental fixtures, not claims of a complete social sim.
sealed class NativeDialogueFixtures : IDisposable
{
    readonly Harmony hooks = new Harmony("offline.navy.dialogue-provider");
    readonly Dictionary<FieldInfo, object> registries = new Dictionary<FieldInfo, object>();
    readonly MethodInfo oldTrigger = AccessTools.Method(typeof(NavyFixtures), "Trigger");
    internal static NativeDialogueFixtures Current;
    internal static Dictionary<string, ShipMessage> Queue => (Dictionary<string, ShipMessage>)AccessTools.Field(typeof(StarSystem), "_messagesEnRoute").GetValue(CrewSim.system);
    internal System.Collections.Generic.IReadOnlyCollection<ShipMessage> Pending => Queue.Values;
    internal void ClearPending() => Queue.Clear();
    internal readonly List<string> Effects = new List<string>();
    internal readonly List<string> LogMessages = new List<string>();
    internal static readonly string[] RegistryFiles =
    {
        "interactions/interactions.json", "interactions/interactions_social_core.json",
        "tsv/output/stakes/interactions/interactions_STKShipSurrender_PlayerDemand.json",
        "condtrigs/condtrigs.json", "condtrigs/condtrigs2.json",
        "tsv/output/stakes/condtrigs/condTriggers_STKShipSurrender_PlayerDemand.json",
        "loot/loot.json", "tsv/output/stakes/loot/loot_STKShipSurrender_PlayerDemand.json",
        "shipSpecs/shipspecs.json", "personSpecs/personspecs.json"
    };
    readonly UnityEngine.ILogHandler previousLog = UnityEngine.Debug.unityLogger.logHandler;
    sealed class LogSink : UnityEngine.ILogHandler
    {
        public void LogFormat(UnityEngine.LogType type, UnityEngine.Object context, string format, params object[] args) => Current.LogMessages.Add(string.Format(format, args));
        public void LogException(Exception exception, UnityEngine.Object context) => throw exception;
    }
    bool triggerRemoved;

    internal NativeDialogueFixtures()
    {
        Current = this;
        UnityEngine.Debug.unityLogger.logHandler = new LogSink();
        try
        {
            Initialize();
        }
        catch { Dispose(); throw; }
    }

    void Initialize()
    {
        void Registry(string name, object value)
        {
            var field = AccessTools.Field(typeof(DataHandler), name);
            registries[field] = field.GetValue(null);
            field.SetValue(null, value);
        }
        var interactions = Read<JsonInteraction>("interactions/interactions.json")
            .Concat(Read<JsonInteraction>("interactions/interactions_social_core.json"))
            .Concat(Read<JsonInteraction>("tsv/output/stakes/interactions/interactions_STKShipSurrender_PlayerDemand.json"));
        Registry("dictInteractions", interactions.GroupBy(j => j.strName).ToDictionary(g => g.Key, g => g.Last()));
        var triggers = Read<JsonCondTrigger>("condtrigs/condtrigs.json").Concat(Read<JsonCondTrigger>("condtrigs/condtrigs2.json"))
            .Concat(Read<JsonCondTrigger>("tsv/output/stakes/condtrigs/condTriggers_STKShipSurrender_PlayerDemand.json"));
        Registry("dictCTs", triggers.GroupBy(j => j.strName).ToDictionary(g => g.Key, g => CondTrigger.CloneFromJSON(g.Last())));
        var loot = Read<JsonLoot>("loot/loot.json").Concat(Read<JsonLoot>("tsv/output/stakes/loot/loot_STKShipSurrender_PlayerDemand.json"));
        Registry("dictLoot", loot.GroupBy(j => j.strName).ToDictionary(g => g.Key, g => Loot.CloneFromJSON(g.Last())));
        Registry("dictShipSpecs", Read<JsonShipSpec>("shipSpecs/shipspecs.json").GroupBy(j => j.strName).ToDictionary(g => g.Key, g => g.Last()));
        Registry("dictPersonSpecs", Read<JsonPersonSpec>("personSpecs/personspecs.json").GroupBy(j => j.strName).ToDictionary(g => g.Key, g => g.Last()));
        Registry("dictConds", new Dictionary<string, JsonCond>());
        Registry("dictSocialStats", new Dictionary<string, SocialStats>());
        Registry("mapCOs", new Dictionary<string, CondOwner>());
        hooks.Unpatch(AccessTools.Method(typeof(DataHandler), nameof(DataHandler.GetCondTrigger), new[] { typeof(string) }), oldTrigger);
        triggerRemoved = true;
        void Patch(Type type, string name, string prefix, Type[] args = null) => hooks.Patch(AccessTools.Method(type, name, args)
            ?? throw new MissingMethodException(type.FullName, name), prefix: new HarmonyMethod(typeof(NativeDialogueFixtures), prefix));
        Patch(typeof(DataHandler), nameof(DataHandler.GetString), nameof(Translation));
        Patch(typeof(CondOwner), nameof(CondOwner.ValidateParent), nameof(Parent));
        Patch(typeof(CondOwner), nameof(CondOwner.GetNetInteractionResult), nameof(Personality));
        Patch(typeof(CondOwner), nameof(CondOwner.RecentlyTried), nameof(Recency), new[] { typeof(Interaction), typeof(bool) });
        Patch(typeof(CondOwner), nameof(CondOwner.AddRecentlyTried), nameof(Quiet), new[] { typeof(string) });
        Patch(typeof(Comms), "ParseMessageText", nameof(MessageText));
        Patch(typeof(Comms), "NotifyPlayer", nameof(Quiet));
        Patch(typeof(Ship), nameof(Ship.IsPlayerShip), nameof(Player));
        Patch(typeof(Interaction), nameof(Interaction.ApplyEffects), nameof(ApplyEffects));
    }

    static T[] Read<T>(string file) => JsonConvert.DeserializeObject<T[]>(File.ReadAllText(Path.Combine(InstallationPaths.Data, file)));

    internal static void Bind(NativeWorld world, Dictionary<string, Dictionary<string, double>> conditions = null, Dictionary<string, string[]> relationships = null)
    {
        NativeWorld.Set(CrewSim.system, "_messagesEnRoute", new Dictionary<string, ShipMessage>());
        world.DialogueConditions = conditions ?? new Dictionary<string, Dictionary<string, double>>
        {
            ["captain-subject"] = new Dictionary<string, double> { ["IsHuman"] = 1 },
            ["captain-player"] = new Dictionary<string, double> { ["IsHuman"] = 1, ["IsPlayer"] = 1 }
        };
        foreach (var pair in new[] { (world.Captain, "captain-subject"), (CrewSim.coPlayer, "captain-player") })
        {
            // Fixture crew are not scene-registered objects. Seed their stable
            // identity before native interaction binding; the public rename
            // setter updates unavailable ship/Unity scene registries.
            NativeWorld.Set(pair.Item1, "_strID", pair.Item2);
            pair.Item1.strName = pair.Item2;
            pair.Item1.bAlive = true;
            pair.Item1.pspec = NativeWorld.Empty<PersonSpec>();
            NativeWorld.Set(pair.Item1.pspec, "co", pair.Item1);
            pair.Item1.mapConds = world.DialogueConditions[pair.Item2].ToDictionary(p => p.Key, p => new Condition(null) { strName = p.Key, fCount = p.Value });
            DataHandler.mapCOs[pair.Item2] = pair.Item1;
        }
        foreach (var captain in new[] { world.Captain, CrewSim.coPlayer })
        {
            var other = ReferenceEquals(captain, world.Captain) ? CrewSim.coPlayer : world.Captain;
            captain.socUs = NativeWorld.Empty<Social>();
            NativeWorld.Set(captain.socUs, "dictPeople", new Dictionary<string, Relationship>
            {
                [other.strName] = new Relationship { aRelationships = (relationships == null ? new[] { "RELFriend" } : relationships[captain.strID]).ToList() }
            });
        }
    }
    internal static Dictionary<string, Dictionary<string, double>> CaptureCrew(NativeWorld world) => world.DialogueConditions == null ? null
        : new[] { world.Captain, CrewSim.coPlayer }.ToDictionary(c => c.strID, c => c.mapConds.ToDictionary(p => p.Key, p => p.Value.fCount));
    internal static Dictionary<string, string[]> CaptureRelationships(NativeWorld world) => world.DialogueConditions == null ? null
        : new[] { world.Captain, CrewSim.coPlayer }.ToDictionary(c => c.strID,
            c => c.socUs.GetRelationship(ReferenceEquals(c, world.Captain) ? CrewSim.coPlayer.strName : world.Captain.strName).aRelationships.ToArray());

    internal static void RestoreComms(Ship ship, JsonCommData data)
        => RestoreComms(ship.Comms, data);

    internal static void RestoreComms(Comms comms, JsonCommData data)
    {
        AccessTools.Method(typeof(Comms), "SetData").Invoke(comms, new object[] { data });
        foreach (var message in comms.GetMessages())
            message.Interaction?.PostGameLoad();
    }
    internal void Deliver()
    {
        var pending = Pending.ToArray();
        foreach (var message in pending)
        {
            // Native DeliverMessages requires epoch strictly AFTER arrival.
            // Delivery event fanout is the scene fixture; SendMessage and its
            // ID-based deduplication execute unpatched against the native queue.
            if (message.AvailableTime >= StarSystem.fEpoch)
                continue;
            Queue.Remove(message.ID);
            var target = NativeWorld.Current.Resolve(message.ReceiverRegId);
            AccessTools.Method(typeof(Comms), "ReceiveMessage").Invoke(target.Comms, new object[] { message });
        }
    }
    static bool Translation(string __0, ref string __result)
    {
        __result = __0;
        return false;
    }
    static bool Parent(ref bool __result)
    {
        __result = true;
        return false;
    }
    static bool Personality(ref float __result)
    {
        __result = 0;
        return false;
    }
    static bool Recency(ref double __result)
    {
        __result = -1;
        return false;
    }
    static bool Quiet() => false;
    static bool MessageText(Interaction ia, ref string __result)
    {
        __result = ia.strName;
        return false;
    }
    static bool Player(Ship __instance, ref bool __result)
    {
        __result = __instance.strRegID == "PLAYER";
        return false;
    }
    static bool ApplyEffects(Interaction __instance)
    {
        Current.Effects.Add(__instance.strName);
        return false;
    }
    public void Dispose()
    {
        hooks.UnpatchSelf();
        UnityEngine.Debug.unityLogger.logHandler = previousLog;
        if (triggerRemoved)
            new Harmony("offline.navy.fixtures").Patch(AccessTools.Method(typeof(DataHandler), nameof(DataHandler.GetCondTrigger), new[] { typeof(string) }), prefix: new HarmonyMethod(oldTrigger));
        foreach (var pair in registries)
            pair.Key.SetValue(null, pair.Value);
    }
}
