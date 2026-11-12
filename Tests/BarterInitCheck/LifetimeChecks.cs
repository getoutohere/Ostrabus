using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using Ostrabus.Optimizations;
using Ostranauts.Core;
using Ostranauts.Ships;
using UnityEngine;

static partial class Program {
    static readonly HashSet<UnityEngine.Object> destroyed = new HashSet<UnityEngine.Object>(new RefComparer());
    static Action<CondOwner> afterPost;
    static int stops, disposals;
    static bool SameObject(UnityEngine.Object a, UnityEngine.Object b) {
        if (ReferenceEquals(a, null)) return ReferenceEquals(b, null) || destroyed.Contains(b);
        if (ReferenceEquals(b, null)) return destroyed.Contains(a);
        return ReferenceEquals(a, b);
    }
    static JsonItem[] TwoItems() => new[] { new JsonItem { strID = "a" }, new JsonItem { strID = "b" } };
    static void Invalidate(CondOwner co) {
        destroyed.Add(co);
        if (objects.TryGetValue(co, out var go)) destroyed.Add(go);
    }
    static void CheckNativeDestroyedSnapshot() {
        var ship = Hull(TwoItems());
        afterPost = co => { if (co.strID == "a") Invalidate(Items(ship)["b"]); };
        try {
            Run(ship);
            throw new Exception("Native post-load snapshot did not reproduce the invalid Unity component access");
        } catch (NullReferenceException error) {
            Check(error.Message == "fixture destroyed Unity component" && !ship.FullyLoaded,
                "Native snapshot failure changed or published readiness");
        } finally { afterPost = null; }
    }
    static IEnumerator AtFinalization(BarterZoneShip ship) {
        var iterator = ship.Init(100);
        int count = 0;
        while (!events.Contains("zones")) {
            Check(iterator.MoveNext() && ++count < 10000, "No finalization yield reached");
            frame++;
        }
        Check(!ship.FullyLoaded, "Finalization already published readiness");
        return iterator;
    }
    static void CheckInterruptedInitialization(Harmony fixture, Harmony production) {
        var originalWorld = CrewSim.system;
        int previousQuota = quota;
        try {
            quota = 1; // Pause before every item/phase, including snapshot entries.
            for (int which = 0; which < 3; which++) {
                var ship = Hull(TwoItems());
                var iterator = AtFinalization(ship);
                int before = events.Count;
                if (which == 0) CrewSim.system = Empty<StarSystem>();
                else if (which == 1) ship.json = new JsonShip { aItems = TwoItems() };
                else destroyed.Add(ship.gameObject);
                Check(!iterator.MoveNext(), "Invalidated barter lifetime continued loading");
                Check(events.Count == before && !ship.FullyLoaded, "Invalidated copy ran more work or published readiness");
                (iterator as IDisposable)?.Dispose();
                CrewSim.system = originalWorld;
            }
            var discarded = Hull(TwoItems()); discarded.json = null;
            Check(!discarded.Init(100).MoveNext() && !discarded.FullyLoaded, "Already destroyed copy attempted initialization");
            var lost = Hull(TwoItems()); var pending = AtFinalization(lost);
            Invalidate(Items(lost)["a"]);
            while (pending.MoveNext()) frame++;
            Check(lost.FullyLoaded && !events.Contains("post:a") && events.Contains("post:b"),
                "Dead snapshot entry was touched or prevented a valid remaining item from loading");
            var consumed = Hull(TwoItems());
            afterPost = Invalidate;
            Run(consumed);
            Check(consumed.FullyLoaded, "Self-consumed post-load item caused a dead gameObject access");
            afterPost = null;
            CheckResetCancellation(fixture, production);
        } finally { afterPost = null; quota = previousQuota; CrewSim.system = originalWorld; }
    }
    sealed class Pending : IEnumerator, IDisposable {
        public object Current => null;
        public bool MoveNext() => true;
        public void Reset() => throw new NotSupportedException();
        public void Dispose() { disposals++; }
    }
    static readonly Dictionary<Coroutine, IDisposable> running = new Dictionary<Coroutine, IDisposable>();
    static bool Stop(Coroutine __0) { stops++; running[__0].Dispose(); return false; }
    static bool Destroy(UnityEngine.Object __0) { destroyed.Add(__0); return false; }
    static bool DestroyCO(CondOwner __instance) { events.Add("reset-component"); Invalidate(__instance); return false; }
    static void CheckResetCancellation(Harmony fixture, Harmony production) {
        var instance = AccessTools.Field(typeof(MonoSingleton<AsyncShipLoader>), "_instance");
        var oldInstance = instance.GetValue(null);
        var oldMap = DataHandler.mapCOs;
        bool oldInitialized = DataHandler.bInitialised;
        try {
            Prefix(fixture, typeof(MonoBehaviour), "StopCoroutine", nameof(Stop), new[] { typeof(Coroutine) });
            Prefix(fixture, typeof(UnityEngine.Object), "DestroyImmediate", nameof(Destroy), new[] { typeof(UnityEngine.Object) });
            Prefix(fixture, typeof(CondOwner), "Destroy", nameof(DestroyCO));
            Prefix(fixture, typeof(Debug), "LogWarning", nameof(Noop), new[] { typeof(object) });
            production.CreateClassProcessor(typeof(BarterResetPatch)).Patch();
            DataHandler.bInitialised = true; DataHandler.mapCOs = new Dictionary<string, CondOwner>();
            instance.SetValue(null, null);
            DataHandler.Init();
            Check(instance.GetValue(null) == null, "Reset created an async singleton instead of inspecting the existing one");
            var ship = Hull(TwoItems()); ship.strRegID = "RESET-COPY";
            var pending = AtFinalization(ship);
            var loader = Empty<AsyncShipLoader>();
            var coroutine = Empty<Coroutine>(); GC.SuppressFinalize(coroutine);
            var second = Empty<Coroutine>(); GC.SuppressFinalize(second);
            running[coroutine] = (IDisposable)pending; running[second] = new Pending();
            var jobs = new List<Coroutine> { coroutine, second };
            Set(loader, "_loadingCoroutine", jobs);
            Set(loader, "_loadStateDict", new Dictionary<string, IAsyncLoadable> { [ship.strRegID] = ship });
            instance.SetValue(null, loader);
            foreach (var co in Items(ship).Values) DataHandler.mapCOs[co.strID] = co;
            var ordinary = Empty<CondOwner>(); DataHandler.mapCOs["ordinary-world-object"] = ordinary;
            int before = events.Count; stops = disposals = 0;
            DataHandler.Init(); // Real native reset, including remaining-CO traversal.
            Check(stops == 2 && disposals == 1 && jobs.Count == 0, "Reset did not cancel every existing async job before cleanup");
            Check(ship.json == null && !loader.IsShipLoading(ship.strRegID), "Reset retained the offscreen copy/cache");
            Check(!ship.FullyLoaded && !pending.MoveNext(), "Cancelled barter work resumed after global data reset");
            Check(events.Count == before + 1 && events[before] == "reset-component",
                "Native reset reached copied COs before offscreen unload removed them");
            Check(DataHandler.mapCOs.Count == 0, "Native reset did not finish registry cleanup");
            DataHandler.Init();
            Check(stops == 2, "Repeated reset cancelled already released jobs again");
        } finally {
            instance.SetValue(null, oldInstance); DataHandler.mapCOs = oldMap; DataHandler.bInitialised = oldInitialized;
            running.Clear();
        }
    }
    static void Set(object target, string field, object value) => AccessTools.Field(target.GetType(), field).SetValue(target, value);
}
