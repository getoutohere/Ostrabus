using System;
using System.Collections.Generic;
using HarmonyLib;

namespace ShipExecutivePrototype;

// Physical tanks and dock connectivity are provider inputs. Native TakeFuel,
// SyncFuel, factory construction, pause checks and transfer decisions execute.
sealed class NativeFuelFixtures : IDisposable
{
    readonly Harmony hooks = new Harmony("offline.navy.fuel-provider");
    readonly System.Reflection.MethodInfo oldRemove;
    internal static NativeFuelFixtures Current;
    internal int SyncCalls;
    internal readonly List<string> Events = new List<string>();

    internal NativeFuelFixtures()
    {
        Current = this;
        var remove = AccessTools.Method(typeof(Ship), nameof(Ship.RemoveGasMass));
        oldRemove = AccessTools.Method(typeof(NativeWorld), "RemoveFuel");
        hooks.Unpatch(remove, oldRemove);
        hooks.Patch(remove, prefix: new HarmonyMethod(typeof(NativeFuelFixtures), nameof(Remove)));
        hooks.Patch(AccessTools.Method(typeof(Ship), nameof(Ship.GetRCSRemain)), prefix: new HarmonyMethod(typeof(NativeFuelFixtures), nameof(Remain)));
        hooks.Patch(AccessTools.PropertyGetter(typeof(Ship), nameof(Ship.GetRCSMinimumFuelAmount)), prefix: new HarmonyMethod(typeof(NativeFuelFixtures), nameof(Minimum)));
        hooks.Patch(AccessTools.Method(typeof(Ship), nameof(Ship.GetAllDockedShips)), prefix: new HarmonyMethod(typeof(NativeFuelFixtures), nameof(Docked)));
        hooks.Patch(AccessTools.Method(typeof(Ship), nameof(Ship.RefuelRCS)), prefix: new HarmonyMethod(typeof(NativeFuelFixtures), nameof(Add)));
        hooks.Patch(AccessTools.Method(typeof(Ship), nameof(Ship.SyncFuel)), prefix: new HarmonyMethod(typeof(NativeFuelFixtures), nameof(Sync)));
    }

    static Dictionary<string, double> Tanks => NativeWorld.Current.PhysicalFuel;
    static void Sync() => Current.SyncCalls++;
    static bool Remain(Ship __instance, ref double __result)
    {
        __result = Tanks[__instance.strRegID];
        return false;
    }
    static bool Minimum(ref double __result)
    {
        __result = NativeWorld.Current.FuelMinimum;
        return false;
    }
    static bool Docked(Ship __instance, ref IReadOnlyList<Ship> __result)
    {
        var donor = NativeWorld.Current.Resolve(NativeWorld.Current.FuelDonor);
        __result = __instance == NativeWorld.Current.Subject && donor != null ? new[] { donor } : Array.Empty<Ship>();
        return false;
    }
    static bool Add(Ship __instance, float amount, ref float __result)
    {
        Tanks[__instance.strRegID] += amount;
        Current.Events.Add(StarSystem.fEpoch + ":add:" + __instance.strRegID + ":" + amount);
        __result = 0;
        return false;
    }
    static bool Remove(Ship __instance, float fMassNeeded, ref float __result)
    {
        __result = (float)Math.Min(Tanks[__instance.strRegID], fMassNeeded);
        Tanks[__instance.strRegID] -= __result;
        Current.Events.Add(StarSystem.fEpoch + ":remove:" + __instance.strRegID + ":" + __result);
        return false;
    }
    public void Dispose()
    {
        hooks.UnpatchSelf();
        new Harmony("offline.executive-prototype.world").Patch(AccessTools.Method(typeof(Ship), nameof(Ship.RemoveGasMass)), prefix: new HarmonyMethod(oldRemove));
    }
}
