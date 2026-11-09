using System;
using System.Collections.Generic;
using HarmonyLib;
using Ostranauts.Core;
using Ostranauts.Ships;
using Ostranauts.Ships.AIPilots;
using Ostranauts.Ships.Commands;
using Ostranauts.ShipGUIs.Utilities;

static partial class Program {
    static Ship retrieveTow;
    static HaulerRetrievingPilot retrievingPilot;
    static int haulTicks, flyTicks;
    static bool failedPickup;
    // Substitute scene docking and steering only. Native FFWD, queue wrappers,
    // target selection and sticky-sequence transitions are under test.
    static bool RetrieveDock(Dock __instance, ref CommandStatus __result) {
        if (!failedPickup) Connect(__instance.ShipUs, retrieveTow);
        __instance.ShipUs.shipScanTarget = null; __instance.ShipUs.shipSituTarget = null; __instance.ShipUs.targetRegID = null;
        __result = new CommandStatus(failedPickup ? CommandCode.Failure : CommandCode.Finished, __instance); return false;
    }
    static bool RetrieveHaul(HaulShip __instance, ref CommandStatus __result) {
        haulTicks++;
        retrievingPilot.GetTarget().ApplyToShip(__instance.ShipUs);
        __result = new CommandStatus(CommandCode.Running, __instance); return false;
    }
    static bool RetrieveFly(FlyTo __instance, ref CommandStatus __result) {
        flyTicks++; __result = new CommandStatus(CommandCode.Running, __instance); return false;
    }
    static bool Secured(ref bool __result) { __result = true; return false; }
    static bool FixtureMass(ref double __result) { __result = 6827.87; return false; }
    static void CheckRetrieverMotion(Harmony diagnostic) {
        diagnostic.Patch(AccessTools.PropertyGetter(typeof(Ship), nameof(Ship.Mass)), prefix:new HarmonyMethod(typeof(Program), nameof(FixtureMass)));
        diagnostic.Patch(AccessTools.PropertySetter(typeof(Ship), nameof(Ship.Gravity)), prefix:new HarmonyMethod(typeof(Program), nameof(Quiet)));
        diagnostic.Patch(AccessTools.Method(typeof(Ship), nameof(Ship.AccrueTowGroupLoad)), prefix:new HarmonyMethod(typeof(Program), nameof(Quiet)));
        var tug = MakeShip("ROUTE-TUG"); var tow = MakeShip("ROUTE-TOW");
        tug.objSS.vAccRCS = new UnityEngine.Vector2(1E-10f, 0); tug.objSS.fA = .01f;
        var group = new SolarSystem.DockGroup { Anchor = Empty<ShipSitu>() };
        group.Anchor.CopyFrom(tug.objSS, false);
        foreach (var ship in new[] { tug, tow }) {
            ship.objSS._dockGroup = group;
            ship.objSS._dockOffsetPos = new Ostranauts.Utils.Models.Point(0, 0);
            group.DockedShips.Add(new SolarSystem.DockGroupMember(ship, false));
        }
        var nav = new NavData(tug);
        Set(nav, "_navPoints", new List<NavDataPoint> {
            new NavDataPoint(StarSystem.fEpoch-10, Empty<ShipSitu>()),
            new NavDataPoint(StarSystem.fEpoch+7200, Empty<ShipSitu>())
        });
        tug.objSS.NavData = nav;
        double before = StarSystem.fEpoch;
        for (int second = 1; second <= 3600; second++) {
            StarSystem.fEpoch = before+second;
            tug.objSS.TimeAdvance(1);
            Check(tug.objSS.vVelX == 0 && tug.objSS.fW == 0 && tug.objSS.vAccRCS.x == 0, "Native plotted coast resets member controls before group advance");
            group.TimeAdvance(1);
        }
        double speed = tug.objSS.vVelX*149597870.7;
        Check(group.Anchor.NavData == null && tug.objSS.NavData == nav, "Member route never reaches native group anchor");
        Check(speed > 53.85 && speed < 53.86 && tug.objSS.fW > 35.9, "Native group overwrites plotted coast with continuously accelerating/spinning anchor");
        Console.WriteLine("Native plotted tow coast overwritten by group: +"+speed+" km/s, spin="+tug.objSS.fW+" rad/s in one hour; no production motion patches.");
        // Control: ordinary maneuver updates use this native synchronization path.
        // The plotted update above does not call it.
        tug.objSS.TimeAdvance(1); tug.UpdateDockedShips(tug.objSS); group.TimeAdvance(1);
        Check(Math.Abs(tug.objSS.vVelX*149597870.7-speed) < 1E-8 && tug.objSS.fW == 0 && group.Anchor.vAccRCS.x == 0 && group.Anchor.fA == 0,
            "Native group input synchronization removes stale thrust/spin acceleration");
    }
    static void CheckRetrieverSff(bool patched) {
        var diagnostic = new Harmony("offline.retriever-sff.boundaries");
        try {
            diagnostic.Patch(AccessTools.Method(typeof(Dock), nameof(Dock.ResolveInstantly)), prefix:new HarmonyMethod(typeof(Program), nameof(RetrieveDock)));
            diagnostic.Patch(AccessTools.Method(typeof(HaulShip), nameof(HaulShip.RunCommand)), prefix:new HarmonyMethod(typeof(Program), nameof(RetrieveHaul)));
            diagnostic.Patch(AccessTools.Method(typeof(FlyTo), nameof(FlyTo.RunCommand)), prefix:new HarmonyMethod(typeof(Program), nameof(RetrieveFly)));
            var braces = AccessTools.Method(typeof(Ship), nameof(Ship.TowBracesSecured));
            diagnostic.Unpatch(braces, HarmonyPatchType.Prefix, "offline.ai-lifecycle.boundaries");
            diagnostic.Patch(braces, prefix:new HarmonyMethod(typeof(Program), nameof(Secured)));
            var tug = MakeShip("RETRIEVER"); retrieveTow = MakeShip("WRECK"); retrieveTow.DMGStatus = Ship.Damage.Derelict;
            Set(tug, "bAIShip", true);
            var home = MakeShip("OKLG"); home.objSS.bIsBO = true; home.objSS.vPosx = .01;
            tug.origin = "OKLG";
            CrewSim.coPlayer = Empty<CondOwner>(); CrewSim.coPlayer.ship = MakeShip("PLAYER");
            Set(CrewSim.coPlayer, "_strID", "PLAYER");
            CrewSim.system = Empty<StarSystem>(); CrewSim.system.dictShips = new Dictionary<string,Ship> { ["OKLG"] = home, [tug.strRegID] = tug, [retrieveTow.strRegID] = retrieveTow };
            var owners = new Dictionary<string,string> { [tug.strRegID] = "OKLGHauler", [retrieveTow.strRegID] = "OKLGCiv" };
            Set(CrewSim.system, "dictShipOwners", owners);
            retrievingPilot = Empty<HaulerRetrievingPilot>(); Back(retrievingPilot, "ShipUs", tug); retrievingPilot.Blackboard = new Blackboard();
            retrievingPilot.PriorityQueue = new QueuedCommandsNode("PriorityQueue", retrievingPilot);
            var fly = new FlyTo(retrievingPilot);
            var main = new StickySequenceCommand("Main", new ICommand[] { new Undock(retrievingPilot), new TargetNewIfNone(retrievingPilot), fly,
                new Dock(retrievingPilot), new HaulShip(retrievingPilot), new DockCargoAndDespawn(retrievingPilot) });
            retrievingPilot.BehaviorTree = new SelectorCommand("BehaviorTree", retrievingPilot.PriorityQueue, main);
            var ai = Controller(tug, retrievingPilot); Set(ai, "_behaviorTree", retrievingPilot.BehaviorTree);
            Connect(tug, retrieveTow); main.SaveData = new[] { "4" }; haulTicks = flyTicks = 0;
            ai.RunAI(); ai.RunAI();
            Check(haulTicks == 2 && flyTicks == 0 && main.SaveData[0] == "4", "Normal native main sequence continues hauling each AI tick");
            // Start at the outbound FlyTo stage, as in the archived Main=2 state.
            ((Dictionary<string,Ship>)AccessTools.Field(typeof(Ship), "aDocked").GetValue(tug)).Clear();
            ((Dictionary<string,Ship>)AccessTools.Field(typeof(Ship), "aDocked").GetValue(retrieveTow)).Clear();
            main.SaveData = new[] { "2" }; Bind(tug, retrieveTow); Set(ai, "_activeCommand", fly); haulTicks = flyTicks = 0;
            ai.FFWD();
            Check(tug.IsDockedWith(retrieveTow) && main.SaveData[0] == "2", "Native SFF pickup leaves Main at outbound FlyTo, despite completed docking");
            Check(retrievingPilot.PriorityQueue.CanExecute(retrievingPilot.Blackboard), "Native SFF queues a one-shot continuation");
            ai.RunAI();
            Check(haulTicks == 1 && ReferenceEquals(tug.shipScanTarget, home) && main.SaveData[0] == "2", "One-shot HaulShip selects home but does not advance main sequence");
            Check(!retrievingPilot.PriorityQueue.CanExecute(retrievingPilot.Blackboard), "One-shot wrapper discards a still-running haul continuation");
            ai.RunAI();
            Console.WriteLine("After one-shot: haul="+haulTicks+" fly="+flyTicks+" active="+ai.ActiveCommandName+" main="+main.SaveData[0]+" docked="+tug.IsDockedWith(retrieveTow)+" secured="+tug.TowBracesSecured());
            Check(haulTicks == 1 && flyTicks == 1 && ai.ActiveCommandName == "FlyTo" && tug.IsDockedWith(retrieveTow), "Next native AI cycle runs FlyTo while towing, instead of continuing HaulShip");
            CheckRetrieverMotion(diagnostic);
            Console.WriteLine(checks + " native retriever SFF/motion checks passed (no production patches installed).");
            if (!patched) return;
            var production = new Harmony("offline.retriever-sff.production");
            try {
                production.CreateClassProcessor(typeof(Ostrabus.OstrabusPlugin).Assembly.GetType("Ostrabus.Haulers.HaulerRetrieverSffPatch", true)).Patch();
                var queue = (Queue<ICommand>)AccessTools.Field(typeof(QueuedCommandsNode), "commandQueue").GetValue(retrievingPilot.PriorityQueue);
                void Reset() {
                    queue.Clear(); Set(retrievingPilot.PriorityQueue, "currentCommand", null);
                    ((Dictionary<string,Ship>)AccessTools.Field(typeof(Ship), "aDocked").GetValue(tug)).Clear();
                    ((Dictionary<string,Ship>)AccessTools.Field(typeof(Ship), "aDocked").GetValue(retrieveTow)).Clear();
                    main.SaveData = new[] { "2" }; Bind(tug, retrieveTow); Set(ai, "_activeCommand", fly);
                    haulTicks = flyTicks = 0; failedPickup = false;
                }
                Reset(); ai.FFWD();
                Check(main.SaveData[0] == "4" && queue.Count == 0, "Fixed pickup advances native hauling stage without one-shot queue");
                ai.RunAI(); ai.RunAI(); ai.RunAI();
                Check(haulTicks == 3 && flyTicks == 0 && main.SaveData[0] == "4" && tug.shipScanTarget == home,
                    "Fixed retrieval continues native hauling across subsequent AI ticks");
                var saved = main.GetSaveData("/BehaviorTree"); main.SaveData = new[] { "2" };
                main.LoadSaveData("/BehaviorTree", saved.ToArray());
                Check(main.SaveData[0] == "4" && queue.Count == 0 && tug.IsDockedWith(retrieveTow), "Native sequence save/reload preserves corrected hauling stage");
                ai.RunAI(); Check(haulTicks == 4 && flyTicks == 0, "Restored native sequence continues hauling");
                Reset(); failedPickup = true; ai.FFWD();
                Check(main.SaveData[0] == "2" && queue.Count == 1 && !tug.IsDocked(), "Failed pickup retains native continuation without advancing Main");
                Reset(); owners[retrieveTow.strRegID] = "PLAYER"; ai.FFWD();
                Check(main.SaveData[0] == "2" && queue.Count == 1, "Player-owned tow is excluded"); owners[retrieveTow.strRegID] = "OKLGCiv";
                Reset(); tug.HideFromSystem = true; ai.FFWD();
                Check(main.SaveData[0] == "2" && queue.Count == 1, "Hidden pooled retriever is excluded"); tug.HideFromSystem = false;
                Reset(); main.SaveData = new[] { "3" }; ai.FFWD();
                Check(main.SaveData[0] == "4" && queue.Count == 0, "Completed Dock stage also continues native hauling");
                Reset(); tug.objSS.NavData = Empty<NavData>(); ai.FFWD();
                Check(main.SaveData[0] == "2" && queue.Count == 1, "Still-plotted outbound approach retains native behavior"); tug.objSS.NavData = null;
                Reset(); retrieveTow.DMGStatus = Ship.Damage.Used; ai.FFWD();
                Check(main.SaveData[0] == "2" && queue.Count == 1, "Connected non-derelict traffic is excluded"); retrieveTow.DMGStatus = Ship.Damage.Derelict;
                Reset(); Set(tug, "nLoadState", Ship.Loaded.Full); ai.FFWD();
                Check(main.SaveData[0] == "2" && queue.Count == 1, "Materialized scene behavior is excluded"); Set(tug, "nLoadState", Ship.Loaded.Shallow);
                Reset();
                var children = (List<ICommand>)AccessTools.Field(typeof(StickySequenceCommand), "_childCommands").GetValue(main);
                var last = children[5]; children[5] = new Hold(retrievingPilot); ai.FFWD();
                Check(main.SaveData[0] == "2" && queue.Count == 1, "Unknown behavior-tree shape fails closed"); children[5] = last;
                Reset(); main.SaveData = new[] { "4" }; ai.FFWD();
                Check(main.SaveData[0] == "4" && queue.Count == 1, "Existing hauling stage is not rewritten");
                Console.WriteLine(checks + " retriever checks passed including production continuation patch.");
            } finally { production.UnpatchSelf(); }
        } finally { diagnostic.UnpatchSelf(); }
    }
}
