using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using Ostrabus.Inspection;
using Ostrabus.Orbital;
using Ostranauts.Ships.AIPilots;

static class Program {
    static int checks;
    static Action<string, string, UnityEngine.LogType> logCallback;
    static T Empty<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    static void Check(bool ok, string message) { checks++; if (!ok) throw new Exception(message); }
    static JObject Read(string id = null) => JObject.Parse(SnapshotJson.Encode(InspectionEventHistory.Read(0, 50, id, "ai")));
    static Ship Hull(string id, int people = 1) {
        var ship = Empty<Ship>(); ship.strRegID = id; ship.publicName = "fixture hull";
        AccessTools.Field(typeof(Ship), "<objSS>k__BackingField").SetValue(ship, new ShipSitu(false) { vPosx = 1.2, vVelY = 3e-8 });
        AccessTools.Field(typeof(Ship), "aPeople").SetValue(ship, Enumerable.Range(0, people).Select(i => {
            var person = Empty<PersonSpec>(); person.strCO = "crew-" + i; person.bAlive = true; return person;
        }).ToList());
        CrewSim.system.dictShips[id] = ship;
        return ship;
    }
    static void RemovalCaller(string message) => logCallback(message, "", UnityEngine.LogType.Log);
    static int Main() {
        CrewSim.system = Empty<StarSystem>(); CrewSim.system.dictShips = new Dictionary<string, Ship>();
        CrewSim.system.aBOs = new Dictionary<string, BodyOrbit>();
        var body = Empty<BodyOrbit>(); body.strName = "fixture-body"; body.dXReal = 1.2; body.fRadius = .01;
        body.dTimeCalcLast = 12345; CrewSim.system.aBOs[body.strName] = body;
        AccessTools.Field(typeof(StarSystem), "dictShipOwners").SetValue(CrewSim.system, new Dictionary<string,string>());
        AIShipManager.dictAIs = new Dictionary<string,List<AIShip>>(); DataHandler.mapCOs = new Dictionary<string,CondOwner>();
        StarSystem.fEpoch = 12345;
        var listener = Empty<InspectionLogHooks>();
        AccessTools.Field(typeof(InspectionLogHooks), "mainThread").SetValue(listener, Thread.CurrentThread.ManagedThreadId);
        logCallback = AccessTools.MethodDelegate<Action<string, string, UnityEngine.LogType>>(
            AccessTools.Method(typeof(InspectionLogHooks), "UnityLog"), listener);
        InspectionEventHistory.Reset();
        var ship = Hull("newborn");
        RemovalCaller("Destroying ship newborn.");
        Check(Read()["items"].Count() == 0, "Normal-time destruction started retaining jump evidence.");
        using (new JumpBoundary("pass")) {
            var worker = new Thread(() => RemovalCaller("Destroying ship newborn.")); worker.Start(); worker.Join();
            Check(Read()["items"].Count() == 0, "Worker log accessed live game state or generated main-thread evidence.");
            logCallback("Destroying ship newborn.", "", UnityEngine.LogType.Warning);
            Check(Read()["items"].Count() == 0, "A warning was mistaken for native destruction.");
            RemovalCaller("Unrelated log"); RemovalCaller("Destroying ship absent.");
            var empty = Hull("crewless", 0); RemovalCaller("Destroying ship crewless.");
            Check(Read()["items"].Count() == 0, "Unrelated, absent or crewless actor generated evidence.");
            var motion = ship.objSS;
            RemovalCaller("Destroying ship newborn.");
            Check(!ship.bDestroyed && !ship.bRemove && ReferenceEquals(ship.objSS, motion) && ship.People.Count == 1,
                "Observation changed native state.");
            // The actor exists entirely between ordinary frame samples. Its
            // detached destruction record must survive registry/crew teardown.
            ship.bDestroyed = true; ship.People.Clear(); CrewSim.system.dictShips.Remove("newborn");
            var row = Read("newborn")["items"].Single(); var data = row["data"];
            Check((string)row["shipId"] == "newborn" && (double)row["gameEpoch"] == 12345, "Missing jump/actor identity.");
            Check((string)data["source"] == "pass" && (string)data["crew"][0]["id"] == "crew-0" && (bool)data["crew"][0]["alive"],
                "Newborn actor identity/crew were not retained independently of live state.");
            Check((double)data["motion"]["xAU"] == 1.2 && (double)data["motion"]["vyAUPerSecond"] == 3e-8, "Motion evidence changed.");
            Check(((string)data["stack"]).Contains(nameof(RemovalCaller)), "Destruction caller was not retained.");
            Check((string)data["nearestStoredBodySurfaces"][0]["id"] == "fixture-body" &&
                (double)data["nearestStoredBodySurfaces"][0]["centerToSurfaceGapKm"] < 0 && body.dTimeCalcLast == 12345,
                "Body penetration evidence was missed or the ephemeris was advanced by observation.");
            var many = Hull("many", 40); RemovalCaller("Destroying ship many.");
            var bounded = Read("many")["items"].Single()["data"];
            Check((int)bounded["crewCount"] == 40 && (bool)bounded["crewTruncated"] && bounded["crew"].Count() == 32,
                "Crew evidence is not bounded.");
            var ai = Empty<AIShip>(); AccessTools.Field(typeof(AIShip), "_shipUs").SetValue(ai, many);
            AccessTools.Field(typeof(AIShip), "_aiCharacter").SetValue(ai, Empty<ScavPilot>());
            AIShipManager.dictAIs["region"] = new List<AIShip> { ai };
            RemovalCaller("Destroying ship many.");
            Check(Read("many")["items"].Count() == 2, "Partially constructed controller prevented capture.");
            for (int i = 0; i < 520; i++) RemovalCaller("Destroying ship many.");
            var full = Read("many");
            Check((long)full["overwritten"] > 0 && full["items"].Count() == 50 && (long)full["latestSequence"] - (long)full["oldestAvailableSequence"] == 511,
                "Existing ring capacity changed or capture grew without bounds.");
        }
        Check(!JumpBoundary.Active, "Capture leaked jump scope.");
        Console.WriteLine(checks + " newborn destruction capture/identity/nonmutation/caller/bounds checks passed.");
        return 0;
    }
}
namespace Ostrabus.Inspection {
    internal static class InspectionApi {
        internal sealed class Reply {
            internal Reply(int status, object body) {}
        }
    }
    // Not used by destruction capture. Fail if unrelated combat serialization runs.
    internal static class DockingInspection {
        internal static object ShipState(Ship ship, Ship other) => throw new InvalidOperationException("Unrelated docking snapshot was evaluated.");
    }
}
