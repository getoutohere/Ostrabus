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
static class Program {
    static bool ShaderId(ref int __result) {
        __result = 1;
        return false;
    }

    static int Main(string[] args) {
        if (args.Length != 1) throw new ArgumentException("Pass a generated Ostrabus.dll.");
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
