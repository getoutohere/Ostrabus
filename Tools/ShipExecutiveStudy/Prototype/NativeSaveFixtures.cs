using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Ostranauts.Core;
using Ostranauts.Core.Models;
using Ostranauts.Objectives;
using Ostranauts.Racing;
using Ostranauts.Ships.AIPilots;
using Ostranauts.Systems;
using Ostranauts.Trading;

namespace ShipExecutivePrototype;

// Native LoadManager.SaveGameData orchestration and AI serialization execute.
// Geometry/inventory/portrait and unrelated subsystem snapshots are data providers.
// This is not qualification of Ship.GetJSON or a complete Unity save/load.
sealed class NativeSaveFixtures : IDisposable
{
    readonly Harmony hooks = new Harmony("offline.native-save.providers");
    readonly Dictionary<FieldInfo, object> statics = new Dictionary<FieldInfo, object>();
    internal static NativeSaveFixtures Current;
    internal NativeWorld World;
    internal readonly NativeArrivalFixtures Population;
    internal Action<Ship> BeforeShip;
    internal string OmitShip;
    internal int Captures;
    internal static int TotalCaptures;
    readonly LoadManager loader = NativeWorld.Empty<LoadManager>();
    readonly ObjectiveTracker objectives = NativeWorld.Empty<ObjectiveTracker>();
    readonly RacingLeagueManager racing = NativeWorld.Empty<RacingLeagueManager>();

