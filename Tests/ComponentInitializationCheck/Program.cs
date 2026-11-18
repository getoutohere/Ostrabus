using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using Ostrabus.MiscBugFixes;
using UnityEngine;

// Run native SetData. Only Unity component creation and behavior/UI setup are
// substituted; the asset-component loop and type lookup execute in the game DLL.
static class Program {
    static int checks;
    static GameObject gameObject;
    static readonly List<Type> added = new List<Type>();
    static bool failCreation;
    static readonly Exception creationError = new InvalidOperationException("component creation fixture failure");
    static T Empty<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    static void Check(bool ok, string message) { checks++; if (!ok) throw new Exception(message); }
    static bool Quiet() => false;
    static void Noop() {}
    static bool GameObjectOf(ref GameObject __result) { __result = gameObject; return false; }
    static bool Add(Type componentType, ref Component __result) {
        if (failCreation) throw creationError;
        added.Add(componentType);
        __result = componentType == null ? null : (Component)FormatterServices.GetUninitializedObject(componentType);
        return false;
    }
    static CondOwner Owner() {
        var owner = Empty<CondOwner>();
        AccessTools.Field(typeof(CondOwner), "_fieldsInitialized").SetValue(owner, true);
        foreach (var field in typeof(CondOwner).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)) {
            var type = field.FieldType;
            if (type != typeof(string) && !type.IsAbstract && !type.IsArray && typeof(IEnumerable).IsAssignableFrom(type) && type.GetConstructor(Type.EmptyTypes) != null)
                field.SetValue(owner, Activator.CreateInstance(type));
        }
        var clock = AccessTools.Field(typeof(CondOwner), nameof(CondOwner.Clock));
        clock.SetValue(owner, Activator.CreateInstance(clock.FieldType));
        return owner;
    }
    static int Main() {
        var fixture = new Harmony("offline.component-initialization.fixture");
        var oldHook = new Harmony("offline.component-initialization.old-hook");
        var repair = new Harmony("offline.component-initialization.repair");
        try {
            gameObject = Empty<GameObject>();
            fixture.Patch(AccessTools.PropertyGetter(typeof(Component), "gameObject"), prefix: new HarmonyMethod(typeof(Program), nameof(GameObjectOf)));
            fixture.Patch(AccessTools.Method(typeof(GameObject), "AddComponent", new[] { typeof(Type) }), prefix: new HarmonyMethod(typeof(Program), nameof(Add)));
            foreach (var method in new[] { "CheckInteractionFlag", "SetUpBehaviours" })
                fixture.Patch(AccessTools.Method(typeof(CondOwner), method), prefix: new HarmonyMethod(typeof(Program), nameof(Quiet)));
            var source = JArray.Parse(File.ReadAllText(Ostrabus.Development.InstallationPaths.Data + "condowners/condowners.json"));
            var definitions = source.OfType<JObject>().Where(row => row["aComponents"] != null).ToArray();
            Check(definitions.Length > 0, "No authored component definitions found.");
            var setData = AccessTools.Method(typeof(CondOwner), nameof(CondOwner.SetData));
            var native = new Dictionary<string, Type[]>();
            // Capture every unpatched baseline before detouring SetData once.
            // Removing a Harmony owner can retain a regenerated method body.
            foreach (var row in definitions) {
                string[] names = row["aComponents"].Values<string>().ToArray();
                var definition = new JsonCondOwner { strName = (string)row["strName"], aComponents = names, nStackLimit = 1 };
                added.Clear(); Owner().SetData(definition, false);
                Check(added.Count == names.Length && added.All(type => type != null), "Native initializer failed: " + definition.strName);
                native.Add(definition.strName, added.ToArray());
            }
            oldHook.Patch(setData, prefix: new HarmonyMethod(typeof(Program), nameof(Noop)));
            foreach (var row in definitions) {
                string[] names = row["aComponents"].Values<string>().ToArray();
                var definition = new JsonCondOwner { strName = (string)row["strName"], aComponents = names, nStackLimit = 1 };
                added.Clear(); Owner().SetData(definition, false);
                Check(added.Count == names.Length && added.All(type => type == null), "Original prefix did not reproduce lost assembly context.");
            }
            oldHook.UnpatchSelf();
            repair.CreateClassProcessor(typeof(LiveMassInitializationPatch)).Patch();
            foreach (var row in definitions) {
                string[] names = row["aComponents"].Values<string>().ToArray();
                var definition = new JsonCondOwner { strName = (string)row["strName"], aComponents = names, nStackLimit = 1 };
                added.Clear(); Owner().SetData(definition, false);
                Check(added.SequenceEqual(native[definition.strName]), "Repair changed/missed authored component creation: " + definition.strName);
                Check(LiveMass.Initializing == 0, "Initialization scope leaked.");
                Console.WriteLine(definition.strName + ": native OK; old hook lost components; repaired hook matches native.");
            }
            foreach (Type type in new[] { typeof(LootSpawner), typeof(LootSpawnerLot), typeof(string), typeof(int), typeof(List<LootSpawner>), typeof(LootSpawner[]) }) {
                Check(LiveMassInitializationPatch.ComponentType(type.AssemblyQualifiedName) == type, "Qualified type resolution changed: " + type);
                Check(LiveMassInitializationPatch.ComponentType(type.FullName) == type, "Bare type resolution changed: " + type);
            }
            Check(LiveMassInitializationPatch.ComponentType("NoSuchNativeComponent") == null, "Unknown component was substituted.");
            Exception caught = null;
            try {
                failCreation = true;
                Owner().SetData(new JsonCondOwner { strName = "failure", aComponents = new[] { "LootSpawner" }, nStackLimit = 1 }, false);
            } catch (Exception ex) { caught = ex; }
            finally { failCreation = false; }
            Check(ReferenceEquals(caught, creationError), "Component failure was swallowed or replaced.");
            Check(LiveMass.Initializing == 0, "Failed component creation leaked the mass initialization scope.");
            Owner().SetData(null, false);
            Check(LiveMass.Initializing == 0, "Null definition leaked the mass initialization scope.");
            Console.WriteLine(checks + " native component initialization and resolution checks passed.");
            return 0;
        } finally { repair.UnpatchSelf(); oldHook.UnpatchSelf(); fixture.UnpatchSelf(); }
    }
}
