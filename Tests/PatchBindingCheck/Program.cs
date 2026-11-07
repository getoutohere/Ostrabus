using System;
using System.Linq;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;

static class Program {
    static bool ShaderId(ref int __result) { __result = 1; return false; }
    static int Main() {
        var engine = new Harmony("ostrabus.offline.binding.engine");
        engine.Patch(AccessTools.Method(typeof(UnityEngine.Shader), nameof(UnityEngine.Shader.PropertyToID), new[] { typeof(string) }),
            prefix: new HarmonyMethod(typeof(Program), nameof(ShaderId)));
        var assembly = typeof(Ostrabus.OstrabusPlugin).Assembly;
        var source = new ManualLogSource("CompatibilityCheck");
        int warnings = 0, failures = 0, classes = 0, skipped = 0;
        source.LogEvent += (sender, entry) => {
            if ((entry.Level & (LogLevel.Warning | LogLevel.Error | LogLevel.Fatal)) != 0) {
                warnings++; Console.WriteLine("PATCH WARNING: " + entry.Data);
            }
        };
        typeof(Ostrabus.OstrabusPlugin).GetField("Logger", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, source);
        var h = new Harmony("ostrabus.offline.all-bindings");
        try {
            foreach (var type in assembly.GetTypes().Where(t => t.GetCustomAttributes(typeof(HarmonyPatch), false).Length > 0).OrderBy(t => t.FullName)) {
                try {
                    var targets = h.CreateClassProcessor(type).Patch();
                    if (targets == null || targets.Count == 0) skipped++;
                    else { classes++; Console.WriteLine("BOUND " + type.FullName + " -> " + targets.Count); }
                } catch (Exception error) {
                    failures++; Console.WriteLine("FAILED " + type.FullName + ": " + error);
                }
            }
            Console.WriteLine("Patch binding audit: " + classes + " classes bound; " + skipped + " disabled by Prepare; " + failures + " failed; " + warnings + " warnings.");
            return failures == 0 && warnings == 0 ? 0 : 1;
        } finally { h.UnpatchSelf(); engine.UnpatchSelf(); source.Dispose(); }
    }
}
