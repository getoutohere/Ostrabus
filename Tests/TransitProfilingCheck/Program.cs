using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using System.Threading;
using HarmonyLib;
using Ostrabus.Inspection;
using Ostrabus.Optimizations;
using Ostranauts.Ships;
using UnityEngine;

static class Program {
    const string Owner = "com.ostranauts.template.profiling.transit";
    static int checks, creates, frame;
    static BarterZoneShip host;
    static bool throwNative;
    static readonly Exception Error = new InvalidOperationException("native fixture error");
    static T Empty<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    static void Check(bool ok, string message) { checks++; if (!ok) throw new Exception(message); }
    static int Owned() => Harmony.GetAllPatchedMethods().Count(m => Harmony.GetPatchInfo(m).Owners.Contains(Owner));
    static bool Frame(ref int __result) { __result = frame; return false; }
    static bool Shader(ref int __result) { __result = 1; return false; }
    static bool Quiet() => false;
    static bool Create(ref GameObject __result) { creates++; Thread.Sleep(8); __result = null; return false; }
    static bool Next() {
        host.CreatePart(new JsonItem(), "fixture", false);
        if (throwNative) throw Error;
        return false;
    }
    static IEnumerable<CodeInstruction> Body(IEnumerable<CodeInstruction> _) {
        yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(Program), nameof(Next)));
        yield return new CodeInstruction(OpCodes.Ret);
    }
    static MethodInfo MoveNext(bool optimized = false) {
        var factory = optimized ? AccessTools.Method(typeof(BarterInitPatch), "Initialize") : AccessTools.Method(typeof(BarterZoneShip), "Init");
        var type = factory.GetCustomAttribute<IteratorStateMachineAttribute>().StateMachineType;
        return AccessTools.Method(type, "MoveNext");
    }
    static int Main() {
        var fixture = new Harmony("offline.transit.profiling");
        try {
            fixture.Patch(AccessTools.PropertyGetter(typeof(Time), "frameCount"), prefix: new HarmonyMethod(typeof(Program), nameof(Frame)));
            fixture.Patch(AccessTools.Method(typeof(Shader), "PropertyToID", new[] { typeof(string) }), prefix: new HarmonyMethod(typeof(Program), nameof(Shader)));
            fixture.Patch(AccessTools.Method(typeof(CrewSim), "Update"), prefix: new HarmonyMethod(typeof(Program), nameof(Quiet)));
            fixture.Patch(AccessTools.Method(typeof(Ship), "CreatePart"), prefix: new HarmonyMethod(typeof(Program), nameof(Create)));
            fixture.Patch(MoveNext(), transpiler: new HarmonyMethod(typeof(Program), nameof(Body)));
            fixture.Patch(MoveNext(true), transpiler: new HarmonyMethod(typeof(Program), nameof(Body)));
            LoadProfiling.Enabled = false;
            host = Empty<BarterZoneShip>(); AccessTools.Field(typeof(Ship), "strRegID").SetValue(host, "fixture");
            Check(!TransitProfiling.Active && Owned() == 0, "Startup installed transit hooks");
            TransitProfiling.Snapshot(); Check(Owned() == 0, "GET installed transit hooks");
            Check(!typeof(TransitProfiling).IsDefined(typeof(HarmonyPatch), false), "PatchAll would install transit profiler");
            TransitProfiling.Start();
            Check(TransitProfiling.Active && LoadProfiling.Enabled && Owned() == 13, "Start failed to install expected 13 methods: " + Owned());
            TransitProfiling.Start(); Check(Owned() == 13, "Repeated start duplicated hooks");
            host.CreatePart(new JsonItem(), "outside", false);
            var iterator = host.Init(50);
            Check(!iterator.MoveNext() && creates == 2, "Root observation changed native calls/result");
            string data = SnapshotJson.Encode(TransitProfiling.Snapshot());
            Check(data.Contains("BarterZoneShip.Init.MoveNext") && data.Contains("Ship.CreatePart"), "Missing coroutine/subphase timings");
            Check(data.Contains("ship=fixture") && data.Contains("frame="), "Missing frame/hull correlation");
            Check(!BarterInitPatch.Initialize(host, 50).MoveNext(), "Optimized iterator observation changed result");
            data = SnapshotJson.Encode(TransitProfiling.Snapshot());
            Check(data.Contains("BarterInitPatch.Initialize.MoveNext") && data.Contains("ship=fixture"), "Missing optimized iterator timing/host");
            throwNative = true;
            Exception caught = null; try { host.Init(50).MoveNext(); } catch (Exception ex) { caught = ex; }
            Check(ReferenceEquals(caught, Error), "Profiler swallowed/replaced native exception");
            Check((int)AccessTools.Field(typeof(TransitProfiling), "loadingDepth").GetValue(null) == 0, "Profiler retained iterator context on exception");
            throwNative = false;
            var update = AccessTools.Method(typeof(CrewSim), "Update"); var crew = Empty<CrewSim>();
            update.Invoke(crew, null); Thread.Sleep(65); frame++; update.Invoke(crew, null);
            var snapshot = (Dictionary<string, object>)TransitProfiling.Snapshot();
            Check(((object[])snapshot["frameGaps"]).Length >= 1, "Frame stall not captured");
            Check((int)snapshot["diagnosticErrors"] == 0, "Diagnostic failures occurred");
            TransitProfiling.Stop();
            Check(!TransitProfiling.Active && !LoadProfiling.Enabled && Owned() == 0, "Stop left hooks or changed launch profiling flag");
            Check(SnapshotJson.Encode(TransitProfiling.Snapshot()).Contains("completed"), "Stop did not retain completed results");
            Check((int)AccessTools.Field(typeof(TransitProfiling), "loadingDepth").GetValue(null) == 0, "Stop retained scope");
            TransitProfiling.Stop(); Check(Owned() == 0, "Repeated stop changed hooks");
            TransitProfiling.Start(); TransitProfiling.Stop(); Check(Owned() == 0 && !LoadProfiling.Enabled, "Restart/stop leaked hooks");
            Console.WriteLine(checks + " transit profiler binding/scoping/exception/frame/unpatch checks passed.");
            return 0;
        } finally { TransitProfiling.Stop(); fixture.UnpatchSelf(); }
    }
}
namespace Ostrabus {
    internal static class OstrabusPlugin { internal static readonly TestLog Logger = new TestLog(); }
    internal sealed class TestLog { internal void LogInfo(string message) {} internal void LogWarning(string message) { throw new Exception(message); } }
}
