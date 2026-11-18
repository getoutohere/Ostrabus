using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Ostrabus.StationMigration;

internal static class NativeAdapter
{
    internal static Catalog Assets()
    {
        return new Catalog(name =>
        {
            if (!DataHandler.dictCOs.TryGetValue(name, out var value)) return null;
            return new JObject { ["strName"] = value.strName, ["strItemDef"] = value.strItemDef,
                ["aStartingConds"] = new JArray(value.aStartingConds ?? Array.Empty<string>()) };
        }, name => DataHandler.dictCOOverlays.TryGetValue(name, out var value) ? new JObject
        {
            ["strName"] = value.strName, ["strCOBase"] = value.strCOBase,
            ["mapModeSwitches"] = new JArray(value.mapModeSwitches ?? Array.Empty<string>())
        } : null, name => DataHandler.dictItemDefs.TryGetValue(name, out var value) ? new JObject
        {
            ["strName"] = value.strName, ["nCols"] = value.nCols, ["bHasSpriteSheet"] = value.bHasSpriteSheet,
            ["aSocketAdds"] = new JArray(value.aSocketAdds), ["aSocketReqs"] = new JArray(value.aSocketReqs),
            ["aSocketForbids"] = new JArray(value.aSocketForbids)
        } : null, name => DataHandler.dictLoot.TryGetValue(name, out var value) ? new JObject
        {
            ["strName"] = value.strName, ["aCOs"] = new JArray(value.aCOs ?? Array.Empty<string>()),
            ["aLoots"] = new JArray(value.aLoots ?? Array.Empty<string>())
        } : null, DataHandler.dictInstallables.Values.Select(value => new JObject
        {
            ["strActionCO"] = value.strActionCO, ["strJobType"] = value.strJobType,
            ["aLootCOs"] = new JArray(value.aLootCOs ?? Array.Empty<string>())
        }), Destroyed);
    }

    internal static bool Destroyed(JObject part)
    {
        var damage = (part["aCondOverrides"] as JArray)?.OfType<JObject>()
            .FirstOrDefault(value => (string)value["CondName"] == "StatDamage");
        if (damage == null) return false;
        double amount = (double)damage["Amount"] * ((bool?)damage["NegativeValue"] == true ? -1 : 1);
        var definition = DataHandler.GetDataCO((string)part["strName"]);
        return definition != null && amount >= definition.GetMaxHealth() && !definition.HasCond("IsDockSys");
    }

    internal static JObject View(JsonShip ship) => new JObject
    {
        ["strRegID"] = ship.strRegID, ["strName"] = ship.strName, ["nGridRotation"] = ship.nGridRotation,
        ["aItems"] = JArray.FromObject(ship.aItems ?? Array.Empty<JsonItem>()),
        ["aCOs"] = ship.aCOs == null ? null : JArray.FromObject(ship.aCOs),
        ["shipCO"] = JObject.FromObject(ship.shipCO)
    };

    internal static List<StationPlan> Prepare(JsonShip[] ships, JArray manifests, Catalog assets,
        Func<string, JObject> saved, Func<string, string> resolveTemplate, double epoch)
    {
        var plans = new List<StationPlan>();
        var byStation = manifests.OfType<JObject>().GroupBy(row => (string)row["station"])
            .ToDictionary(group => group.Key, group => group.ToArray());
        foreach (var ship in ships.Where(ship => ship != null && byStation.ContainsKey(ship.strRegID)))
        {
            // A completed migration is not permission to restore a kiosk the
            // player later removed. Only unprocessed physical sections change.
            if (Migration.Completed(ship.shipCO?.aConds)) continue;
            var view = View(ship);
            var variants = byStation[ship.strRegID];
            string resolved = resolveTemplate(ship.strName);
            var exact = variants.FirstOrDefault(row => (string)row["template"] == resolved);
            // Unknown variants require review; never substitute a different
            // station layout or infer an unauthored kiosk placement.
            if (exact == null) throw new InvalidOperationException("No reviewed placement aligns with station variant: " + ship.strRegID + "/" + resolved);
            plans.Add(Migration.Plan(view, exact, assets, saved, epoch));
        }
        return plans;
    }

    internal static void Commit(JsonShip[] ships, IList<string> markers, IDictionary<string, JsonCondOwnerSave> records,
        IReadOnlyList<StationPlan> plans)
    {
        // Deserialize everything before any mutation. A planning or schema error
        // leaves both station arrays and completion markers untouched.
        var prepared = plans.Select(plan => new
        {
            Ship = ships.Single(ship => ship.strRegID == (string)plan.Original["strRegID"]),
            Items = plan.Updated["aItems"].ToObject<JsonItem[]>(),
            LocalRecords = plan.Updated["aCOs"]?.Type == JTokenType.Array ? plan.Updated["aCOs"].ToObject<JsonCondOwnerSave[]>() : null,
            ShipCO = plan.Updated["shipCO"].ToObject<JsonCondOwnerSave>(),
            Records = plan.Records.ToDictionary(pair => pair.Key, pair => pair.Value.ToObject<JsonCondOwnerSave>()),
            plan.Retired
        }).ToArray();
        foreach (var value in prepared)
        {
            value.Ship.aItems = value.Items;
            value.Ship.aCOs = value.LocalRecords;
            value.Ship.shipCO = value.ShipCO;
            foreach (var pair in value.Records) records[pair.Key] = pair.Value;
            foreach (string id in value.Retired) records.Remove(id);
            if (value.ShipCO.strID != null) records[value.ShipCO.strID] = value.ShipCO;
        }
        if (!markers.Contains(Migration.Marker)) markers.Add(Migration.Marker);
    }
}
