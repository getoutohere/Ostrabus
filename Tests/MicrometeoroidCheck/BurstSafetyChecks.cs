using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HarmonyLib;
using Ostrabus.Development;
using Ostrabus.Micrometeoroids;
using Ostranauts.Ships;
using UnityEngine;

static partial class Program {
    static Ship nativeProxy;
    static int nativeJitter = -20;
    static bool Jitter(int minInclusive, int maxExclusive, ref int __result) { __result = minInclusive == -20 ? nativeJitter : minInclusive; return false; }
    static bool NativeProxy(ref Ship __result) { nativeProxy = Hull("ROUND"); Field(nativeProxy, "<ShipCO>k__BackingField", CO()); __result = nativeProxy; return false; }
    static bool Captain(ref CondOwner __result) { __result = CrewSim.coPlayer; return false; }
    static bool Owner(ref string __result) { __result = "FACTION"; return false; }
    static bool Factions(ref List<JsonFaction> __result) { __result = new List<JsonFaction>(); return false; }
    static Ship SpawnNative(WeaponsSystem weapons, Ship host, ShipSitu target, double speed) {
        var center = new Vector2(host.vShipPos.x + host.nCols / 2f, host.vShipPos.y - host.nRows / 2f);
        float mount = (float)AccessTools.Method(typeof(WeaponsSystem), "GetWeaponPositionOnCircle").Invoke(weapons,
            new object[] { center, new Vector2(weaponPosition.x, weaponPosition.y) });
        return (Ship)AccessTools.Method(typeof(WeaponsSystem), "SpawnProjectile").Invoke(weapons,
            new object[] { "Projectile", speed, mount, false, 0f, target });
    }
    static bool NativeForwardHit(Ship proxy, Ship friend, double lifetime) {
        // Positive-time control for the native circle predictor, whose update
        // sweep also includes the preceding frame. Start/end overlap are real.
        double x = (proxy.objSS.vPosx - friend.objSS.vPosx) * MetresPerAU;
        double y = (proxy.objSS.vPosy - friend.objSS.vPosy) * MetresPerAU;
        double vx = (proxy.objSS.vVelX - friend.objSS.vVelX) * MetresPerAU;
        double vy = (proxy.objSS.vVelY - friend.objSS.vVelY) * MetresPerAU;
        double radius = (proxy.objSS.GetRadiusAU() + friend.objSS.GetRadiusAU()) * MetresPerAU;
        if (x * x + y * y > radius * radius && x * vx + y * vy >= 0) return false;
        return CollisionManager.WillCollide(proxy.objSS, friend.objSS, lifetime);
    }
    static void BurstSafetyChecks() {
        var native = new Harmony("offline.mm.native-projectile-boundaries");
        void Patch(Type type, string name, string handler) {
            foreach (var method in AccessTools.GetDeclaredMethods(type)) if (method.Name == name)
                native.Patch(method, prefix: new HarmonyMethod(typeof(Program), handler));
        }
        var oldPlayer = CrewSim.coPlayer; var oldWorld = CrewSim.system;
        try {
            CrewSim.system = Empty<StarSystem>(); CrewSim.system.dictProjectiles = new Dictionary<string, Ship>();
            CrewSim.coPlayer = CO();
            native.Patch(AccessTools.PropertyGetter(typeof(Ship), "Captain"), prefix: new HarmonyMethod(typeof(Program), nameof(Captain)));
            Patch(typeof(Ostranauts.Ships.AIPilots.AIShip), "SpawnNewShip", nameof(NativeProxy));
            Patch(typeof(StarSystem), "GetShipOwner", nameof(Owner));
            Patch(typeof(Ship), "GetShipFactions", nameof(Factions));
            Patch(typeof(Ship), "SetFactions", nameof(Quiet));
            Patch(typeof(CondOwner), "ClaimShip", nameof(Quiet));
            Patch(typeof(Ship), "ToggleVis", nameof(Quiet));
            Patch(typeof(CollisionManager), "PrioritizeShip", nameof(Quiet));
            native.Patch(AccessTools.Method(typeof(UnityEngine.Random), "Range", new[] { typeof(int), typeof(int) }), prefix: new HarmonyMethod(typeof(Program), nameof(Jitter)));
            var host = Hull("HOST"); host.objSS.Size = 500;
            var system = Fit(host); host.objSS.Size = 500;
            var gun = FitGun(system); var ammo = Bullet(count: 1);
            var friend = Hull("FRIEND"); friend.objSS.Size = 100;
            var group = new List<Ship> { host, friend };
            weaponPosition = new Vector3(0, -5, 0);
            var target = new ShipSitu(false) { vPosy = 5000 / MetresPerAU, vVelY = -750 / MetresPerAU };
            // Actual native SpawnProjectile, aiming and collision predictor across
            // translated solar coordinates, moving targets and moving groups.
            foreach (double worldX in new[] { 0.0, -1.3630193228015934 })
            foreach (double drift in new[] { 0.0, 1500000.0 })
            foreach (double offset in new[] { -1000.0, -100.0, 0.0, 100.0, 1000.0 }) {
                host.objSS.vPosx = worldX; host.objSS.vPosy = 1.62;
                host.objSS.vVelX = drift / MetresPerAU; host.objSS.vVelY = -drift / 4 / MetresPerAU;
                target.vPosx = worldX; target.vPosy = 1.62 + 5000 / MetresPerAU;
                target.vVelX = host.objSS.vVelX + 100 / MetresPerAU;
                target.vVelY = host.objSS.vVelY - 750 / MetresPerAU;
                friend.objSS.vPosx = worldX + offset / MetresPerAU;
                friend.objSS.vPosy = host.objSS.vPosy + 2000 / MetresPerAU;
                friend.objSS.vVelX = host.objSS.vVelX; friend.objSS.vVelY = host.objSS.vVelY;
                var proxy = SpawnNative(system, host, target, 45000);
                Check(proxy.objSS.Size == 80, "Native projectile proxy radius changed");
                Check((BurstSafety.Blocker(system, host, gun, ammo, target, group) != null) == NativeForwardHit(proxy, friend, .6),
                    "Safety disagrees with native spawn/aim/collision: " + worldX + "/" + drift + "/" + offset);
            }
            // Include native randomized salvo offsets: every actually dangerous
            // proxy must have its burst vetoed; a small margin may be conservative.
            flags[ammo]["IsShipWeaponSalvoCount"] = 3;
            foreach (int jitter in new[] { -20, 0, 19 }) for (int i = 0; i < 3; i++) foreach (double offset in new[] { -190.0, 0.0, 190.0 }) {
                nativeJitter = jitter;
                var proxy = SpawnNative(system, host, target, 45000);
                if (i > 0) AccessTools.Method(typeof(WeaponsSystem), "ApplySalvoSpawningOffset").Invoke(system, new object[] { proxy, i });
                friend.objSS.vPosx = proxy.objSS.vPosx + offset / MetresPerAU;
                friend.objSS.vPosy = proxy.objSS.vPosy + 1000 / MetresPerAU;
                if (NativeForwardHit(proxy, friend, .6)) Check(BurstSafety.Blocker(system, host, gun, ammo, target, group) != null, "Native offset proxy could hit an approved friend");
            }
            // Solar-translated paused O-GXD/M-TZ3T geometry. Saved mount position
            // is carried as a small synthetic numeric fixture below, no save zip.
            host.objSS.vPosx = -1.3630193228015934; host.objSS.vPosy = 1.6202080591327854;
            host.objSS.vVelX = -1.0187485624199039E-05; host.objSS.vVelY = 1.5057378888827846E-06;
            host.objSS.fRot = 1.4182261228561401f;
            friend.objSS.vPosx = -1.3630193387564553; friend.objSS.vPosy = 1.6202080615860877;
            friend.objSS.vVelX = host.objSS.vVelX; friend.objSS.vVelY = host.objSS.vVelY;
            friend.objSS.Size = 800;
            double angle = 218.3 * Math.PI / 180;
            target.vPosx = friend.objSS.vPosx + 5000 / MetresPerAU * Math.Cos(angle);
            target.vPosy = friend.objSS.vPosy + 5000 / MetresPerAU * Math.Sin(angle);
            target.vVelX = friend.objSS.vVelX - 750 / MetresPerAU * Math.Cos(angle);
            target.vVelY = friend.objSS.vVelY - 750 / MetresPerAU * Math.Sin(angle);
            host.nCols = 34; host.nRows = 56; host.vShipPos = new Vector2(-135, -6);
            weaponPosition = new Vector3(-109.5f, -11, 0);
            var actual = SpawnNative(system, host, target, 45000);
            bool danger = NativeForwardHit(actual, friend, .6);
            Check((BurstSafety.Blocker(system, host, gun, Bullet(count: 1), target, group) != null) == danger, "Live geometry disagrees with native projectile path");
            Check(!danger, "Expected clear shot in live formation was obstructed");
            bool salvoDanger = false;
            foreach (int jitter in new[] { -20, 0, 19 }) for (int i = 0; i < 3; i++) {
                nativeJitter = jitter;
                var proxy = SpawnNative(system, host, target, 45000);
                if (i > 0) AccessTools.Method(typeof(WeaponsSystem), "ApplySalvoSpawningOffset").Invoke(system, new object[] { proxy, i });
                bool hit = NativeForwardHit(proxy, friend, .6);
                Console.WriteLine("Live formation native proxy " + i + ", jitter=" + jitter + ": collision=" + hit);
                salvoDanger |= hit;
            }
            Check((BurstSafety.Blocker(system, host, gun, Bullet(), target, group) != null) == salvoDanger, "Live salvo veto disagrees with native proxy hazards");
            // Numeric sweep edge controls, independent of native world setup.
            Check(BurstSafety.Crosses(0, 0, 1000, 0, 80, .6), "Spawn overlap ignored");
            Check(!BurstSafety.Crosses(500, 0, 1000, 0, 80, .6), "Past intersection vetoed outward fire");
            Check(!BurstSafety.Crosses(-1000, 0, 1000, 0, 80, .6), "Post-expiry target vetoed fire");
            Check(BurstSafety.Crosses(-1000, 80, 10000, 0, 80, .6), "Tangent proxy radius ignored");
            Check(!BurstSafety.Crosses(-1000, 81, 10000, 0, 80, .6), "Clear proxy-radius miss blocked");
            string assets = File.ReadAllText(Path.Combine(InstallationPaths.Data, "condowners", "condowners_ship_combat.json"));
            int next = assets.IndexOf("\"strName\" : \"ItmAmmo150mm\"", StringComparison.Ordinal);
            string ammoDef = assets.Substring(0, next);
            Check(ammoDef.Contains("IsAmmoDespawnTime=1x0.6") && ammoDef.Contains("IsShipWeaponSalvoCount=1x3"), "Authored 20mm lifetime/salvo changed");
            Check(FiringClear(), "Safety checks retained firing context");
        } finally {
            native.UnpatchSelf(); CrewSim.coPlayer = oldPlayer; CrewSim.system = oldWorld;
            weaponPosition = default; groupGuns.Clear();
        }
    }
}
