using System;
using System.Collections.Generic;
using HarmonyLib;
using Ostranauts.Ships;
using Ostranauts.Tools.ExtensionMethods;
using Ostranauts.Utils.Models;
using UnityEngine;

namespace Ostrabus.Micrometeoroids;

// Event-only preflight of native ballistic proxies; no projectiles, RNG or
// retained state. Keep this contract tested against Setup/SpawnProjectile.
internal static class BurstSafety {
    const double MetresPerAU = 149597863936.0;
    const double LaunchAUPerMetre = 6.6845869117759804E-12;
    const float ProjectileRadiusAU = 80 * 6.684587E-12f;
    static readonly Func<WeaponsSystem, Vector2, Vector2, float> Mount =
        (Func<WeaponsSystem, Vector2, Vector2, float>)AccessTools.Method(typeof(WeaponsSystem), "GetWeaponPositionOnCircle")
        .CreateDelegate(typeof(Func<WeaponsSystem, Vector2, Vector2, float>));
    static readonly Func<WeaponsSystem, ShipSitu, Point, double, float> Aim =
        (Func<WeaponsSystem, ShipSitu, Point, double, float>)AccessTools.Method(typeof(WeaponsSystem), "GetExactFiringAngle")
        .CreateDelegate(typeof(Func<WeaponsSystem, ShipSitu, Point, double, float>));
    static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    internal static bool Crosses(double x, double y, double vx, double vy, double radius, double lifetime) {
        // Coordinates/velocities are projectile relative to the friendly hull,
        // in metres. Restrict the sweep to the projectile's future lifetime.
        double speed2 = vx * vx + vy * vy;
        double time = speed2 > 0 ? Math.Max(0, Math.Min(lifetime, -(x * vx + y * vy) / speed2)) : 0;
        x += time * vx; y += time * vy;
        return x * x + y * y <= radius * radius;
    }
    internal static Ship Blocker(WeaponsSystem weapons, Ship ship, CondOwner gun, CondOwner ammo, ShipSitu target, IReadOnlyList<Ship> group) {
        if (group.Count < 2) return null;
        double speed = gun.GetCondAmount("IsShipWeaponLaunchSpeed");
        double lifetime = ammo.GetCondAmount("IsAmmoDespawnTime");
        double salvo = ammo.GetCondAmount("IsShipWeaponSalvoCount");
        // PDCs currently use ballistic 20mm. Do not silently approve a future
        // incompatible missile/configuration through an unmodeled mechanism.
        if (ammo.HasCond("IsAmmoMissile") || !Finite(speed) || speed <= 0 || !Finite(lifetime) || lifetime < 0 ||
            !Finite(salvo) || salvo > int.MaxValue || gun.tf == null) return ship;
        int count = Math.Max(1, MathUtils.RoundToInt(salvo));
        var center = new Vector2(ship.vShipPos.x + ship.nCols / 2f, ship.vShipPos.y - ship.nRows / 2f);
        float mount = Mount(weapons, center, gun.tf.position.ToVector2());
        float radius = ship.objSS.GetRadiusAU() * 1.1f + ProjectileRadiusAU;
        // These are deliberately float operations, as in native SpawnProjectile.
        double px = ship.objSS.vPosx + radius * Mathf.Cos(mount + MathF.PI / 2f);
        double py = ship.objSS.vPosy + radius * Mathf.Sin(mount + MathF.PI / 2f);
        float aim = Aim(weapons, target, new Point(px, py), speed);
        double vx = ship.objSS.vVelX - Mathf.Sin(aim) * (speed * LaunchAUPerMetre);
        double vy = ship.objSS.vVelY + Mathf.Cos(aim) * (speed * LaunchAUPerMetre);
        if (!Finite(px) || !Finite(py) || !Finite(vx) || !Finite(vy)) return ship;
        // Native salvo spacing follows ABSOLUTE world velocity, not the aiming
        // direction or ship-relative velocity. Bound its +/-20m lateral jitter
        // by radius expansion without drawing a second set of random numbers.
        double norm = Math.Sqrt(vx * vx + vy * vy);
        double nx = norm > 0 ? vx / norm : 0, ny = norm > 0 ? vy / norm : 0;
        for (int member = 0; member < group.Count; member++) {
            var other = group[member];
            if (ReferenceEquals(other, ship) || other == null || other.bDestroyed || other.bRemove ||
                other.bNoCollisions || other.objSS == null) continue;
            double x = (px - other.objSS.vPosx) * MetresPerAU;
            double y = (py - other.objSS.vPosy) * MetresPerAU;
            double dx = (vx - other.objSS.vVelX) * MetresPerAU;
            double dy = (vy - other.objSS.vVelY) * MetresPerAU;
            double collisionRadius = (ProjectileRadiusAU + other.objSS.GetRadiusAU()) * MetresPerAU;
            if (!Finite(x) || !Finite(y) || !Finite(dx) || !Finite(dy) || !Finite(collisionRadius) || collisionRadius < 0) return other;
            for (int i = 0; i < count; i++) {
                double spacing = i * 130.0;
                // Native jitter divides by this float metres/AU constant.
                double margin = i > 0 ? 20.0 / 1.4959786E+11f * MetresPerAU : 0;
                if (Crosses(x + nx * spacing, y + ny * spacing, dx, dy, collisionRadius + margin, lifetime)) return other;
            }
        }
        return null;
    }
}
