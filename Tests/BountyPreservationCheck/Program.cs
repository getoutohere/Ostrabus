using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using Ostrabus.Development;
using Ostrabus.ShipNavigation;
using Ostranauts.Core;
using Ostranauts.Ships;
using Ostranauts.Ships.AIPilots;
using Ostranauts.Systems;

// Actual native retirement, region recycling, stale worker, trigger evaluation
// and bounty reactivation. Unity rendering, physical CO movement, destructive
// teardown and controller construction are instrumented test boundaries.
static class Program {
    static int checks, caughtUp, activations, shipDestroys, personDestroys;
    static float reputation;
    static bool throwMove;
    static CondTrigger dropOff;
    static readonly List<string> logs = new List<string>();
    static T Empty<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    static void Set(object item, string field, object value) => AccessTools.Field(item.GetType(), field).SetValue(item, value);
    static void Back(object item, string property, object value) => Set(item, "<" + property + ">k__BackingField", value);
    static void Check(bool value, string why) { checks++; if (!value) throw new Exception(why); }
    static bool Quiet() => false;
    static bool Equal(UnityEngine.Object __0, UnityEngine.Object __1, ref bool __result) { __result = ReferenceEquals(__0, __1); return false; }
    static bool Unequal(UnityEngine.Object __0, UnityEngine.Object __1, ref bool __result) { __result = !ReferenceEquals(__0, __1); return false; }
    static bool NoCompany(ref JsonCompany __result) { __result = null; return false; }
    static bool Score(ref float __result) { __result = reputation; return false; }
    static bool Trigger(string __0, ref CondTrigger __result) {
        __result = __0 == "TIsValidStationDropOff" ? dropOff : Triggers[__0]; return false;
    }
    static bool Text(string __0, ref string __result) { __result = __0; return false; }
    static bool Catch() { caughtUp++; return false; }
    static bool Log(string __0) { logs.Add(__0); return false; }
    static bool DestroyPerson(CondOwner __instance) {
        personDestroys++; __instance.bDestroyed = true;
        DataHandler.mapCOs.Remove(__instance.strID); return false;
    }
    static bool DestroyShip(Ship __instance) { shipDestroys++; __instance.bDestroyed = true; return false; }
    static bool Move(CondOwner __0, Ship __1) {
        if (throwMove) throw new InvalidOperationException("fixture transfer failure");
        if (__0.ship != null) People(__0.ship).Remove(__0.pspec);
        __0.ship = __1; People(__1).Add(__0.pspec); return false;
    }
    static bool Queue(CondOwner __instance, ref bool __result) { __result = true; return false; }
    static bool Activate(Ship ship) { activations++; Check(!ship.HideFromSystem, "Native reset unhides hull before activation"); return false; }
    static bool Distance(ref float __result) { __result = 0; return false; }
    static bool Portrait(ref UnityEngine.Texture2D __result) { __result = null; return false; }
    static bool Derelict(ref bool __result) { __result = true; return false; }
    static bool PlotShips(ref List<string> __result) { __result = new List<string>(); return false; }
    static bool Inline(Action __0, ref Task __result) { __0(); __result = Task.CompletedTask; return false; }
    static List<PersonSpec> People(Ship ship) => (List<PersonSpec>)AccessTools.Field(typeof(Ship), "aPeople").GetValue(ship);
    static void Cond(CondOwner person, string name, double value = 1) {
        if (value == 0) { person.mapConds.Remove(name); return; }
        var condition = Empty<Condition>(); condition.fCount = value;
        person.mapConds[name] = condition;
    }
    static bool Store(CondOwner __instance, string __0, double __1) { Cond(__instance, __0, __1); return false; }
    static bool Add(CondOwner __instance, string __0, double __1) {
        Cond(__instance, __0, __1 + (__instance.mapConds.TryGetValue(__0, out var c) ? c.fCount : 0)); return false;
    }
    static bool Zero(CondOwner __instance, string __0) { Cond(__instance, __0, 0); return false; }
    static CondOwner Actor(Ship ship, string id) {
        var co = Empty<CondOwner>(); co.bAlive = true; co.ship = ship; Set(co, "_strID", id);
        co.mapConds = new Dictionary<string, Condition>(); Set(co, "aMyShips", new HashSet<string> { ship.strRegID });
        var spec = Empty<PersonSpec>(); spec.strCO = id; Set(spec, "co", co); co.pspec = spec;
        People(ship).Add(spec); DataHandler.mapCOs[id] = co; return co;
    }
    static Ship Hull(string id, bool station = false) {
        var ship = Empty<Ship>(); ship.strRegID = id;
        Back(ship, "objSS", new ShipSitu(false) { bIsBO = station });
        var co = Empty<CondOwner>(); co.mapConds = new Dictionary<string, Condition>(); co.ship = ship;
        Set(co, "_strID", "CO-" + id); Back(ship, "ShipCO", co);
        Set(ship, "nLoadState", Ship.Loaded.Shallow); Set(ship, "aPeople", new List<PersonSpec>());
        Set(ship, "aDocked", new Dictionary<string, Ship>()); Set(ship, "aDockingPorts", station ? new List<string> { "Primary" } : new List<string>());
        Set(ship, "aFactions", new List<JsonFaction>());
        if (station) Cond(co, "StationMaxPopulation", 100);
        var damageType = AccessTools.Property(typeof(Ship), "DamageSystem").PropertyType;
        Back(ship, "DamageSystem", FormatterServices.GetUninitializedObject(damageType));
        ship.fShallowRCSRemassMax = 1000; ship.fShallowRCSRemass = 10;
        CrewSim.system.dictShips[id] = ship;
        return ship;
    }
    static JsonBountySave Contract(CondOwner co, Ship hull) {
        var bounty = new JsonBountySave { strTargetID = co.strID, strTargetRegId = hull.strRegID, strTargetRegion = "STATION", enumAIType = AIType.Navy, nWantedState = 2, fTimeEndOfLife = StarSystem.fEpoch + 100000 };
        BountyManager.Bounties.Add(bounty);
        AccessTools.Method(typeof(BountyManager), "AddBountyConditions").Invoke(null, new object[] { 2, co });
        Cond(hull.ShipCO, "IsBountyTargetShip");
        return bounty;
    }
    static void Reset() {
        BountyManager.Bounties.Clear(); DataHandler.mapCOs.Clear(); CrewSim.system.dictShips.Clear();
        AIShipManager.dictAIs.Clear(); logs.Clear(); reputation = 0; caughtUp = activations = shipDestroys = personDestroys = 0;
        StarSystem.fEpoch = 100000; throwMove = false;
    }
    static readonly Dictionary<string, CondTrigger> Triggers = new Dictionary<string, CondTrigger>();
    static void Assets() {
        var rows = JArray.Parse(File.ReadAllText(Path.Combine(InstallationPaths.Data, "condtrigs", "condtrigs.json")));
        foreach (var name in new[] { "TIsValidStationDropOff", "TIsNPCPrunable", "TIsUnderArrestAlive", "TIsBountyTargetDead" }) {
            var row = rows.Cast<JObject>().Single(r => (string)r["strName"] == name);
            Triggers[name] = new CondTrigger(name, row["aReqs"].Values<string>().ToArray(), row["aForbids"].Values<string>().ToArray(), row["aTriggers"].Values<string>().ToArray(), null) { bAND = (bool)row["bAND"], logReason = false };
        }
        dropOff = Triggers["TIsValidStationDropOff"];
        Check(dropOff.aForbids.Contains("IsDead") && dropOff.aForbids.Contains("IsOKLGPirate"), "Native station rejection assets changed");
        Check(Triggers["TIsNPCPrunable"].aForbids.Contains("IsNotTrafficAI"), "Population asset no longer excludes nontraffic actors");
    }
    static void Dock(bool protectedTarget, string rejection, bool repaired) {
        Reset(); var station = Hull("STATION", true); var hull = Hull("BOUNTY"); var person = Actor(hull, "Captain");
        if (protectedTarget) Contract(person, hull);
        if (rejection == "reputation") reputation = -100;
        if (rejection == "condition") Cond(person, "IsOKLGPirate");
        if (rejection == "dead") { person.bAlive = false; Cond(person, "IsDead"); }
        bool survives = rejection == "none" || protectedTarget && repaired;
        CrewSim.DockAndDespawn(hull, station);
        Check(person.bDestroyed != survives, "Wrong retirement result: " + protectedTarget + "/" + rejection + "/" + repaired);
        Check(caughtUp == 1 && hull.HideFromSystem && hull.strAIDespawnedAt == "STATION", "Native catch-up or parking bypassed");
        Check(person.OwnsShip(hull.strRegID) == (protectedTarget && repaired), "Original ownership not retained selectively");
        Check(!person.bAlive == (rejection == "dead"), "Preservation altered life/death");
        if (!survives) { Check(personDestroys == 1 && logs.Any(s => s.Contains("NAV_LOG_CREW_DESTROY")), "Native ordinary removal bypassed"); return; }
        Check(ReferenceEquals(person.ship, station) && person.OwnsShip("STATION"), "Native station transfer/claim lost");
        Check(!logs.Any(s => s.Contains("NAV_LOG_CREW_DESTROY")), "Preserved target falsely logged as vanished");
        if (!protectedTarget || !repaired || !person.bAlive) return;
        var saved = Newtonsoft.Json.JsonConvert.DeserializeObject<JsonBountySave[]>(Newtonsoft.Json.JsonConvert.SerializeObject(BountyManager.GetSave()));
        var restore = BountyManager.Init(saved);
        while (restore.MoveNext()) { }
        Check(BountyPreservation.Target(person) && BountyPreservation.Reserved(hull), "Native bounty save/restore lost identity-based protection");
        // Exercise the native due-pass scheduler, not a forced AddCrew call.
        Set(typeof(BountyManager), "_lastMoveAIsTimeStamp", 0d);
        AccessTools.Method(typeof(BountyManager), "MoveTargets").Invoke(null, new object[] { "STATION" });
        Check(activations == 1 && ReferenceEquals(person.ship, hull) && !hull.HideFromSystem && !hull.bDestroyed,
            "Native due-pass failed to reboard the same preserved actor and hull");
        Check(person.OwnsShip(hull.strRegID), "Native reboarding lost original claim");
    }
    static void Set(Type type, string field, object value) => AccessTools.Field(type, field).SetValue(null, value);

