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
    [ThreadStatic] static CondOwner firingWeapon;
    [ThreadStatic] static int roundsFired;

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
        if (ReferenceEquals(weapon, firingWeapon)) roundsFired = ammo?.Count ?? 0;
        return ammo;
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
        double miss = 1;
        if (ship.aElectronicSystemCOs != null) foreach (var sensor in ship.aElectronicSystemCOs) miss *= SensorMiss(sensor);
        var guns = weapons.GetWeaponsAndRotations(defensiveOnly: true);
        foreach (var gun in guns) {
            var co = gun.Item1.CO;
            if (!Operational(co) || !co.HasCond("IsShipWeaponPDC")) continue;
            if (Covers(weapons, ship, gun.Item2, target, 90, double.PositiveInfinity))
                miss *= 1 - SensorContribution(co.GetCondAmount("IsShipWeaponLaunchSpeed"), co.GetCondAmount("IsShipWeaponArcRange"));
        }
        bool detected = UnityEngine.Random.value < 1 - miss;
        bool intercepted = false;
        int bursts = 0, rounds = 0;
        if (detected) {
            var singleGun = new List<CondOwner>(1);
            foreach (var gun in guns) {
                var co = gun.Item1.CO;
                if (!Operational(co) || !co.HasCond("IsShipWeaponPDC") || co.HasCond("IsPDCTargetModeShipsOnly") ||
                    co.HasCond("IsJammedWeapon") || WeaponsSystem.IsReloading(co)) continue;
                if (!Covers(weapons, ship, gun.Item2, target, co.GetCondAmount("IsShipWeaponArcAngle"), co.GetCondAmount("IsShipWeaponArcRange"))) continue;
                // Each covering gun fires one burst, even if another gun also succeeds.
                // Native Shoot owns ammunition selection, destruction, reload and effects.
                singleGun.Clear(); singleGun.Add(co);
                var previousWeapon = firingWeapon;
                int previousRounds = roundsFired;
                try {
                    firingWeapon = co; roundsFired = 0;
                    if (!weapons.ShootAuto(singleGun, target)) continue;
                    bursts++; rounds += roundsFired;
                    double chance = BurstChance(roundsFired, co.GetCondAmount("IsShipWeaponLaunchSpeed"));
                    if (UnityEngine.Random.value < chance) intercepted = true;
                } finally { firingWeapon = previousWeapon; roundsFired = previousRounds; }
            }
        }
        MeteorTest.Defense(angle, 1 - miss, detected, intercepted, bursts, rounds);
        OstrabusPlugin.Logger?.LogInfo($"[Micrometeoroids] {ship.strRegID}: angle={angle:F1}, detection={1 - miss:P1}, detected={detected}, bursts={bursts}, rounds={rounds}, intercepted={intercepted}.");
        return intercepted;
    }
}
