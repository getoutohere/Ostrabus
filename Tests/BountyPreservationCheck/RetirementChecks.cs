using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Ostrabus.ShipNavigation;

static partial class Program {
    static bool populationFixture;
    static readonly JsonFaction homeFaction = new JsonFaction { strName = "EJDR" };
    static readonly JsonFaction otherFaction = new JsonFaction { strName = "VNCA" };
    static Ship Retiring(string id = "RETIRED", int crew = 1) {
        var ship = Hull(id); ship.bRemove = true; Cond(ship.ShipCO, "IsStale", 80000);
        Set(ship, "aFactions", new List<JsonFaction> { homeFaction });
        for (int i = 0; i < crew; i++) { var co = Actor(ship, id + "-" + i); Cond(co, "IsHuman"); }
        return ship;
    }
    static Ship Residents(string id, int population, int capacity, bool friendly = true) {
        var ship = Hull(id, true); Cond(ship.ShipCO, "StationMaxPopulation", capacity);
        Set(ship, "aFactions", new List<JsonFaction> { friendly ? homeFaction : otherFaction });
        for (int i = 0; i < population; i++) { var co = Actor(ship, id + "-" + i); Cond(co, "IsHuman"); }
        return ship;
    }
    static bool Recycle(Ship ship) => (bool)AccessTools.Method(typeof(StarSystem), "TryRecycleCrew").Invoke(CrewSim.system, new object[] { ship });
    static bool PopulationCOs(Ship __instance, CondTrigger __0, ref List<CondOwner> __result) {
        if (!populationFixture) return true;
        // Scene-container enumeration is a boundary. Use the native trigger on
        // the real crew records, not a hardcoded prunability decision.
        __result = __instance.GetPeople(__0, false); return false;
    }
    static void RetirementCases(Harmony boundary, Harmony production) {
        int before = checks;
        Reset(); var full = Residents("FULL", 4, 4); var source = Retiring(); var person = source.GetPeople(false).Single();
        Check(!Recycle(source) && ReferenceEquals(person.ship, source) && caughtUp == 0,
            "Unpatched full-station control no longer reproduces failed crew recycling.");
        production.CreateClassProcessor(typeof(CrewRetirementPatch)).Patch();
        Check(Recycle(source) && ReferenceEquals(person.ship, full) && full.Population == 5 && source.Population == 0,
            "Retired crew did not use native transfer into a full compatible station.");
        Check(person.bAlive && !person.bDestroyed && person.OwnsShip(full.strRegID) && !person.OwnsShip(source.strRegID) && caughtUp == 1,
            "Overflow changed life, ownership or native catch-up.");
        Check(Triggers["TIsNPCPrunable"].Triggered(person), "Returned pilot became immune to normal population management.");
        source.Destroy(false);
        Check(source.bDestroyed && person.bAlive && ReferenceEquals(person.ship, full), "Empty hull retirement affected the returned pilot.");

        Reset(); full = Residents("FULL", 4, 4); var room = Residents("ROOM", 3, 5); source = Retiring();
        Check(Recycle(source) && room.Population == 4 && full.Population == 4, "Overflow displaced an available native station choice.");
        Reset(); var first = Residents("FIRST", 1, 5); var second = Residents("SECOND", 0, 5); source = Retiring();
        Check(Recycle(source) && first.Population == 2 && second.Population == 0, "Normal native station order changed.");
        Reset(); var crowded = Residents("CROWDED", 6, 4); var foreign = Residents("FOREIGN", 0, 4, false);
        var least = Residents("LEAST", 4, 4); var tie = Residents("TIE", 4, 4); source = Retiring(crew: 2);
        var originalPeople = source.GetPeople(false).ToArray();
        Check(Recycle(source) && least.Population == 6 && crowded.Population == 6 && foreign.Population == 0 && tie.Population == 4,
            "Fallback did not retain exact faction matching and stable least-excess selection.");
        Check(originalPeople.All(p => p.bAlive && ReferenceEquals(p.ship, least) && least.People.Contains(p.pspec)) && caughtUp == 2,
            "Multi-person handoff lost an identity or native catch-up.");

        foreach (string state in new[] { "no-retirement", "no-timer", "young-timer", "station", "docked", "full-source" }) {
            Reset(); full = Residents("FULL", 4, 4); source = Retiring();
            if (state == "no-retirement") source.bRemove = false;
            if (state == "no-timer") Cond(source.ShipCO, "IsStale", 0);
            if (state == "young-timer") Cond(source.ShipCO, "IsStale", StarSystem.fEpoch - 10);
            if (state == "station") source.objSS.bIsBO = true;
            if (state == "docked") ((Dictionary<string, Ship>)AccessTools.Field(typeof(Ship), "aDocked").GetValue(source))[full.strRegID] = full;
            if (state == "full-source") Set(source, "nLoadState", Ship.Loaded.Full);
            Check(!Recycle(source) && source.Population == 1 && full.Population == 4, "Overflow affected another disposal context: " + state);
        }
        foreach (string state in new[] { "zero-capacity", "hidden", "no-ports", "full-scene", "removing", "foreign" }) {
            Reset(); full = Residents("FULL", 4, 4, state != "foreign"); source = Retiring();
            if (state == "zero-capacity") Cond(full.ShipCO, "StationMaxPopulation", 0);
            if (state == "hidden") full.HideFromSystem = true;
            if (state == "no-ports") Set(full, "aDockingPorts", new List<string>());
            if (state == "full-scene") Set(full, "nLoadState", Ship.Loaded.Full);
            if (state == "removing") full.bRemove = true;
            Check(!Recycle(source) && source.Population == 1, "Unsafe or unmanaged overflow destination accepted: " + state);
        }
        Reset(); source = Retiring();
        Check(!Recycle(source), "Missing stations created a parallel holding pool.");

        Reset(); full = Residents("FULL", 6, 4); source = Retiring();
        var reservedHull = Hull("BOUNTY-HULL"); var target = Actor(full, "Protected"); Cond(target, "IsHuman"); Contract(target, reservedHull);
        Check(Recycle(source) && full.Population == 8, "Population-control setup failed.");
        boundary.Patch(AccessTools.Method(typeof(Ship), nameof(Ship.GetCOs)), prefix: new HarmonyMethod(typeof(Program), nameof(PopulationCOs)));
        populationFixture = true;
        try {
            AIShipManager.fTimeOfNextPassShip = 1;
            AccessTools.Method(typeof(AIShipManager), "SpawnPassShip").Invoke(null, null);
            Check(full.Population == 1 && personDestroys == 7 && !target.bDestroyed,
                "Native population reduction failed or removed a protected bounty target.");
            Check(AIShipManager.fTimeOfNextPassShip > StarSystem.fEpoch && !BountyPreservation.StaleCandidates(new[] { reservedHull }).Contains(reservedHull),
                "Native population schedule or existing bounty-hull protection changed.");
        } finally { populationFixture = false; }
        try { CrewRetirementPatch.Transpiler(Array.Empty<CodeInstruction>()).ToArray(); throw new Exception("Accepted changed recycling IL"); }
        catch (InvalidOperationException) { checks++; }
        Console.WriteLine("Crew retirement: " + (checks - before) + " native baseline/repair/population checks passed.");
    }
}