    static void Stale(bool repaired) {
        Reset();
        var parked = Hull("PARKED"); var carrier = Hull("CARRIER"); var ordinary = Hull("ORDINARY");
        var person = Actor(carrier, "Captain"); Contract(person, parked);
        foreach (var ship in new[] { parked, carrier, ordinary }) { ship.HideFromSystem = true; Cond(ship.ShipCO, "IsStale", 80000); }
        AccessTools.Method(typeof(StarSystem), "CleanupStaleShips").Invoke(null, null);
        Check(ordinary.bRemove, "Ordinary stale hull no longer prunes");
        Check(parked.bRemove != repaired && carrier.bRemove != repaired, "Stale worker lost target/carrier or reserved hull protection");
        if (!repaired) return;
        // Native expiry removes records directly, leaving the old hull marker.
        // Protection must end with the CONTRACT, not that orphaned marker.
        BountyManager.Bounties[0].fTimeEndOfLife = 1;
        AccessTools.Method(typeof(BountyManager), "EvaluateAllBounties").Invoke(null, null);
        Check(BountyManager.Bounties.Count == 0 && parked.ShipCO.HasCond("IsBountyTargetShip"), "Expiry control did not exercise retained native hull marker");
        AccessTools.Method(typeof(StarSystem), "CleanupStaleShips").Invoke(null, null);
        Check(parked.bRemove && carrier.bRemove, "Expired contract left immortal hulls");
    }

