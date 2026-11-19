using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using System.IO;
using BepInEx.Logging;
using HarmonyLib;
using Newtonsoft.Json;
using System.Security.Cryptography;

static class Program {
    static string Hash(string path) {
        using (var sha = SHA256.Create())
        using (var file = File.OpenRead(path))
            return BitConverter.ToString(sha.ComputeHash(file)).Replace("-", "").ToLowerInvariant();
    }
    static string TypeName(Type type) {
        if (type.IsByRef) return TypeName(type.GetElementType()) + "&";
        if (type.IsArray) return TypeName(type.GetElementType()) + "[" + new string(',', type.GetArrayRank() - 1) + "]";
        if (type.IsGenericParameter) return "!" + type.GenericParameterPosition;
        return type.IsGenericType
            ? type.GetGenericTypeDefinition().FullName.Replace('+', '/') + "<" + string.Join(",", type.GetGenericArguments().Select(TypeName)) + ">"
            : type.FullName.Replace('+', '/');
    }
    static object Target(MethodBase method) => new {
        assembly = method.DeclaringType.Assembly.GetName().Name,
        type = TypeName(method.DeclaringType), name = method.Name,
        parameters = method.GetParameters().Select(p => TypeName(p.ParameterType)).ToArray(),
        returns = method is MethodInfo info ? TypeName(info.ReturnType) : "System.Void"
    };
    static bool ShaderId(ref int __result) { __result = 1; return false; }
    static int Main(string[] args) {
        if (args.Length==1 && args[0]=="hull-interop") return HullInterop.Run();
        var engine = new Harmony("ostrabus.offline.binding.engine");
        engine.Patch(AccessTools.Method(typeof(UnityEngine.Shader), nameof(UnityEngine.Shader.PropertyToID), new[] { typeof(string) }),
            prefix: new HarmonyMethod(typeof(Program), nameof(ShaderId)));
        var assembly = typeof(Ostrabus.OstrabusPlugin).Assembly;
        // The full build enables obituaries at startup. Bind those observers
        // alongside existing lifecycle patches without opening files or a scene.
        assembly.GetType("Ostrabus.Diagnostics.Obituaries.ObituaryRecorder", throwOnError: false)?
            .GetField("Enabled", BindingFlags.Static | BindingFlags.NonPublic)?.SetValue(null, true);
        var source = new ManualLogSource("CompatibilityCheck");
        int warnings = 0, failures = 0, classes = 0, skipped = 0;
        var report = new List<object>();
        source.LogEvent += (sender, entry) => {
            if ((entry.Level & (LogLevel.Warning | LogLevel.Error | LogLevel.Fatal)) != 0) {
                warnings++; Console.WriteLine("PATCH WARNING: " + entry.Data);
            }
        };
        typeof(Ostrabus.OstrabusPlugin).GetField("Logger", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, source);
        var h = new Harmony("ostrabus.offline.all-bindings");
        try {
            // Request-only inspectors do not carry Harmony attributes. Validate
            // their private native field contracts without creating Unity objects.
            try {
                var layout = assembly.GetType("Ostrabus.Inspection.NavLayoutInspection", throwOnError: true);
                System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(layout.TypeHandle);
                Console.WriteLine("Nav layout inspection: native field bindings initialized.");
                var fine = assembly.GetType("Ostrabus.QoL.CoursePlot.CourseFineControls", throwOnError: false);
                if (fine != null) {
                    System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(fine.TypeHandle);
                    Console.WriteLine("Course fine controls: native precision field bindings initialized.");
                }
            } catch (Exception error) {
                failures++; Console.WriteLine("FAILED nav layout inspection: " + error);
            }
            foreach (var type in assembly.GetTypes().Where(t => t.GetCustomAttributes(typeof(HarmonyPatch), false).Length > 0).OrderBy(t => t.FullName)) {
                try {
                    var targets = h.CreateClassProcessor(type).Patch();
                    // ClassProcessor.Patch returns generated replacement methods.
                    // Report original targets from Harmony's installed patch table.
                    var originals = h.GetPatchedMethods().Where(method => {
                        var info = Harmony.GetPatchInfo(method);
                        return info.Prefixes.Concat(info.Postfixes).Concat(info.Transpilers).Concat(info.Finalizers)
                            .Any(patch => patch.PatchMethod.DeclaringType == type);
                    });
                    report.Add(new { patch = type.FullName, targets = originals.Select(Target).ToArray() });
                    if (targets == null || targets.Count == 0) skipped++;
                    else { classes++; Console.WriteLine("BOUND " + type.FullName + " -> " + targets.Count); }
                } catch (Exception error) {
                    failures++; Console.WriteLine("FAILED " + type.FullName + ": " + error);
                }
            }
            if (args.Length == 2 && args[0] == "--companion") {
                if (assembly.GetType("Ostrabus.ApartmentRefuel.Plugin") != null) { failures++; Console.WriteLine("FAILED companion was compiled into omnibus"); }
                var companion = Assembly.LoadFrom(System.IO.Path.GetFullPath(args[1]));
                foreach (var type in companion.GetTypes().Where(t => t.GetCustomAttributes(typeof(HarmonyPatch), false).Length > 0)) {
                    try {
                        var targets = h.CreateClassProcessor(type).Patch();
                        if (targets == null || targets.Count != 1) throw new Exception("Companion target count changed");
                        classes++; Console.WriteLine("BOUND COMPANION " + type.FullName);
                    } catch (Exception error) { failures++; Console.WriteLine("FAILED COMPANION " + type.FullName + ": " + error); }
                }
            }
            Console.WriteLine("Patch binding audit: " + classes + " classes bound; " + skipped + " disabled by Prepare; " + failures + " failed; " + warnings + " warnings.");
            int reportIndex = Array.IndexOf(args, "--report");
            if (reportIndex >= 0) File.WriteAllText(args[reportIndex + 1], JsonConvert.SerializeObject(new {
                scope = "Actual offline Harmony targets; binding is not a semantic or live validation claim.",
                gameAssembly = typeof(Ship).Assembly.Location, pluginAssembly = assembly.Location,
                gameMvid = typeof(Ship).Module.ModuleVersionId, pluginMvid = assembly.ManifestModule.ModuleVersionId,
                gameSha256 = Hash(typeof(Ship).Assembly.Location), pluginSha256 = Hash(assembly.Location),
                classes, skipped, failures, warnings, patches = report
            }, Formatting.Indented));
            return failures == 0 && warnings == 0 ? 0 : 1;
        } finally { h.UnpatchSelf(); engine.UnpatchSelf(); source.Dispose(); }
    }
}
