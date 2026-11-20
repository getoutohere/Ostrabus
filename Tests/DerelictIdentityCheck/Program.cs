using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Serialization;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using Ostrabus.MiscBugFixes;
using Ostranauts.Core.Models;
using Ostranauts.Ships;
using Ostranauts.Ships.Commands;
using SolarSystem;
using UnityEngine;

static class Program {
    static int checks, integerDraws, fractionalDraws, spawnCalls;
    static readonly Queue<int> random = new Queue<int>();
    static readonly List<string> warnings = new List<string>();
    static Ship spawned;
    static string expectedTemplate;
    static readonly MethodInfo convert = AccessTools.Method(typeof(EnvironmentEntities), "SpawnShipFromStellarObject", new[] { typeof(IStellarObject), typeof(string) });
    static T Empty<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
    static void Reject(Action action, string message) {
        bool rejected = false; try { action(); } catch (InvalidOperationException) { rejected = true; }
        Check(rejected, message);
    }
    static bool Equal(UnityEngine.Object __0, UnityEngine.Object __1, ref bool __result) { __result = ReferenceEquals(__0, __1); return false; }
    static bool Unequal(UnityEngine.Object __0, UnityEngine.Object __1, ref bool __result) { __result = !ReferenceEquals(__0, __1); return false; }
    static bool Noop() => false;
    static IEnumerable<CodeInstruction> NoChange(IEnumerable<CodeInstruction> instructions) => instructions;
    static bool Warn(object message) { warnings.Add(message?.ToString()); return false; }
    static bool IntRange(int __0, int __1, ref int __result) {
        if (random.Count == 0) throw new Exception("Native RNG consumed an unexpected integer draw");
        integerDraws++; __result = random.Dequeue();
        Check(__result >= __0 && __result < __1, "Fixture integer outside native RNG bounds"); return false;
    }
    static bool FloatRange(float __0, float __1, ref float __result) {
        fractionalDraws++; __result = __0 + .75f * (__1 - __0); return false;
    }
    static bool Spawn(string strJson, string strRegID, ref Ship __result) {
        spawnCalls++;
        Check(strJson == expectedTemplate && strRegID == spawned.strRegID, "Converter changed the template or explicit registration at scene allocation");
        __result = spawned; return false;
    }
    static void Reset(params int[] draws) {
        CrewSim.system = Empty<StarSystem>();
        CrewSim.system.dictShips = new Dictionary<string, Ship>();
        CrewSim.system.dictStellarObjects = new DictionaryArray<string, IStellarObject>();
        CrewSim.system.aBOs = new Dictionary<string, BodyOrbit>();
        warnings.Clear(); random.Clear(); foreach (int draw in draws) random.Enqueue(draw);
        integerDraws = fractionalDraws = spawnCalls = 0;
    }
    static Ship Vessel(string id, string name, Ship.TypeClassification classification = Ship.TypeClassification.Default) {
        var ship = Empty<Ship>(); ship.strRegID = id; ship.publicName = name; ship.Classification = classification;
        AccessTools.Property(typeof(Ship), nameof(Ship.objSS)).SetValue(ship, new ShipSitu());
        AccessTools.Field(typeof(Ship), "aDockingPorts").SetValue(ship, new List<string>());
        AccessTools.Property(typeof(Ship), nameof(Ship.ShipCO)).SetValue(ship, Empty<CondOwner>());
        ship.DMGStatus = Ship.Damage.Derelict; return ship;
    }
    static StellarDerelict Wreck(string id, string template) => new StellarDerelict(new JsonStellarDerelictSave {
        strID = id, strLoot = template, nSize = 200, nMapID = 801, strParent = "1036 Ganymed", fBreakInMultiplier = .4f
    });
    static Ship Convert(IStellarObject source, string generatedName) {
        expectedTemplate = source.strLoot; spawned = Vessel(source.strID, generatedName);
        spawned.strXPDR = source.strID; spawned.bXPDRAntenna = true;
        CrewSim.system.dictStellarObjects[source.strID] = source;
        var ship = (Ship)convert.Invoke(null, new object[] { source, "OKLG" });
        Check(ReferenceEquals(ship, spawned) && ship.strRegID == source.strID,
            "Conversion replaced the identity: source=" + source.GetType().Name + "/" + source.strID + ", result=" + ship?.strRegID + ", allocation=" + spawned?.strRegID + ", spawnCalls=" + spawnCalls + ", classification=" + spawned?.Classification + ", warnings=" + string.Join("|", warnings));
        Check(!CrewSim.system.dictStellarObjects.ContainsKey(source.strID), "Conversion did not retire its deferred entry");
        return ship;
    }
    static void Baseline() {
        Reset();
        Check(Convert(Wreck("M-H67", "SalvagePodSmall"), "Native Generated Name").publicName == "M-H67", "Unpatched native name-loss reproduction failed");
        Check(warnings.Count == 0, "Unpatched native conversion unexpectedly used our warning");
        Reset();
        Convert(new Asteroid(new JsonStellarObject { strID = "FIELD-BASE", strTypeLoot = "Asteroid", nSize = 200 }, "FIELD"), "Asteroid");
        Reset(0, 0, 0, 0, 0, 0, 0, 0);
        var a = EnvironmentEntities.CreateStellarObject("SalvagePodSmall", null, Ship.TypeClassification.Default, 801);
        var b = EnvironmentEntities.CreateStellarObject("Mesa", null, Ship.TypeClassification.Default, 801);
        Check(a.strID == b.strID && CrewSim.system.dictStellarObjects.Count == 1 &&
            ReferenceEquals(CrewSim.system.dictStellarObjects[a.strID], b), "Unpatched native deferred-ID overwrite reproduction failed");
        Check(integerDraws == 8 && fractionalDraws == 4, "Unpatched native RNG control changed");
    }
    static void Names() {
        foreach (string template in new[] { "SalvagePodSmall", "Mesa" }) {
            var path = Path.Combine(Ostrabus.Development.InstallationPaths.Game, "Ostranauts_Data", "StreamingAssets", "data", "ships", template + ".json");
            var asset = JArray.Parse(File.ReadAllText(path))[0];
            Check((string)asset["publicName"] == "$TEMPLATE", "Native asset no longer requests a generated name");
            Reset(); var ship = Convert(Wreck("M-H67", template), "Native Generated Name");
            Check(ship.publicName == "Native Generated Name", "Wreck conversion lost its generated name");
            Check(ship.Classification == Ship.TypeClassification.Default && ship.fBreakInMultiplier == .4f &&
                !ship.bXPDRAntenna && ship.strXPDR == null && ship.objSS.bIsNoFees, "Native conversion behavior changed beyond naming");
            Check(integerDraws == 0 && fractionalDraws == 0, "Name preservation drew additional random numbers");
            Check(warnings.Count == 1 && warnings[0].Contains("Native Generated Name") && warnings[0].Contains("M-H67") && warnings[0].Contains("Preserved derelict name"), "Name intervention warning missing context");
            var observer = Vessel("O-OBSERVER", "Observer"); observer.shipScanTarget = ship;
            var fly = Empty<FlyTo>(); AccessTools.Field(typeof(BaseCommand), "_shipUs").SetValue(fly, observer);
            Check(fly.DescriptionFriendly == "Flying to M-H67", "AI navigation description changed with preserved name");
            Reset(); Convert(Wreck("M-H67", template), "M-H67");
            Check(warnings.Count == 0, "Already callsign-only hull falsely reported a restored name");
        }
        Reset();
        var asteroid = new Asteroid(new JsonStellarObject { strID = "FIELD-AST", strTypeLoot = "Asteroid", nSize = 200 }, "FIELD");
        var rock = Convert(asteroid, "Asteroid");
        Check(rock.publicName == "FIELD-AST" && rock.Classification == Ship.TypeClassification.Asteroid && warnings.Count == 0, "Asteroid naming or classification changed");
        foreach (bool member in new[] { false, true }) {
            Reset();
            var source = new Asteroid(new JsonStellarObject { strID = "FIELD-MEMBER", strTypeLoot = "Asteroid", nSize = 200 }, "FIELD");
            var field = Empty<AsteroidField>();
            var asteroids = new List<Asteroid>(); if (member) asteroids.Add(source);
            AccessTools.Field(typeof(AsteroidField), nameof(AsteroidField.Asteroids)).SetValue(field, asteroids);
            CrewSim.system.aBOs["NON-FIELD"] = Empty<BodyOrbit>();
            CrewSim.system.aBOs["FIELD"] = field;
            var materialized = Convert(source, "Asteroid");
            Check(materialized.publicName == source.strID && asteroids.Count == 0 && warnings.Count == 0,
                "Asteroid native field removal or finally continuation changed");
        }
        Reset();
        var station = new StellarShip(new JsonStellarShipSave { strID = "HQCH-SEC1", strLoot = "SecurityOutpost", strPublicName = "Authored Station", Classification = Ship.TypeClassification.Outpost });
        var hull = Convert(station, "Allocation Boundary Name");
        Check(hull.publicName == "Authored Station" && hull.Classification == Ship.TypeClassification.Outpost && warnings.Count == 0, "Authored station identity changed");
    }
    static void Registrations() {
        Reset(0, 0, 0);
        Check(Ship.GenerateID("OKLG") == "OKLG-AAA" && integerDraws == 3 && fractionalDraws == 2 && warnings.Count == 0, "Collision-free explicit allocation changed RNG or identity");
        Reset(0, 0, 0); CrewSim.system = null;
        Check(Ship.GenerateID("M") == "M-AAA" && warnings.Count == 0, "Native allocation before a world exists was disrupted");
        Reset(0, 0, 0, 0);
        Check(Ship.GenerateID() == "H-AAA" && integerDraws == 4 && fractionalDraws == 2 && warnings.Count == 0, "Collision-free native colony roll changed");
        foreach (string prefix in new[] { "M", "OKLG", "HQCH-SEC1", "AsteroidFieldCentral" }) {
            foreach (string kind in new[] { "loaded", "wreck", "station", "asteroid", "null-deferred" }) {
                Reset(0, 0, 0, 1, 1, 1);
                string occupied = prefix + "-AAA";
                if (kind == "loaded") CrewSim.system.dictShips[occupied] = null;
                else if (kind == "wreck") CrewSim.system.dictStellarObjects[occupied] = Wreck(occupied, "Mesa");
                else if (kind == "station") CrewSim.system.dictStellarObjects[occupied] = new StellarShip(new JsonStellarShipSave { strID = occupied, strLoot = "SecurityOutpost", strPublicName = "Station", Classification = Ship.TypeClassification.Outpost });
                else if (kind == "asteroid") CrewSim.system.dictStellarObjects[occupied] = new Asteroid(new JsonStellarObject { strID = occupied, nSize = 200 }, prefix);
                else CrewSim.system.dictStellarObjects[occupied] = null;
                Check(Ship.GenerateID(prefix) == prefix + "-BBB", "Collision retry lost its explicit prefix or reused an occupied ID");
                Check(integerDraws == 6 && fractionalDraws == 4 && warnings.Count == 1 && warnings[0].Contains(occupied), "Collision warning or native retry RNG changed");
                Check(warnings[0].Contains(kind == "loaded" ? "loaded ship" : "deferred space object"), "Collision warning names the wrong registry");
            }
        }
        Reset(0, 0, 0, 0, 1, 1, 1, 1);
        CrewSim.system.dictStellarObjects["H-AAA"] = Wreck("H-AAA", "Mesa");
        Check(Ship.GenerateID() == "V-BBB" && warnings.Count == 1, "Default-prefix retry no longer follows native colony draws");
        Reset(0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1);
        var first = EnvironmentEntities.CreateStellarObject("SalvagePodSmall", null, Ship.TypeClassification.Default, 801);
        var second = EnvironmentEntities.CreateStellarObject("Mesa", null, Ship.TypeClassification.Default, 801);
        Check(first.strID == "H-AAA" && second.strID == "H-BBB" && CrewSim.system.dictStellarObjects.Count == 2 &&
            ReferenceEquals(CrewSim.system.dictStellarObjects[first.strID], first), "Guard failed to preserve both natively created wrecks");
        Check(warnings.Count == 1 && warnings[0].Contains("H-AAA"), "Actual creation collision was not reported");
        Reset(); CrewSim.system.dictStellarObjects["M-H67"] = Wreck("M-H67", "Mesa");
        var restored = Wreck("M-H67", "SalvagePodSmall");
        var explicitObject = EnvironmentEntities.CreateStellarObject("Mesa", null, Ship.TypeClassification.Default, 801, regId: "M-H67");
        Check(restored.strID == "M-H67" && explicitObject.strID == "M-H67" && integerDraws == 0 && warnings.Count == 0, "Explicit-ID restoration/conversion was rerolled");
        var result = Convert(restored, "Restored Name");
        Check(result.strRegID == "M-H67" && result.publicName == "Restored Name", "Explicit-ID materialization changed identity");
        // Many collisions exercise native retries without adding a population scan or saved reservation state.
        Reset();
        for (int i = 0; i < 20; i++) {
            string suffix = new string("ABCDEFGHJKLMNPQRSTUVWXYZ0123456789"[i], 3);
            CrewSim.system.dictStellarObjects["M-" + suffix] = null;
            random.Enqueue(i); random.Enqueue(i); random.Enqueue(i);
        }
        random.Enqueue(20); random.Enqueue(20); random.Enqueue(20);
        Check(Ship.GenerateID("M") == "M-WWW" && warnings.Count == 20 && integerDraws == 63, "Repeated collision handling or logging failed");
    }
    static void Contracts() {
        var reader = typeof(PatchProcessor).GetMethods().Single(method => method.Name == "GetOriginalInstructions" &&
            method.GetParameters().Length == 2 && !method.GetParameters()[1].ParameterType.IsByRef);
        var originalName = ((IEnumerable<CodeInstruction>)reader.Invoke(null, new object[] { convert, null })).ToList();
        var names = DerelictNamePatch.Transpiler(originalName).ToList();
        Check(names.Count == originalName.Count + 1 && names.Count(x => x.Calls(AccessTools.Method(typeof(DerelictName), nameof(DerelictName.Preserve)))) == 1,
            "Name patch must substitute one field write and retain one explicit finally continuation");
        var generate = AccessTools.Method(typeof(Ship), nameof(Ship.GenerateID));
        var originalId = ((IEnumerable<CodeInstruction>)reader.Invoke(null, new object[] { generate, null })).ToList();
        var ids = ShipRegistrationPatch.Transpiler(originalId).ToList();
        Check(ids.Count == originalId.Count && ids.Zip(originalId, (a,b) => a.opcode != b.opcode || !Equals(a.operand,b.operand)).Count(x=>x) == 2, "ID patch must change only the registry call and retry argument");
        Reject(() => DerelictNamePatch.Transpiler(new[] { new CodeInstruction(OpCodes.Ret) }).ToList(), "Name patch accepted an unknown native layout");
        Reject(() => ShipRegistrationPatch.Transpiler(new[] { new CodeInstruction(OpCodes.Ret) }).ToList(), "ID patch accepted an unknown native layout");
    }
    static void Main(string[] args) {
        var fixtures = new Harmony("ostrabus.tests.derelict-identity.fixtures");
        var repairs = new Harmony("ostrabus.tests.derelict-identity.repairs");
        try {
            void Prefix(Type type, string method, string prefix, Type[] args = null) => fixtures.Patch(AccessTools.Method(type, method, args), prefix: new HarmonyMethod(typeof(Program), prefix));
            Prefix(typeof(UnityEngine.Object), "op_Equality", nameof(Equal)); Prefix(typeof(UnityEngine.Object), "op_Inequality", nameof(Unequal));
            Prefix(typeof(Debug), nameof(Debug.LogWarning), nameof(Warn), new[] { typeof(object) });
            Prefix(typeof(UnityEngine.Random), "Range", nameof(IntRange), new[] { typeof(int), typeof(int) });
            Prefix(typeof(UnityEngine.Random), "Range", nameof(FloatRange), new[] { typeof(float), typeof(float) });
            Prefix(typeof(StarSystem), "SpawnShip", nameof(Spawn), new[] { typeof(string), typeof(string), typeof(Ship.Loaded), typeof(Ship.Damage), typeof(string), typeof(int), typeof(bool), typeof(string) });
            Prefix(typeof(Ship), "ToggleVis", nameof(Noop));
            Prefix(typeof(CondOwner), "SetCondAmount", nameof(Noop), new[] { typeof(string), typeof(double), typeof(double) });
            StellarShip.SizeLookupDict["SalvagePodSmall"] = 200; StellarShip.SizeLookupDict["Mesa"] = 500;
            Baseline(); Contracts();
            if (args.Contains("--finally-control")) {
                repairs.Patch(convert, transpiler: new HarmonyMethod(typeof(Program), nameof(NoChange)));
                Reset();
                var rock = new Asteroid(new JsonStellarObject { strID = "FIELD-CONTROL", strTypeLoot = "Asteroid", nSize = 200 }, "FIELD");
                expectedTemplate = rock.strLoot; spawned = Vessel(rock.strID, "Asteroid");
                CrewSim.system.dictStellarObjects[rock.strID] = rock;
                var result = convert.Invoke(null, new object[] { rock, "OKLG" });
                Check(result == null && spawned.Classification == Ship.TypeClassification.Asteroid && warnings.Any(x => x.Contains("Unknown StellarObject type")),
                    "No-op Harmony converter no longer reproduces the finally fallthrough; review the compatibility continuation");
                Console.WriteLine(checks + " native/no-op Harmony finally-control assertions passed; neither production repair was installed.");
                return;
            }
            repairs.CreateClassProcessor(typeof(DerelictNamePatch)).Patch();
            repairs.CreateClassProcessor(typeof(ShipRegistrationPatch)).Patch();
            Names(); Registrations();
            Console.WriteLine(checks + " native derelict-name/registration assertions passed, including unpatched defect controls, interventions, RNG and log checks. No live game or saves modified.");
        } finally { repairs.UnpatchSelf(); fixtures.UnpatchSelf(); }
    }
}
