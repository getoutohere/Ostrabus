using System;
using System.Collections.Generic;
using HarmonyLib;
using Ostrabus.Inspection;

static partial class Program {
    static int detaches, towReleases, moorReleases;
    static bool failDetach, throwDetach, changeOwner, protectCrew, leaveReverse;
    static Dictionary<string,Ship> Links(Ship ship) => (Dictionary<string,Ship>)AccessTools.Field(typeof(Ship), "aDocked").GetValue(ship);
    static void Attach(Ship a, Ship b, bool mooring = false) {
        Links(a).Add((mooring ? "MP|" : "DP|") + b.strRegID, b); Links(b).Add((mooring ? "MP|" : "DP|") + a.strRegID, a);
    }
    static bool TowRelease() { towReleases++; return false; }
    static bool Detach(Ship __instance, Ship __0) {
        detaches++;
        if (throwDetach) throw new InvalidOperationException("fixture detachment failure");
        if (failDetach) return false;
        Links(__instance).Clear(); if (!leaveReverse) Links(__0).Clear();
        __instance.objSS._dockGroup = null; __0.objSS._dockGroup = null;
        if (changeOwner) ((Dictionary<string,string>)AccessTools.Field(typeof(StarSystem), "dictShipOwners").GetValue(world))[__instance.strRegID] = "player";
        if (protectCrew) foreach (var co in crews[__instance.strRegID]) allowed.Remove(co.strID);
        return false;
    }
    static bool Unmoor(Ship __instance, Ship __0) { moorReleases++; return Detach(__instance, __0); }
    static void ForceRefused(Ship ship, string reason) {
        int before = hulls + people + homeRemovals + visibility + detaches + towReleases;
        var preview = ShipPruning.Run(ship.strRegID, true, null, true);
        Check(preview.Status == 200 && !(bool)Body(preview)["eligible"], "Forced preview missed refusal: " + reason);
        Check(ShipPruning.Run(ship.strRegID, false, "PRUNE", true).Status == 409, "Forced apply missed refusal: " + reason);
        Check(hulls + people + homeRemovals + visibility + detaches + towReleases == before, "Forced refusal mutated state: " + reason);
    }
    static void CheckForce(Harmony h) {
        Patch(h, typeof(Ship), nameof(Ship.Undock), nameof(Detach));
        Patch(h, typeof(Ship), nameof(Ship.UnMoorFrom), nameof(Unmoor));
        Patch(h, typeof(Ship), nameof(Ship.ToggleTowBracesShallow), nameof(TowRelease));
        var owners = (Dictionary<string,string>)AccessTools.Field(typeof(StarSystem), "dictShipOwners").GetValue(world);
        var target = Make("FORCE", 1); var peer = Make("PEER", 1); Attach(target, peer);
        int before = detaches + towReleases + hulls + people;
        var dry = ShipPruning.Run(target.strRegID, true, null, true);
        Check((bool)Body(dry)["eligible"] && ((List<string>)Body(dry)["detachFrom"]).Contains(peer.strRegID), "Force preview missing native detach plan");
        Check(detaches + towReleases + hulls + people == before && target.IsDocked() && peer.IsDocked(), "Force preview changed group");
        Check(ShipPruning.Run(target.strRegID, false, null, true).Status == 400 && detaches + towReleases + hulls + people == before, "Force bypassed confirmation");
        owners[target.strRegID] = "player"; ForceRefused(target, "target player ownership"); owners.Clear();
        owners[peer.strRegID] = "player"; ForceRefused(target, "connected player ownership"); owners.Clear();
        CrewSim.coPlayer.ship = peer; ForceRefused(target, "connected player location"); CrewSim.coPlayer.ship = null;
        plotShips.Add(peer.strRegID); ForceRefused(target, "connected plot ship"); plotShips.Clear();
        plotShips.Add(target.strRegID); ForceRefused(target, "target plot ship"); plotShips.Clear();
        peer.objSS.bIsBO = true; ForceRefused(target, "connected station"); peer.objSS.bIsBO = false;
        allowed.Remove("FORCE-0"); ForceRefused(target, "protected target crew"); allowed.Add("FORCE-0");
        AccessTools.Field(typeof(Ship), "nLoadState").SetValue(peer, Ship.Loaded.Full); ForceRefused(target, "loaded peer");
        AccessTools.Field(typeof(Ship), "nLoadState").SetValue(peer, Ship.Loaded.Shallow);
        var indirect = Make("INDIRECT"); Attach(peer, indirect); owners[indirect.strRegID] = "player"; ForceRefused(target, "indirect player connection"); owners.Clear();
        Attach(target, indirect); ForceRefused(target, "cycle unsafe for native recursive cache walk"); Links(target).Remove("DP|INDIRECT"); Links(indirect).Remove("DP|FORCE");
        Links(peer).Remove("DP|INDIRECT"); Links(indirect).Clear();
        Links(target).Add("SECOND-PORT", peer); Links(peer).Add("SECOND-PORT", target);
        ForceRefused(target, "duplicate connection unsafe for native recursive cache walk"); Links(target).Remove("SECOND-PORT"); Links(peer).Remove("SECOND-PORT");
        Links(peer).Clear(); ForceRefused(target, "nonreciprocal links"); Links(peer).Add("DP|FORCE", target);
        var group = new SolarSystem.DockGroup(); group.DockedShips.Add(new SolarSystem.DockGroupMember(target, false));
        group.DockedShips.Add(new SolarSystem.DockGroupMember(indirect, false)); target.objSS._dockGroup = group;
        ForceRefused(target, "unresolved group member"); target.objSS._dockGroup = null;
        var stale = Make("STALE-GROUP"); stale.objSS._dockGroup = new SolarSystem.DockGroup(); ForceRefused(stale, "group without direct links");
        var limited = Make("LIMIT-0"); var tail = limited;
        for (int i = 1; i < 33; i++) { var next = Make("LIMIT-" + i); Attach(tail, next); tail = next; }
        ForceRefused(limited, "oversized graph");

        failDetach = true; before = hulls + people + homeRemovals;
        var failure = ShipPruning.Run(target.strRegID, false, "PRUNE", true);
        Check(failure.Status == 409 && hulls + people + homeRemovals == before && target.IsDocked(), "Incomplete detachment deleted hull/crew"); failDetach = false;
        throwDetach = true; failure = ShipPruning.Run(target.strRegID, false, "PRUNE", true);
        Check(failure.Status == 409 && hulls + people + homeRemovals == before, "Native detach exception escaped or deleted crew"); throwDetach = false;
        leaveReverse = true; failure = ShipPruning.Run(target.strRegID, false, "PRUNE", true);
        Check(failure.Status == 409 && hulls + people + homeRemovals == before && world.dictShips.ContainsKey(target.strRegID), "Dangling reciprocal reference allowed deletion");
        leaveReverse = false; Links(target).Clear(); Links(peer).Clear(); Attach(target, peer);
        changeOwner = true; failure = ShipPruning.Run(target.strRegID, false, "PRUNE", true);
        Check(failure.Status == 409 && hulls + people + homeRemovals == before && !target.IsDocked(), "Ownership was not rechecked after detachment callback"); changeOwner = false; owners.Clear(); Attach(target, peer);
        protectCrew = true; failure = ShipPruning.Run(target.strRegID, false, "PRUNE", true);
        Check(failure.Status == 409 && hulls + people + homeRemovals == before, "Crew protection was not rechecked after detachment callback"); protectCrew = false; allowed.Add("FORCE-0"); Attach(target, peer);
        int crewBefore = people, hullBefore = hulls;
        var result = ShipPruning.Run(target.strRegID, false, "PRUNE", true);
        Check(result.Status == 200 && (bool)Body(result)["removed"] && hulls == hullBefore + 1 && people == crewBefore + 1, "Force did not prune exactly the requested hull/crew");
        Check(world.dictShips.ContainsKey(peer.strRegID) && crews[peer.strRegID].Count == 1 && !peer.IsDocked(), "Force pruned or left a reference on connected peer");
        Check(((List<string>)Body(result)["detachedFrom"]).Contains(peer.strRegID) && (bool)Body(result)["force"], "Force result missing detach outcome");
        var moored = Make("MOORED"); var asteroidTow = Make("MOOR-PEER"); Attach(moored, asteroidTow, true); int unmoors = moorReleases;
        Check(ShipPruning.Run(moored.strRegID, false, "PRUNE", true).Status == 200 && moorReleases == unmoors + 1 && world.dictShips.ContainsKey(asteroidTow.strRegID), "Mooring was not dispatched through native UnMoorFrom");
        CheckNativeDetach(h);
    }
    static bool NoPhysics() => false;
    static bool NativeText(ref string __result) { __result = "fixture"; return false; }
    static bool NativeMass(ref double __result) { __result = 100; return false; }
    static void CheckNativeDetach(Harmony h) {
        h.Unpatch(AccessTools.Method(typeof(Ship), nameof(Ship.Undock)), HarmonyPatchType.Prefix, h.Id);
        h.Unpatch(AccessTools.Method(typeof(Ship), nameof(Ship.ToggleTowBracesShallow)), HarmonyPatchType.Prefix, h.Id);
        Patch(h, typeof(StarSystem), nameof(StarSystem.GetNearestBO), nameof(NoPhysics));
        Patch(h, typeof(StarSystem), nameof(StarSystem.GetGreatestGravBO), nameof(NoPhysics));
        Patch(h, typeof(CrewSim), nameof(CrewSim.GetSelectedCrew), nameof(NoPhysics));
        Patch(h, typeof(ShipSitu), nameof(ShipSitu.UpdateTime), nameof(NoPhysics));
        Patch(h, typeof(Ship), nameof(Ship.LogAdd), nameof(NoPhysics));
        Patch(h, typeof(Ship), "get_Mass", nameof(NativeMass));
        Patch(h, typeof(DataHandler), nameof(DataHandler.GetString), nameof(NativeText));
        var target = Make("NATIVE", 1); var peer = Make("NATIVE-PEER"); Attach(target, peer);
        var group = new SolarSystem.DockGroup(); group.DockedShips.Add(new SolarSystem.DockGroupMember(target, false)); group.DockedShips.Add(new SolarSystem.DockGroupMember(peer, false));
        target.objSS._dockGroup = peer.objSS._dockGroup = group;
        int before = hulls;
        var result = ShipPruning.Run(target.strRegID, false, "PRUNE", true);
        Check(result.Status == 200 && hulls == before + 1, "Actual native Undock/group teardown failed: " + (Body(result).TryGetValue("reason", out var why) ? why : result.Status));
        Check(!target.HasDockGroup && !peer.HasDockGroup && !peer.IsDocked() && group.DockedShips.Count == 0, "Actual native detachment retained group/reciprocal references");
        Check(world.dictShips.ContainsKey(peer.strRegID), "Native-detach force removed peer hull");
        var leaf = Make("NATIVE-LEAF"); var middle = Make("NATIVE-MIDDLE", 1); var far = Make("NATIVE-FAR", 1);
        Attach(leaf, middle); Attach(middle, far);
        var chain = new SolarSystem.DockGroup();
        foreach (var hull in new[] { leaf, middle, far }) { chain.DockedShips.Add(new SolarSystem.DockGroupMember(hull, false)); hull.objSS._dockGroup = chain; }
        result = ShipPruning.Run(leaf.strRegID, false, "PRUNE", true);
        Check(result.Status == 200, "Native chain detachment failed: " + (Body(result).TryGetValue("reason", out why) ? why : result.Status));
        Check(Links(middle).ContainsValue(far) && Links(far).ContainsValue(middle) && !Links(middle).ContainsValue(leaf), "Native detachment broke surviving peer links");
        Check(middle.HasDockGroup && far.HasDockGroup && ReferenceEquals(middle.DockGroup, far.DockGroup) &&
            middle.DockGroup.DockedShips.Count == 2 && !middle.DockGroup.DockedShips.Exists(m => m.RegId == leaf.strRegID), "Native detachment retained deleted hull in surviving group");
        Check(crews[middle.strRegID].Count == 1 && crews[far.strRegID].Count == 1, "Forced leaf prune deleted surviving group crew");
        Console.WriteLine("Native Ship.Undock and DockGroup.UndockShip execute in the forced pair case; gravity/ephemeris, mass and presentation are fixture boundaries. Prune destruction/crew eligibility remain explicit test doubles.");
    }
}
