using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using HarmonyLib;
using Ostranauts.Ships;
using Ostranauts.Trading;
using Ostrabus.Micrometeoroids;
using UnityEngine;

static class Program {
    static int checks, destroyed, projectiles;
    static int defenseAttempts, impacts;
    static bool defenseResult;
    static bool throwSpawn;
    static bool throwShot;
    static Ship ship, spawned, logged;
    static readonly Dictionary<CondOwner, Dictionary<string, double>> flags = new Dictionary<CondOwner, Dictionary<string, double>>();
    static readonly Dictionary<CondOwner, int> ammunition = new Dictionary<CondOwner, int>();
    static readonly Queue<float> rolls = new Queue<float>();
    static T Empty<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    static void Field(object obj, string name, object value) => AccessTools.Field(obj.GetType(), name).SetValue(obj, value);
    static void Check(bool ok, string reason) { checks++; if (!ok) throw new Exception(reason); }
    static void Near(double actual, double expected, string reason) => Check(Math.Abs(actual - expected) < 1e-9, reason + ": " + actual);
    static bool Equal(UnityEngine.Object __0, UnityEngine.Object __1, ref bool __result) { __result = ReferenceEquals(__0, __1); return false; }
    static bool Unequal(UnityEngine.Object __0, UnityEngine.Object __1, ref bool __result) { __result = !ReferenceEquals(__0, __1); return false; }
    static bool Has(CondOwner __instance, string strName, ref bool __result) { __result = flags.TryGetValue(__instance, out var map) && map.TryGetValue(strName, out var v) && v > 0; return false; }
    static bool Amount(CondOwner __instance, string strName, ref double __result) { __result = flags.TryGetValue(__instance, out var map) && map.TryGetValue(strName, out var v) ? v : 0; return false; }
    static bool SetAmount(CondOwner __instance, string strName, double __1) { flags[__instance][strName] = __1; return false; }
    static bool Zero(CondOwner __instance, string strName) { flags[__instance][strName] = 0; return false; }
    static bool Value(ref float __result) { if (rolls.Count == 0) throw new Exception("Unexpected random roll"); __result = rolls.Dequeue(); return false; }
    static bool RangeFloat(float minInclusive, float maxInclusive, ref float __result) { __result = maxInclusive; return false; }
    static bool Pos(ref Vector3 __result) { __result = default; return false; }
    static bool ItemOf(ref Item __result) { __result = Empty<Item>(); return false; }
    static bool Quiet() => false;
    static bool ShaderId(ref int __result) { __result = 1; return false; }
    static bool TimeScale(ref float __result) { __result = 1; return false; }
    static bool Ammo(CondOwner weaponCo, ref List<CondOwner> __result) {
        __result = new List<CondOwner>();
        for (int i = 0; i < ammunition[weaponCo]; i++) __result.Add(CO());
        return false;
    }
    static bool Projectile() { if (throwShot) throw new InvalidOperationException("fixture projectile error"); projectiles++; return false; }
    static bool Destroy() { destroyed++; return false; }
    static bool Spawn(Ship __0) {
        if (throwSpawn) throw new InvalidOperationException("fixture spawn error");
        spawned = __0;
        MeteorTest.Defense(90, .825873184639489, true, false, 1, 18);
        return false;
    }
    static bool Log(Ship __instance) { logged = __instance; return false; }
    static bool Atmosphere(ref bool __result) { __result = false; return false; }
    static bool Torch(ref bool __result) { __result = false; return false; }
    static bool Text(ref string __result) { __result = "impact "; return false; }
    static bool DefenseProbe(ref bool __result) { defenseAttempts++; __result = defenseResult; return false; }
    static bool Impact() { impacts++; return false; }
    static bool Mode(ref JsonAttackMode __result) { __result = new JsonAttackMode(); return false; }
    static bool Selected(ref CondOwner __result) { __result = CrewSim.coPlayer; return false; }
    static CondOwner CO(params string[] conditions) {
        var co = Empty<CondOwner>(); flags.Add(co, new Dictionary<string, double>());
        co.tf = Empty<Transform>();
        foreach (string c in conditions) flags[co][c] = 1;
        return co;
    }
    static Ship Hull(string id, int cols = 10, int rows = 10) {
        var result = Empty<Ship>(); Field(result, "strRegID", id); Field(result, "nLoadState", Ship.Loaded.Full);
        result.nCols = cols; result.nRows = rows; result.aTiles = new List<Tile> { null };
        Field(result, "<objSS>k__BackingField", new ShipSitu(false)); result.aElectronicSystemCOs = new List<CondOwner>();
        return result;
    }
    static CondOwner Gun(double speed = 45000, double range = 15000, float rotation = 0, int rounds = 18) {
        var co = CO("IsInstalled", "IsPowered", "IsShipWeaponPDC", "IsShipDefensiveWeapon");
        flags[co]["IsShipWeaponLaunchSpeed"] = speed; flags[co]["IsShipWeaponArcRange"] = range;
        flags[co]["IsShipWeaponArcAngle"] = 85; flags[co]["IsShipWeaponReloadingSpeed"] = .12;
        var wrapper = Empty<DataCOWrapper>(); Field(wrapper, "_co", co);
        // Rotation is read from CO.Item by the native wrapper; fixture the collection
        // at its public seam so no Unity Item GameObject is required.
        gunList.Add(Tuple.Create(wrapper, rotation)); ammunition[co] = rounds;
        return co;
    }
    static readonly List<Tuple<DataCOWrapper, float>> gunList = new List<Tuple<DataCOWrapper, float>>();
    static bool Guns(ref List<Tuple<DataCOWrapper, float>> __result) { __result = gunList; return false; }
    static WeaponsSystem SystemFor(Ship host) { var ws = Empty<WeaponsSystem>(); Field(ws, "_ship", host); return ws; }
    static void Prefix(Harmony h, Type type, string method, string prefix, Type[] args = null) =>
        h.Patch(AccessTools.Method(type, method, args), prefix: new HarmonyMethod(typeof(Program), prefix));
    static bool Defend(WeaponsSystem ws, float angle, params float[] random) {
        rolls.Clear(); foreach (var r in random) rolls.Enqueue(r);
        bool result = ws.TriggerMicroMeteoroidDefense(angle);
        Check(rolls.Count == 0, "Wrong number of detection/burst rolls");
        return result;
    }
    static int Main() {
        var h = new Harmony("offline.micrometeoroids");
        try {
            Prefix(h, typeof(Shader), "PropertyToID", nameof(ShaderId), new[] { typeof(string) });
            Prefix(h, typeof(UnityEngine.Object), "op_Equality", nameof(Equal));
            Prefix(h, typeof(UnityEngine.Object), "op_Inequality", nameof(Unequal));
            Prefix(h, typeof(CondOwner), "HasCond", nameof(Has), new[] { typeof(string), typeof(bool) });
            Prefix(h, typeof(CondOwner), "GetCondAmount", nameof(Amount), new[] { typeof(string), typeof(bool) });
            Prefix(h, typeof(CondOwner), "SetCondAmount", nameof(SetAmount));
            Prefix(h, typeof(CondOwner), "ZeroCondAmount", nameof(Zero));
            h.Patch(AccessTools.PropertyGetter(typeof(UnityEngine.Random), "value"), prefix: new HarmonyMethod(typeof(Program), nameof(Value)));
            Prefix(h, typeof(UnityEngine.Random), "Range", nameof(RangeFloat), new[] { typeof(float), typeof(float) });
            h.Patch(AccessTools.PropertyGetter(typeof(Transform), "position"), prefix: new HarmonyMethod(typeof(Program), nameof(Pos)));
            h.Patch(AccessTools.PropertyGetter(typeof(CondOwner), "Item"), prefix: new HarmonyMethod(typeof(Program), nameof(ItemOf)));
            Prefix(h, typeof(WeaponsSystem), "GetWeaponsAndRotations", nameof(Guns));
            Prefix(h, typeof(WeaponsSystem), "GetAmmoShot", nameof(Ammo));
            Prefix(h, typeof(WeaponsSystem), "SetupProjectile", nameof(Projectile));
            Prefix(h, typeof(WeaponsSystem), "GetShipAttackMode", nameof(Quiet));
            Prefix(h, typeof(CondOwner), "PopHeadFromStack", nameof(Quiet));
            Prefix(h, typeof(CondOwner), "Destroy", nameof(Destroy));
            Prefix(h, typeof(AudioManager), "PlayAudioEmitter", nameof(Quiet));
            Prefix(h, typeof(CrewSim), "CamShake", nameof(Quiet));
            Prefix(h, typeof(AIShipManager), "CheckShootingWitnesses", nameof(Quiet));
            Prefix(h, typeof(UnityEngine.Events.UnityEvent<string, string>), "Invoke", nameof(Quiet), new[] { typeof(string), typeof(string) });
            AudioManager.am = Empty<AudioManager>(); CrewSim.objInstance = Empty<CrewSim>();
            StarSystem.fEpoch = 100;
            h.CreateClassProcessor(typeof(MeteorBurstCapturePatch)).Patch();
            h.CreateClassProcessor(typeof(MeteorDefensePatch)).Patch();
            h.CreateClassProcessor(typeof(MeteorBypassPatch)).Patch();
            h.CreateClassProcessor(typeof(MeteorTargetPatch)).Patch();

            Near(MeteorDefense.SensorContribution(30000, 12000), .4, "Smartlink sensor");
            Near(MeteorDefense.SensorContribution(45000, 15000), .5477225575051661, "BNW sensor");
            Near(MeteorDefense.SensorContribution(20000, 12000), .3265986323710904, "Soyuz sensor");
            Near(MeteorDefense.BurstChance(18, 30000), .85, "Smartlink burst");
            Near(MeteorDefense.BurstChance(18, 45000), .9419052498068888, "BNW burst");
            Check(MeteorDefense.BurstChance(0, 45000) == 0 && MeteorDefense.BurstChance(18, 0) == 0, "No free empty/invalid burst");
            double previous = 0;
            for (int n = 1; n <= 34; n++) { double p = MeteorDefense.BurstChance(n, 45000); Check(p > previous && p < 1, "Burst monotonicity"); previous = p; }

            ship = Hull("A"); var ws = SystemFor(ship); var gun = Gun();
            Check(!Defend(ws, 90, .9f), "Undetected event intercepted");
            Check(projectiles == 0 && destroyed == 0, "Undetected event fired");
            Check(!Defend(ws, 90, 0, .99f), "Deliberately missed burst intercepted");
            Check(projectiles == 1 && destroyed == 18, "Miss did not fire/consume native ammo");
            Near(flags[gun]["IsReloading"], 100.12, "Miss did not retain native reload");
            Check(!Defend(ws, 90, 0), "Reloading gun fired");
            Check(projectiles == 1, "Reload bypass");
            flags[gun]["IsReloading"] = 0;
            Check(Defend(ws, 90, 0, 0), "Detected hit failed");
            Check(destroyed == 36, "Hit did not consume ammo");
            flags[gun]["IsReloading"] = 0; ammunition[gun] = 1;
            Check(!Defend(ws, 90, 0, .5f), "One round incorrectly used full burst chance");
            Check(destroyed == 37, "One-round native consumption");
            flags[gun]["IsReloading"] = 0; ammunition[gun] = 0;
            Check(!Defend(ws, 90, 0), "Empty gun intercepted");
            Check(projectiles == 3, "Empty gun fired");
            ammunition[gun] = 18; flags[gun]["IsJammedWeapon"] = 1;
            Check(!Defend(ws, 90, 0), "Jammed gun intercepted");
            flags[gun]["IsJammedWeapon"] = 0; flags[gun]["IsPDCTargetModeShipsOnly"] = 1;
            Check(!Defend(ws, 90, 0), "Ships-only gun intercepted meteor");
            flags[gun]["IsPDCTargetModeShipsOnly"] = 0;
            // 44 degrees off boresight: inside detection quadrant, outside 85-degree firing arc.
            Check(!Defend(ws, 134, 0), "Detection quadrant incorrectly expanded firing arc");
            Check(!Defend(ws, 270, 0), "Rear approach detected without sensor coverage");
            var radar = CO("IsInstalled", "IsPowered", "IsSensorRadar", "IsSensorRadarOn");
            var lidar = CO("IsInstalled", "IsPowered", "IsSensorLidar", "IsSensorLidarOn");
            Near(MeteorDefense.SensorMiss(radar), .7, "Active radar"); Near(MeteorDefense.SensorMiss(lidar), .55, "Active lidar");
            foreach (var co in new[] { radar, lidar }) {
                foreach (string bad in new[] { "IsOff", "IsDamaged" }) { flags[co][bad] = 1; Near(MeteorDefense.SensorMiss(co), 1, "Disabled sensor contributed"); flags[co][bad] = 0; }
                flags[co]["IsPowered"] = 0; Near(MeteorDefense.SensorMiss(co), 1, "Unpowered sensor contributed"); flags[co]["IsPowered"] = 1;
                flags[co]["IsInstalled"] = 0; Near(MeteorDefense.SensorMiss(co), 1, "Loose sensor contributed"); flags[co]["IsInstalled"] = 1;
            }
            ship.aElectronicSystemCOs.Add(radar); ship.aElectronicSystemCOs.Add(lidar);
            Check(!Defend(ws, 270, 0), "Omnidirectional sensor incorrectly expanded gun coverage");
            var second = Gun();
            Check(Defend(ws, 90, 0, .99f, 0), "Second PDC could not catch first miss");
            Check(flags[gun]["IsReloading"] > 100 && flags[second]["IsReloading"] > 100, "Both guns did not fire");
            flags[gun]["IsReloading"] = flags[second]["IsReloading"] = 0;
            Check(Defend(ws, 90, 0, 0, .99f), "First PDC success lost");
            Check(flags[second]["IsReloading"] > 100, "First success suppressed simultaneous second burst");
            flags[gun]["IsReloading"] = flags[second]["IsReloading"] = 0; throwShot = true;
            bool threw = false; try { Defend(ws, 90, 0); } catch (InvalidOperationException) { threw = true; }
            Check(threw && AccessTools.Field(typeof(MeteorDefense), "firingWeapon").GetValue(null) == null, "Exception retained combat CO reference");
            throwShot = false;

            var other = Hull("B", 20, 20); var shallow = Hull("SHALLOW"); Field(shallow, "nLoadState", Ship.Loaded.Shallow);
            var dead = Hull("DEAD"); dead.bDestroyed = true;
            var scene = new List<Ship> { ship, other, shallow, dead };
            int a = 0, b = 0;
            for (int i = 0; i < 10000; i++) { var chosen = MeteorTargets.Select(scene, ship, (i + .5) / 10000); if (ReferenceEquals(chosen, ship)) a++; else if (ReferenceEquals(chosen, other)) b++; else throw new Exception("Ineligible hull selected"); }
            Check(a == 2000 && b == 8000, "Area-weighted scene selection");
            for (int i = 0; i < 100; i++) Check(ReferenceEquals(MeteorTargets.Select(scene, ship, i / 100.0), MeteorTargets.Select(scene, other, i / 100.0)), "Crossing docking port changed target selection");
            Check(ReferenceEquals(MeteorTargets.Select(null, ship, .5), ship), "Missing scene fallback");
            Check(ReferenceEquals(MeteorTargets.Select(new[] { shallow, dead }, ship, .5), ship), "Empty candidate fallback");

            // Execute the actual native spawn with the bypass removed. Only the
            // defense outcome and scene effects are fixtures; damage routing is native.
            h.Unpatch(AccessTools.Method(typeof(WeaponsSystem), nameof(WeaponsSystem.TriggerMicroMeteoroidDefense)), AccessTools.Method(typeof(MeteorDefensePatch), "Prefix"));
            Prefix(h, typeof(WeaponsSystem), nameof(WeaponsSystem.TriggerMicroMeteoroidDefense), nameof(DefenseProbe));
            Prefix(h, typeof(DamageSystem), "DamageRayRandom", nameof(Impact));
            Prefix(h, typeof(DataHandler), "GetAttackMode", nameof(Mode));
            Prefix(h, typeof(DataHandler), "GetLoot", nameof(Quiet));
            Prefix(h, typeof(DataHandler), "GetString", nameof(Text), new[] { typeof(string), typeof(bool) });
            Prefix(h, typeof(AudioManager), "PlayAudioEmitterDelayed", nameof(Quiet));
            Prefix(h, typeof(CrewSim), "GetSelectedCrew", nameof(Selected));
            Prefix(h, typeof(CondOwner), "LogMessage", nameof(Quiet));
            Prefix(h, typeof(CrewSim), "ResetTimeScale", nameof(Quiet));
            CrewSim.coPlayer = CO();
            Field(ship, "<WeaponsSystem>k__BackingField", ws);
            Field(ship, "<DamageSystem>k__BackingField", Empty<DamageSystem>());
            defenseResult = true;
            for (int i = 0; i < 100; i++) StarSystem.SpawnMicroMeteoroid(ship, 1, false);
            Check(defenseAttempts == 100 && impacts == 0, "Native 50% bypass survived / success damaged hull");
            defenseResult = false; StarSystem.SpawnMicroMeteoroid(ship, 1, true);
            Check(defenseAttempts == 101 && impacts == 1, "Failed interception did not enter native damage branch");

            // Execute the actual patched BeatManager method. All eligibility remains
            // native; replace only world/Unity effects and the downstream spawn.
            Prefix(h, typeof(StarSystem), "SpawnMicroMeteoroid", nameof(Spawn));
            h.Patch(AccessTools.PropertyGetter(typeof(Time), "timeScale"), prefix: new HarmonyMethod(typeof(Program), nameof(TimeScale)));
            Prefix(h, typeof(Ship), "LogAdd", nameof(Log));
            Prefix(h, typeof(StarSystem), "IsInAtmo", nameof(Atmosphere));
            h.Patch(AccessTools.PropertyGetter(typeof(Ship), "IsUsingTorchDrive"), prefix: new HarmonyMethod(typeof(Program), nameof(Torch)));
            AccessTools.Field(typeof(CrewSim), "aLoadedShips").SetValue(null, scene);
            CrewSim.coPlayer = CO(); Field(CrewSim.coPlayer, "_objShip", ship); CrewSim.system = Empty<StarSystem>();
            AccessTools.Field(typeof(BeatManager), "dictEventChances").SetValue(null, new Dictionary<string, float> { ["tension_micrometeoroid"] = 1 });
            AccessTools.Field(typeof(BeatManager), "fRoll").SetValue(null, 0f);
            rolls.Clear(); rolls.Enqueue(.9f);
            var beat = AccessTools.Method(typeof(BeatManager), "Micrometeoroid");
            Check((bool)beat.Invoke(null, null) && ReferenceEquals(spawned, other) && ReferenceEquals(logged, other), "Actual BM did not redirect strike and log together");
            foreach (string exclusion in new[] { "bOnStation", "bDockedWithStation" }) {
                AccessTools.Field(typeof(BeatManager), exclusion).SetValue(null, true); spawned = null;
                Check(!(bool)beat.Invoke(null, null) && spawned == null, "Native station exclusion changed");
                AccessTools.Field(typeof(BeatManager), exclusion).SetValue(null, false);
            }
            // The explicit testing entry point must refresh native cached flags,
            // force only event selection, and restore all temporary state.
            AccessTools.Field(typeof(BeatManager), "fRoll").SetValue(null, .42f);
            AccessTools.Field(typeof(BeatManager), "bOnStation").SetValue(null, true);
            AccessTools.Field(typeof(BeatManager), "bDockedWithStation").SetValue(null, true);
            AccessTools.Field(typeof(BeatManager), "dictEventChances").SetValue(null, new Dictionary<string, float> { ["tension_micrometeoroid"] = 0 });
            rolls.Enqueue(.9f);
            var test = MeteorTest.Trigger();
            Check(test.Triggered && test.ShipId == "B" && test.Detected == true && test.Intercepted == false && test.Bursts == 1 && test.Rounds == 18, "Test endpoint did not use BM/fair target/outcome capture");
            Near((float)AccessTools.Field(typeof(BeatManager), "fRoll").GetValue(null), (double).42f, "Test call changed ongoing event roll");
            Check((bool)AccessTools.Field(typeof(BeatManager), "bOnStation").GetValue(null) && (bool)AccessTools.Field(typeof(BeatManager), "bDockedWithStation").GetValue(null), "Test call did not restore cached eligibility");
            Check(AccessTools.Field(typeof(MeteorTest), "current").GetValue(null) == null, "Test result context retained");
            ship.objSS.bIsBO = true;
            test = MeteorTest.Trigger();
            Check(!test.Triggered && test.ShipId == null && !test.Detected.HasValue, "Test call bypassed on-station exclusion");
            ship.objSS.bIsBO = false; ship.objSS.bBOLocked = true;
            Check(!MeteorTest.Trigger().Triggered, "Test call bypassed station-docking exclusion");
            ship.objSS.bBOLocked = false;
            throwSpawn = true; rolls.Enqueue(.9f);
            threw = false; try { MeteorTest.Trigger(); } catch (TargetInvocationException) { threw = true; }
            Check(threw && AccessTools.Field(typeof(MeteorTest), "current").GetValue(null) == null, "Failed test retained result context");
            Near((float)AccessTools.Field(typeof(BeatManager), "fRoll").GetValue(null), (double).42f, "Failed test changed ongoing event roll");
            Check((bool)AccessTools.Field(typeof(BeatManager), "bOnStation").GetValue(null) && (bool)AccessTools.Field(typeof(BeatManager), "bDockedWithStation").GetValue(null), "Failed test changed cached eligibility");
            Console.WriteLine(checks + " micrometeoroid native firing/selection/probability checks passed.");
            return 0;
        } finally { h.UnpatchSelf(); }
    }
}

namespace Ostrabus {
    internal static class OstrabusPlugin { internal static readonly TestLog Logger = new TestLog(); }
    internal sealed class TestLog { internal void LogInfo(string message) {} }
}
