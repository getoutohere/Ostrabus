using System;
using System.Collections.Generic;
using System.Diagnostics;
using HarmonyLib;
using Ostrabus.ShipNavigation;
using Ostranauts.Core;
using Ostranauts.Ships;
using Ostranauts.Ships.AIPilots;
using Ostranauts.Ships.Commands;
using Ostranauts.Trading;

namespace Ostrabus.Inspection;

// Explicit developer operations, never called by an update/simulation hook.
internal static partial class FreightRecovery {
    static readonly AccessTools.FieldRef<AIShip,ICommand> Active = AccessTools.FieldRefAccess<AIShip,ICommand>("_activeCommand");
    static readonly AccessTools.FieldRef<AIShip,ICommand> Reaction = AccessTools.FieldRefAccess<AIShip,ICommand>("_activeReaction");
    static readonly AccessTools.FieldRef<AIShip,Ship> RawHull = AccessTools.FieldRefAccess<AIShip,Ship>("_shipUs");
    static readonly AccessTools.FieldRef<QueuedCommandsNode,Queue<ICommand>> Pending = AccessTools.FieldRefAccess<QueuedCommandsNode,Queue<ICommand>>("commandQueue");
    static readonly AccessTools.FieldRef<QueuedCommandsNode,ICommand> Current = AccessTools.FieldRefAccess<QueuedCommandsNode,ICommand>("currentCommand");
    static readonly AccessTools.FieldRef<SelectorCommand,List<ICommand>> SelectorChildren = AccessTools.FieldRefAccess<SelectorCommand,List<ICommand>>("_childCommands");
    static readonly AccessTools.FieldRef<StickySequenceCommand,List<ICommand>> StickyChildren = AccessTools.FieldRefAccess<StickySequenceCommand,List<ICommand>>("_childCommands");
    static readonly AccessTools.FieldRef<PersonSpec,CondOwner> Crew = AccessTools.FieldRefAccess<PersonSpec,CondOwner>("co");
    static readonly System.Reflection.FieldInfo AIQueue = AccessTools.Field(typeof(AIShipManager),"_aIQueue");
    static readonly AccessTools.FieldRef<AIShipQueue,Queue<AIShip>> QueueEntries = AccessTools.FieldRefAccess<AIShipQueue,Queue<AIShip>>("_aIShipQueue");
    static readonly AccessTools.FieldRef<AIShipQueue,List<AIShip>> PriorityEntries = AccessTools.FieldRefAccess<AIShipQueue,List<AIShip>>("_priorityQueue");
    const int MaxControllers=4096;
    static Dictionary<string,object> Map(params object[] pairs) => SnapshotJson.Map(pairs);
    static InspectionApi.Reply Reply(int code, params object[] pairs) => new InspectionApi.Reply(code,Map(pairs));
    static bool Finite(double n) => !double.IsNaN(n) && !double.IsInfinity(n);
    static bool Id(string id) {
        if (string.IsNullOrEmpty(id) || id.Length>64) return false;
        foreach (char c in id) if (char.IsWhiteSpace(c) || char.IsControl(c) || c=='/' || c=='\\') return false;
        return true;
    }
    internal static bool IsRoute(string path) {
        if (path == null || !path.StartsWith("/economy/freight/",StringComparison.Ordinal)) return false;
        var parts=path.Split('/');
        return parts.Length>=5 && (parts[4]=="approach" || parts[4]=="recover");
    }
    internal static InspectionApi.Reply Run(string path, bool apply, string confirm) {
        var parts=path.Split('/');
        if (!IsRoute(path) || (parts.Length!=5 && parts.Length!=6)) return Reply(400,"error","Expected /economy/freight/{id}/approach[/{stationId}] or /recover/{stationId}.");
        bool recover=parts[4]=="recover";
        string id,stationId;
        try { id=Uri.UnescapeDataString(parts[3]);stationId=parts.Length==6 ? Uri.UnescapeDataString(parts[5]) : null; }
        catch (UriFormatException) { return Reply(400,"error","Malformed registry ID."); }
        if (!Id(id) || stationId != null && !Id(stationId)) return Reply(400,"error","Expected exact ship and station registry IDs.");
        if (recover && stationId == null) return Reply(400,"error","Lost cargo has no durable destination field; an explicit destination station is required.");
        if (apply && confirm!=(recover ? "RECOVER" : "APPROACH")) return Reply(400,"error","POST requires confirm="+(recover ? "RECOVER" : "APPROACH")+".");
        var world=CrewSim.system;var player=CrewSim.coPlayer;
        var manager=MarketSingleton.GetValue(null) as MarketManager;
        if (world?.dictShips == null || AIShipManager.dictAIs == null || ReferenceEquals(manager,null) || MarketMap(manager) == null ||
            player == null || string.IsNullOrEmpty(player.strID) || PlotManager.dictPlotsActive == null)
            return Reply(503,"error","World, ownership, AI, market or plot state unavailable.");
        var markets=MarketMap(manager);
        if (!markets.TryGetValue(id,out var source) || source == null) return Reply(404,"error","Source cargo market is absent; no cargo can be credited again.","id",id);
        if (source.ShipRegId!=id) return Reply(409,"error","Source market identity does not match its registry key.");
        world.dictShips.TryGetValue(id,out var ship);
        if (recover && ship != null) return Reply(409,"error","A hull still exists; use approach so its crew and AI can deliver naturally.");
        if (world.GetShipOwner(id)==player.strID || ReferenceEquals(ship,player.ship) && ship != null) return Reply(409,"error","Player-owned hulls/markets are excluded.");
        var plots=PlotManager.GetShipsInPlots();
        if (plots == null || plots.Contains(id)) return Reply(409,"error","Source is plot-protected or plot protection is unavailable.");
        var index=ControllerInspection.Capture(Stopwatch.GetTimestamp());
        if (!index.Complete) return Reply(503,"error","Controller census incomplete; retry without making changes.");
        AIShip ai=null;
        if (!recover) {
            ai=index.Single(id,ship);
            var reason=ApproachEligibility(ship,ai,player);
            if (reason != null) return Reply(apply ? 409 : 200,"id",id,"eligible",false,"reason",reason);
            stationId=stationId ?? ship.shipScanTarget?.strRegID;
            if (stationId!=ship.shipScanTarget?.strRegID) return Reply(409,"error","Approach must retain the current destination; this endpoint does not reroute freight.");
        }
        if (!Id(stationId) || !world.dictShips.TryGetValue(stationId,out var station) || station?.objSS == null || station.bDestroyed || station.bRemove || station.HideFromSystem ||
            !station.objSS.bIsBO || !station.IsStation() || stationId==id || !markets.TryGetValue(stationId,out var destination) || destination == null ||
            destination.ShipRegId!=stationId || !Finite(station.objSS.vPosx) || !Finite(station.objSS.vPosy) || !Finite(station.objSS.vVelX) || !Finite(station.objSS.vVelY))
            return Reply(409,"error","Destination must resolve to an available native station and matching market.");
        var error=Manifest(source,out var manifest,out int total);
        if (error == null) error=ValidateDestination(markets,id,destination,manifest,out _);
        if (error != null) return Reply(apply ? 409 : 200,"id",id,"stationId",stationId,"eligible",false,"reason",error);
        var stale=new HashSet<AIShip>();
        if (recover) {
            error=StaleControllers(id,stale,out var queue);
            if (error != null) return Reply(409,"error",error);
        }
        var quantities=new Dictionary<string,object>();foreach (var pair in manifest) quantities.Add(pair.Key,pair.Value.Count);
        var plan=Map("stationId",stationId,"destinationSource",recover ? "explicit-caller-choice" : "current-native-target",
            "cargo",quantities,"units",total,"staleControllers",stale.Count,
            "nativeTravelStepOnly",!recover,"deliverImmediately",recover,"fuelRefill",false,
            "stagingDistanceKm",recover ? null : (object)10,"nextNativeCommand",recover ? null : "DockCargoAndDespawn",
            "note",recover ? "Caller must establish the historical destination. Credit surviving cargo once; remove only its orphaned market/AI/hub/ownership references. No NPCs or hulls are deleted." :
                "Complete native FlyTo travel, stage 10 km away with matching velocity and resume the existing saved arrival checkpoint. AI delivers on a subsequent tick. Capacity is checked now, not reserved.");
        if (!apply) return Reply(200,"id",id,"eligible",true,"applied",false,"plan",plan);
        return recover ? Recover(manager,id,stationId,source,destination,manifest,stale,plan) : Approach(ship,ai,station,plan);
    }
    static string ApproachEligibility(Ship ship, AIShip ai, CondOwner player) {
        if (ship?.objSS == null || ship.bDestroyed || ship.bRemove || ship.HideFromSystem || !ship.IsAIShip || ship.LoadState!=Ship.Loaded.Shallow ||
            ship.objSS.bIsBO || ship.objSS.bBOLocked || ship.objSS.bOrbitLocked || !Finite(ship.objSS.vPosx) || !Finite(ship.objSS.vPosy)) return "Source must be an independent visible shallow AI vessel in free flight with finite coordinates.";
        if (ship.IsDocked() || ship.HasDockGroup) return "Attached ships are excluded; no docking/towing group will be teleported.";
        if (!(ai?.AICharacter is HaulerCargoPilot) || ai.Blackboard == null || !ReferenceEquals(ai.AICharacter.ShipUs,ship)) return "One unambiguous native cargo controller is required.";
        if (Reaction(ai) != null || ship.shipCombatTarget != null || ai.Blackboard.Get<bool>("InCombat")) return "Active combat/reactions are excluded.";
        if (!(Active(ai) is FlyTo) && !(Active(ai) is DockCargoAndDespawn)) return "Only normal cargo flight/docking may be completed.";
        if (!ArrivalStep(ai,out _,out _,out _)) return "Native cargo arrival sequence could not be identified; no behavior state was changed.";
        if (Active(ai) is DockCargoAndDespawn && DockCargoAndDespawn.IsWithinRangeSpeed(ship.objSS,ship.shipScanTarget?.objSS)) return "Native arrival is already ready; let the simulation execute it.";
        var priority=ai.AICharacter.PriorityQueue;
        if (priority == null || Pending(priority) == null || Pending(priority).Count!=0 || Current(priority) != null) return "Queued/overriding orders are excluded.";
        if (ship.People == null || ship.People.Count==0) return "Crew must already exist.";
        bool conscious=false;
        foreach (var person in ship.People) {
            var co=person == null ? null : Crew(person);
            if (co == null || co.bDestroyed || !co.bAlive || !ReferenceEquals(co.ship,ship) || ReferenceEquals(co,player) ||
                player.Company?.mapRoster?.ContainsKey(co.strID)==true) return "Crew must be resolved, alive, aboard their hull and outside the player's roster.";
            if (!co.HasCond("Unconscious")) conscious=true;
        }
        return conscious ? null : "No conscious crew can continue native AI arrival.";
    }
    static bool ArrivalStep(AIShip ai, out StickySequenceCommand sequence, out DockCargoAndDespawn command, out int index) {
        sequence=null;command=null;index=-1;
        if (!(ai?.AICharacter?.BehaviorTree is SelectorCommand root)) return false;
        var children=SelectorChildren(root);
        if (children == null || children.Count>32) return false;
        foreach (var child in children) if (child is StickySequenceCommand candidate) {
            var steps=StickyChildren(candidate);
            if (steps == null || steps.Count>32) return false;
            for (int i=1;i<steps.Count;i++) if (steps[i] is DockCargoAndDespawn dock && steps[i-1] is FlyTo) {
                if (sequence != null) return false;
                sequence=candidate;command=dock;index=i;
            }
        }
        return sequence != null;
    }
    static InspectionApi.Reply Approach(Ship ship, AIShip ai, Ship station, object plan) {
        if (!ArrivalStep(ai,out var sequence,out var arrival,out int arrivalIndex)) return Reply(409,"error","Native arrival sequence changed; retry inspection.");
        double dx=ship.objSS.vPosx-station.objSS.vPosx,dy=ship.objSS.vPosy-station.objSS.vPosy;
        double length=Math.Sqrt(dx*dx+dy*dy);
        if (!Finite(length) || length<=0) { dx=1;dy=0;length=1; }
        NavigationRoute.Stop(ship);
        ai.Blackboard.Remove("TripFuelPrediction");
        new TargetData(station).ApplyToShip(ship);
        ship.fAIPauseTimer=0;
        var outcome=new FlyTo(ai.AICharacter).ResolveInstantly(ai.Blackboard);
        // Native placement hugs the Size-based hull radius, which can be much
        // smaller than a station's actual/docked geometry. Stage clear of it,
        // still inside the native cargo arrival gate, rather than teleporting
        // into traffic. Its existing serialized sequence owns the next step.
        const double near=10/149597870.7;
        ship.objSS.vPosx=station.objSS.vPosx+dx/length*near;
        ship.objSS.vPosy=station.objSS.vPosy+dy/length*near;
        ship.objSS.vBOOffsetx=dx/length*near;ship.objSS.vBOOffsety=dy/length*near;
        ship.objSS.bIgnoreGrav=false;
        bool placed=outcome.Status==CommandCode.Finished && ship.objSS.NavData == null && DockCargoAndDespawn.IsWithinRangeSpeed(ship.objSS,station.objSS);
        if (placed) {
            sequence.SaveData=new[]{arrivalIndex.ToString(System.Globalization.CultureInfo.InvariantCulture)};
            Active(ai)=arrival;
        }
        return Reply(placed ? 200 : 500,"id",ship.strRegID,"eligible",true,"applied",placed,"plan",plan,
            "nativeStatus",outcome.Status.ToString(),"withinNativeDockingGate",placed,
            "position",PlayerInspection.Position(ship),"cargoStillRegistered",MarketMap(MarketSingleton.GetValue(null) as MarketManager).ContainsKey(ship.strRegID),
            "note","No Dock command was executed. Inspect subsequent native arrival; do not assume cargo has already been delivered.");
    }
    static string StaleControllers(string id, HashSet<AIShip> result, out AIShipQueue queue) {
        queue=AIQueue.GetValue(null) as AIShipQueue;
        if (queue == null || QueueEntries(queue) == null || PriorityEntries(queue) == null) return "Native AI queue is unavailable.";
        int scanned=0;
        bool Add(AIShip ai) {
            if (++scanned>MaxControllers) return false;
            var hull=ai == null ? null : RawHull(ai);
            if (hull?.strRegID==id) result.Add(ai);
            return true;
        }
        foreach (var region in AIShipManager.dictAIs.Values) if (region != null) foreach (var ai in region) if (!Add(ai)) return "AI cleanup scan exceeds request bound.";
        foreach (var ai in QueueEntries(queue)) if (!Add(ai)) return "AI cleanup scan exceeds request bound.";
        foreach (var ai in PriorityEntries(queue)) if (!Add(ai)) return "AI cleanup scan exceeds request bound.";
        if (result.Count>32) return "More than 32 stale controllers name this hull; inspect before bulk cleanup.";
        foreach (var ai in result) if (RawHull(ai).LoadState>Ship.Loaded.Shallow && !RawHull(ai).bDestroyed) return "A stale reference still names a materialized hull; inspect it before recovering cargo.";
        return null;
    }
}