    internal NativeSaveFixtures(NativeWorld world, NativeArrivalFixtures population)
    {
        Current = this;
        World = world;
        Population = population;
        try
        {
            Initialize();
        }
        catch { Dispose(); throw; }
    }
    void Initialize()
    {
        void Static(Type type, string name, object value)
        {
            var field = AccessTools.Field(type, name);
            statics[field] = field.GetValue(null);
            field.SetValue(null, value);
        }
        Static(typeof(DataHandler), "dictCOSaves", new Dictionary<string, JsonCondOwnerSave>());
        Static(typeof(GigManager), "aJobs", new List<JsonJobSave>());
        Static(typeof(CrewSim), "aPatchesApplied", new List<string>());
        Static(typeof(CrewSim), "objInstance", NativeWorld.Empty<CrewSim>());
        CrewSim.objInstance.workManager = NativeWorld.Empty<WorkManager>();
        NativeWorld.Set(objectives, "_allObjectives", new List<Objective>());
        objectives.subscribedShips = new List<string>();
        Static(typeof(MonoSingleton<ObjectiveTracker>), "_instance", objectives);
        Static(typeof(MonoSingleton<RacingLeagueManager>), "_instance", racing);
        Static(typeof(MonoSingleton<MarketManager>), "_instance", NativeWorld.Empty<MarketManager>());
        void Patch(Type type, string name, string prefix, Type[] args = null) => hooks.Patch(AccessTools.Method(type, name, args)
            ?? throw new MissingMethodException(type.FullName, name), prefix: new HarmonyMethod(typeof(NativeSaveFixtures), prefix),
            transpiler: new HarmonyMethod(typeof(NativeArrivalFixtures), nameof(NativeArrivalFixtures.ExcludedBody)));
        Patch(typeof(CrewSim), "CustomInfosString", nameof(Custom));
        Patch(typeof(Ship), nameof(Ship.GetCOs), nameof(Cos), new[] { typeof(CondTrigger), typeof(bool), typeof(bool), typeof(bool) });
        Patch(typeof(Ship), nameof(Ship.GetJSON), nameof(ShipJson));
        Patch(typeof(CondOwner), nameof(CondOwner.GetJSONSave), nameof(PersonJson));
        Patch(typeof(CondOwner), nameof(CondOwner.GetLotCOs), nameof(Lots));
        Patch(typeof(DataHandler), nameof(DataHandler.GetShipImageByteArrays), nameof(Images));
        Patch(typeof(WorkManager), nameof(WorkManager.GetUnclaimedTasksSaveData), nameof(Tasks));
        Patch(typeof(BountyManager), nameof(BountyManager.GetSave), nameof(Bounties));
        Patch(typeof(Ledger), nameof(Ledger.GetJSONSave), nameof(LedgerData));
        Patch(typeof(PlotManager), nameof(PlotManager.GetJSONSave), nameof(Plots));
        Patch(typeof(PlotManager), nameof(PlotManager.GetJSONSaveOld), nameof(Plots));
        Patch(typeof(StarSystem), nameof(StarSystem.GetJSONSave), nameof(SystemJson));
        Patch(typeof(MarketManager), nameof(MarketManager.GetJSONSave), nameof(Markets));
        Patch(typeof(RacingLeagueManager), nameof(RacingLeagueManager.GetJson), nameof(Races));
        // Remove the deliberately skeletal enemy AI fixture from serialization;
        // the subject's real native controller/tree/blackboard serialization runs.
        AIShipManager.dictAIs["HOME"].RemoveAll(ai => !ReferenceEquals(ai, World.AI));
    }
    internal SaveDto Collect(string folder)
    {
        Captures++;
        TotalCaptures++;
        try
        {
            return (SaveDto)AccessTools.Method(typeof(LoadManager), "SaveGameData").Invoke(loader, new object[] { folder });
        }
        catch (TargetInvocationException error) { throw new InvalidOperationException("Native collector failed", error.InnerException ?? error); }
    }
    internal NativeWorld LoadWorld(SaveDto dto)
    {
        var game = dto.jGameSave.Item2;
        var world = new NativeWorld(game.objSystem.dfEpoch);
        var ships = dto.dictShips.Values.ToArray();
        // The fixture constructor seeds standard hulls; absence in an archive
        // must not recreate one or turn a deferred destination physical again.
        foreach (string id in world.Ships.Keys.Except(ships.Select(s => s.strRegID)).ToArray())
            world.Ships.Remove(id);
        world.LoadStellar(game.objSystem.aStellarShips, game.objSystem.aStellarDerelicts);
        foreach (var saved in ships)
        {
            var ship = world.Resolve(saved.strRegID) ?? world.Vessel(saved.strRegID, 0);
            NativeWorld.Back(ship, "objSS", new ShipSitu(saved.objSS));
            ship.json = saved;
            ship.HideFromSystem = saved.bShipHidden;
            ship.strAIDespawnedAt = saved.strAIDespawnedAt;
            ship.fShallowRCSRemass = saved.fShallowRCSRemass;
            ship.fShallowRCSRemassMax = saved.fShallowRCSRemassMax;
            ship.RCSCount = saved.nRCSCount;
            NativeWorld.Set(ship, "nRCSCountRaw", saved.nRCSCountRaw);
            NativeWorld.Set(ship, "fRCSExhaustV", saved.nRCSExhaustV);
            ship.fAIPauseTimer = saved.fAIPauseTimer;
            ship.fAIDockingExpire = saved.fAIDockingExpire;
        }
        foreach (var saved in ships)
        {
            var ship = world.Resolve(saved.strRegID);
            ship.shipScanTarget = world.Resolve(saved.strScanTargetID);
            ship.shipSituTarget = saved.objSituScanTarget == null ? null : new ShipSitu(saved.objSituScanTarget);
            ship.targetRegID = saved.strScanTargetID;
            var canonical = world.ResolveTarget(saved.strScanTargetID);
            if (canonical != null)
                canonical.ApplyToShip(ship);
            ship.shipCombatTarget = world.Resolve(saved.strCombatTargetID);
        }
        World = world;
        Population.Reload(world, ships);
        var ai = (game.objAIShipManager.aAIShips ?? Array.Empty<JsonAIShipSave>()).SingleOrDefault(s => s.strRegId == "SUBJECT");
        AIShipManager.dictAIs["HOME"].RemoveAll(a => !ReferenceEquals(a, world.AI) || ai == null);
        if (ai != null)
        {
            world.AI.LoadBehaviorTreeSave(ai.aTreeSave);
            world.Pilot.Blackboard.LoadSavedData(ai.ablackboard);
            if (!string.IsNullOrEmpty(ai.strActiveCommand))
            {
                var type = typeof(Ostranauts.Ships.Commands.BaseCommand).Assembly.GetType("Ostranauts.Ships.Commands." + ai.strActiveCommand);
                var active = type == null ? null : world.Pilot.BehaviorTree.FindCommand(type);
                if (active == null)
                    throw new NotSupportedException("Native active command reconstruction outside qualified tree: " + ai.strActiveCommand);
                NativeWorld.Set(world.AI, "_activeCommand", active);
            }
        }
        return world;
    }
    static bool Custom(ref string[] __result)
    {
        __result = new[] { "UnrelatedMod=unchanged" };
        return false;
    }
    static bool Cos(Ship __instance, ref List<CondOwner> __result)
    {
        __result = Current.Population.People.Values.Select(p => p.Person).Where(p => !p.bDestroyed && p.ship == __instance).ToList();
        return false;
    }
    static bool ShipJson(Ship __instance, ref JsonShip __result)
    {
        Current.BeforeShip?.Invoke(__instance);
        if (__instance.strRegID == Current.OmitShip)
            throw new InvalidOperationException("injected ship serialization failure");
        __result = new JsonShip
        {
            strName = __instance.strRegID,
            strRegID = __instance.strRegID,
            objSS = __instance.objSS.GetJSON(),
            aItems = Array.Empty<JsonItem>(),
            bAIShip = __instance.IsAIShip,
            bShipHidden = __instance.HideFromSystem,
            strAIDespawnedAt = __instance.strAIDespawnedAt,
            fAIPauseTimer = __instance.fAIPauseTimer,
            fAIDockingExpire = __instance.fAIDockingExpire,
            fShallowRCSRemass = __instance.fShallowRCSRemass,
            fShallowRCSRemassMax = __instance.fShallowRCSRemassMax,
            nRCSCount = __instance.RCSCount,
            nRCSCountRaw = (int)AccessTools.Field(typeof(Ship), "nRCSCountRaw").GetValue(__instance),
            nRCSExhaustV = (float)AccessTools.Field(typeof(Ship), "fRCSExhaustV").GetValue(__instance),
            strScanTargetID = __instance.shipScanTarget?.strRegID ?? __instance.targetRegID,
            objSituScanTarget = __instance.shipSituTarget?.GetJSON(),
            strCombatTargetID = __instance.shipCombatTarget?.strRegID,
            aDocked = Current.Population.Tow == null ? null : __instance == Current.World.Subject ? new Dictionary<string, string> { ["tow"] = Current.Population.Tow.strRegID }
                : __instance == Current.Population.Tow ? new Dictionary<string, string> { ["root"] = Current.World.Subject.strRegID } : null
        };
        return false;
    }
    internal JsonCondOwnerSave SavePerson(ArrivalPerson person) => new JsonCondOwnerSave
    {
        strID = person.Person.strID,
        strFriendlyName = person.Person.strName,
        bAlive = person.Person.bAlive,
        strRegIDLast = person.Person.ship?.strRegID,
        aFactions = new[] { person.Reputation < 0 ? "enemy" : "friendly" },
        aConds = person.Person.mapConds.Select(p => p.Key + "=" + p.Value.fCount.ToString("R", CultureInfo.InvariantCulture)).ToArray(),
        aMyShips = person.Claims.ToArray(),
        aQueue = person.Queued.Select(q => new JsonInteractionSave { strName = q, objUs = person.Person.strID, objThem = person.Person.strID }).ToArray()
    };
    static bool PersonJson(CondOwner __instance, ref JsonCondOwnerSave __result)
    {
        __result = Current.SavePerson(Current.Population.People[__instance.strID]);
        return false;
    }
    static bool Lots(ref List<CondOwner> __result)
    {
        __result = new List<CondOwner>();
        return false;
    }
    static bool Images(ref Dictionary<string, byte[]> __result)
    {
        __result = null;
        return false;
    }
    static bool Tasks(ref Task2[] __result)
    {
        __result = Array.Empty<Task2>();
        return false;
    }
    static bool Bounties(ref JsonBountySave[] __result)
    {
        __result = Array.Empty<JsonBountySave>();
        return false;
    }
    static bool LedgerData(ref JsonLedgerLI[] __result)
    {
        __result = Array.Empty<JsonLedgerLI>();
        return false;
    }
    static bool Plots(ref JsonPlotSave[] __result)
    {
        __result = Array.Empty<JsonPlotSave>();
        return false;
    }
    static bool SystemJson(ref JsonStarSystemSave __result)
    {
        __result = new JsonStarSystemSave
        {
            strName = "fixture-system",
            dfEpoch = StarSystem.fEpoch,
            dictShips = Current.World.Ships.Keys.ToArray(),
            aStellarShips = Current.World.SaveStellarShips(),
            aStellarDerelicts = Current.World.SaveStellarDerelicts()
        };
        return false;
    }
    static bool Markets(ref JsonMarketSave __result)
    {
        __result = new JsonMarketSave();
        return false;
    }
    static bool Races(ref JsonRacingManagerSave __result)
    {
        __result = new JsonRacingManagerSave();
        return false;
    }
    public void Dispose()
    {
        hooks.UnpatchSelf();
        foreach (var pair in statics)
            pair.Key.SetValue(null, pair.Value);
    }
}
