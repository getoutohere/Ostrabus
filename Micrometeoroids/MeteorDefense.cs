using System;
using System.Collections.Generic;
using HarmonyLib;
using Ostranauts.Utils.Models;
using Ostranauts.Ships;
using UnityEngine;

namespace Ostrabus.Micrometeoroids;

internal static class MeteorDefense {
    const double MetresPerAU = 149597863936.0;
    static readonly AccessTools.FieldRef<WeaponsSystem, Ship> Host = AccessTools.FieldRefAccess<WeaponsSystem, Ship>("_ship");
    struct Firing {
        internal CondOwner Weapon;
        internal WeaponsSystem System;
        internal Ship Ship, Blocker;
        internal ShipSitu Target;
        internal IReadOnlyList<Ship> Group;
        internal int Rounds;
    }
    [ThreadStatic] static Firing firing;
    readonly struct Gun {
        internal readonly Ship Ship;
        internal readonly WeaponsSystem Weapons;
        internal readonly CondOwner CO;
        internal readonly float Rotation;
        internal Gun(Ship ship, WeaponsSystem weapons, CondOwner co, float rotation) {
            Ship = ship; Weapons = weapons; CO = co; Rotation = rotation;
        }
    }

    internal static double SensorContribution(double speed, double range) =>
        speed > 0 && range > 0 ? Math.Min(1, .4 * Math.Sqrt(speed / 30000 * (range / 12000))) : 0;
    internal static double BurstChance(int rounds, double speed) =>
        rounds > 0 && speed > 0 ? 1 - Math.Pow(.15, rounds / 18.0 * (speed / 30000)) : 0;
    internal static bool Operational(CondOwner co) => co != null && !co.bDestroyed && co.HasCond("IsInstalled") &&
        co.HasCond("IsPowered") && !co.HasCond("IsOff") && !co.HasCond("IsDamaged");
    internal static double SensorMiss(CondOwner co) {
        if (!Operational(co)) return 1;
        double miss = 1;
        if (co.HasCond("IsSensorRadar") && co.HasCond("IsSensorRadarOn")) miss *= .70;
        if (co.HasCond("IsSensorLidar") && co.HasCond("IsSensorLidarOn")) miss *= .55;
        return miss;
    }
    internal static bool Covers(WeaponsSystem weapons, Ship ship, float rotation, ShipSitu target, double arc, double range) {
        float bearing = weapons.GetItemsDefaultFiringAngle(rotation) + Mathf.PI / 2;
        return WeaponsSystem.IsPointInView(ship.objSS.vPos, new Point(Math.Cos(bearing), Math.Sin(bearing)), target.vPos, range, arc);
    }
    internal static List<CondOwner> CaptureAmmo(List<CondOwner> ammo, CondOwner weapon) {
        if (!ReferenceEquals(weapon, firing.Weapon)) return ammo;
        firing.Rounds = 0;
        if (ammo != null && ammo.Count > 0 && ammo[0] != null) {
            firing.Blocker = BurstSafety.Blocker(firing.System, firing.Ship, weapon, ammo[0], firing.Target, firing.Group);
            if (firing.Blocker != null) {
                string reason = ReferenceEquals(firing.Blocker, firing.Ship) ? "ballistic path could not be validated" : $"native projectile paths may hit connected hull {firing.Blocker.strRegID}";
                OstrabusPlugin.Logger?.LogInfo($"[Micrometeoroids] {firing.Ship.strRegID}/{weapon.strID}: burst withheld; {reason}.");
                return null;
            }
            firing.Rounds = ammo.Count;
        }
        return ammo;
    }
    internal static List<Ship> Group(Ship ship) {
        var group = new List<Ship> { ship };
        void Add(Ship other) {
            if (other != null && !other.bDestroyed && !other.bRemove && other.objSS != null &&
                !(other is AsyncRatingShip) && !(other is AsyncPreviewShip) && !(other is BarterZoneShip) && !group.Contains(other)) group.Add(other);
        }
        // Native group membership handles chains without recursively traversing
        // docking links (which can contain cycles with multiple braces).
        if (ship.DockGroup != null) foreach (var member in ship.DockGroup.DockedShips) Add(member?.Ship);
        else {
            var docked = ship.GetAllDockedShips();
            if (docked != null) foreach (var other in docked) Add(other);
        }
        return group;
    }
    internal static bool Defend(WeaponsSystem weapons, float angle) {
        var ship = Host(weapons);
        if (ship == null || ship.LoadState < Ship.Loaded.Edit || ship.objSS == null) return false;
        double radians = angle * Math.PI / 180;
        var target = new ShipSitu(ship.objSS, createPath: false);
        target.vPosx = ship.objSS.vPosx + 5000 / MetresPerAU * Math.Cos(radians);
        target.vPosy = ship.objSS.vPosy + 5000 / MetresPerAU * Math.Sin(radians);
        var incoming = (ship.objSS.vPos - target.vPos).normalized * (750 / MetresPerAU);
        target.vVelX = ship.objSS.vVelX + incoming.X;
        target.vVelY = ship.objSS.vVelY + incoming.Y;
        var group = Group(ship);
        var guns = new List<Gun>();
        double miss = 1;
        int defenders = 0;
        foreach (var hull in group) {
            if (hull.LoadState < Ship.Loaded.Edit) continue;
            var system = ReferenceEquals(hull, ship) ? weapons : hull.WeaponsSystem;
            if (system == null) continue;
            defenders++;
            if (hull.aElectronicSystemCOs != null) foreach (var sensor in hull.aElectronicSystemCOs) miss *= SensorMiss(sensor);
            foreach (var weapon in system.GetWeaponsAndRotations(defensiveOnly: true)) {
                var co = weapon.Item1.CO;
                if (!Operational(co) || !co.HasCond("IsShipWeaponPDC")) continue;
                guns.Add(new Gun(hull, system, co, weapon.Item2));
                if (Covers(system, hull, weapon.Item2, target, 90, double.PositiveInfinity))
                    miss *= 1 - SensorContribution(co.GetCondAmount("IsShipWeaponLaunchSpeed"), co.GetCondAmount("IsShipWeaponArcRange"));
            }
        }
        bool detected = UnityEngine.Random.value < 1 - miss;
        bool intercepted = false;
        int bursts = 0, rounds = 0, blocked = 0;
        if (detected) {
            var singleGun = new List<CondOwner>(1);
            foreach (var gun in guns) {
                var co = gun.CO;
                if (!Operational(co) || co.HasCond("IsPDCTargetModeShipsOnly") ||
                    co.HasCond("IsJammedWeapon") || WeaponsSystem.IsReloading(co)) continue;
                if (!Covers(gun.Weapons, gun.Ship, gun.Rotation, target, co.GetCondAmount("IsShipWeaponArcAngle"), co.GetCondAmount("IsShipWeaponArcRange"))) continue;
                // Each covering gun fires one burst, even if another gun also succeeds.
                // Native Shoot owns ammunition selection, destruction, reload and effects.
                singleGun.Clear(); singleGun.Add(co);
                var previous = firing;
                try {
                    firing = new Firing { Weapon = co, System = gun.Weapons, Ship = gun.Ship, Target = target, Group = group };
                    if (!gun.Weapons.ShootAuto(singleGun, target)) {
                        if (firing.Blocker != null) blocked++;
                        continue;
                    }
                    bursts++; rounds += firing.Rounds;
                    double chance = BurstChance(firing.Rounds, co.GetCondAmount("IsShipWeaponLaunchSpeed"));
                    if (UnityEngine.Random.value < chance) intercepted = true;
                } finally { firing = previous; }
            }
        }
        MeteorTest.Defense(angle, 1 - miss, detected, intercepted, bursts, rounds, group.Count, defenders, blocked);
        OstrabusPlugin.Logger?.LogInfo($"[Micrometeoroids] {ship.strRegID}: angle={angle:F1}, group={group.Count}, defenders={defenders}, detection={1 - miss:P1}, detected={detected}, blocked={blocked}, bursts={bursts}, rounds={rounds}, intercepted={intercepted}.");
        return intercepted;
    }
}
