using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using Ostrabus.Development;
using Ostranauts.Core;
using Ostranauts.Ships.AIPilots;
using Ostranauts.Ships.Commands;

static partial class Program {
    const double AU = 149597872;
    static int checks, created;
    static bool emptyUniverse, moored, lowFuel;
    static readonly Dictionary<CondOwner, Dictionary<string, double>> Conditions = new Dictionary<CondOwner, Dictionary<string, double>>();
    static readonly Dictionary<string, string> Owners = new Dictionary<string, string>();
    static readonly List<AIShip> Constructed = new List<AIShip>();
    static Ship primary, terminal, haven, player;
    static readonly Type Policy = typeof(Ostrabus.OstrabusPlugin).Assembly.GetType("Ostrabus.ShipNavigation.Encounters.FactionEncounter", true);
    static readonly Type PatchType = Policy.Assembly.GetType("Ostrabus.ShipNavigation.Encounters.FactionEncounterPatch", true);
    static T Empty<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    static void Set(object instance, string field, object value) => AccessTools.Field(instance.GetType(), field).SetValue(instance, value);
    static object Call(string name, params object[] args) => AccessTools.Method(Policy, name).Invoke(null, args);
    static void Check(bool ok, string reason) { checks++; if (!ok) throw new Exception(reason); }
    static bool Quiet() => false;
    static bool Equal(UnityEngine.Object __0, UnityEngine.Object __1, ref bool __result) { __result = ReferenceEquals(__0, __1); return false; }
    static bool Unequal(UnityEngine.Object __0, UnityEngine.Object __1, ref bool __result) { __result = !ReferenceEquals(__0, __1); return false; }
    static bool Amount(CondOwner __instance, string strName, ref double __result) { __result = Conditions.TryGetValue(__instance, out var flags) && flags.TryGetValue(strName, out double value) ? value : 0; return false; }
    static bool Has(CondOwner __instance, string strName, ref bool __result) { double value = 0; Amount(__instance, strName, ref value); __result = value > 0; return false; }
    static bool Add(CondOwner __instance, string strName, double fAmount) { Conditions[__instance][strName] = fAmount; return false; }
    static bool Zero(CondOwner __instance, string strName) { Conditions[__instance].Remove(strName); return false; }
    static bool Owner(string strRegID, ref string __result) { __result = Owners.TryGetValue(strRegID, out var owner) ? owner : "npc"; return false; }
    static bool Selected(ref CondOwner __result) { __result = CrewSim.coPlayer; return false; }
    static bool Universe(ref bool __result) { __result = emptyUniverse; return false; }
    static bool Moored(ref bool __result) { __result = moored; return false; }
    static bool Unmanned(ref bool __result) { __result = false; return false; }
    static bool Fuel(ref bool __result) { __result = lowFuel; return false; }
    static bool Atmo(ref bool __result) { __result = false; return false; }
    static bool Speed(ref double __result) { __result = 500 / AU; return false; }
    static bool Pois(Ship station, ref List<Ship> __result) { __result = new List<Ship> { station }; return false; }
    static bool Safe(ShipSitu __0, double __1, double __3, double __4, ref bool __result) { __0.vPosx = __3 + __1; __0.vPosy = __4; __result = true; return false; }
    static bool Command(Ostranauts.Ships.AIPilots.Interfaces.IAICharacter aiCharacter, ref ICommand __result) { var command = Empty<FlyToPatrol>(); Set(command, "_shipUs", aiCharacter.ShipUs); __result = command; return false; }
    static bool Setup(AIShip __instance) {
        created++;
        var hull = Hull("NATIVE-" + created);
        hull.objSS.vPosx = primary.objSS.vPosx; hull.objSS.vPosy = primary.objSS.vPosy;
        SetPilot(__instance, hull, __instance.HomeStation);
        Constructed.Add(__instance);
        return false;
    }
    static CondOwner CO() { var person = Empty<CondOwner>(); person.bAlive = true; Conditions[person] = new Dictionary<string, double>(); return person; }
    static Ship Hull(string id, bool station = false) {
        var ship = Empty<Ship>(); ship.strRegID = id; ship.IsAIShip = !station;
        Set(ship, "<objSS>k__BackingField", new ShipSitu(false) { bIsBO = station, strBOPORShip = station ? id : null });
        Set(ship, "<ShipCO>k__BackingField", CO());
        Set(ship, "nLoadState", Ship.Loaded.Shallow); Set(ship, "aDocked", new Dictionary<string, Ship>());
        Set(ship, "aDockingPorts", station ? new List<string> { "Primary" } : new List<string>());
        ship.fShallowRCSRemass = ship.fShallowRCSRemassMax = 1000;
        CrewSim.system.dictShips[id] = ship; Owners[id] = id == "BCRS" ? "CCRE" : id == "COHO" ? "BeltPirates" : "GalileanConfederacy";
        return ship;
    }
    static void SetPilot(AIShip ai, Ship ship, string home) {
        var pilot = Empty<NavyPilot>(); Set(pilot, "<ShipUs>k__BackingField", ship); Set(pilot, "<CaptainUs>k__BackingField", CO());
        pilot.Blackboard = new Blackboard(); pilot.PriorityQueue = new QueuedCommandsNode("PriorityQueue", pilot);
        Set(ai, "_shipUs", ship); Set(ai, "_aiCharacter", pilot); Set(ai, "<Blackboard>k__BackingField", pilot.Blackboard);
        Set(ai, "<HomeStation>k__BackingField", home);
    }
    static double[] State(BodyOrbit body) {
        var type = Policy.Assembly.GetType("Ostrabus.Orbital.NativeEphemeris");
        object[] args = { body, StarSystem.fEpoch, null, null };
        Check((bool)AccessTools.Method(type, "State").Invoke(null, args), "Fixture orbit did not resolve");
        double Number(object vector, string field) => (double)AccessTools.Field(vector.GetType(), field).GetValue(vector);
        return new[] { Number(args[2], "X"), Number(args[2], "Y"), Number(args[3], "X"), Number(args[3], "Y") };
    }
    static void Pose(Ship ship, Ship reference, double xKm = 0, double yKm = 0, double vxKmS = 0, double vyKmS = 0) {
        var state = State(CrewSim.system.aBOs[reference.strRegID]);
        ship.objSS.vPosx = state[0] + xKm / AU; ship.objSS.vPosy = state[1] + yKm / AU;
        ship.objSS.vVelX = state[2] + vxKmS / AU; ship.objSS.vVelY = state[3] + vyKmS / AU;
    }
    static void Reset() {
        created = 0; Constructed.Clear(); Conditions.Clear(); Owners.Clear(); emptyUniverse = moored = lowFuel = false;
        CrewSim.system = Empty<StarSystem>(); CrewSim.system.dictShips = new Dictionary<string, Ship>(); CrewSim.system.aBOs = new Dictionary<string, BodyOrbit>();
        AIShipManager.dictAIs = new Dictionary<string, List<AIShip>> { ["BCER"] = new List<AIShip>() }; AIShipManager.strATCLast = "BCER";
    }
    static void FreshWorld() {
        Reset();
        StarSystem.fEpoch = 65633872851.578644;
        Assets();
        primary = Hull("BCER", true); terminal = Hull("BCRS", true); haven = Hull("COHO", true);
        Conditions[primary.ShipCO]["StationInfluenceRange"] = 11500;
        Conditions[terminal.ShipCO]["StationInfluenceRange"] = 11500;
        Conditions[haven.ShipCO]["StationInfluenceRange"] = 3000;
        foreach (var station in new[] { primary, terminal, haven }) { Conditions[station.ShipCO]["StationMaxFactionNavy"] = 20; Pose(station, station); }
        AccessTools.Field(typeof(AIShipManager), "dictSpawnRegIDs").SetValue(null, new Dictionary<string, List<string>> { ["BCER"] = new List<string> { "BCER", "BCRS", "COHO" } });
        AccessTools.Field(typeof(BeatManager), "dictEventChances").SetValue(null, new Dictionary<string, float> { ["tension_factionship"] = .05f });
        AccessTools.Field(typeof(BeatManager), "fRoll").SetValue(null, 0f);
        CrewSim.coPlayer = CO(); Set(CrewSim.coPlayer, "_strID", "player"); player = Hull("PLAYER");
        CrewSim.coPlayer.ship = player; Set(player, "nLoadState", Ship.Loaded.Full); Pose(player, primary, 2000, 0, .4);
    }
    static bool Event(int severity = 4) => BeatManager.DebugFactionShip(severity);
    static void Main() {
        var boundary = new Harmony("offline.faction-staging.boundaries"); var production = new Harmony("offline.faction-staging.production");
        void Prefix(Type type, string name, string handler) { foreach (var method in AccessTools.GetDeclaredMethods(type)) if (method.Name == name) boundary.Patch(method, prefix: new HarmonyMethod(typeof(Program), handler)); }
        try {
            Prefix(typeof(UnityEngine.Object), "op_Equality", nameof(Equal)); Prefix(typeof(UnityEngine.Object), "op_Inequality", nameof(Unequal));
            foreach (var method in AccessTools.GetDeclaredMethods(typeof(CondOwner))) {
                if (method.Name == "HasCond" && method.GetParameters()[0].ParameterType == typeof(string)) boundary.Patch(method, prefix: new HarmonyMethod(typeof(Program), nameof(Has)));
                if (method.Name == "GetCondAmount" && method.GetParameters()[0].ParameterType == typeof(string)) boundary.Patch(method, prefix: new HarmonyMethod(typeof(Program), nameof(Amount)));
            }
            Prefix(typeof(CondOwner), "AddCondAmount", nameof(Add)); Prefix(typeof(CondOwner), "ZeroCondAmount", nameof(Zero));
            Prefix(typeof(CrewSim), "GetSelectedCrew", nameof(Selected)); Prefix(typeof(StarSystem), "GetShipOwner", nameof(Owner));
            boundary.Patch(AccessTools.PropertyGetter(typeof(Endgame), "IsEmptyUniverse"), prefix: new HarmonyMethod(typeof(Program), nameof(Universe)));
            boundary.Patch(AccessTools.PropertyGetter(typeof(Ship), "NavPlayerManned"), prefix: new HarmonyMethod(typeof(Program), nameof(Unmanned)));
            Prefix(typeof(Ship), "IsMooredAsteroid", nameof(Moored)); Prefix(typeof(StarSystem), "IsInAtmo", nameof(Atmo));
            Prefix(typeof(AIShipManager), "GetPOIs", nameof(Pois)); Prefix(typeof(AIShipManager), "LowFuel", nameof(Fuel));
            Prefix(typeof(NavyPilot), "MaxSpeed", nameof(Speed)); Prefix(typeof(AIShip), "SetupAI", nameof(Setup)); Prefix(typeof(AIShip), "CreateCommand", nameof(Command));
            Prefix(typeof(StarSystem), "SetSituToRandomSafeCoords", nameof(Safe)); Prefix(typeof(ShipSitu), "LockToBO", nameof(Quiet));
            FreshWorld(); Pose(player, primary, .64 * AU, 0, -1530);
            Check(Event(), "Vanilla did not reproduce distant encounter"); Check(created == 4, "Vanilla convoy size differs");
            foreach (var ai in Constructed) {
                Check(Math.Abs(ai.Ship.objSS.vVelX - player.objSS.vVelX) < 1e-20, "Vanilla did not copy cruise velocity");
                Check(Math.Abs(ai.Ship.objSS.vPosx - player.objSS.vPosx) * AU < 551, "Vanilla convoy not player-relative");
            }
            production.CreateClassProcessor(PatchType).Patch();
            FreshWorld(); Pose(player, primary, .64 * AU, 0, -1530);
            Check(!Event() && created == 0 && Constructed.Count == 0, "Distant staging created actors before refusal");
            GeometryChecks(); RecruitmentChecks();
            FreshWorld(); Check(Event() && created == 4, "Ordinary local event lost native severity");
            foreach (var ai in Constructed) Check(ai.Ship.objSS.vVelX == player.objSS.vVelX && ai.Ship.shipSituTarget != null, "Fresh hull lost native staging/goal");
            FreshWorld(); emptyUniverse = true;
            Check(!Event() && created == 0, "Empty-universe exclusion lost");
            FreshWorld(); AccessTools.Field(typeof(BeatManager), "fRoll").SetValue(null, .06f);
            Check(!(bool)AccessTools.Method(typeof(BeatManager), "FactionShip").Invoke(null, new object[] { 4d }) && created == 0,
                "Eligible location bypassed native weighted roll");
            Check(Math.Abs((float)AccessTools.Field(typeof(BeatManager), "fRoll").GetValue(null) - .01f) < 1e-7,
                "Native weighted decline consumed the wrong amount");
            FreshWorld(); moored = true;
            Check(!Event() && created == 0 && CrewSim.coPlayer.GetCondAmount("IsDueFactionSpawn") == 1,
                "Native on-task deferral lost");
            FreshWorld(); Conditions[CrewSim.coPlayer]["IsDueFactionSpawn"] = 4; Pose(player, primary, .64 * AU);
            Check(!Event() && CrewSim.coPlayer.GetCondAmount("IsDueFactionSpawn") == 4 && !CrewSim.coPlayer.HasCond("IsEnemySpawnBlocked"),
                "Distant decline consumed pending encounter or falsely blocked later staging");
            FreshWorld(); Conditions[CrewSim.coPlayer]["CeresP3Finale_Completed_Titan"] = 1;
            Check(Event() && created == 6, "Eligible postplot battle lost native six-ship composition");
            FreshWorld(); Conditions[CrewSim.coPlayer]["CeresP3Finale_Completed_Titan"] = 1; Pose(player, primary, .64 * AU);
            Check(!Event() && created == 0, "Postplot battle bypassed physical area");
            Console.WriteLine("Faction encounter: " + checks + " native staging, eligibility, geometry, cap and asset checks passed.");
        } finally { production.UnpatchSelf(); boundary.UnpatchSelf(); CrewSim.coPlayer = null; CrewSim.system = null; }
    }
}
