using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Ostranauts.Core;
using Ostranauts.Ships.Commands;
using Ostranauts.Ships.Comms;
using Ostranauts.Ships.Sensors;

namespace ShipExecutivePrototype;

// Equipment, sensor signatures and dialogue/world outcomes are fixtures.
// Native threat filtering, reputation decisions and surrender command bodies run.
sealed class NavyFixtures : IDisposable
{
    readonly Harmony hooks = new Harmony("offline.navy.fixtures");
    readonly List<(MethodBase Target, MethodInfo Prefix)> restored = new List<(MethodBase, MethodInfo)>();
    internal static NavyFixtures Current;
    internal CondOwner EnemyCaptain;
    internal bool AcceptDemand;
    internal int EquipmentProfile = 1;
    internal BodyOrbit LocalBody;
    internal float CollisionRadius;
    internal readonly List<string> Sent = new List<string>();

    internal NavyFixtures()
    {
        Current = this;
        Replace(typeof(AreThreatsNearby), "RunCommand", null);
        Replace(typeof(SurrenderDemand), "RunCommand", null);
        Replace(typeof(Comms), "GetCaptain", nameof(Captain));
        Replace(typeof(Comms), "SendMessage", nameof(Message), new[] { typeof(string), typeof(string), typeof(string) });
        Replace(typeof(StarSystem), "GetShipOwner", nameof(Owner));
        Replace(typeof(CondOwner), "GetFactionScore", nameof(Score), new[] { typeof(string) });
        Replace(typeof(SetEngagementProfile), "RunCommand", nameof(Profile));
        Replace(typeof(DataHandler), "GetCondTrigger", nameof(Trigger), new[] { typeof(string) });
        Replace(typeof(CollisionManager), "GetCollisionDistanceAU", nameof(Collision), new[] { typeof(ShipSitu), typeof(ShipSitu) });
        hooks.Patch(AccessTools.Method(typeof(BaseCommand), "GetVisibleShipSignatures"), prefix: new HarmonyMethod(typeof(NavyFixtures), nameof(Signatures)));
        hooks.Patch(AccessTools.Method(typeof(CondOwner), "GetAllFactions"), prefix: new HarmonyMethod(typeof(NavyFixtures), nameof(Factions)));
        hooks.Patch(AccessTools.PropertyGetter(typeof(Ship), "IsUsingTorchDrive"), prefix: new HarmonyMethod(typeof(NavyFixtures), nameof(No)));
        hooks.Patch(AccessTools.Method(typeof(CrewSim), "TimeElapsedScaled"), prefix: new HarmonyMethod(typeof(NavyFixtures), nameof(Elapsed)));
        hooks.Patch(AccessTools.Method(typeof(DataHandler), "GetCommand"), prefix: new HarmonyMethod(typeof(NavyFixtures), nameof(CommandType)));
        hooks.Patch(AccessTools.Method(typeof(CrewSim), "GetSelectedCrew"), prefix: new HarmonyMethod(typeof(NavyFixtures), nameof(Selected)));
        hooks.Patch(AccessTools.Method(typeof(StarSystem), "GetNearestBO"), prefix: new HarmonyMethod(typeof(NavyFixtures), nameof(Body)));
        hooks.Patch(AccessTools.Method(typeof(BodyOrbit), "UpdateTime"), prefix: new HarmonyMethod(typeof(NavyFixtures), nameof(Quiet)));
        hooks.Patch(AccessTools.Method(typeof(CondTrigger), "Triggered"), prefix: new HarmonyMethod(typeof(NavyFixtures), nameof(Triggered)));
    }