    static void Regional(bool protectedTarget, bool repaired) {
        Reset(); var station = Hull("STATION", true); var hull = Hull("BOUNTY"); var person = Actor(hull, "Captain");
        if (protectedTarget) Contract(person, hull);
        var pilot = Empty<NavyPilot>(); Back(pilot, "ShipUs", hull); pilot.Blackboard = new Blackboard();
        var ai = Empty<AIShip>(); Set(ai, "_shipUs", hull); Set(ai, "_aiCharacter", pilot); Back(ai, "Blackboard", pilot.Blackboard);
        AIShipManager.dictAIs["STATION"] = new List<AIShip> { ai };
        // Empty faction map uses native bounty ATC fallback; ordinary NPCs use
        // OKLG_UNK, which is absent in this deliberately minimal fixture.
        var destinations = new Dictionary<string, List<string>> { { "Friendly", new List<string> { station.strRegID } } };
        AccessTools.Method(typeof(AIShipManager), "AIShipCleanup").Invoke(null, new object[] { ai, destinations, station });
        bool retain = protectedTarget && repaired;
        Check(hull.bDestroyed != retain && shipDestroys == (retain ? 0 : 1), "Wrong native regional hull disposition");
        Check(ReferenceEquals(person.ship, station) && !person.bDestroyed, "Regional native crew transfer was bypassed");
        Check(person.OwnsShip(hull.strRegID) == retain, "Region departure lost reserved-hull ownership");
        if (!retain) return;
        Check(hull.HideFromSystem && hull.strAIDespawnedAt == "STATION" && AIShipManager.GetAIShipByRegID(hull.strRegID) == null,
            "Protected regional hull was not natively unregistered and parked");
        StarSystem.fEpoch += 14400;
        AccessTools.Method(typeof(StarSystem), "CleanupStaleShips").Invoke(null, null);
        Check(!hull.bRemove, "Four-hour parked interval destroyed reserved hull");
        Set(typeof(BountyManager), "_lastMoveAIsTimeStamp", 0d);
        AccessTools.Method(typeof(BountyManager), "MoveTargets").Invoke(null, new object[] { "STATION" });
        Check(activations == 1 && ReferenceEquals(person.ship, hull) && !hull.HideFromSystem, "Region-retired target cannot resume in original ship");
    }

