using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using HarmonyLib;
using UnityEngine;

// Execute native persistence/loader contracts without creating Unity objects.
// This is feasibility evidence, not a production station migration.
internal static class Program
{
    static int checks;
    static string restoredDefinition;
    static bool partRequested;
    static int missingRecordWarnings;
    static void Check(bool value, string message)
    {
        checks++;
        if (!value)
            throw new InvalidOperationException(message);
    }

    static bool CaptureRestore(string strCO, JsonCondOwnerSave jcos, ref CondOwner __result)
    {
        if (jcos == null)
            return true;
        restoredDefinition = strCO;
        __result = null;
        return false;
    }

    static bool CapturePart(ref GameObject __result)
    {
        partRequested = true;
        __result = null;
        return false;
    }

    static bool CaptureWarning(object message)
    {
        if (message?.ToString().Contains("missing save data") == true)
            missingRecordWarnings++;
        return false;
    }

    static int Main()
    {
        var owner = new Harmony("ostrabus.offline.station-migration-contracts");
        var factory = AccessTools.Method(typeof(DataHandler), nameof(DataHandler.GetCondOwner), new[]
        {
            typeof(string), typeof(string), typeof(string), typeof(bool),
            typeof(string), typeof(JsonCondOwnerSave), typeof(string), typeof(Transform)
        });
        var create = AccessTools.Method(typeof(Ship), nameof(Ship.CreatePart));
        var spawn = AccessTools.Method(typeof(Ship), "SpawnItems");
        object previousLive = AccessTools.Field(typeof(DataHandler), "mapCOs").GetValue(null);
        object previousSaved = AccessTools.Field(typeof(DataHandler), "dictCOSaves").GetValue(null);
        var live = new Dictionary<string, CondOwner>();
        var saved = new Dictionary<string, JsonCondOwnerSave>();
        AccessTools.Field(typeof(DataHandler), "mapCOs").SetValue(null, live);
        AccessTools.Field(typeof(DataHandler), "dictCOSaves").SetValue(null, saved);
        owner.Patch(factory, prefix: new HarmonyMethod(typeof(Program), nameof(CaptureRestore)));
        owner.Patch(create, prefix: new HarmonyMethod(typeof(Program), nameof(CapturePart)));
        owner.Patch(AccessTools.Method(typeof(Debug), nameof(Debug.LogWarning), new[] { typeof(object) }),
            prefix: new HarmonyMethod(typeof(Program), nameof(CaptureWarning)));
        try
        {
            var item = new JsonItem { strID = "saved-kiosk", strName = "ItmKioskHQCHScrap01" };
            var record = new JsonCondOwnerSave { strID = item.strID, strCODef = "ItmKiosk01" };
            saved[item.strID] = record;
            DataHandler.GetCondOwner(item.strName, item.strID, null, false);
            Check(restoredDefinition == "ItmKiosk01", "Native saved definition no longer overrides ship-item name");

            record.strCODef = item.strName;
            restoredDefinition = null;
            DataHandler.GetCondOwner(item.strName, item.strID, null, false);
            Check(restoredDefinition == item.strName, "Synchronized saved/item definitions were not honored");

            var alreadyLive = (CondOwner)FormatterServices.GetUninitializedObject(typeof(CondOwner));
            live[item.strID] = alreadyLive;
            restoredDefinition = null;
            var returned = DataHandler.GetCondOwner(item.strName, item.strID, null, false);
            Check(ReferenceEquals(returned, alreadyLive) && restoredDefinition == null,
                "Native factory no longer prefers an existing live object over saved data");
            live.Clear();

            var ship = (Ship)FormatterServices.GetUninitializedObject(typeof(Ship));
            ship.strRegID = "test-station";
            saved.Clear();
            object[] arguments =
            {
                new List<JsonItem> { item }, false, Ship.Loaded.Edit,
                new Dictionary<string, CondOwner>(), new List<CondOwner>()
            };
            partRequested = false;
            spawn.Invoke(ship, arguments);
            Check(!partRequested && missingRecordWarnings == 1,
                "New ordinary-load item without saved CO data did not take the native missing-record path");

            saved[item.strID] = record;
            partRequested = false;
            spawn.Invoke(ship, arguments);
            Check(partRequested && missingRecordWarnings == 1,
                "New item with saved CO data did not pass native restoration admission");
            Console.WriteLine("PASS: " + checks + " native station-migration persistence contracts; game MVID " +
                typeof(Ship).Assembly.ManifestModule.ModuleVersionId +
                ". Unity allocation/conversion and placement are fixture boundaries.");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
        finally
        {
            owner.UnpatchSelf();
            AccessTools.Field(typeof(DataHandler), "mapCOs").SetValue(null, previousLive);
            AccessTools.Field(typeof(DataHandler), "dictCOSaves").SetValue(null, previousSaved);
        }
    }
}