    void Replace(Type type, string name, string prefix, Type[] parameters = null)
    {
        var method = AccessTools.Method(type, name, parameters);
        foreach (var patch in Harmony.GetPatchInfo(method)?.Prefixes.Where(p => p.owner == "offline.executive-prototype.world") ?? Enumerable.Empty<Patch>())
            restored.Add((method, patch.PatchMethod));
        hooks.Unpatch(method, HarmonyPatchType.Prefix, "offline.executive-prototype.world");
        if (prefix != null)
            hooks.Patch(method, prefix: new HarmonyMethod(typeof(NavyFixtures), prefix));
    }
    public void Dispose()
    {
        hooks.UnpatchSelf();
        var original = new Harmony("offline.executive-prototype.world");
        foreach (var patch in restored)
            original.Patch(patch.Target, prefix: new HarmonyMethod(patch.Prefix));
    }
    static bool No(ref bool __result)
    {
        __result = false;
        return false;
    }
    static bool Quiet() => false;
    static bool Collision(ref float __result)
    {
        __result = Current.CollisionRadius;
        return false;
    }
    static bool Trigger(string __0, ref CondTrigger __result)
    {
        __result = __0 == "TIsHasRequestedHelp" ? new CondTrigger { strName = __0 } : null;
        return false;
    }
    static bool Triggered(CondTrigger __instance, ref bool __result)
    {
        if (__instance.strName != "TIsHasRequestedHelp")
            return true;
        __result = false;
        return false;
    }
    static bool Body(ref BodyOrbit __result)
    {
        __result = Current.LocalBody;
        return false;
    }
    static bool Selected(ref CondOwner __result)
    {
        __result = CrewSim.coPlayer;
        return false;
    }
    static bool CommandType(string __0, ref Type __result)
    {
        // Native registry loading is a fixture boundary. The factory itself,
        // constructor, SaveData setter and command body still execute natively.
        __result = typeof(BaseCommand).Assembly.GetType("Ostranauts.Ships.Commands." + __0, false);
        return false;
    }
    static bool Elapsed(ref float __result)
    {
        __result = NativeNavyActor.Current?.SteeringDelta ?? 0;
        return false;
    }
    static bool Owner(string __0, ref string __result)
    {
        __result = __0 == "ENEMY" ? "enemy" : "friendly";
        return false;
    }
    static bool Score(string __0, ref float __result)
    {
        __result = __0 == "enemy" ? -100 : 100;
        return false;
    }
    static bool Factions(CondOwner __instance, ref List<string> __result)
    {
        __result = new List<string> { ReferenceEquals(__instance, Current.EnemyCaptain) ? "enemy" : "friendly" };
        return false;
    }
    static bool Captain(Comms __instance, ref CondOwner captain, ref bool __result)
    {
        if (ReferenceEquals(__instance, NativeWorld.Current.Enemy.Comms))
        {
            if (Current.EnemyCaptain == null || !ReferenceEquals(Current.EnemyCaptain.ship, NativeWorld.Current.Enemy))
            {
                Current.EnemyCaptain = NativeWorld.Empty<CondOwner>();
                Current.EnemyCaptain.bAlive = true;
                Current.EnemyCaptain.ship = NativeWorld.Current.Enemy;
            }
            captain = Current.EnemyCaptain;
        }
        else
            captain = NativeWorld.Current.Captain;
        __result = captain != null;
        return false;
    }
    static bool Signatures(ref Dictionary<Ship, ShipSignature> __result)
    {
        __result = new Dictionary<Ship, ShipSignature>();
        if (NativeWorld.Current.Threat)
            __result[NativeWorld.Current.Enemy] = NativeWorld.Empty<ShipSignature>();
        return false;
    }
    static bool Message(string ia, ref bool __result)
    {
        Current.Sent.Add(ia);
        __result = ia != "STKShipSurrender_AIDemand_Intro" || Current.AcceptDemand;
        return false;
    }
    static bool Profile(BaseCommand __instance, Blackboard blackboard, ref CommandStatus __result)
    {
        blackboard.Set("EnagementProfile", Current.EquipmentProfile);
        blackboard.Set("EnagementRange", Current.EquipmentProfile == 1 ? 9.6 : 100.0);
        __result = new CommandStatus(CommandCode.Finished, __instance);
        return false;
    }
}
