using System;
using System.Collections.Generic;
using System.Diagnostics;
using HarmonyLib;
using Ostranauts.Ships.AIPilots;

namespace Ostrabus.Inspection;

// Instances belong to one main-thread request. Nothing retains controllers or
// hulls after the detached response is built; no sampling or gameplay hooks.
internal sealed class ControllerInspection {
    internal sealed class Entry {
        internal string Region;
        internal int Slot;
        internal AIShip AI;
        internal Ship Hull;
    }
    static readonly AccessTools.FieldRef<AIShip,Ship> RawHull = AccessTools.FieldRefAccess<AIShip,Ship>("_shipUs");
    const int MaxScan = 4096, MaxDetails = 32;
    readonly List<Entry> entries = new List<Entry>();
    readonly Dictionary<string,List<Entry>> byId = new Dictionary<string,List<Entry>>(StringComparer.Ordinal);
    internal bool Complete { get; private set; }
    internal int Scanned => entries.Count;
    static Dictionary<string,object> Map(params object[] pairs) => SnapshotJson.Map(pairs);
    static bool Expired(long began) => Stopwatch.GetTimestamp()-began >= Stopwatch.Frequency/1000;
    internal static Ship Resolve(string id) => id != null && CrewSim.system?.dictShips != null &&
        CrewSim.system.dictShips.TryGetValue(id,out var hull) ? hull : null;

    internal static ControllerInspection Capture(long began) {
        var result=new ControllerInspection();
        if (AIShipManager.dictAIs == null) return result;
        foreach (var region in AIShipManager.dictAIs) {
            if (region.Value == null) continue;
            for (int slot=0; slot<region.Value.Count; slot++) {
                if (result.Scanned>=MaxScan || (result.Scanned>0 && Expired(began))) return result;
                var ai=region.Value[slot];
                var hull=ai == null ? null : RawHull(ai);
                var entry=new Entry { Region=region.Key, Slot=slot, AI=ai, Hull=hull };
                result.entries.Add(entry);
                if (hull?.strRegID == null) continue;
                if (!result.byId.TryGetValue(hull.strRegID,out var group)) result.byId.Add(hull.strRegID,group=new List<Entry>());
                group.Add(entry);
            }
        }
        result.Complete=true;
        return result;
    }
    internal bool HomeMatches(string id, string station) {
        if (id == null || !byId.TryGetValue(id,out var group)) return false;
        var hull=Resolve(id);
        foreach (var entry in group) if (ReferenceEquals(entry.Hull,hull) && entry.AI?.HomeStation == station) return true;
        return false;
    }
    internal AIShip Single(string id, Ship hull) {
        if (!Complete || hull == null || id == null || !byId.TryGetValue(id,out var group)) return null;
        AIShip found=null;
        foreach (var entry in group) {
            if (!ReferenceEquals(entry.Hull,hull) || entry.AI == null) continue;
            if (found != null && !ReferenceEquals(found,entry.AI)) return null;
            found=entry.AI;
        }
        return found?.AICharacter == null ? null : found;
    }
    internal string Region(AIShip ai) {
        if (ai == null) return null;
        string found=null;
        foreach (var entry in entries) if (ReferenceEquals(entry.AI,ai)) {
            if (found != null && found != entry.Region) return null;
            found=entry.Region;
        }
        return found;
    }
    internal Dictionary<string,object> Snapshot(string id, Ship hull, bool details=true) {
        List<Entry> group=null;
        if (id != null) byId.TryGetValue(id,out group);
        var rows=new List<object>();
        int matching=0, stale=0;
        var unique=new HashSet<AIShip>();
        if (group != null) foreach (var entry in group) {
            if (hull != null && ReferenceEquals(entry.Hull,hull)) { matching++; if (entry.AI != null) unique.Add(entry.AI); }
            else stale++;
            if (details && rows.Count<MaxDetails) rows.Add(Row(entry));
        }
        return Map("registered", matching>0 ? (object)true : Complete ? false : (object)null,
            "registrationCount", group?.Count ?? 0, "liveHullRegistrationCount", matching,
            "liveHullControllerCount", unique.Count,
            "duplicateRegistrations", matching>1 ? (object)true : Complete ? false : (object)null,
            "multipleControllers", unique.Count>1 ? (object)true : Complete ? false : (object)null,
            "staleHullReferences", stale, "registrations", rows, "registrationsTruncated", (group?.Count ?? 0)>rows.Count,
            "scanComplete", Complete, "registrationsExamined", Scanned,
            "note", "Counts are lower bounds when scanComplete is false. Registry entries are not assumed to represent distinct controllers. No canonical controller is selected when live controllers conflict.");
    }
    object Row(Entry entry) {
        var hull=entry.Hull;
        var live=Resolve(hull?.strRegID);
        string command=null;
        try { command=entry.AI?.ActiveCommandName; } catch (NullReferenceException) { }
        int known=hull?.strRegID != null && byId.TryGetValue(hull.strRegID,out var group) ? group.Count : 0;
        return Map("region", entry.Region, "slot", entry.Slot, "shipId", hull?.strRegID,
            "registrationsForIdObserved",known,
            "nullController", entry.AI == null, "nullHull", hull == null, "hullDestroyed", hull?.bDestroyed,
            "registryHullPresent", live != null, "hullMatchesRegistry", hull != null && ReferenceEquals(hull,live),
            "role", entry.AI?.AICharacter?.AIType.ToString(), "pilotType", entry.AI?.AICharacter?.GetType().Name,
            "hasPilot",entry.AI?.AICharacter != null,
            "home", entry.AI?.HomeStation, "activeCommand", command);
    }
    internal InspectionApi.Reply Page(int offset, int limit, string id=null) {
        var selected=entries;
        if (id != null && !byId.TryGetValue(id,out selected)) selected=new List<Entry>();
        if (offset>=selected.Count && !Complete) return new InspectionApi.Reply(503,Map("error","Controller scan incomplete before requested offset; retry."));
        var rows=new List<object>();
        int end=Math.Min(selected.Count,offset+limit);
        for (int i=offset; i<end; i++) rows.Add(Row(selected[i]));
        var data=Map("items",rows,"nextOffset",end<selected.Count ? (object)end : null,
            "scanComplete",Complete,"registrationsExamined",Scanned,"scanLimit",MaxScan,
            "pagination","Live registry order, not an atomic census. A null nextOffset does not imply a complete census when scanComplete is false; retry or use a direct hull ID.");
        if (id != null) {
            var summary=Snapshot(id,Resolve(id),false);summary.Remove("registrations");summary.Remove("registrationsTruncated");
            data["id"]=id;data["summary"]=summary;
        }
        return new InspectionApi.Reply(200,data);
    }
}
