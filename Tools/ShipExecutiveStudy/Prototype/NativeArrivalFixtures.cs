using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Ostranauts.Core;

namespace ShipExecutivePrototype;

sealed class ArrivalPerson
{
    internal CondOwner Person;
    internal float Reputation = 100;
    internal readonly HashSet<string> Claims = new HashSet<string>();
    internal readonly List<string> Queued = new List<string>();
}

// Actual DockAndDespawn, drop-off trigger, reputation branching, and unregister
// execute. Physical person movement/destruction, visibility and crew scheduling
// are providers with observable state. Company policy is explicitly not modeled.
sealed class NativeArrivalFixtures : IDisposable
{
    readonly Harmony hooks = new Harmony("offline.native-arrival.providers");
    readonly List<(MethodBase Method, MethodInfo Prefix, string Owner)> removed = new List<(MethodBase, MethodInfo, string)>();
    internal static NativeArrivalFixtures Current;
    internal readonly Dictionary<string, ArrivalPerson> People = new Dictionary<string, ArrivalPerson>();
    internal readonly List<string> Calls = new List<string>();
    internal Action<string> BeforeMove;
    internal Action AfterNativeBody;
    internal Ship Tow;

    internal NativeArrivalFixtures(NativeWorld world)
    {
        Current = this;
        NativeDialogueFixtures.Bind(world);
        Add(world.Captain, "captain-subject", world.Subject);
        Add(CrewSim.coPlayer, "captain-player", CrewSim.coPlayer.ship);
        void Replace(Type type, string name, string prefix, Type[] args = null)
        {
            var method = AccessTools.Method(type, name, args) ?? throw new MissingMethodException(type.FullName, name);
            foreach (var patch in Harmony.GetPatchInfo(method)?.Prefixes.Where(p => p.owner == "offline.executive-prototype.world") ?? Enumerable.Empty<Patch>())
            {
                removed.Add((method, patch.PatchMethod, patch.owner));
                hooks.Unpatch(method, patch.PatchMethod);
            }
            bool sceneBody = name != nameof(CrewSim.DockAndDespawn);
            if (prefix != null)
                hooks.Patch(method, prefix: new HarmonyMethod(typeof(NativeArrivalFixtures), prefix),
                transpiler: sceneBody ? new HarmonyMethod(typeof(NativeArrivalFixtures), nameof(ExcludedBody)) : null);
        }
        Replace(typeof(CrewSim), nameof(CrewSim.DockAndDespawn), nameof(Enter));
        hooks.Patch(AccessTools.Method(typeof(CrewSim), nameof(CrewSim.DockAndDespawn)), postfix: new HarmonyMethod(typeof(NativeArrivalFixtures), nameof(Exited)));
        Replace(typeof(Ship), nameof(Ship.GetPeople), nameof(GetPeople), new[] { typeof(bool) });
        Replace(typeof(Ship), nameof(Ship.GetAllDockedShips), nameof(Docked));
        Replace(typeof(Ship), nameof(Ship.GetShipFactions), nameof(Factions));
        Replace(typeof(CondOwner), nameof(CondOwner.GetFactionScore), nameof(Score), new[] { typeof(List<string>) });
        Replace(typeof(CondOwner), nameof(CondOwner.CatchUp), nameof(CatchUp));
        Replace(typeof(CondOwner), nameof(CondOwner.ClaimShip), nameof(Claim));
        Replace(typeof(CondOwner), nameof(CondOwner.UnclaimShip), nameof(Unclaim));
        Replace(typeof(CondOwner), nameof(CondOwner.LogMove), nameof(Quiet));
        Replace(typeof(CondOwner), nameof(CondOwner.Destroy), nameof(Destroy));
        Replace(typeof(CrewSim), nameof(CrewSim.MoveCO), nameof(Move));
        Replace(typeof(CondOwner), nameof(CondOwner.ZeroCondAmount), nameof(Zero));
        Replace(typeof(CondOwner), nameof(CondOwner.SetCondAmount), nameof(Set));
        Replace(typeof(CondOwner), nameof(CondOwner.HasQueuedInteraction), nameof(HasQueued));
        Replace(typeof(CondOwner), nameof(CondOwner.QueueInteraction), nameof(Queue));
        Replace(typeof(Ship), nameof(Ship.ToggleVis), nameof(Visibility));
        Replace(typeof(Ship), nameof(Ship.LogAdd), nameof(Quiet));
    }
    internal ArrivalPerson Add(CondOwner person, string id, Ship ship)
    {
        NativeWorld.Set(person, "_strID", id);
        person.strName = id;
        person.ship = ship;
        person.bAlive = true;
        person.mapConds ??= new Dictionary<string, Condition>();
        person.mapConds["IsHuman"] = new Condition(null) { strName = "IsHuman", fCount = 1 };
        DataHandler.mapCOs[id] = person;
        var state = new ArrivalPerson { Person = person };
        state.Claims.Add(ship.strRegID);
        People.Add(id, state);
        return state;
    }
    internal ArrivalPerson Add(string id, Ship ship, string forbidden = null, float reputation = 100)
    {
        var state = Add(NativeWorld.Empty<CondOwner>(), id, ship);
        state.Reputation = reputation;
        if (forbidden != null)
            state.Person.mapConds[forbidden] = new Condition(null) { strName = forbidden, fCount = 1 };
        return state;
    }
    internal void Reload(NativeWorld world, JsonShip[] ships)
    {
        People.Clear();
        DataHandler.mapCOs.Clear();
        NativeDialogueFixtures.Bind(world);
        Tow = world.Resolve(ships.First(s => s.strRegID == "SUBJECT").aDocked?.Values.FirstOrDefault());
        foreach (var saved in ships.SelectMany(s => s.aCOs ?? Array.Empty<JsonCondOwnerSave>()))
        {
            var person = saved.strID == "captain-subject" ? world.Captain : saved.strID == "captain-player" ? CrewSim.coPlayer : NativeWorld.Empty<CondOwner>();
            var state = Add(person, saved.strID, world.Resolve(saved.strRegIDLast));
            person.bAlive = saved.bAlive;
            person.mapConds.Clear();
            foreach (string condition in saved.aConds ?? Array.Empty<string>())
            {
                var parts = condition.Split('=');
                person.mapConds[parts[0]] = new Condition(null) { strName = parts[0], fCount = double.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture) };
            }
            state.Reputation = saved.aFactions?.Contains("enemy") == true ? -100 : 100;
            state.Claims.Clear();
            foreach (string claim in saved.aMyShips ?? Array.Empty<string>())
                state.Claims.Add(claim);
            foreach (var queued in saved.aQueue ?? Array.Empty<JsonInteractionSave>())
                state.Queued.Add(queued.strName);
        }
        if (!People.ContainsKey("captain-player"))
            throw new InvalidOperationException("Fixture archive lacks its player");
        if (!People.ContainsKey("captain-subject"))
        {
            // Do not turn the fixture constructor's temporary captain into an
            // NPC resurrected from an archive where native code destroyed it.
            DataHandler.mapCOs.Remove("captain-subject");
            world.Captain.bDestroyed = true;
            world.Captain.bAlive = false;
        }
    }
    static void Enter()
    {
        NativeWorld.Current.BeforeArrival?.Invoke();
        NativeWorld.Current.ArrivalCalls++;
    }
    static void Exited() => Current.AfterNativeBody?.Invoke();
    static bool Quiet() => false;
    internal static IEnumerable<CodeInstruction> ExcludedBody(MethodBase __originalMethod)
    {
        Type type = ((MethodInfo)__originalMethod).ReturnType;
        if (type != typeof(void))
            yield return type == typeof(float) ? new CodeInstruction(OpCodes.Ldc_R4, 0f)
                : type == typeof(double) ? new CodeInstruction(OpCodes.Ldc_R8, 0d)
                : type.IsValueType ? new CodeInstruction(OpCodes.Ldc_I4_0) : new CodeInstruction(OpCodes.Ldnull);
        yield return new CodeInstruction(OpCodes.Ret);
    }
    static bool GetPeople(Ship __instance, bool bAllowDocked, ref List<CondOwner> __result)
    {
        __result = Current.People.Values.Select(p => p.Person).Where(p => !p.bDestroyed && (p.ship == __instance || bAllowDocked && __instance == NativeWorld.Current.Subject && p.ship == Current.Tow)).ToList();
        return false;
    }
    static bool Docked(Ship __instance, ref IReadOnlyList<Ship> __result)
    {
        __result = Current.Tow != null && __instance == NativeWorld.Current.Subject ? new[] { Current.Tow }
            : Current.Tow != null && __instance == Current.Tow ? new[] { NativeWorld.Current.Subject } : Array.Empty<Ship>();
        return false;
    }
    static bool Factions(ref List<JsonFaction> __result)
    {
        __result = new List<JsonFaction> { new JsonFaction { strName = "friendly" } };
        return false;
    }
    static bool Score(CondOwner __instance, ref float __result)
    {
        __result = Current.People[__instance.strID].Reputation;
        return false;
    }
    static bool CatchUp(CondOwner __instance)
    {
        Current.Calls.Add("catchup:" + __instance.strID);
        return false;
    }
    static bool Claim(CondOwner __instance, string strRegID)
    {
        Current.People[__instance.strID].Claims.Add(strRegID);
        return false;
    }
    static bool Unclaim(CondOwner __instance, string strRegID)
    {
        Current.People[__instance.strID].Claims.Remove(strRegID);
        return false;
    }
    static bool Move(CondOwner objCO, Ship objShipNew)
    {
        Current.BeforeMove?.Invoke(objCO.strID);
        Current.Calls.Add("move:" + objCO.strID);
        objCO.ship = objShipNew;
        return false;
    }
    static bool Destroy(CondOwner __instance)
    {
        Current.Calls.Add("destroy:" + __instance.strID);
        __instance.bDestroyed = true;
        __instance.bAlive = false;
        DataHandler.mapCOs.Remove(__instance.strID);
        return false;
    }
    static bool Zero(CondOwner __instance, string strName)
    {
        __instance.mapConds?.Remove(strName);
        return false;
    }
    static bool Set(CondOwner __instance, string strName, double dAmount)
    {
        __instance.mapConds ??= new Dictionary<string, Condition>();
        __instance.mapConds[strName] = new Condition(null) { strName = strName, fCount = dAmount };
        return false;
    }
    static bool HasQueued(CondOwner __instance, string strName, ref bool __result)
    {
        __result = Current.People[__instance.strID].Queued.Contains(strName);
        return false;
    }
    static bool Queue(CondOwner __instance, Interaction objInteraction, ref bool __result)
    {
        Current.People[__instance.strID].Queued.Add(objInteraction.strName);
        __result = true;
        return false;
    }
    static bool Visibility(Ship __instance, bool bShow)
    {
        Current.Calls.Add("visible:" + __instance.strRegID + ":" + bShow);
        return false;
    }
    public void Dispose()
    {
        hooks.UnpatchSelf();
        foreach (var patch in removed)
            new Harmony(patch.Owner).Patch(patch.Method, prefix: new HarmonyMethod(patch.Prefix));
    }
}
