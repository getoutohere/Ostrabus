using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using HarmonyLib;
using Ostrabus.Inspection;
using Ostranauts.Core;
using Ostranauts.Core.Models;
using Ostranauts.Ships;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.SceneManagement;

static class Program {
    const string Owner = "com.ostranauts.template.inspection.memory";
    static int checks, nativeCalls;
    sealed class RefComparer : IEqualityComparer<UnityEngine.Object> {
        public bool Equals(UnityEngine.Object a, UnityEngine.Object b) => ReferenceEquals(a, b);
        public int GetHashCode(UnityEngine.Object obj) => RuntimeHelpers.GetHashCode(obj);
    }
    static readonly HashSet<UnityEngine.Object> dead = new HashSet<UnityEngine.Object>(new RefComparer());
    static readonly Exception nativeError = new InvalidOperationException("native fixture exception");
    static bool throwNative;
    static T Empty<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    static void Field(object obj, string name, object value) => AccessTools.Field(obj.GetType(), name).SetValue(obj, value);
    static void Check(bool ok, string message) { checks++; if (!ok) throw new Exception(message); }
    static Dictionary<string, object> Map(object obj) => (Dictionary<string, object>)obj;
    static bool IsNull(UnityEngine.Object obj) => ReferenceEquals(obj, null) || dead.Contains(obj);
    static bool Equal(UnityEngine.Object __0, UnityEngine.Object __1, ref bool __result) {
        __result = ReferenceEquals(__0, __1) || IsNull(__0) && IsNull(__1); return false;
    }
    static bool Unequal(UnityEngine.Object __0, UnityEngine.Object __1, ref bool __result) { Equal(__0, __1, ref __result); __result = !__result; return false; }
    static bool Long(ref long __result) { __result = 123456; return false; }
    static bool Name(ref string __result) { __result = "fixture"; return false; }
    static bool Objects(ref UnityEngine.Object[] __result) { __result = new UnityEngine.Object[] { Empty<Texture2D>(), Empty<Mesh>(), Empty<Material>() }; return false; }
    static bool ShaderId(ref int __result) { __result = 1; return false; }
    static IEnumerable<CodeInstruction> Body(IEnumerable<CodeInstruction> _) {
        yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(Program), nameof(NativeBody)));
        yield return new CodeInstruction(OpCodes.Ret);
    }
    static void NativeBody() { nativeCalls++; if (throwNative) throw nativeError; }
    static void Prefix(Harmony h, Type type, string method, string prefix, Type[] args = null) =>
        h.Patch(AccessTools.Method(type, method, args), prefix: new HarmonyMethod(typeof(Program), prefix));
    static int OwnedPatches() => Harmony.GetAllPatchedMethods().Count(m => Harmony.GetPatchInfo(m).Owners.Contains(Owner));
    static int Subscribers(string name) {
        var d = AccessTools.Field(typeof(SceneManager), name).GetValue(null) as Delegate;
        return d?.GetInvocationList().Count(x => x.Method.DeclaringType == typeof(MemoryDiagnosticHooks)) ?? 0;
    }
    static Ship Hull(string id) {
        var ship = Empty<Ship>(); Field(ship, "strRegID", id); Field(ship, "nLoadState", Ship.Loaded.Full);
        Field(ship, "mapICOs", new DictionaryArray<string, CondOwner>());
        ship.aTiles = new List<Tile>(); return ship;
    }
    static CondOwner CO(string id, string type, Ship ship = null) {
        var co = Empty<CondOwner>(); Field(co, "_strID", id); co.strName = type; Field(co, "_objShip", ship);
        co.mapConds = new Dictionary<string, Condition>();
        return co;
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    static WeakReference TemporarySample() {
        var co = CO("collectible", "TIL"); MemoryDiagnostics.SampleCO(co, "test-weak-only"); return new WeakReference(co);
    }
    static int Main() {
        var engine = new Harmony("offline.memory.engine");
        try {
            Prefix(engine, typeof(UnityEngine.Object), "op_Equality", nameof(Equal));
            Prefix(engine, typeof(UnityEngine.Object), "op_Inequality", nameof(Unequal));
            Prefix(engine, typeof(Shader), "PropertyToID", nameof(ShaderId), new[] { typeof(string) });
            foreach (string method in new[] { "GetMonoUsedSizeLong", "GetMonoHeapSizeLong", "GetTotalAllocatedMemoryLong", "GetTotalReservedMemoryLong", "GetTotalUnusedReservedMemoryLong", "GetRuntimeMemorySizeLong" })
                Prefix(engine, typeof(Profiler), method, nameof(Long));
            Prefix(engine, typeof(Resources), "FindObjectsOfTypeAll", nameof(Objects), new[] { typeof(Type) });
            engine.Patch(AccessTools.PropertyGetter(typeof(UnityEngine.Object), "name"), prefix: new HarmonyMethod(typeof(Program), nameof(Name)));

            DataHandler.mapCOs = new Dictionary<string, CondOwner>();
            CrewSim.COAnimators = new List<Animator> { null };
            AccessTools.Field(typeof(CrewSim), "aLoadedShips").SetValue(null, new List<Ship>());
            AccessTools.Field(typeof(MonoSingleton<AsyncShipLoader>), "_instance").SetValue(null, null);
            CrewSim.system = Empty<StarSystem>(); CrewSim.system.dictShips = new Dictionary<string, Ship>();
            Check(OwnedPatches() == 0 && !MemoryDiagnostics.Active, "Memory hooks installed before explicit start");
            var inactive = MemoryDiagnostics.Read(0, 10);
            Check(!(bool)Map(inactive)["active"] && OwnedPatches() == 0, "Reading status installed hooks");
            Check(AccessTools.Field(typeof(MonoSingleton<AsyncShipLoader>), "_instance").GetValue(null) == null, "Status created async singleton");
            Check(!typeof(MemoryDiagnosticHooks).IsDefined(typeof(HarmonyPatch), false), "PatchAll would install memory hooks");

            var world = Hull("WORLD"); var detached = Hull("DETACHED"); var destroyed = Hull("DESTROYED"); destroyed.bDestroyed = true;
            CrewSim.system.dictShips.Add("WORLD", world);
            var a = CO("a", "TIL", world); var b = CO("b", "Floor", detached); var c = CO("c", "Floor", destroyed);
            var d = CO("d", "TIL"); dead.Add(d);
            DataHandler.mapCOs.Add("a", a); DataHandler.mapCOs.Add("b", b); DataHandler.mapCOs.Add("c", c); DataHandler.mapCOs.Add("d", d);
            var census = Map(MemoryDiagnostics.Census());
            Check((int)census["scanned"] == 4 && (int)census["unityDestroyedWrappers"] == 1 && (int)census["deadAnimatorEntries"] == 1, "Census did not distinguish dead wrappers");
            var kinds = ((object[])census["ownerKinds"]).Cast<Dictionary<string, object>>().ToDictionary(x => (string)x["key"], x => (int)x["count"]);
            Check(kinds["world-ship"] == 1 && kinds["outside-world-registry"] == 1 && kinds["destroyed-ship"] == 1 && kinds["unowned"] == 1, "Ownership census mismatch");
            Check(DataHandler.mapCOs.Count == 4 && !a.bDestroyed, "Census changed game state");
            Check(AccessTools.Field(typeof(MonoSingleton<AsyncShipLoader>), "_instance").GetValue(null) == null, "Census created async singleton");

            MemoryDiagnostics.Start();
            Check(MemoryDiagnostics.Active && OwnedPatches() == 11, "Explicit start did not install expected 11 method hooks: " + OwnedPatches());
            Check(Subscribers("sceneLoaded") == 1 && Subscribers("sceneUnloaded") == 1, "Scene subscriptions missing");
            MemoryDiagnostics.Start(); Check(OwnedPatches() == 11 && Subscribers("sceneLoaded") == 1, "Repeated start duplicated hooks");
            var tile = Empty<Tile>(); Field(tile, "_coProps", a); tile.Destroy();
            Check(MemoryDiagnostics.TileDestroyCalls == 1 && DataHandler.mapCOs["a"] == a && tile.coProps == null, "Tile observation changed native cleanup");
            // Execute instrumented OnDestroy's real body (clock is null in fixture).
            AccessTools.Method(typeof(CondOwner), "OnDestroy").Invoke(a, null);
            Check(MemoryDiagnostics.UnityDestroyCalls == 1 && MemoryDiagnostics.WithoutManagedCleanup == 1 && MemoryDiagnostics.RegisteredAtUnityDestroy == 1, "Missing managed cleanup not counted");
            c.bDestroyed = true; AccessTools.Method(typeof(CondOwner), "OnDestroy").Invoke(c, null);
            Check(MemoryDiagnostics.UnityDestroyCalls == 2 && MemoryDiagnostics.WithoutManagedCleanup == 1, "Normal cleanup counted as skipped");
            Check(DataHandler.mapCOs.Count == 4, "Observer removed registered objects");

            // Replace only native engine-heavy bodies, preserving production observers.
            foreach (var method in new[] { AccessTools.Method(typeof(Ship), "Destroy"), AccessTools.Method(typeof(CrewSim), "EmptyScene"), AccessTools.Method(typeof(DataHandler), "Init"), AccessTools.Method(typeof(CondOwner), "Destroy") })
                engine.Patch(method, transpiler: new HarmonyMethod(typeof(Program), nameof(Body)));
            a.Destroy(); Check(MemoryDiagnostics.ManagedDestroyCalls == 1 && nativeCalls == 1, "Managed cleanup observation suppressed native call");
            world.Destroy(); Check(nativeCalls == 2, "Ship observer suppressed native call");
            CrewSim.objInstance = Empty<CrewSim>(); CrewSim.objInstance.EmptyScene(); Check(nativeCalls == 3, "Boundary observer suppressed native call");
            throwNative = true;
            Exception caught = null; try { world.Destroy(); } catch (Exception ex) { caught = ex; }
            Check(ReferenceEquals(caught, nativeError), "Ship finalizer swallowed or replaced native exception");
            caught = null; try { DataHandler.Init(); } catch (Exception ex) { caught = ex; }
            Check(ReferenceEquals(caught, nativeError), "Boundary finalizer swallowed or replaced native exception");
            throwNative = false;
            string records = SnapshotJson.Encode(MemoryDiagnostics.Read(0, 50));
            Check(records.Contains("Ship.Destroy") && records.Contains("DataHandler.Init:after") && records.Contains("InvalidOperationException"), "Lifecycle/exception records missing or not detached");

            var weak = TemporarySample();
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); // Offline fixture only.
            Check(!weak.IsAlive, "Diagnostic samples retained their COs strongly");
            for (int i = 0; i < 600; i++) MemoryDiagnostics.SampleCO(a, "bounded-test");
            MemoryDiagnostics.Snapshot();
            var latest = Map(((Dictionary<string, object>[])Map(MemoryDiagnostics.Read(0, 1))["items"])[0]["census"]);
            Check((int)Map(latest["weakSamples"])["count"] == 512, "Weak sample capacity unbounded");
            var assets = Map(MemoryDiagnostics.Assets());
            Check((int)assets["unityObjects"] == 3 && (long)assets["textureBytes"] == 123456 && (long)assets["meshBytes"] == 123456 && (long)assets["materialBytes"] == 123456, "Explicit asset breakdown incorrect");
            for (int i = 0; i < 270; i++) MemoryDiagnostics.Record("bounded-test", null);
            var history = Map(MemoryDiagnostics.Read(1, 1000));
            Check(((Dictionary<string, object>[])history["items"]).Length == 256, "Event ring unbounded");
            long nativeCount = MemoryDiagnostics.ManagedDestroyCalls;
            MemoryDiagnostics.Stop();
            Check(!MemoryDiagnostics.Active && OwnedPatches() == 0, "Stop left native memory hooks installed");
            Check(Subscribers("sceneLoaded") == 0 && Subscribers("sceneUnloaded") == 0, "Stop left scene subscriptions installed");
            Check(((System.Collections.IList)AccessTools.Field(typeof(MemoryDiagnostics), "samples").GetValue(null)).Count == 0, "Stop retained diagnostic weak samples");
            a.Destroy(); Check(MemoryDiagnostics.ManagedDestroyCalls == nativeCount, "Stopped trace still observes native work");
            MemoryDiagnostics.Stop(); Check(OwnedPatches() == 0, "Repeated stop reinstalled hooks");
            MemoryDiagnostics.Start(); Check(OwnedPatches() == 11, "Could not restart trace");
            MemoryDiagnostics.Stop(); Check(OwnedPatches() == 0, "Restart/stop leaked hooks");
            Check(MemoryInspection.IsControl("/memory/start") && MemoryInspection.IsControl("/memory/stop") && MemoryInspection.IsControl("/memory/snapshot") && MemoryInspection.IsControl("/memory/assets") && !MemoryInspection.IsControl("/memory"), "Control route matching incorrect");
            Console.WriteLine(checks + " memory diagnostics lifecycle/ownership/unpatch/weak-reference checks passed.");
            return 0;
        } finally { MemoryDiagnostics.Stop(); engine.UnpatchSelf(); }
    }
}

namespace Ostrabus {
    internal static class OstrabusPlugin { internal static readonly TestLog Logger = new TestLog(); }
    internal sealed class TestLog { internal void LogInfo(string value) {} internal void LogWarning(string value) { throw new Exception(value); } }
}
namespace Ostrabus.Inspection {
    internal sealed class InspectionApi {
        internal sealed class Reply { internal readonly int Status; internal readonly object Body; internal Reply(int status, object body) { Status = status; Body = body; } }
    }
}
