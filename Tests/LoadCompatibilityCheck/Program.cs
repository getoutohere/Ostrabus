using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using HarmonyLib;
using Ostranauts;

static class Program {
    static int checks;
    static readonly List<string> labels = new List<string>();
    static Type Production(string name) => typeof(Ostrabus.OstrabusPlugin).Assembly.GetType("Ostrabus.Optimizations." + name, true);
    static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
    static bool Progress(string textToDisplay) { labels.Add(textToDisplay); return false; }
    static bool GetProgress(ref float __result) { __result = 0; return false; }
    static T Empty<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));

    static void IndexedLookup() {
        var method = typeof(CrewSim).GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)
            .SelectMany(type => type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            .Single(candidate => candidate.ReturnType == typeof(bool) && candidate.GetParameters().Length == 1 &&
                candidate.GetParameters()[0].ParameterType == typeof(JsonShip) &&
                candidate.DeclaringType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .Any(field => field.FieldType == typeof(JsonCondOwnerSave)));
        var capture = FormatterServices.GetUninitializedObject(method.DeclaringType);
        var saved = new JsonCondOwnerSave();
        method.DeclaringType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Single(field => field.FieldType == typeof(JsonCondOwnerSave)).SetValue(capture, saved);
        var predicate = (Func<JsonShip, bool>)Delegate.CreateDelegate(typeof(Func<JsonShip, bool>), capture, method);
        var replacement = AccessTools.Method(Production("SavedCoShipLookupPatch"), "AnySavedCoShip");
        var ships = new[] { new JsonShip { strRegID = "A" }, new JsonShip { strRegID = "B" }, new JsonShip { strRegID = null } };
        foreach (string id in new[] { "A", "B", "C", "b", "", null }) {
            saved.strRegIDLast = id;
            for (int repeat = 0; repeat < 3; repeat++) {
                bool actual = (bool)replacement.Invoke(null, new object[] { ships, predicate });
                Check(actual == ships.Any(predicate), "Indexed lookup changed the actual native saved-CO predicate");
            }
        }
        saved.strRegIDLast = "B";
        var nonArray = ships.ToList();
        Check((bool)replacement.Invoke(null, new object[] { nonArray, predicate }) == nonArray.Any(predicate),
            "Non-array lookup did not preserve native fallback");
        Check((long)AccessTools.Field(Production("SavedCoShipLookupPatch"), "_optimizedLookupCount").GetValue(null) > 0,
            "Updated native compiler closure silently bypassed the optimization");
        Check((long)AccessTools.Field(Production("SavedCoShipLookupPatch"), "_fallbackLookupCount").GetValue(null) > 0,
            "Fallback control was not exercised");
    }

    static StarSystem World() {
        var world = Empty<StarSystem>();
        foreach (var field in typeof(StarSystem).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)) {
            var type = field.FieldType;
            if (type != typeof(string) && typeof(IEnumerable).IsAssignableFrom(type) && !type.IsAbstract && !type.IsArray && type.GetConstructor(Type.EmptyTypes) != null)
                field.SetValue(world, Activator.CreateInstance(type));
        }
        return world;
    }
    static JsonStarSystemSave Save() {
        var bodies = Enumerable.Range(0, 36).Select(index => new JsonBodyOrbitSave {
            strName = index == 0 ? "Sol" : "body-" + index, fPeriod = 31556926, nEnvironmentMapID = index
        }).ToArray();
        return new JsonStarSystemSave { dfEpoch = 1000, aBOs = bodies,
            dictBOHierarchy = bodies.Skip(1).Select(body => body.strName + "=Sol").ToArray(),
            aStellarShips = Array.Empty<JsonStellarShipSave>(), aStellarDerelicts = Array.Empty<JsonStellarDerelictSave>() };
    }
    static string State(StarSystem world) => string.Join("|", world.aBOs.OrderBy(pair => pair.Key).Select(pair =>
        pair.Key + ":" + pair.Value.boParent?.strName + ":" + pair.Value.EnvironmentMapID));
    static int Drain(IEnumerator iterator, List<object> nonNull) {
        int yields = 0;
        try {
            while (iterator.MoveNext()) {
                yields++; if (iterator.Current != null) nonNull.Add(iterator.Current);
                Check(yields < 1000, "Native system restore failed to finish");
            }
        } finally { (iterator as IDisposable)?.Dispose(); }
        return yields;
    }
    static void Batching(Harmony production) {
        var original = World(); CrewSim.system = original; labels.Clear();
        var originalNonNull = new List<object>();
        int originalYields = Drain(original.Init(Save(), Array.Empty<JsonShip>()), originalNonNull);
        var originalLabels = labels.ToArray();
        foreach (string name in new[] { "SystemInitBatchProgressPatch", "SystemInitBatchPatch" })
            production.CreateClassProcessor(Production(name)).Patch();
        var batched = World(); CrewSim.system = batched; labels.Clear();
        IEnumerator iterator = batched.Init(Save(), Array.Empty<JsonShip>());
        Check(iterator.GetType() == Production("SystemInitBatching"), "Native restore did not use its production batching wrapper");
        var batchedNonNull = new List<object>();
        int batchedYields = Drain(iterator, batchedNonNull);
        Check(State(original) == State(batched), "Batching changed native body, hierarchy or map restoration");
        Check(labels.SequenceEqual(originalLabels), "Batching changed progress phase order");
        Check(batchedNonNull.SequenceEqual(originalNonNull), "Batching lost a native non-null yield");
        Check(batchedYields < originalYields && batchedYields > 0, "Cheap native restore yields were not coalesced");
        Check(World().Init(null, Array.Empty<JsonShip>()).GetType() != Production("SystemInitBatching"),
            "New-game fallback scheduling was wrapped");
        AccessTools.Field(Production("SystemInitBatchPatch"), "Enabled").SetValue(null, false);
        try {
            var disabled = World(); CrewSim.system = disabled;
            Check(disabled.Init(Save(), Array.Empty<JsonShip>()).GetType() != Production("SystemInitBatching"),
                "Disabled optimization changed native scheduling");
        } finally { AccessTools.Field(Production("SystemInitBatchPatch"), "Enabled").SetValue(null, true); }
    }
    static void Main() {
        var boundary = new Harmony("offline.load-compatibility.boundaries");
        var production = new Harmony("offline.load-compatibility.production");
        try {
            boundary.Patch(AccessTools.Method(typeof(LoadingScreen), nameof(LoadingScreen.SetProgressBar)), prefix: new HarmonyMethod(typeof(Program), nameof(Progress)));
            boundary.Patch(AccessTools.Method(typeof(LoadingScreen), nameof(LoadingScreen.GetProgress)), prefix: new HarmonyMethod(typeof(Program), nameof(GetProgress)));
            IndexedLookup(); Batching(production);
            Console.WriteLine(checks + " native compiler-closure and restore-coroutine compatibility checks passed; loading-screen presentation is a fixture boundary.");
        } finally { production.UnpatchSelf(); boundary.UnpatchSelf(); }
    }
}
