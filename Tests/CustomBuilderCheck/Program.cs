using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Collections;
using BepInEx.Logging;
using HarmonyLib;
using Newtonsoft.Json.Linq;

// Deliberately no reference to a normal Ostrabus build: load only the generated
// DLL passed by the caller. Binding runs inside the game's bundled Mono runtime.
static partial class Program {
    static bool ShaderId(ref int __result) {
        __result = 1;
        return false;
    }
    static int motionEntries;
    static bool MotionProbe() { motionEntries++; return false; }
    static void SffDecisionComposition(Assembly assembly, Harmony harmony) {
        var observer = assembly.GetType("Ostrabus.Orbital.OrbitalStepObservationPatch", true);
        var advance = assembly.GetType("Ostrabus.Orbital.SffAdvance", true);
        var orbital = assembly.GetType("Ostrabus.Orbital.OrbitalSimulation", true);
        var simulation = assembly.GetType("Ostrabus.Orbital.SffSimulation", true);
        var enabled = AccessTools.Property(orbital, "Enabled");
        var stepping = AccessTools.Property(advance, "Active");
        var gui = AccessTools.Field(typeof(GUIFFWD), "bFFWDActive");
        object priorEnabled = enabled.GetValue(null), priorStepping = stepping.GetValue(null), priorGui = gui.GetValue(null);
        var begin = AccessTools.Method(simulation, "Begin");
        harmony.Patch(begin, prefix: new HarmonyMethod(typeof(Program), nameof(MotionProbe)));
        try {
            var world = (StarSystem)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(StarSystem));
            enabled.SetValue(null, true); stepping.SetValue(null, true); gui.SetValue(null, true);
            motionEntries = 0;
            AccessTools.Method(observer, "Prefix").Invoke(null, new object[] { world, .5d, null });
            if (motionEntries != 1) throw new Exception("Generated SFF build drops sub-minute decision-step physics.");
            stepping.SetValue(null, false);
            AccessTools.Method(observer, "Prefix").Invoke(null, new object[] { world, .5d, null });
            if (motionEntries != 1) throw new Exception("Generated SFF build captures ordinary short updates.");
            gui.SetValue(null, false);
            AccessTools.Method(observer, "Prefix").Invoke(null, new object[] { world, 1500d, null });
            if (motionEntries != 1) throw new Exception("Generated SFF build captured an untagged non-GUI update.");
            var boundary = assembly.GetType("Ostrabus.Orbital.JumpBoundary", true);
            using ((IDisposable)Activator.CreateInstance(boundary, BindingFlags.Instance | BindingFlags.NonPublic, null, new object[] { "pass" }, null)) {
                AccessTools.Method(observer, "Prefix").Invoke(null, new object[] { world, 1500d, null });
                stepping.SetValue(null, true);
                AccessTools.Method(observer, "Prefix").Invoke(null, new object[] { world, .5d, null });
                if (motionEntries != 3 || (bool)gui.GetValue(null)) throw new Exception("Generated PASS build drops tagged long/short physics or fakes the GUI flag.");
            }
            Console.WriteLine("Custom SFF/PASS composition: tagged short/long windows execute; ordinary untagged updates retain their gates.");
        } finally {
            harmony.Unpatch(begin, HarmonyPatchType.Prefix, harmony.Id);
            stepping.SetValue(null, priorStepping); enabled.SetValue(null, priorEnabled); gui.SetValue(null, priorGui);
        }
    }

    static Action<double> worldCheck;
    static bool WorldProbe(double fTimeDelta) { worldCheck(fTimeDelta); return false; }
    static void CollisionJumpComposition(Assembly assembly, Harmony harmony) {
        var boundary = assembly.GetType("Ostrabus.Orbital.JumpBoundary", true);
        var orbital = assembly.GetType("Ostrabus.Orbital.OrbitalSimulation", true);
        var advance = assembly.GetType("Ostrabus.Orbital.SffAdvance", true);
        var update = AccessTools.Method(typeof(StarSystem), nameof(StarSystem.Update));
        harmony.Patch(update, prefix: new HarmonyMethod(typeof(Program), nameof(WorldProbe)));
        try {
            if ((bool)AccessTools.Property(orbital, "Enabled").GetValue(null)) throw new Exception("Collision-only build enabled orbital simulation.");
            int calls = 0;
            worldCheck = seconds => {
                calls++;
                if (seconds != 30 || !(bool)AccessTools.Property(boundary, "Active").GetValue(null) || GUIFFWD.Active)
                    throw new Exception("Collision-only PASS lost its discontinuity tag or altered native progression.");
            };
            var world = (StarSystem)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(StarSystem));
            AccessTools.Method(advance, "Pass").Invoke(null, new object[] { world, 30d });
            if (calls != 1 || (bool)AccessTools.Property(boundary, "Active").GetValue(null)) throw new Exception("Collision-only PASS leaked state or changed world-update count.");
            Console.WriteLine("Custom collision-only composition: PASS history boundary works with orbital simulation disabled.");
        } finally { worldCheck = null; harmony.Unpatch(update, HarmonyPatchType.Prefix, harmony.Id); }
    }

    static int Main(string[] args) {
        if (args.Length != 1) throw new ArgumentException("Pass a generated Ostrabus.dll.");
        var orbitalVelocityCheck = CaptureOrbitalVelocityControl();
        var engine = new Harmony("ostrabus.custom-check.engine");
        engine.Patch(
            AccessTools.Method(typeof(UnityEngine.Shader), nameof(UnityEngine.Shader.PropertyToID),
                new[] { typeof(string) }),
            prefix: new HarmonyMethod(typeof(Program), nameof(ShaderId)));

        var assembly = Assembly.LoadFrom(Path.GetFullPath(args[0]));
        var directory = new DirectoryInfo(Path.GetDirectoryName(Path.GetFullPath(args[0])));
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "selection.json")))
            directory = directory.Parent;
        if (directory == null)
            throw new ArgumentException("No generated selection.json found beside/above the DLL.");

        var plan = JObject.Parse(File.ReadAllText(Path.Combine(directory.FullName, "selection.json")));
        var features = plan["included"].Values<string>().ToArray();
        var source = new ManualLogSource("CustomBindingCheck");
        int warnings = 0, failures = 0, bound = 0, skipped = 0;
        source.LogEvent += (sender, entry) => {
            if ((entry.Level & (LogLevel.Warning | LogLevel.Error | LogLevel.Fatal)) != 0) {
                warnings++;
                Console.WriteLine("PATCH WARNING: " + entry.Data);
            }
        };
        assembly.GetType("Ostrabus.OstrabusPlugin", true)
            .GetField("Logger", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, source);

        var harmony = new Harmony("ostrabus.custom-check.bindings");
        try {
            var rows = ((IEnumerable)assembly.GetType("Ostrabus.PdaSettings.SettingsRegistry", true)
                .GetField("All", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null))
                .Cast<object>()
                .Select(row => (string)row.GetType()
                    .GetField("Id", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(row))
                .OrderBy(id => id).ToArray();
            var expected = (plan["pdaAssets"].Value<bool>()
                ? plan["settings"].Values<string>()
                : Enumerable.Empty<string>()).OrderBy(id => id).ToArray();
            if (!rows.SequenceEqual(expected)) throw new Exception("Runtime PDA rows differ from selected features.");

            // Reflection avoids the shipped Harmony reference's framework-only
            // ILGenerator identity while this harness targets netstandard.
            var read = typeof(PatchProcessor).GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Single(method => method.Name == "GetOriginalInstructions" &&
                    method.GetParameters().Length == 2 && !method.GetParameters()[1].ParameterType.IsByRef);
            var code = (System.Collections.Generic.IEnumerable<CodeInstruction>)read.Invoke(null, new object[] {
                AccessTools.Method(assembly.GetType("Ostrabus.OstrabusPlugin", true), "Awake"), null
            });
            var registrations = code.Where(instruction => instruction.opcode == OpCodes.Ldtoken)
                .Select(instruction => (Type)instruction.operand).ToArray();
            if (!registrations.Select(type => type.FullName).OrderBy(name => name)
                .SequenceEqual(plan["patches"].Select(patch => patch["type"].Value<string>()).OrderBy(name => name)))
                throw new Exception("Runtime bootstrap differs from selected patch classes.");

            foreach (var type in registrations) {
                try {
                    var targets = harmony.CreateClassProcessor(type).Patch();
                    if (targets == null || targets.Count == 0) skipped++;
                    else bound++;
                } catch (Exception error) {
                    failures++;
                    Console.WriteLine("FAILED " + type.FullName + ": " + error);
                }
            }

            if (features.Contains("sff")) SffDecisionComposition(assembly, harmony);
            orbitalVelocityCheck(features.Contains("orbital-velocity"));
            if (features.Contains("hull-collisions") && !features.Contains("sff")) CollisionJumpComposition(assembly, harmony);

            foreach (var item in new[] {
                new[] { "em-propagation", "Ostrabus.EMPropagation.EmPropagation", "com.ostranauts.template.em-propagation" },
                new[] { "sensors", "Ostrabus.Sensors.SensorContacts", "com.ostranauts.template.sensors" },
                new[] { "worktrace", "Ostrabus.Diagnostics.WorkTrace", "com.ostranauts.template.diagnostics.work" }
            }) {
                if (!features.Contains(item[0])) continue;
                var type = assembly.GetType(item[1], true);
                try {
                    AccessTools.Method(type, "SetEnabled").Invoke(null, new object[] { true });
                    if (!Harmony.GetAllPatchedMethods().Any(method => Harmony.GetPatchInfo(method).Owners.Contains(item[2])))
                        throw new Exception("Dynamic feature installed no patches: " + item[0]);
                } catch (Exception error) {
                    failures++;
                    Console.WriteLine("FAILED dynamic " + item[0] + ": " + error);
                } finally {
                    AccessTools.Method(type, "Stop").Invoke(null, null);
                }
                if (Harmony.GetAllPatchedMethods().Any(method => Harmony.GetPatchInfo(method).Owners.Contains(item[2])))
                    throw new Exception("Dynamic feature left hooks installed: " + item[0]);
            }

            Console.WriteLine($"Custom patch binding: {bound} classes bound; {skipped} disabled by Prepare; {failures} failed; {warnings} warnings.");
            return failures == 0 && warnings == 0 ? 0 : 1;
        } finally {
            harmony.UnpatchSelf();
            engine.UnpatchSelf();
            source.Dispose();
        }
    }
}