    static void PolicyControls() {
        Reset(); var station = Hull("STATION", true); var hull = Hull("BOUNTY"); var person = Actor(hull, "Captain");
        Contract(person, hull); Cond(person, "IsHuman");
        var prunable = Triggers["TIsNPCPrunable"];
        Check(!prunable.Triggered(person), "Native overpopulation asset can still select a bounty target");
        var ordinary = Actor(station, "Ordinary"); Cond(ordinary, "IsHuman");
        Check(prunable.Triggered(ordinary), "Population policy changed for ordinary human");
        // Reserve reuse already has a native IsBountyTargetShip exclusion.
        hull.HideFromSystem = true;
        Set(CrewSim.system, "dictShipOwners", new Dictionary<string, string> { { hull.strRegID, "Faction" } });
        Check(AIShip.FindShipForFaction("Faction") == null, "Ordinary traffic commandeered parked bounty hull");
        // Preserve ownership only when it still exists; never grant a missing
        // claim, player ownership or a replacement hull.
        person.UnclaimShip(hull.strRegID);
        BountyPreservation.Unclaim(person, hull.strRegID);
        Check(!person.OwnsShip(hull.strRegID), "Preservation invented ownership after capture");
        Cond(hull.ShipCO, "IsStale", 90000); hull.bRemove = true;
        BountyPreservation.StaleCandidates(new[] { hull });
        Check(hull.bRemove, "Existing deliberate removal was undone");
        hull.bRemove = false;
        person.bAlive = false; Cond(person, "IsDead");
        BountyPreservation.RegionalDisposal(hull, false);
        Check(!hull.bDestroyed && !person.bAlive, "Derelict sweep removed reserved hull or revived dead target");
        var unrelated = Hull("UNRELATED"); BountyPreservation.RegionalDisposal(unrelated, false);
        Check(unrelated.bDestroyed, "Ordinary derelict disposal was blocked");
        // Direct combat/debug/world-reset destruction is outside these hooks.
        int before = shipDestroys; hull.Destroy(false);
        Check(hull.bDestroyed && shipDestroys == before + 1, "Direct ship destruction was intercepted");
        before = personDestroys; person.Destroy();
        Check(person.bDestroyed && personDestroys == before + 1, "Direct actor destruction was intercepted");
        Check(Harmony.GetPatchInfo(AccessTools.PropertySetter(typeof(CondOwner), nameof(CondOwner.Kill))) == null,
            "Policy installed a global death veto");
    }

    static void DerelictSweep(bool protectedTarget, bool repaired) {
        Reset(); var station = Hull("STATION", true); station.objSS.bBOLocked = true;
        var hull = Hull("BOUNTY"); var person = Actor(hull, "Captain");
        if (protectedTarget) Contract(person, hull);
        Set(CrewSim.system, "dictShipOwners", new Dictionary<string, string> { { "STATION", "Faction" }, { hull.strRegID, "Faction" } });
        AccessTools.Method(typeof(AIShipManager), "DerelictCleanup").Invoke(null, new object[] { "STATION" });
        Check(hull.bDestroyed != (protectedTarget && repaired), "Native regional derelict sweep disposition wrong");
        Check(!hull.HideFromSystem, "Derelict sweep incorrectly hid a retained hull");
    }

