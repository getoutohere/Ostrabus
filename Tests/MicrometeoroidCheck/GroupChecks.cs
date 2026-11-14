using System;
using System.Collections.Generic;
using HarmonyLib;
using Ostrabus.Micrometeoroids;
using Ostranauts.Ships;
using Ostranauts.Trading;
using SolarSystem;

static partial class Program {
    const double MetresPerAU = 149597863936.0;
    static WeaponsSystem Fit(Ship hull, float rotation = 0) {
        hull.objSS.fRot = rotation;
        hull.objSS.Size = 50;
        var system = SystemFor(hull);
        Field(hull, "<WeaponsSystem>k__BackingField", system);
        groupGuns[system] = new List<Tuple<DataCOWrapper, float>>();
        return system;
    }
    static CondOwner FitGun(WeaponsSystem system, float rotation = 0, int rounds = 18) {
        var co = Gun(rotation: rotation, rounds: rounds);
        groupGuns[system].Add(gunList[gunList.Count - 1]); gunList.RemoveAt(gunList.Count - 1);
        return co;
    }
    static void Connect(params Ship[] hulls) {
        var group = new DockGroup();
        foreach (var hull in hulls) {
            group.DockedShips.Add(new DockGroupMember(hull, false));
            Field(hull.objSS, "_dockGroup", group);
        }
    }
    static MeteorTest.Result Outcome(WeaponsSystem system, float angle, params float[] random) {
        var context = AccessTools.Field(typeof(MeteorTest), "current");
        var previous = context.GetValue(null);
        var result = new MeteorTest.Result();
        try {
            context.SetValue(null, result);
            bool intercepted = Defend(system, angle, random);
            Check(result.Intercepted == intercepted, "Defense report disagrees with outcome");
            return result;
        } finally { context.SetValue(null, previous); }
    }
    static void GroupDefenseChecks() {
        int startProjectiles = projectiles, startDestroyed = destroyed;
        int oldProjectiles = projectiles, oldDestroyed = destroyed;
        var rear = Hull("REAR"); var front = Hull("FRONT");
        front.objSS.vPosy = 400 / MetresPerAU;
        var rearSystem = Fit(rear); var frontSystem = Fit(front, (float)Math.PI);
        Connect(rear, front);
        var rearFore = FitGun(rearSystem);
        // Front hull faces the opposite way; its local aft faces incoming +Y.
        var frontFore = FitGun(frontSystem, 180);
        var radar = CO("IsInstalled", "IsPowered", "IsSensorRadar", "IsSensorRadarOn"); front.aElectronicSystemCOs.Add(radar);
        Check(MeteorDefense.Group(rear).Count == 2 && MeteorDefense.Group(front).Count == 2, "Connected group depends on selected impact hull");
        var target = new ShipSitu(false) { vPosy = 5000 / MetresPerAU };
        Check(ReferenceEquals(BurstSafety.Blocker(rearSystem, rear, rearFore, Bullet(), target, MeteorDefense.Group(rear)), front), "Front connected hull failed burst interlock");
        Check(BurstSafety.Blocker(frontSystem, front, frontFore, Bullet(), target, MeteorDefense.Group(front)) == null, "Rear hull incorrectly blocked outward fire");
        var outcome = Outcome(rearSystem, 90, 0, 0);
        Check(outcome.Intercepted == true, "Peer PDC failed to defend struck rear hull");
        Check(outcome.GroupShips == 2 && outcome.DefendingShips == 2 && outcome.BlockedGuns == 1 && outcome.Bursts == 1 && outcome.Rounds == 18,
            "Group/blocked/native firing diagnostics are inaccurate");
        Check(ReferenceEquals(firedSystem, frontSystem) && Math.Abs(firedTarget.vPosy - 5000 / MetresPerAU) * MetresPerAU < .001,
            "Peer did not use its own native weapons system and the common world-space incoming target");
        Check(projectiles == oldProjectiles + 1 && destroyed == oldDestroyed + 18, "Blocked PDC fired or group shot duplicated");
        Check(!flags[rearFore].ContainsKey("IsReloading") && flags[frontFore]["IsReloading"] > 100, "Blocked weapon consumed a reload opportunity");
        flags[frontFore]["IsReloading"] = 0;
        Check(Defend(frontSystem, 90, 0, 0), "Front-hull target could not use the same safe outward gun");
        Check(!flags[rearFore].ContainsKey("IsReloading"), "Changing impact hull let obstructed gun fire");
        flags[frontFore]["IsReloading"] = 0; flags[rearFore]["IsOff"] = 1;
        Check(Defend(rearSystem, 90, 0, 0), "Disabling inward PDC prevented peer protection");
        flags[frontFore]["IsReloading"] = 0; flags[frontFore]["IsPDCTargetModeShipsOnly"] = 1;
        oldProjectiles = projectiles;
        Check(!Defend(rearSystem, 90, 0) && projectiles == oldProjectiles, "Ships-only peer incorrectly intercepted");
        flags[frontFore]["IsPDCTargetModeShipsOnly"] = 0;

        // Reverse incoming direction: the rear hull's outward aft gun protects
        // a strike on the front hull; the front hull's inward gun is barred.
        var rearAft = FitGun(rearSystem, 180); var frontAft = FitGun(frontSystem);
        Check(Defend(frontSystem, 270, 0, 0), "Mutual defense failed in opposite direction");
        Check(flags[rearAft]["IsReloading"] > 100 && !flags[frontAft].ContainsKey("IsReloading"), "Opposite inward gun was not blocked");

        // The same physical sensors/guns are pooled exactly once, independent of
        // impact hull. A high detection roll would fail one radar but passes two.
        flags[frontFore]["IsOff"] = 1; flags[rearAft]["IsOff"] = 1; flags[frontAft]["IsOff"] = 1;
        rear.aElectronicSystemCOs.Add(CO("IsInstalled", "IsPowered", "IsSensorRadar", "IsSensorRadarOn"));
        var rearDetection = Outcome(rearSystem, 90, .4f);
        var frontDetection = Outcome(frontSystem, 90, .4f);
        Check(rearDetection.Detected == true && rearDetection.Intercepted == false && rearDetection.Bursts == 0, "Remote radar did not contribute to detection");
        Check(frontDetection.Detected == true && frontDetection.Intercepted == false && frontDetection.Bursts == 0, "Detection pooling depends on target hull");
        Near(rearDetection.DetectionProbability.Value, .51, "Two radars were not pooled exactly once");
        Near(frontDetection.DetectionProbability.Value, .51, "Changing struck hull changed pooled probability");
        // There is no burst roll in either call, so these checks also ensure that
        // detected events with no ready PDCs are not automatically intercepted.
        var tip = Hull("TIP"); tip.objSS.vPosy = 800 / MetresPerAU;
        var tipSystem = Fit(tip); var tipGun = FitGun(tipSystem);
        Connect(rear, front, tip);
        flags[rearFore]["IsOff"] = 0; flags[frontFore]["IsOff"] = 0;
        flags[frontFore]["IsReloading"] = 0;
        oldProjectiles = projectiles; oldDestroyed = destroyed;
        Check(Defend(rearSystem, 90, 0, 0), "Three-hull chain did not share defense");
        Check(projectiles == oldProjectiles + 1 && destroyed == oldDestroyed + 18 && flags[tipGun]["IsReloading"] > 100,
            "Chain fired through an intermediate hull or duplicated defense");
        Check(!flags[rearFore].ContainsKey("IsReloading") && flags[frontFore]["IsReloading"] == 0, "Inner chain guns fired");
        // Repeated native membership entries must never multiply sensors/bursts.
        rear.DockGroup.DockedShips.Add(new DockGroupMember(tip, false));
        Check(MeteorDefense.Group(rear).Count == 3, "Duplicate group membership counted twice");

        // No nearby hull outside the group participates in the interlock or fire.
        var stranger = Hull("STRANGER"); stranger.objSS.vPosy = 1000 / MetresPerAU;
        Check(MeteorDefense.Group(rear).Count == 3, "Unconnected scene hull joined group defense");
        tip.bDestroyed = true; Check(MeteorDefense.Group(rear).Count == 2, "Destroyed connected hull retained"); tip.bDestroyed = false;
        tip.bRemove = true; Check(MeteorDefense.Group(rear).Count == 2, "Removing connected hull retained"); tip.bRemove = false;
        Field(tip, "nLoadState", Ship.Loaded.Shallow);
        oldProjectiles = projectiles;
        outcome = Outcome(rearSystem, 90, 0);
        Check(outcome.Intercepted == false && projectiles == oldProjectiles, "Shallow hull fired or stopped obstructing loaded peers");
        Check(outcome.GroupShips == 3 && outcome.DefendingShips == 2 && outcome.BlockedGuns == 2 && outcome.Rounds == 0, "Shallow/blocked group diagnostics are inaccurate");
        Field(tip, "nLoadState", Ship.Loaded.Full);

        // Forward-sector occupancy alone is not a threat. Continuing rounds
        // can, however, hit a friendly beyond the incoming fragment.
        var probe = Hull("PROBE"); probe.objSS.Size = 50;
        var groupList = new List<Ship> { rear, probe };
        probe.objSS.vPosx = 400 / MetresPerAU; probe.objSS.vPosy = 360 / MetresPerAU;
        Check(BurstSafety.Blocker(rearSystem, rear, rearFore, Bullet(), target, groupList) == null, "Off-path hull vetoed the entire sector");
        probe.objSS.vPosy = -400 / MetresPerAU;
        Check(BurstSafety.Blocker(rearSystem, rear, rearFore, Bullet(), target, groupList) == null, "Behind-shooter hull blocked outgoing rounds");
        probe.objSS.vPosx = 0; probe.objSS.vPosy = 6000 / MetresPerAU;
        Check(ReferenceEquals(BurstSafety.Blocker(rearSystem, rear, rearFore, Bullet(), target, groupList), probe), "Hull beyond MM escaped the continuing-round check");
        probe.objSS.vPosx = 400 / MetresPerAU; probe.objSS.vPosy = 100 / MetresPerAU;
        Check(BurstSafety.Blocker(rearSystem, rear, rearFore, Bullet(), target, groupList) == null, "Clear path was blocked");

        // Thread-static firing context still clears if a peer's native shot fails.
        flags[tipGun]["IsReloading"] = 0; throwShot = true;
        bool threw = false; try { Defend(rearSystem, 90, 0); } catch (InvalidOperationException) { threw = true; }
        Check(threw && FiringClear(), "Peer exception retained a weapon reference");
        throwShot = false;
        groupGuns.Clear();
        // Existing single-ship assertions below use these native-effect counters.
        projectiles = startProjectiles; destroyed = startDestroyed;
        firedSystem = null; firedTarget = null;
    }
}
