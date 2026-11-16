using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using HarmonyLib;
using Ostranauts.Core;
using Ostranauts.Ships.AIPilots;
using Ostranauts.Ships.Commands;
using Ostranauts.ShipGUIs.Utilities;

static partial class Program {
    static int checks;
    static T Empty<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    static void Set(object item, string field, object value) => AccessTools.Field(item.GetType(), field).SetValue(item, value);
    static void Back(object item, string property, object value) => Set(item, "<" + property + ">k__BackingField", value);
    static void Check(bool ok, string why) { checks++; if (!ok) throw new Exception(why); }
    static bool Skip() => false;
    static bool Equal(UnityEngine.Object __0, UnityEngine.Object __1, ref bool __result) { __result = ReferenceEquals(__0, __1); return false; }
    static bool Unequal(UnityEngine.Object __0, UnityEngine.Object __1, ref bool __result) { __result = !ReferenceEquals(__0, __1); return false; }
    static bool Owner(StarSystem __instance, string __0, ref string __result) { __result = __0 == "owned" ? "player" : "npc"; return false; }
    static bool Stop(Ship __instance) { __instance.objSS.ResetAccelerations(); return false; }
    static bool Station(ref bool __result) { __result = false; return false; }
    static bool Body(string strName, ref BodyOrbit __result) { __result = strName == "Mercury" ? mercury : null; return false; }
    static BodyOrbit mercury;
    static Ship manuallyPiloted;
    static bool Manned(Ship __instance, ref bool __result) { __result = ReferenceEquals(__instance, manuallyPiloted); return false; }
    static Ship Vessel(string id, bool ai = true) {
        var ship = Empty<Ship>(); ship.strRegID = id;
        Back(ship, "objSS", new ShipSitu(false));
        Set(ship, "bAIShip", ai); Set(ship, "nLoadState", Ship.Loaded.Shallow);
        Set(ship, "aDocked", new Dictionary<string, Ship>());
        ship.json = new JsonShip();
        return ship;
    }
    static NavData Route(Ship ship, ShipSitu endpoint, double end = 1000) {
        var nav = new NavData(ship);
        nav.AddNavDataPoint(new NavDataPoint(900, new ShipSitu(false), 0, 0, 0,rcsCount:0F));
        nav.AddNavDataPoint(new NavDataPoint(end, endpoint, 0, 0, 0,rcsCount:0F));
        ship.objSS.NavData = nav;
        return nav;
    }
    static ShipSitu Target() => new ShipSitu(false) { bBOLocked = true, strBOPORShip = "Mercury", vBOOffsetx = 1e-6,
        vPosx = .05, vPosy = .31, vVelX = -3.7e-7, vVelY = 1.1e-7 };
    static ShipSitu Endpoint(bool anchored = false) => new ShipSitu(false) { vPosx = .0526545431161257, vPosy = .310313937695962,
        vVelX = -3.70820756448453e-7, vVelY = 1.0932513822004e-7, fRot = -2.860316f,
        bBOLocked = anchored, bIsBO = anchored, strBOPORShip = anchored ? "Mercury" : null };
    static void Completion(bool patched) {
        foreach (bool fast in new[] { false, true }) foreach (bool anchoredEnd in new[] { false, true })
        foreach (string kind in new[] { "ai", "non-ai", "owned", "current-player", "manned" }) {
            AccessTools.Field(typeof(GUIFFWD), "bFFWDActive").SetValue(null, fast);
            StarSystem.fEpoch = 1001;
            var ship = Vessel(kind, kind != "non-ai");
            CrewSim.coPlayer.ship = kind == "current-player" ? ship : null;
            manuallyPiloted = kind == "manned" ? ship : null;
            var original = Target(); ship.shipSituTarget = original;
            var endpoint = Endpoint(anchoredEnd);
            var nav = Route(ship, endpoint);
            bool completesPlacement = fast && !anchoredEnd;
            bool preserve = !completesPlacement || patched && kind == "ai";
            bool result = nav.TimeAdvance(ship.objSS);
            Check(result == fast, "Native completion result changed");
            Check(ship.objSS.NavData == null, "Completed plan retained");
            Check(ReferenceEquals(ship.shipSituTarget, original) == preserve, "Destination preservation scope: " + kind + "; patched=" + patched + "; SFF=" + fast + "; body=" + anchoredEnd);
            if (completesPlacement) {
                Check(ship.objSS.vPosx == endpoint.vPosx && ship.objSS.vVelX == endpoint.vVelX, "Native final placement changed");
                var copy = new ShipSitu(ship.shipSituTarget.GetJSON());
                Check(copy.bBOLocked == preserve && (copy.strBOPORShip == "Mercury") == preserve, "Target save restoration changed anchoring");
            }
        }
        // Unfinished routes cannot enter the completion helper.
        var pending = Vessel("ai"); var target = Target(); pending.shipSituTarget = target;
        manuallyPiloted = null;
        var active = Route(pending, Endpoint(), 2000);
        CrewSim.coPlayer.ship = null; StarSystem.fEpoch = 1001;
        Check(active.TimeAdvance(pending.objSS) && ReferenceEquals(pending.shipSituTarget, target) && ReferenceEquals(pending.objSS.NavData, active), "Unfinished route changed");
    }
    public static int Main() {
        var boundary = new Harmony("offline.ai-route.boundaries");
        void Patch(MethodBase method, string name) => boundary.Patch(method, prefix: new HarmonyMethod(typeof(Program), name));
        Patch(AccessTools.Method(typeof(UnityEngine.Object), "op_Equality"), nameof(Equal));
        Patch(AccessTools.Method(typeof(UnityEngine.Object), "op_Inequality"), nameof(Unequal));
        // Body ephemerides and final placement integration are Unity boundaries.
        // Native playback, destination writes/copies, command exits and JSON reconstruction run unchanged.
        Patch(AccessTools.Method(typeof(ShipSitu), nameof(ShipSitu.UpdateTime)), nameof(RefreshBoundary));
        Patch(AccessTools.Method(typeof(ShipSitu), nameof(ShipSitu.TimeAdvance)), nameof(AdvanceBoundary));
        Patch(AccessTools.Method(typeof(StarSystem), nameof(StarSystem.GetShipOwner)), nameof(Owner));
        Patch(AccessTools.Method(typeof(Ship), nameof(Ship.StopManeuver)), nameof(Stop));
        Patch(AccessTools.Method(typeof(Ship), nameof(Ship.IsStation)), nameof(Station));
        Patch(AccessTools.PropertyGetter(typeof(Ship), nameof(Ship.NavPlayerManned)), nameof(Manned));
        Patch(AccessTools.Method(typeof(StarSystem), nameof(StarSystem.GetBO)), nameof(Body));
        CrewSim.system = Empty<StarSystem>(); CrewSim.system.dictShips = new Dictionary<string, Ship>();
        CrewSim.coPlayer = Empty<CondOwner>(); Set(CrewSim.coPlayer, "_strID", "player");
        mercury = Empty<BodyOrbit>(); mercury.dVelX = 2e-7; mercury.dVelY = -1e-7;
        Completion(false);
        var production = new Harmony("offline.ai-route.production");
        production.CreateClassProcessor(typeof(Ostrabus.OstrabusPlugin).Assembly.GetType("Ostrabus.MiscBugFixes.SffNavigationDestinationPatch", true)).Patch();
        Completion(true);
        MooringCompletion(boundary, production);
        PilotRefusal(boundary, production);
        Recovery();
        HoldContinuation();
        Transport();
        PlaybackCompletion(boundary, production);
        AccessTools.Field(typeof(GUIFFWD), "bFFWDActive").SetValue(null, false);
        Console.WriteLine(checks + " native route-completion/recovery checks passed.");
        return 0;
    }
}