    static void CustodyAndHandIn() {
        foreach (string state in new[] { "subdued", "unconscious", "dead", "player-owned", "fully-loaded" }) {
            Reset(); var station = Hull("STATION", true); var hull = Hull("BOUNTY"); var person = Actor(station, "Captain");
            Contract(person, hull);
            if (state == "subdued") Cond(person, "IsSubdued");
            if (state == "unconscious") Cond(person, "Unconscious");
            if (state == "dead") { person.bAlive = false; Cond(person, "IsDead"); }
            if (state == "player-owned") Set(CrewSim.system, "dictShipOwners", new Dictionary<string, string> { { station.strRegID, "Player" } });
            if (state == "fully-loaded") Set(station, "nLoadState", Ship.Loaded.Full);
            Set(typeof(BountyManager), "_lastMoveAIsTimeStamp", 0d);
            AccessTools.Method(typeof(BountyManager), "MoveTargets").Invoke(null, new object[] { "STATION" });
            Check(activations == 0 && ReferenceEquals(person.ship, station), "Preservation forced departure despite native custody/load: " + state);
        }
        foreach (bool alive in new[] { true, false }) {
            Reset(); var station = Hull("STATION", true); var hull = Hull("BOUNTY"); var person = Actor(station, "Captain");
            var bounty = Contract(person, hull); bounty.strRegIDDropoff = station.strRegID;
            Cond(person, alive ? "IsSubdued" : "IsDead"); person.bAlive = alive;
            var kiosk = Empty<CondOwner>(); kiosk.ship = station;
            var result = BountyManager.TryToArrest(kiosk);
            Check(result.result == "Reward granted!" && ReferenceEquals(result.Bounty, bounty), "Native accepted hand-in failed");
            Check(person.bDestroyed && BountyManager.Bounties.Count == 0 && !hull.ShipCO.HasCond("IsBountyTargetShip"), "Hand-in did not release target/hull protection");
            Check(!BountyPreservation.Reserved(hull), "Completed hand-in retained immortal hull");
        }
    }

