using System;
using System.Collections.Generic;
using HarmonyLib;
using Ostranauts.Trading;

namespace Ostrabus.SaveCompatibility;
[HarmonyPatch(typeof(Ship), nameof(Ship.InitShip), new[] { typeof(bool), typeof(Ship.Loaded), typeof(string), typeof(string) })]
internal static class LegacyRcsLoadingPatch
{
    static readonly Func<Ship, JsonItem, bool> Destroyed = AccessTools.MethodDelegate<Func<Ship, JsonItem, bool>>(AccessTools.Method(typeof(Ship), "IsItemDestroyed"));
    static readonly AccessTools.FieldRef<DataCO, double> CachedHealth = AccessTools.FieldRefAccess<DataCO, double>("maxHealth");
    internal static bool ItemDestroyed(Ship ship, JsonItem item)
    {
        if (item.GetCondAmountOverride("StatDamage").HasValue)
        {
            // Native asset loading prepares these health caches. Do not trigger
            // a cold loot expansion (and its random reads) during migration.
            if (DataHandler.dictDataCOs == null || !DataHandler.dictDataCOs.TryGetValue(item.strName, out var data) || CachedHealth(data) <= 0)
                throw new InvalidOperationException("native part-health cache is not ready: " + item.strName);
        }

        return Destroyed(ship, item);
    }

    static void Prefix(Ship __instance, bool bTemplateOnly, Ship.Loaded nLoad)
    {
        if (nLoad != Ship.Loaded.Shallow || __instance.LoadState >= nLoad || !LegacyRcsCaches.Missing(__instance.json))
            return;
        try
        {
            var saved = __instance.json;
            Dictionary<string, JsonCondOwnerSave> local = null;
            if (saved.aCOs != null)
            {
                local = new Dictionary<string, JsonCondOwnerSave>(StringComparer.Ordinal);
                foreach (var co in saved.aCOs)
                    if (!string.IsNullOrEmpty(co?.strID))
                        local[co.strID] = co;
            }

            JsonCondOwnerSave Find(string id)
            {
                if (local != null && local.TryGetValue(id, out var own))
                    return own;
                return DataHandler.dictCOSaves != null && DataHandler.dictCOSaves.TryGetValue(id, out var shared) ? shared : null;
            }

            JsonCondOwner Definition(string name) => name == null ? null : DataHandler.GetCondOwnerDef(name);
            var conditions = new Dictionary<string, Condition>(StringComparer.Ordinal);
            Condition Condition(string name)
            {
                if (name == null)
                    return null;
                if (conditions.TryGetValue(name, out var existing))
                    return existing;
                if (!DataHandler.dictConds.TryGetValue(name, out var value))
                    return null;
                return conditions[name] = new Condition(value);
            }

            var gate = DataHandler.GetCondTrigger("TIsRCSClusterAudioEmitter");
            // StarSystem.Init restores every saved hull with bTemplateOnly=false.
            // Never-visited hulls still contain cached blueprint parts without
            // individual CO records. Read their definitions without spawning them.
            // Use persisted visit state: InitShip has not copied it to Ship yet.
            bool allowDefinitionFallback = bTemplateOnly || saved.fLastVisit == 0;
            if (!LegacyRcsCaches.TryDerive(saved, gate, Find, Definition, Condition, out var restored, out string reason, allowDefinitionFallback, item => ItemDestroyed(__instance, item)))
            {
                if (restored.NativeRebuild)
                    return;
                OstrabusPlugin.Logger?.LogWarning("[SaveCompatibility] RCS caches left unchanged for " + saved.strRegID + ": " + reason);
                return;
            }

            saved.nRCSCountRaw = restored.Count;
            saved.nRCSExhaustV = restored.Exhaust;
            OstrabusPlugin.Logger?.LogInfo("[SaveCompatibility] Restored RCS caches for " + saved.strRegID + ": " + restored.Count + " active thrusters.");
        }
        catch (Exception error)
        {
            OstrabusPlugin.Logger?.LogWarning("[SaveCompatibility] Cannot restore RCS caches for " + __instance.strRegID + ": " + error.Message);
        }
    }
}
