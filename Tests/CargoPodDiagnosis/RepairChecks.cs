using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Ostrabus.MiscBugFixes;
using Ostranauts.Core;
using Ostranauts.Core.Models;
using Ostranauts.Ships;
using Ostranauts.Trading;

static partial class Program {
    static Dictionary<string, ShipMarket> Registry => (Dictionary<string, ShipMarket>)AccessTools.Field(typeof(MarketManager), "_market").GetValue(manager);
    static DictionaryArray<string, CondOwner> Inventory(Ship ship) => (DictionaryArray<string, CondOwner>)AccessTools.Field(typeof(Ship), "mapICOs").GetValue(ship);
    static Ship RepairShip(string id) {
        var ship = Empty<Ship>(); ship.strRegID = id;
        ship.MarketConfigs = new Dictionary<string, string>();
        ship.json = new JsonShip { aItems = Array.Empty<JsonItem>(), aMarketConfigs = new Dictionary<string, string>() };
        Field(ship, "nLoadState", Ship.Loaded.Full); Field(ship, "_bDoneLoading", true);
        Field(ship, "mapICOs", new DictionaryArray<string, CondOwner>());
        return ship;
    }
    static CondOwner Item(Ship ship, string id, bool installed = true) {
        var co = Empty<CondOwner>(); Field(co, "_strID", id); Field(co, "_objShip", ship);
        co.mapConds = new Dictionary<string, Condition> { ["IsCargoPod"] = null, ["IsMarketActor"] = null };
        if (installed) co.mapConds["IsInstalled"] = null;
        co.mapGUIPropMaps = new Dictionary<string, Dictionary<string, string>> { ["Trader"] = new Dictionary<string, string> { ["strMarketConfig"] = "CargoPodSpec" } };
        Inventory(ship)[id] = co; DataHandler.mapCOs[id] = co;
        return co;
    }
    static MarketActorConfig Register(Ship ship, string id) {
        ship.MarketConfigs[id] = "CargoPodSpec"; ship.json.aMarketConfigs[id] = "CargoPodSpec";
        MarketManager.AddMarketActorToShip(ship, id);
        return Registry[ship.strRegID].GetConfigForMarketActor(id);
    }
    static CargoPodFix.Plan Plan(Ship ship) {
        var inv = CargoPodFix.Read(ship);
        var candidates = new HashSet<string>(inv.Pods.Keys.Concat(ship.MarketConfigs.Keys));
        if (ship.json?.aMarketConfigs != null) candidates.UnionWith(ship.json.aMarketConfigs.Keys);
        if (Registry.TryGetValue(ship.strRegID, out var market)) foreach (var c in market.GetConfigs()) candidates.Add(c.COwnerId);
        return CargoPodFix.Inspect(inv, Registry, CargoPodFix.Index(CrewSim.system, candidates, Registry), null);
    }
    static void Apply(CargoPodFix.Plan plan) => Check(CargoPodFix.Apply(plan, Registry, out string error), "Repair failed: " + error);
    static bool FailBuild() => throw new InvalidOperationException("injected native cache rebuild failure");
    static int buildCalls;
    static bool FailSecondBuild() {
        if (++buildCalls == 2) throw new InvalidOperationException("injected second rebuild failure");
        return true;
    }
    static void RepairChecks(Harmony h) {
        Registry.Clear(); DataHandler.mapCOs.Clear();
        CrewSim.system = new StarSystem();
        var ship = RepairShip("OWNED"); CrewSim.system.dictShips[ship.strRegID] = ship;
        for (int i = 0; i < 4; i++) { Item(ship, "pod-" + i); Register(ship, "pod-" + i); Register(ship, "ghost-" + i); }
        var market = Registry[ship.strRegID];
        var keep = market.GetConfigForMarketActor("pod-0"); Seed(keep, 3);
        var plan = Plan(ship);
        Check(plan.Error == null && plan.Changes.Count == 4 && plan.Changes.All(x => x.Remove), "Physical 4 / global 8 baseline not identified");
        Check(market.GetConfigs().Count == 8 && ship.MarketConfigs.Count == 8 && ship.json.aMarketConfigs.Count == 8, "Dry run changed state");
        Apply(plan);
        Check(market.GetConfigs().Count == 4 && ship.MarketConfigs.Count == 4 && ship.json.aMarketConfigs.Count == 4, "Empty ghosts not reconciled in all three maps");
        Check(ReferenceEquals(keep, market.GetConfigForMarketActor("pod-0")) && keep.Stock["TestMetal"].Count == 3, "Real stock changed");
        Check(Plan(ship).Changes.Count == 0, "Repair not idempotent");
        ship.json.aMarketConfigs["saved-only-ghost"] = "CargoPodSpec";
        Check(Plan(ship).Changes.Single().Id == "saved-only-ghost", "Saved-only mapping drift ignored"); Apply(Plan(ship));
        Check(!ship.json.aMarketConfigs.ContainsKey("saved-only-ghost"), "Saved-only ghost not removed");
        Check(market.GetJson().configMap.Count == 4, "Repair not reflected in native serialization");
        // Empty stock native serialization/reload is also exercised by the baseline.
        keep.Stock.Clear();
        var loaded = new ShipMarket(market.GetJson());
        Check(loaded.GetConfigs().Count == 4 && loaded.GetConfigs().All(c => !c.COwnerId.StartsWith("ghost")), "Ghosts returned after native reload");

        Item(ship, "missing");
        plan = Plan(ship);
        Check(plan.Changes.Single().Register, "Missing physical pod registration not proposed");
        Apply(plan);
        Check(market.GetConfigForMarketActor("missing") != null && ship.json.aMarketConfigs["missing"] == "CargoPodSpec", "Missing pod not restored");
        Seed(keep, 2); ship.MarketConfigs.Remove("pod-0"); ship.json.aMarketConfigs.Remove("pod-0");
        plan = Plan(ship);
        Check(plan.Changes.Single().Register == false, "Existing cargo would be overwritten on mapping repair");
        Apply(plan);
        Check(ReferenceEquals(keep, market.GetConfigForMarketActor("pod-0")) && keep.Stock["TestMetal"].Count == 2, "Mapping repair discarded paid-for cargo");

        var paidGhost = Register(ship, "paid-ghost"); Seed(paidGhost, 7);
        Register(ship, "loose-pod"); Item(ship, "loose-pod", installed: false);
        Register(ship, "live-only"); var liveOnly = Item(ship, "live-only", installed: false); Inventory(ship).Remove("live-only");
        plan = Plan(ship);
        Check(plan.Changes.Count == 0 && plan.Preserved == 3, "Occupied/loose/cargo-bearing entries not protected");
        Check(paidGhost.Stock["TestMetal"].Count == 7 && DataHandler.mapCOs["live-only"] == liveOnly, "Protected records changed");
        Register(ship, "foreign-id");
        var foreign = RepairShip("FOREIGN"); CrewSim.system.dictShips[foreign.strRegID] = foreign; Item(foreign, "foreign-id");
        Check(Plan(ship).Changes.Count == 0, "Foreign physical item allowed orphan removal");
        Inventory(foreign).Remove("foreign-id"); DataHandler.mapCOs.Remove("foreign-id"); Register(foreign, "foreign-id");
        Check(Plan(ship).Changes.Count == 0, "Foreign market/mapping allowed orphan removal");

        Register(ship, "stale-plan"); plan = Plan(ship); Item(ship, "stale-plan", installed: false);
        Check(!CargoPodFix.Apply(plan, Registry, out _) && market.GetConfigForMarketActor("stale-plan") != null, "Stale physical evidence was applied");
        Inventory(ship).Remove("stale-plan"); DataHandler.mapCOs.Remove("stale-plan");
        plan = Plan(ship); Registry[ship.strRegID] = new ShipMarket(ship.strRegID);
        Check(!CargoPodFix.Apply(plan, Registry, out _), "Changed ship market accepted"); Registry[ship.strRegID] = market;

        // Force a failure after native removal has changed the actor map/cache.
        var mapping = ship.MarketConfigs; var savedMapping = ship.json.aMarketConfigs;
        var actors = AccessTools.Field(typeof(ShipMarket), "_marketMapping").GetValue(market);
        var caches = new[] { "_stock", "_maxVirtualInventory", "_priceModifiers", "_productionMaps", "_loadedMarketActors" }
            .ToDictionary(n => n, n => AccessTools.Field(typeof(ShipMarket), n).GetValue(market));
        Field(market, "_cachedTotalMass", 123.5);
        int actorCount = market.GetConfigs().Count;
        var rebuild = AccessTools.Method(typeof(ShipMarket), "BuildConfig");
        h.Patch(rebuild, prefix: new HarmonyMethod(typeof(Program), nameof(FailBuild)));
        Check(!CargoPodFix.Apply(Plan(ship), Registry, out var failure) && failure.Contains("injected"), "Injected native rebuild failure not handled");
        h.Unpatch(rebuild, HarmonyPatchType.Prefix, h.Id);
        Check(ReferenceEquals(Registry[ship.strRegID], market) && ReferenceEquals(mapping, ship.MarketConfigs) && ReferenceEquals(savedMapping, ship.json.aMarketConfigs), "Rollback replaced native market/mapping identities");
        Check(ReferenceEquals(actors, AccessTools.Field(typeof(ShipMarket), "_marketMapping").GetValue(market)) && market.GetConfigs().Count == actorCount && mapping.ContainsKey("stale-plan"), "Rollback lost orphan/mapping");
        foreach (var cache in caches) Check(ReferenceEquals(cache.Value, AccessTools.Field(typeof(ShipMarket), cache.Key).GetValue(market)), "Rollback replaced " + cache.Key);
        Check((double)AccessTools.Field(typeof(ShipMarket), "_cachedTotalMass").GetValue(market) == 123.5 && keep.Stock["TestMetal"].Count == 2 && paidGhost.Stock["TestMetal"].Count == 7, "Rollback changed cached mass or cargo");
        Apply(Plan(ship));
        var retiring = RepairShip("ROLLBACK"); CrewSim.system.dictShips[retiring.strRegID] = retiring;
        Register(retiring, "only-ghost"); Item(retiring, "new-real-pod");
        var originalMarket = Registry[retiring.strRegID];
        var retirePlan = Plan(retiring);
        retirePlan.Changes.Sort((a, b) => b.Remove.CompareTo(a.Remove));
        buildCalls = 0; h.Patch(rebuild, prefix: new HarmonyMethod(typeof(Program), nameof(FailSecondBuild)));
        Check(!CargoPodFix.Apply(retirePlan, Registry, out _) && buildCalls == 2, "Market removal/recreation failure not exercised");
        h.Unpatch(rebuild, HarmonyPatchType.Prefix, h.Id);
        Check(ReferenceEquals(Registry[retiring.strRegID], originalMarket) && originalMarket.GetConfigForMarketActor("only-ghost") != null && originalMarket.GetConfigForMarketActor("new-real-pod") == null,
            "Rollback after native market retirement/recreation failed");
        Check(retiring.MarketConfigs.Count == 1 && retiring.json.aMarketConfigs.Count == 1 && retiring.MarketConfigs.ContainsKey("only-ghost"), "Rollback retained partial new mapping");

        ShallowRepairChecks();
        ParserChecks();
        ScopeChecks();
        Console.WriteLine("Cargo pod repair: native registration/removal/cache rebuilding, save/reload, preservation, rollback and parser checks passed.");
    }
    static void ShallowRepairChecks() {
        var ship = RepairShip("SHALLOW"); CrewSim.system.dictShips[ship.strRegID] = ship;
        Field(ship, "nLoadState", Ship.Loaded.Shallow);
        var conditions = new[] { "IsCargoPod=1x1", "IsMarketActor=1x1", "IsInstalled=1x1" };
        var definition = new DataCO(new JsonCondOwner { strName = "TestPod", mapGUIPropMaps = new[] { "Trader", "PodGPM" } });
        DataHandler.dictDataCOs = new Dictionary<string, DataCO> { ["TestPod"] = definition };
        DataHandler.dictGUIPropMaps = new Dictionary<string, Dictionary<string, string>> { ["PodGPM"] = new Dictionary<string, string> { ["strMarketConfig"] = "CargoPodSpec" } };
        DataHandler.dictCOSaves = new Dictionary<string, JsonCondOwnerSave>();
        ship.json.aItems = new[] { new JsonItem { strID = "saved-pod", strName = "TestPod", aCondOverrides = conditions.Select(c => new DataCond(c)).ToArray() } };
        var plan = Plan(ship);
        Check(plan.Error == null && plan.Inventory.Pods.Count == 1 && plan.Changes.Single().Register, "Saved pod definition/override identification failed");
        definition.JCO.mapGUIPropMaps = new[] { "Trader", "WrongBaseMap" };
        definition.JCOO = new JsonCOOverlay { mapGUIPropMaps = new[] { "Trader", "PodGPM" } };
        Check(CargoPodFix.Read(ship).Pods["saved-pod"] == "CargoPodSpec", "Native overlay GPM replacement ignored");
        Apply(plan);
        Check(ship.LoadState == Ship.Loaded.Shallow && Inventory(ship).Count == 0 && ship.json.aMarketConfigs.Count == 1, "Shallow repair materialized a ship");
        ship.json.aItems[0].aCondOverrides = null;
        ship.json.aCOs = new[] { new JsonCondOwnerSave { strID = "saved-pod", strCODef = "TestPod", aConds = conditions } };
        Check(Plan(ship).Inventory.Pods.Count == 1, "Saved conditions not decoded");
        DataHandler.dictCOSaves["saved-pod"] = new JsonCondOwnerSave { strID = "saved-pod", strCODef = "TestPod", aConds = conditions, aCondZeroes = new[] { "IsInstalled" } };
        plan = Plan(ship);
        Check(plan.Inventory.Pods.Count == 0 && plan.Changes.Count == 0 && plan.Preserved == 1, "Newer offscreen saved state ignored");
        DataHandler.dictCOSaves.Clear();
        ship.json.aItems = new[] { ship.json.aItems[0], ship.json.aItems[0] };
        Check(Plan(ship).Preserved == 1 && Plan(ship).Changes.Count == 0, "Duplicate physical identities accepted");
        ship.json.aItems = new[] { ship.json.aItems[0] };
        var loader = Empty<AsyncShipLoader>();
        var singleton = AccessTools.Field(typeof(MonoSingleton<AsyncShipLoader>), "_instance"); singleton.SetValue(null, loader);
        var copies = new Dictionary<string, IAsyncLoadable>(); Field(loader, "_loadStateDict", copies);
        var remote = Empty<BarterZoneShip>(); remote.strRegID = ship.strRegID;
        remote.MarketConfigs = new Dictionary<string, string>(); remote.json = new JsonShip { aMarketConfigs = new Dictionary<string, string>() };
        Field(remote, "mapICOs", new DictionaryArray<string, CondOwner>()); copies[ship.strRegID] = remote;
        Check(Plan(ship).Error != null, "Incomplete remote inventory accepted");
        Field(remote, "_fullyLoaded", true);
        Check(Plan(ship).Error != null, "Conflicting remote inventory accepted");
        Item(remote, "saved-pod");
        ship.MarketConfigs.Clear(); ship.json.aMarketConfigs.Clear();
        plan = Plan(ship); Check(plan.Error == null && ReferenceEquals(plan.Inventory.Remote, remote), "Matching authoritative remote inventory rejected");
        Apply(plan);
        Check(remote.MarketConfigs.ContainsKey("saved-pod") && remote.json.aMarketConfigs.ContainsKey("saved-pod") && ((Ship)remote).json == null, "Hidden remote JSON mapping not synchronized");
        remote.json.aMarketConfigs.Clear();
        Check(Plan(ship).Changes.Count == 1, "Remote-only mapping drift missed"); Apply(Plan(ship));
        Check(remote.json.aMarketConfigs.ContainsKey("saved-pod"), "Remote-only mapping drift not repaired");
        singleton.SetValue(null, null);
        Field(ship, "nLoadState", Ship.Loaded.Edit); Check(Plan(ship).Error != null, "Editing inventory accepted");
        Field(ship, "nLoadState", Ship.Loaded.Full); Field(ship, "_bDoneLoading", false); Check(Plan(ship).Error != null, "Unfinished full inventory accepted");
        Check(CargoPodFix.Read(Empty<AsyncRatingShip>()).Error != null && CargoPodFix.Read(Empty<AsyncPreviewShip>()).Error != null, "Presentation copy accepted");
    }
    static void ParserChecks() {
        foreach (string text in new[] { "cargopodfix", "  CARGOPODFIX dryrun  ", "cargopodfix O-GXD" }) {
            Check(CargoPodFixCommand.Parse(text, out bool apply, out _, out string error) && !apply && error == null, "Dry-run parser failed: " + text);
        }
        foreach (string text in new[] { "cargopodfix apply O-GXD", "cargopodfix O-GXD apply" })
            Check(CargoPodFixCommand.Parse(text, out bool apply, out string id, out string error) && apply && id == "O-GXD" && error == null, "Apply parser failed");
        foreach (string text in new[] { "cargopodfix apply dryrun", "cargopodfix O-A O-B", "cargopodfix ../save" })
            Check(CargoPodFixCommand.Parse(text, out _, out _, out string error) && error != null, "Malformed command accepted");
        Check(!CargoPodFixCommand.Parse("cargopodfixer apply", out _, out _, out _) && !CargoPodFixCommand.Parse(null, out _, out _, out _), "Unrelated console command intercepted");
        CrewSim.objInstance = null; Check(CargoPodFix.Busy() != null, "Loading guard failed");
    }
    static void ScopeChecks() {
        var own = RepairShip("PLAYER-SHIP"); var other = RepairShip("NPC-SHIP");
        CrewSim.system.dictShips[own.strRegID] = own; CrewSim.system.dictShips[other.strRegID] = other;
        Register(own, "player-ghost"); Register(other, "npc-ghost");
        Field(CrewSim.system, "dictShipOwners", new Dictionary<string, string> { [own.strRegID] = "PLAYER", [other.strRegID] = "NPC" });
        CrewSim.coPlayer = Empty<CondOwner>(); Field(CrewSim.coPlayer, "_strID", "PLAYER");
        CrewSim.objInstance = Empty<CrewSim>(); Field(CrewSim.objInstance, "_finishedLoading", true);
        AccessTools.Field(typeof(MonoSingleton<MarketManager>), "_instance").SetValue(null, manager);
        var messages = new List<string>();
        CargoPodFix.Run(false, null, messages.Add);
        Check(Registry.ContainsKey(own.strRegID) && messages.Any(m => m.Contains("1 proposed repairs")), "Command dry run mutated state or missed owned ship");
        CargoPodFix.Run(true, "npc-ship", messages.Add);
        Check(Registry.ContainsKey(other.strRegID), "Call-sign filter repaired non-player ship");
        CargoPodFix.Run(true, "player-ship", messages.Add);
        Check(!Registry.ContainsKey(own.strRegID) && own.MarketConfigs.Count == 0 && own.json.aMarketConfigs.Count == 0, "Last empty ghost not removed through native market retirement");
        Check(Registry[other.strRegID].GetConfigForMarketActor("npc-ghost") != null, "Command modified NPC market");
        CargoPodFix.Run(true, null, messages.Add);
        Check(messages.Last().Contains("0 proposed repairs"), "Second command invocation not idempotent");
        CrewSim.bShipEdit = true; Check(CargoPodFix.Busy() != null, "Editing guard failed"); CrewSim.bShipEdit = false;
    }
}
