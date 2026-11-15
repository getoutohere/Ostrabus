using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace ShipExecutivePrototype;

// Installs real, already-built production patches in this offline process.
// The optional experimental ownership binding is separate from those patches.
sealed class RetainedWorldServices : IDisposable
{
    internal static RetainedWorldServices Current;
    readonly Harmony patches = new Harmony("offline.executive-world.retained");
    readonly Harmony binding = new Harmony("offline.executive-world.binding");
    readonly Dictionary<System.Reflection.FieldInfo, object> savedFlags = new Dictionary<FieldInfo, object>();
    internal readonly bool BindingEnabled;
    internal readonly List<object> Windows = new List<object>();
    internal static readonly string[] PatchNames = { "OrbitalStepObservationPatch", "OrbitalGravityPatch", "CoastScopePatch", "CoastMotionPatch", "SffMotionPatch", "SffBeforeCatchupPatch", "SffCatchupPatch",
        "Economy.EconomicFreightMotionPatch", "Economy.EconomicCatchupPatch", "Economy.EconomicFreightCatchupPatch", "Economy.EconomicProductionPatch", "Economy.EconomicDispatchPatch", "Economy.EconomicTraderSyncPatch",
        "Economy.CargoTransferPatch", "Economy.MarketWithdrawalPatch", "Economy.MarketBatchDepositPatch" };
    static bool preparingOwnedActor;
    internal static Type Type(string name) => typeof(Ostrabus.OstrabusPlugin).Assembly.GetType("Ostrabus.Orbital." + name, true);
    internal static object Call(string type, string method, params object[] args) => AccessTools.Method(Type(type), method).Invoke(null, args);
    internal static object Scope(string type) => AccessTools.Field(Type(type), "Current").GetValue(null);
    internal static Dictionary<string, object> Snapshot(string type) => (Dictionary<string, object>)Call(type, "Snapshot");

    internal RetainedWorldServices(bool bindingEnabled = false)
    {
        Current = this;
        BindingEnabled = bindingEnabled;
        var enabled = AccessTools.Field(Type("OrbitalSimulation"), "<Enabled>k__BackingField");
        savedFlags[enabled] = enabled.GetValue(null);
        enabled.SetValue(null, true);
        foreach (string service in new[] { "SffSimulation", "Economy.SffEconomy" })
        {
            var flag = AccessTools.Field(Type(service), "Enabled");
            savedFlags[flag] = flag.GetValue(null);
            flag.SetValue(null, true);
        }
        foreach (string patch in PatchNames)
            patches.CreateClassProcessor(Type(patch)).Patch();
        foreach (string service in new[] { "SffSimulation", "Economy.SffEconomy" })
            binding.Patch(AccessTools.Method(Type(service), "Begin"), postfix: new HarmonyMethod(typeof(RetainedWorldServices), nameof(Began)));
        if (bindingEnabled)
        {
            binding.Patch(AccessTools.Method(Type("SffSimulation"), "Prepare"), prefix: new HarmonyMethod(typeof(RetainedWorldServices), nameof(Preparing)),
                finalizer: new HarmonyMethod(typeof(RetainedWorldServices), nameof(Prepared)));
            binding.Patch(AccessTools.Method(Type("SffSimulation"), "IsTravelCommand"), prefix: new HarmonyMethod(typeof(RetainedWorldServices), nameof(TravelOwnership)));
        }
    }

    static void Began(MethodBase __originalMethod, object[] __args, object __result)
    {
        if (__result != null)
            Current.Windows.Add(new
            {
                service = __originalMethod.DeclaringType.Name,
                start = StarSystem.fEpoch,
                seconds = (double)__args[1]
            });
    }
    static void Preparing(List<Ship> members, out bool __state)
    {
        __state = preparingOwnedActor;
        var actor = NativeWorldTimeBridge.Current.Actor;
        preparingOwnedActor = actor.Machine.State.Authority == "Executive" && members.Contains(actor.World.Subject);
    }
    static Exception Prepared(Exception __exception, bool __state)
    {
        preparingOwnedActor = __state;
        return __exception;
    }
    static bool TravelOwnership(ref bool __result)
    {
        if (!preparingOwnedActor)
            return true;
        __result = false;
        return false;
    }

    sealed class SharedWindow : IDisposable
    {
        internal IDisposable Motion, Economy;
        public void Dispose()
        {
            try
            {
                Economy?.Dispose();
            }
            finally { Motion?.Dispose(); }
        }
    }
    internal IDisposable EnterSharedWindow(StarSystem world, double seconds, bool passenger)
    {
        if (!BindingEnabled || !passenger || GUIFFWD.Active || seconds < 60)
            return null;
        if (Scope("SffSimulation") != null || Scope("Economy.SffEconomy") != null)
            throw new InvalidOperationException("Competing retained world scope");
        Call("OrbitalSimulation", "ObserveUpdate", world, seconds);
        var scope = new SharedWindow();
        try
        {
            scope.Economy = (IDisposable)Call("Economy.SffEconomy", "Begin", world, seconds, true);
            scope.Motion = (IDisposable)Call("SffSimulation", "Begin", world, seconds, true);
            return scope;
        }
        catch { scope.Dispose(); throw; }
    }

    public void Dispose()
    {
        Call("SffSimulation", "Reset");
        Call("Economy.SffEconomy", "Reset");
        Call("CoastIntegration", "Reset");
        patches.UnpatchSelf();
        binding.UnpatchSelf();
        preparingOwnedActor = false;
        foreach (var flag in savedFlags)
            flag.Key.SetValue(null, flag.Value);
        Current = null;
    }
}
