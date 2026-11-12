using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Ostranauts.Core;
using Ostranauts.Ships;
using Ostranauts.Ships.AIPilots;
using Ostranauts.Ships.AIPilots.Interfaces;
using Ostranauts.Ships.Commands;
using Ostranauts.ShipGUIs.Utilities;
using Ostrabus.MiscBugFixes;

// These are actual shipped role constructors, target selectors and FFWD entrypoints.
// Crew/asset creation and physical instant docking are explicit fixture boundaries.
static partial class TrafficChecks {
    static int checks, homeCalls, regionalCalls, instantCalls;
    static double fuel;
    static bool docked;
    static double regionalX, regionalY;
    static Ship home, destination;
    static CommandCode instantResult;
    static string[] homeFilter;
    static readonly Dictionary<string, CommandCode> leafResults = new Dictionary<string, CommandCode>();
    static readonly List<string> leafCalls = new List<string>();
    static bool Leaf(BaseCommand __instance, ref CommandStatus __result) {
        string name = __instance.GetType().Name; leafCalls.Add(name);
        __result = new CommandStatus(leafResults.TryGetValue(name, out var result) ? result : CommandCode.Finished, __instance);
        return false;
    }
    static T Empty<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    static void Set(object value, string name, object data) => AccessTools.Field(value.GetType(), name).SetValue(value, data);
    static void Check(bool value, string reason) { checks++; if (!value) throw new Exception(reason); }
    static bool Quiet() => false;
    static bool Owner(ref string __result) { __result = "owner-faction"; return false; }
    static bool Nearest(List<string> __3, ref Ship __result) { homeCalls++; homeFilter = __3?.ToArray(); __result = home; return false; }
    static bool Yes(ref bool __result) { __result = true; return false; }
    static bool No(ref bool __result) { __result = false; return false; }
    static bool DrawFloat(float __0, float __1, ref float __result) { __result = __0 + (__1 - __0) * .6f; return false; }
    static bool DrawInt(int __0, ref int __result) { __result = __0; return false; }
    static bool Speed(ref double __result) { __result = 1e-9; return false; }
    static bool Fuel(ref double __result) { __result = fuel; return false; }
    static bool Docked(ref bool __result) { __result = docked; return false; }
    static bool Regional(double __0, double __1, ref Ship __result) {
        regionalCalls++; regionalX = __0; regionalY = __1; __result = home; return false;
    }
    static bool Instant(BaseCommand __instance, ref CommandStatus __result) {
        instantCalls++; __result = new CommandStatus(instantResult, __instance); return false;
    }
    static void Collections(object value) {
        foreach (var field in value.GetType().GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)) {
            Type type = field.FieldType;
            if (type != typeof(string) && !type.IsArray && !type.IsAbstract && typeof(IEnumerable).IsAssignableFrom(type) && type.GetConstructor(Type.EmptyTypes) != null)
                field.SetValue(value, Activator.CreateInstance(type));
        }
    }
    static Ship Hull(string id) {
        var ship = Empty<Ship>(); Collections(ship); ship.strRegID = id;
        Set(ship, "<objSS>k__BackingField", new ShipSitu(false) { vPosx = 2, vPosy = -3 });
        Set(ship, "nLoadState", Ship.Loaded.Shallow);
        ship.fShallowRCSRemass = 100;
        Set(ship, "<ShipCO>k__BackingField", Empty<CondOwner>());
        CrewSim.system.dictShips[id] = ship;
        return ship;
    }
    static JObject Tree(ICommand command, string path) {
        var node = new JObject { ["path"] = path, ["type"] = command.GetType().FullName,
            ["name"] = (string)AccessTools.Field(typeof(BaseCommand), "_nodeName").GetValue(command),
            ["source"] = command.GetType().Namespace + "/" + command.GetType().Name + ".cs" };
        node["methods"] = new JArray(new[] { "CanExecute", "RunCommand", "OnExit" }.Select(name => {
            var method = AccessTools.Method(command.GetType(), name);
            return new JObject { ["type"] = method.DeclaringType.FullName, ["method"] = method.Name };
        }));
        var field = AccessTools.Field(command.GetType(), "_childCommands");
        var children = field?.GetValue(command) as IEnumerable<ICommand>;
        node["children"] = children == null ? new JArray() : new JArray(children.Select((child, index) => Tree(child, path + "/" + index)));
        if (command is QueuedCommandsNode) node["dynamicQueue"] = true;
        return node;
    }
    internal static void Run(bool hiddenVisits = false, bool visitRepair = false) {
        var h = new Harmony("offline.cargo-civilian-model");
        try {
            h.Patch(AccessTools.Method(typeof(BaseCharacter), "AssignCaptain"), prefix: new HarmonyMethod(typeof(TrafficChecks), nameof(Quiet)));
            CrewSim.system = Empty<StarSystem>(); Collections(CrewSim.system);
            AIShipManager.dictAIs = new Dictionary<string, List<AIShip>>();
            home = Hull("traffic-source"); destination = Hull("traffic-destination");
            var carrier = Hull("traffic-carrier"); var visitor = Hull("traffic-civilian");
            var cargo = new HaulerCargoPilot(home.strRegID, carrier, "unused-fixture-loot");
            var civilian = new CivilianPilot(home.strRegID, visitor);
            Set(civilian, "<CaptainUs>k__BackingField", Empty<CondOwner>());
            var trees = new JObject { ["cargo"] = Tree(cargo.BehaviorTree, "cargo"), ["civilian"] = Tree(civilian.BehaviorTree, "civilian") };
            string path = "Navigation/BehaviorGraph/Traffic/control-trees.json";
            Directory.CreateDirectory("obj/TrafficAudit");
            File.WriteAllText("obj/TrafficAudit/control-trees.json", trees.ToString(Formatting.Indented) + "\n");
            if (File.Exists(path)) Check(JToken.DeepEquals(trees, JToken.Parse(File.ReadAllText(path))), "Native traffic tree changed from the reviewed snapshot");

            Check(cargo.GetTarget().Ship == home, "Cargo pilot lost its constructor home fallback");
            carrier.shipScanTarget = destination;
            Check(cargo.GetTarget().Ship == destination, "Cargo pilot ignored the explicitly dispatched destination");
            carrier.shipScanTarget = null;
            Check(cargo.GetTarget().Ship == home, "Cleared cargo target did not fall back to origin/home");
            h.Patch(AccessTools.Method(typeof(StarSystem), "GetNearestStationRegional"), prefix: new HarmonyMethod(typeof(TrafficChecks), nameof(Regional)));
            Set(cargo, "_homeStation", "");
            cargo.GetTarget();
            Check(regionalX == carrier.objSS.vPosx && regionalY == carrier.objSS.vPosy, "Cargo missing-home regional lookup did not use real coordinates");

            h.Patch(AccessTools.Method(typeof(DockCargoAndDespawn), nameof(DockCargoAndDespawn.ResolveInstantly)), prefix: new HarmonyMethod(typeof(TrafficChecks), nameof(Instant)));
            h.Patch(AccessTools.Method(typeof(Dock), nameof(Dock.ResolveInstantly)), prefix: new HarmonyMethod(typeof(TrafficChecks), nameof(Instant)));
            var cargoFly = new FlyTo(cargo);
            carrier.objSS.NavData = new NavData(carrier);
            instantCalls = 0; instantResult = CommandCode.Finished;
            Check(ReferenceEquals(cargo.FFWD(cargoFly, cargo.Blackboard), cargoFly) && instantCalls == 0, "Cargo FFWD instantly arrives despite a stored route");
            carrier.objSS.NavData = null;
            foreach (var status in new[] { CommandCode.Finished, CommandCode.Failure, CommandCode.Running }) {
                instantResult = status; instantCalls = 0;
                ICommand next = cargo.FFWD(cargoFly, cargo.Blackboard);
                Check(instantCalls == 1 && (status == CommandCode.Running ? ReferenceEquals(next, cargoFly) : next is DockCargoAndDespawn), "Cargo FFWD status handoff differs from native source");
            }
            visitor.objSS.NavData = new NavData(visitor); var civFly = new FlyTo(civilian);
            instantCalls = 0;
            Check(ReferenceEquals(civilian.FFWD(civFly, civilian.Blackboard), civFly) && instantCalls == 0, "Civilian FFWD ignores a stored route");
            visitor.objSS.NavData = null; instantResult = CommandCode.Finished;
            Check(civilian.FFWD(civFly, civilian.Blackboard) is Hold, "Successful civilian instant dock did not hand off to a new Hold");
            instantResult = CommandCode.Failure;
            h.Patch(AccessTools.Method(typeof(UnityEngine.Debug), "LogWarning", new[] { typeof(object) }), prefix: new HarmonyMethod(typeof(TrafficChecks), nameof(Quiet)));
            Check(ReferenceEquals(civilian.FFWD(civFly, civilian.Blackboard), civFly), "Failed civilian instant dock did not retain original command");

            instantResult = CommandCode.Finished; visitor.shipUndock = null;
            civilian.FFWD(new Undock(civilian), civilian.Blackboard);
            Check(regionalX == visitor.objSS.vPosx && regionalY == visitor.objSS.vPosx && regionalY != visitor.objSS.vPosy,
                "Native civilian FFWD did not reproduce the x/x regional lookup");
            visitor.shipUndock = destination; regionalCalls = 0;
            civilian.FFWD(new Undock(civilian), civilian.Blackboard);
            Check(visitor.shipScanTarget == destination && visitor.shipUndock == null && regionalCalls == 0,
                "Civilian undock with a remembered partner should not take the bad coordinate fallback");

            h.Patch(AccessTools.Method(typeof(StarSystem), "GetShipOwner"), prefix: new HarmonyMethod(typeof(TrafficChecks), nameof(Owner)));
            h.Patch(AccessTools.Method(typeof(StarSystem), "GetNearestStation"), prefix: new HarmonyMethod(typeof(TrafficChecks), nameof(Nearest)));
            homeCalls = 0;
            Check(((IAICharacter)civilian).GetTargetHome().Ship == home && homeCalls == 1, "Native interface home dispatch did not execute");
            var method = typeof(CivilianPilot).GetInterfaceMap(typeof(IAICharacter));
            int homeIndex = Array.FindIndex(method.InterfaceMethods, m => m.Name == "GetTargetHome");
            Check(method.TargetMethods[homeIndex].DeclaringType == typeof(BaseCharacter), "Civilian home override is not the inherited interface mapping expected from new virtual");
            Check(homeFilter == null, "Interface home call unexpectedly applies the civilian owner filter");
            civilian.GetTargetHome();
            Check(homeFilter != null && homeFilter.SequenceEqual(new[] { "owner-faction" }), "Direct civilian home call did not apply its owner filter");

            // An already assigned hidden reserve hull is accepted by cargo target
            // resolution; roaming eligibility is separately tested below.
            destination.HideFromSystem = true; carrier.shipScanTarget = destination;
            Check(cargo.GetTarget().Ship == destination, "Hidden live destination was unexpectedly rejected");
            home.Classification = destination.Classification = Ship.TypeClassification.GroundStation;
            h.Patch(AccessTools.PropertyGetter(typeof(Ship), nameof(Ship.HasDockingPorts)), prefix: new HarmonyMethod(typeof(TrafficChecks), nameof(Yes)));
            home.objSS.bIsBO = destination.objSS.bIsBO = true;
            home.objSS.bIsRegion = destination.objSS.bIsRegion = true;
            home.objSS.vPosx = 2; home.objSS.vPosy = -3;
            destination.objSS.vPosx = 2; destination.objSS.vPosy = 2;
            h.Unpatch(AccessTools.Method(typeof(StarSystem), "GetNearestStationRegional"), AccessTools.Method(typeof(TrafficChecks), nameof(Regional)));
            Check(CrewSim.system.GetNearestStationRegional(visitor.objSS.vPosx, visitor.objSS.vPosy) == home, "Native nearest-station fixture geometry is wrong");
            visitor.shipUndock = null;
            civilian.FFWD(new Undock(civilian), civilian.Blackboard);
            Check(visitor.shipScanTarget == destination, "Native civilian x/x fallback did not actually choose the wrong station");

            foreach (var has in AccessTools.GetDeclaredMethods(typeof(CondOwner)).Where(m => m.Name == "HasCond" && m.ReturnType == typeof(bool)))
                h.Patch(has, prefix: new HarmonyMethod(typeof(TrafficChecks), nameof(No)));
            h.Patch(AccessTools.Method(typeof(UnityEngine.Random), nameof(UnityEngine.Random.Range), new[] { typeof(float), typeof(float) }), prefix: new HarmonyMethod(typeof(TrafficChecks), nameof(DrawFloat)));
            h.Patch(AccessTools.Method(typeof(UnityEngine.Random), nameof(UnityEngine.Random.Range), new[] { typeof(int), typeof(int) }), prefix: new HarmonyMethod(typeof(TrafficChecks), nameof(DrawInt)));
            h.Patch(AccessTools.Method(typeof(Ship), nameof(Ship.GetRCSRemain)), prefix: new HarmonyMethod(typeof(TrafficChecks), nameof(Fuel)));
            h.Patch(AccessTools.Method(typeof(Ship), nameof(Ship.IsDocked)), prefix: new HarmonyMethod(typeof(TrafficChecks), nameof(Docked)));
            var hold = new Hold(civilian); fuel = 100; docked = false;
            visitor.objSS.NavData = new NavData(visitor);
            civilian.FFWD(hold, civilian.Blackboard);
            Check(visitor.shipScanTarget == destination, "Free positive-fuel Hold did not reproduce the wrong-station FFWD even with stored NavData");
            foreach (bool attached in new[] { false, true }) {
                docked = attached; fuel = attached ? 100 : 0; instantCalls = 0;
                visitor.fAIDockingExpire = -123;
                Check(ReferenceEquals(civilian.FFWD(hold, civilian.Blackboard), hold) && instantCalls == 0
                    && visitor.fAIDockingExpire >= 1 && visitor.fAIDockingExpire <= 30,
                    "Attached or empty Hold should reset its timer without instant docking");
            }
            docked = false; fuel = 100; visitor.objSS.NavData = null;
            h.Patch(AccessTools.Method(typeof(AIShip), nameof(AIShip.CalculateMaxSpeed)), prefix: new HarmonyMethod(typeof(TrafficChecks), nameof(Speed)));
            foreach (var budget in AccessTools.GetDeclaredMethods(typeof(AIShipManager)).Where(m => m.Name == "BingoFuelCheck"))
                h.Patch(budget, prefix: new HarmonyMethod(typeof(TrafficChecks), nameof(No)));
            AIShipManager.strATCLast = "traffic-fixture"; AIShipManager.dictAIs[AIShipManager.strATCLast] = new List<AIShip>();
            CrewSim.system.dictShips.Remove(carrier.strRegID);
            var reserve = Hull("traffic-hidden-reserve"); reserve.HideFromSystem = true;
            Check(civilian.GetTarget().Ship == reserve, "Civilian ship-selection path did not reproduce hidden reserve selection");
            ((Dictionary<string, string>)AccessTools.Field(typeof(StarSystem), "dictShipOwners").GetValue(CrewSim.system))[reserve.strRegID] = "owner-faction";
            Check(AIShip.FindShipForFaction("owner-faction") == reserve,
                "Selected hidden hull is not simultaneously eligible for native spawn-pool reuse");
            if (hiddenVisits) HiddenVisitInvestigation(h, civilian, visitor, reserve);
            if (visitRepair) CivilianVisitRepair(h);
            TreeSemantics(cargo, civilian);
            CoordinateRepair(h, civilian, visitor);
            Console.WriteLine(checks + " native traffic role checks passed; actual constructor trees exported. Crew creation and physical instant docking are prescribed boundaries; regional geometry executes natively.");
        } finally { h.UnpatchSelf(); }
    }
    static void CoordinateRepair(Harmony h, CivilianPilot civilian, Ship visitor) {
        h.CreateClassProcessor(typeof(CivilianSffCoordinatesPatch)).Patch();
        fuel = 100; docked = false;
        foreach (bool holding in new[] { false, true }) foreach (bool route in new[] { false, true })
            foreach (var status in new[] { CommandCode.Finished, CommandCode.Failure, CommandCode.Running }) {
                visitor.shipUndock = null; visitor.shipScanTarget = destination;
                visitor.objSS.NavData = route ? new NavData(visitor) : null;
                instantResult = status; instantCalls = 0;
                ICommand command = holding ? (ICommand)new Hold(civilian) : new Undock(civilian);
                ICommand next = civilian.FFWD(command, civilian.Blackboard);
                Check(visitor.shipScanTarget == home && instantCalls == 1,
                    "Corrected civilian Undock/Hold chose the wrong station or changed instant-docking eligibility");
                Check(status == CommandCode.Finished ? next is Hold : ReferenceEquals(next, command),
                    "Coordinate correction changed native success/failure/Running continuation");
            }
        visitor.objSS.NavData = null; visitor.shipUndock = destination; instantResult = CommandCode.Finished;
        civilian.FFWD(new Undock(civilian), civilian.Blackboard);
        Check(visitor.shipScanTarget == destination && visitor.shipUndock == null,
            "Coordinate correction changed remembered-partner docking");
        foreach (bool attached in new[] { false, true }) {
            docked = attached; fuel = attached ? 100 : 0; instantCalls = 0;
            visitor.shipScanTarget = destination; visitor.fAIDockingExpire = -123;
            var hold = new Hold(civilian);
            Check(ReferenceEquals(civilian.FFWD(hold, civilian.Blackboard), hold) && instantCalls == 0
                && visitor.shipScanTarget == destination && visitor.fAIDockingExpire >= 1 && visitor.fAIDockingExpire <= 30,
                "Coordinate correction changed attached/empty-fuel Hold behavior");
        }
        fuel = 100; docked = false; visitor.objSS.NavData = new NavData(visitor); instantCalls = 0;
        var fly = new FlyTo(civilian);
        Check(ReferenceEquals(civilian.FFWD(fly, civilian.Blackboard), fly) && instantCalls == 0,
            "Coordinate correction changed existing civilian flight handling");
        visitor.objSS.NavData = null; instantResult = CommandCode.Finished;
        Check(civilian.FFWD(fly, civilian.Blackboard) is Hold && visitor.shipScanTarget == destination,
            "Coordinate correction changed ordinary flight arrival target");
        bool rejected = false;
        try { CivilianSffCoordinatesPatch.Transpiler(Array.Empty<CodeInstruction>()).ToArray(); }
        catch (InvalidOperationException) { rejected = true; }
        Check(rejected, "Coordinate transpiler accepted an unknown native method layout");
    }
    static IEnumerable<ICommand> Commands(ICommand command) {
        yield return command;
        var children = AccessTools.Field(command.GetType(), "_childCommands")?.GetValue(command) as IEnumerable<ICommand>;
        if (children != null) foreach (var child in children) foreach (var nested in Commands(child)) yield return nested;
    }
    static void TreeSemantics(HaulerCargoPilot cargo, CivilianPilot civilian) {
        var h = new Harmony("offline.traffic.tree-semantics");
        try {
            foreach (var type in Commands(cargo.BehaviorTree).Concat(Commands(civilian.BehaviorTree))
                .Where(c => !(c is ICommandNode)).Select(c => c.GetType()).Distinct()) {
                h.Patch(AccessTools.Method(type, "RunCommand"), prefix: new HarmonyMethod(typeof(TrafficChecks), nameof(Leaf)));
                h.Patch(AccessTools.Method(type, "CanExecute"), prefix: new HarmonyMethod(typeof(TrafficChecks), nameof(Yes)));
            }
            void Run(BaseCharacter pilot, Dictionary<string, CommandCode> input) {
                leafCalls.Clear(); leafResults.Clear();
                foreach (var pair in input) leafResults[pair.Key] = pair.Value;
                pilot.BehaviorTree.RunCommand(pilot.Blackboard);
            }
            Run(cargo, new Dictionary<string, CommandCode> { ["AmIDamaged"] = CommandCode.Failure });
            Check(leafCalls.Contains("TargetNewIfNone") && !leafCalls.Contains("TargetNearestStation"),
                "Healthy threatened cargo does not take the native flee sequence expected from source");
            Run(cargo, new Dictionary<string, CommandCode>());
            Check(leafCalls.Contains("SurrenderOffer") && leafCalls.Contains("TargetNearestStation") && !leafCalls.Contains("TargetNewIfNone"),
                "Damaged targeted cargo did not surrender then enter flee before Main");
            Run(civilian, new Dictionary<string, CommandCode>());
            Check(leafCalls.Contains("SurrenderOffer") && !leafCalls.Contains("DeployDecoys") && !leafCalls.Contains("TargetNewIfNone"),
                "Civilian health/surrender response did not precede defensive/fight/Main branches");
            Run(civilian, new Dictionary<string, CommandCode> { ["AmIDamaged"] = CommandCode.Failure });
            Check(leafCalls.Contains("DeployDecoys") && !leafCalls.Contains("SetEngagementProfile"), "Civilian defensive decoys did not precede armed response");
            Run(civilian, new Dictionary<string, CommandCode> { ["AmIDamaged"] = CommandCode.Failure, ["DoIHaveDecoys"] = CommandCode.Failure });
            Check(leafCalls.Contains("SetEngagementProfile") && leafCalls.Contains("KeepWithinFiringDistance"), "Civilian short-range armed branch was not represented");
            Run(civilian, new Dictionary<string, CommandCode> { ["AmIDamaged"] = CommandCode.Failure, ["DoIHaveDecoys"] = CommandCode.Failure, ["DoIPreferShortRange"] = CommandCode.Failure });
            Check(leafCalls.Contains("KeepAtDistance") && !leafCalls.Contains("KeepWithinFiringDistance"), "Civilian long-range armed fallback was not represented");
            Run(civilian, new Dictionary<string, CommandCode> { ["AmIDamaged"] = CommandCode.Failure, ["DoIHaveDecoys"] = CommandCode.Failure, ["AmIArmed"] = CommandCode.Failure });
            Check(leafCalls.Contains("TargetEscapeStation") && !leafCalls.Contains("TargetNewIfNone"), "Unarmed civilian did not enter flee before normal work");
            Run(civilian, new Dictionary<string, CommandCode> { ["AmIInCombat"] = CommandCode.Failure });
            Check(leafCalls.Contains("TargetNearestStation") && !leafCalls.Contains("TargetNewIfNone"), "Civilian threat flee not represented separately from in-combat flee");
        } finally { h.UnpatchSelf(); }
    }
}
