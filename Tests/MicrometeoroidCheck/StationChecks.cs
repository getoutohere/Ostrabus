using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;
using Ostrabus.Micrometeoroids;
using Ostrabus.ShipNavigation;

static partial class Program {
    static void Link(Ship a, Ship b) {
        Links(a)[b.strRegID] = b;
        Links(b)[a.strRegID] = a;
    }
    static Dictionary<string, Ship> Links(Ship hull) {
        var field = AccessTools.Field(typeof(Ship), "aDocked");
        var links = (Dictionary<string, Ship>)field.GetValue(hull);
        if (links == null) { links = new Dictionary<string, Ship>(); field.SetValue(hull, links); }
        return links;
    }
    static StarSystem StationWorld(params Ship[] hulls) {
        var world = Empty<StarSystem>();
        world.dictShips = new Dictionary<string, Ship>();
        foreach (var hull in hulls) world.dictShips.Add(hull.strRegID, hull);
        return world;
    }
    static void StationSelectionChecks() {
        var previousWorld = CrewSim.system;
        var previousFlag = StationDockedCollisions.Flag;
        try {
            var station = Hull("STATION", 1000, 1000); station.objSS.bIsBO = true;
            var visitor = Hull("VISITOR"); var tail = Hull("TAIL"); var free = Hull("FREE");
            CrewSim.system = StationWorld(station, visitor, tail, free);
            Link(station, visitor); Link(visitor, tail);
            Check(MeteorTargets.Weight(station) == 0, "Station entered MM target pool");
            Check(MeteorTargets.Weight(visitor) == 0, "Station visitor entered MM target pool");
            Check(MeteorTargets.Weight(tail) == 0, "Indirect station docking chain entered MM target pool");
            StationDockedCollisions.Flag = Empty<ConfigEntry<bool>>();
            Field(StationDockedCollisions.Flag, "_typedValue", false);
            Check(MeteorTargets.Weight(tail) == 0, "Collision toggle disabled station MM immunity");
            var scene = new[] { station, visitor, tail, free };
            for (int i = 0; i < 100; i++)
                Check(ReferenceEquals(MeteorTargets.Select(scene, tail, i / 100d), free), "Protected hull won weighted selection");
            Check(MeteorTargets.Select(null, station, .5) == null, "Station escaped immunity through fallback");
            Check(MeteorTargets.Select(new[] { station, visitor, tail }, tail, .5) == null, "Station chain escaped through empty-pool fallback");
            // A shallow intermediate still connects the materialized outer hull.
            Field(visitor, "nLoadState", Ship.Loaded.Shallow);
            Check(MeteorTargets.Weight(tail) == 0, "Shallow intermediate broke station-chain immunity");
            Field(visitor, "nLoadState", Ship.Loaded.Full);
            Link(tail, station);
            Check(MeteorTargets.Weight(tail) == 0, "Cyclic station docking graph lost immunity");
            Links(tail).Remove(station.strRegID); Links(station).Remove(tail.strRegID);
            // Native permanent identities count without relying on orbit-lock flags.
            station.objSS.bIsBO = false; Field(station, "_subStation", true);
            Check(MeteorTargets.Weight(station) == 0 && MeteorTargets.Weight(tail) == 0, "Substation or attached hull lost immunity");
            Field(station, "_subStation", false); station.Classification = Ship.TypeClassification.GroundStation;
            Check(MeteorTargets.Weight(station) == 0 && MeteorTargets.Weight(tail) == 0, "Ground station or attached hull lost immunity");
            station.Classification = Ship.TypeClassification.Skyscraper;
            Check(MeteorTargets.Weight(tail) == 0, "Skyscraper docking chain lost immunity");
            station.Classification = Ship.TypeClassification.Default; station.objSS.bIsBO = true;
            Links(station).Remove(visitor.strRegID); Links(visitor).Remove(station.strRegID);
            Check(MeteorTargets.Weight(visitor) > 0 && MeteorTargets.Weight(tail) > 0, "Detached free group remained immune");
            Connect(station, visitor, tail); // Stale physics membership alone is not a docking connection.
            Check(MeteorTargets.Weight(tail) > 0, "Stale DockGroup incorrectly granted station immunity");
            visitor.objSS.bBOLocked = true;
            Check(MeteorTargets.Weight(visitor) > 0, "Free body-locked wreck was mistaken for a station visitor");
            visitor.objSS.bBOLocked = false;
            // A one-way or replaced-hull link must not extend immunity.
            Links(tail)[station.strRegID] = station;
            Check(MeteorTargets.Weight(tail) > 0, "One-way stale link granted immunity");
            Links(station)[tail.strRegID] = tail;
            CrewSim.system.dictShips[station.strRegID] = Hull(station.strRegID);
            Check(MeteorTargets.Weight(tail) > 0, "Replaced station object granted immunity");
            CrewSim.system.dictShips[station.strRegID] = station;
            // Shallow save-backed links must work without loading their interiors.
            foreach (var hull in new[] { station, visitor, tail }) Field(hull, "aDocked", null);
            station.json = new JsonShip { aDocked = new Dictionary<string, string> { ["s"] = visitor.strRegID } };
            visitor.json = new JsonShip { aDocked = new Dictionary<string, string> { ["s"] = station.strRegID, ["t"] = tail.strRegID } };
            tail.json = new JsonShip { aDocked = new Dictionary<string, string> { ["t"] = visitor.strRegID } };
            Check(MeteorTargets.Weight(tail) == 0, "Save-backed station docking chain lost immunity");
            visitor.json.aDocked.Remove("s"); station.json.aDocked.Clear();
            Check(MeteorTargets.Weight(tail) > 0, "Save-backed detached group retained immunity");
            free.objSS.bIsBO = true; free.Classification = Ship.TypeClassification.Asteroid;
            Check(MeteorTargets.Weight(free) > 0, "Asteroid identity was mistaken for station shelter");
        } finally { CrewSim.system = previousWorld; StationDockedCollisions.Flag = previousFlag; }
    }
    static void StationBeatChecks(MethodInfo beat) {
        var previousWorld = CrewSim.system;
        var previousPlayerShip = CrewSim.coPlayer.ship;
        var loaded = AccessTools.Field(typeof(CrewSim), "aLoadedShips");
        var previousLoaded = loaded.GetValue(null);
        var roll = AccessTools.Field(typeof(BeatManager), "fRoll");
        try {
            var station = Hull("BM-STATION", 1000, 1000); station.objSS.bIsBO = true;
            var visitor = Hull("BM-VISITOR"); var tail = Hull("BM-TAIL"); var free = Hull("BM-FREE");
            CrewSim.system = StationWorld(station, visitor, tail, free);
            Link(station, visitor); Link(visitor, tail);
            loaded.SetValue(null, new List<Ship> { station, visitor, tail, free });
            Field(CrewSim.coPlayer, "_objShip", free);
            roll.SetValue(null, 0f); rolls.Enqueue(0f); spawned = logged = null;
            Check((bool)beat.Invoke(null, null) && ReferenceEquals(spawned, free) && ReferenceEquals(logged, free),
                "Actual BeatManager selected or logged a protected hull");
            // Even stale native cached eligibility cannot reintroduce a protected fallback.
            loaded.SetValue(null, new List<Ship> { station, visitor, tail });
            Field(CrewSim.coPlayer, "_objShip", tail);
            roll.SetValue(null, 0f); rolls.Enqueue(.5f); spawned = logged = null;
            Check(!(bool)beat.Invoke(null, null) && spawned == null && logged == null,
                "All-protected event spawned damage, wrote a false impact, or claimed success");
            Near((float)roll.GetValue(null), -1, "Empty pool did not use native rejection-roll accounting");
            Check(rolls.Count == 0, "Empty-pool event consumed unexpected randomness");
        } finally {
            loaded.SetValue(null, previousLoaded); CrewSim.system = previousWorld;
            Field(CrewSim.coPlayer, "_objShip", previousPlayerShip);
        }
    }
}