    static void ExceptionsAndGuards() {
        Reset(); var station = Hull("STATION", true); var hull = Hull("BOUNTY"); var person = Actor(hull, "Captain"); Contract(person, hull);
        Cond(person, "IsOKLGPirate"); throwMove = true;
        try { CrewSim.DockAndDespawn(hull, station); throw new Exception("Swallowed transfer exception"); }
        catch (InvalidOperationException e) { Check(e.Message == "fixture transfer failure", "Changed native exception"); }
        finally { throwMove = false; }
        Check(!person.bDestroyed && person.OwnsShip(hull.strRegID) && !hull.HideFromSystem, "Exceptional transfer lost actor/claim or falsely completed retirement");
        foreach (Action invoke in new Action[] {
            () => BountyRetirementPatch.Transpiler(Array.Empty<CodeInstruction>(), AccessTools.Method(typeof(CrewSim), "DockAndDespawn")).ToArray(),
            () => BountyRegionalDisposalPatch.Transpiler(Array.Empty<CodeInstruction>()).ToArray(),
            () => BountyStaleCandidatesPatch.Transpiler(Array.Empty<CodeInstruction>()).ToArray()
        }) {
            try { invoke(); throw new Exception("Accepted changed native shape"); }
            catch (InvalidOperationException) { checks++; }
        }
    }
    static void Main() {
        var boundary = new Harmony("offline.bounty-preservation.boundaries");
        var policy = new Harmony("offline.bounty-preservation.production");
        void Patch(Type type, string name, string handler, Type[] args = null) => boundary.Patch(AccessTools.Method(type, name, args), prefix: new HarmonyMethod(typeof(Program), handler));
        try {
            Patch(typeof(UnityEngine.Object), "op_Equality", nameof(Equal)); Patch(typeof(UnityEngine.Object), "op_Inequality", nameof(Unequal));
            Patch(typeof(UnityEngine.Debug), "Log", nameof(Quiet), new[] { typeof(object) });
            Patch(typeof(CondOwner), "ValidateParent", nameof(Quiet)); Patch(typeof(CondOwner), "get_Company", nameof(NoCompany));
            Patch(typeof(CondOwner), "GetFactionScore", nameof(Score), new[] { typeof(List<string>) });
            Patch(typeof(CondOwner), "GetFactionScore", nameof(Score), new[] { typeof(string) });
            Patch(typeof(CondOwner), "CatchUp", nameof(Catch)); Patch(typeof(CondOwner), "Destroy", nameof(DestroyPerson));
            Patch(typeof(CondOwner), "LogMove", nameof(Quiet)); Patch(typeof(CondOwner), "HasQueuedInteraction", nameof(Queue));
            Patch(typeof(CondOwner), "SetCondAmount", nameof(Store)); Patch(typeof(CondOwner), "ZeroCondAmount", nameof(Zero));
            Patch(typeof(CondOwner), "AddCondAmount", nameof(Add)); Patch(typeof(Ship), "LogAdd", nameof(Log));
            Patch(typeof(Ship), "ToggleVis", nameof(Quiet)); Patch(typeof(Ship), "Destroy", nameof(DestroyShip));
            Patch(typeof(Ship), "Maneuver", nameof(Quiet)); Patch(typeof(Ship), "SetThrust", nameof(Quiet));
            Patch(typeof(CondOwner), "AICancelAll", nameof(Quiet)); Patch(typeof(CondOwner), "RemoveFromCurrentHome", nameof(Quiet));
            Patch(typeof(BountyManager), "Payout", nameof(Quiet)); Patch(typeof(FaceAnim2), "GetPNG", nameof(Portrait));
            Patch(typeof(MathUtils), "GetDistance", nameof(Distance), new[] { typeof(CondOwner), typeof(CondOwner) });
            Patch(typeof(Ship), "IsDerelict", nameof(Derelict)); Patch(typeof(PlotManager), "GetShipsInPlots", nameof(PlotShips));
            Patch(typeof(CrewSim), "MoveCO", nameof(Move)); Patch(typeof(DataHandler), "GetCondTrigger", nameof(Trigger), new[] { typeof(string) });
            Patch(typeof(DataHandler), "GetString", nameof(Text)); Patch(typeof(Task), "Run", nameof(Inline), new[] { typeof(Action) });
            Patch(typeof(AIShipManager), "AddAIToShip", nameof(Activate), new[] { typeof(Ship), typeof(AIType), typeof(string) });
            Patch(typeof(ShipSitu), "PushbackFrom", nameof(Quiet));
            Patch(AccessTools.Property(typeof(Ship), "DamageSystem").PropertyType, "RemoveShallowDamage", nameof(Quiet));
            CrewSim.system = Empty<StarSystem>(); CrewSim.system.dictShips = new Dictionary<string, Ship>();
            Set(CrewSim.system, "dictShipOwners", new Dictionary<string, string>());
            DataHandler.mapCOs = new Dictionary<string, CondOwner>();
            DataHandler.dictConds = new Dictionary<string, JsonCond>(); DataHandler.dictSocialStats = new Dictionary<string, SocialStats>();
            CrewSim.coPlayer = Empty<CondOwner>(); Set(CrewSim.coPlayer, "_strID", "Player");
            AIShipManager.dictAIs = new Dictionary<string, List<AIShip>>();
            Triggers["TIsPlayerCrew"] = new CondTrigger("fixturePlayerCrew", new[] { "IsPlayerCrew" }, null, null, null) { logReason = false };
            Assets();
            foreach (var reject in new[] { "none", "condition", "reputation", "dead" }) { Dock(false, reject, false); Dock(true, reject, false); }
            Stale(false); Regional(true, false); Regional(false, false);
            DerelictSweep(true, false); DerelictSweep(false, false);
            policy.CreateClassProcessor(typeof(BountyRetirementPatch)).Patch();
            policy.CreateClassProcessor(typeof(BountyRegionalDisposalPatch)).Patch();
            policy.CreateClassProcessor(typeof(BountyStaleCandidatesPatch)).Patch();
            foreach (var reject in new[] { "none", "condition", "reputation", "dead" }) { Dock(false, reject, true); Dock(true, reject, true); }
            Stale(true); Regional(true, true); Regional(false, true);
            DerelictSweep(true, true); DerelictSweep(false, true);
            policy.CreateClassProcessor(typeof(BountyDepartureFuelPatch)).Patch();
            Dock(true, "condition", true); Regional(true, true);
            PolicyControls(); CustodyAndHandIn(); ExceptionsAndGuards();
            Console.WriteLine("Bounty preservation: " + checks + " checks passed.");
        } finally { policy.UnpatchSelf(); boundary.UnpatchSelf(); BountyManager.Bounties.Clear(); CrewSim.system = null; CrewSim.coPlayer = null; }
    }
}
