using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using Ostranauts.ShipGUIs.NavStation;
using Ostranauts.ShipGUIs.Utilities;
using Ostranauts.Ships.AIPilots;
using Ostranauts.Ships.Commands;
using UnityEngine;
using Ostrabus.ShipNavigation;

static partial class Program {
    static bool TextKey(string __0, ref string __result) { __result = __0; return false; }
    static bool NoiseOnly(bool bNoiseOnly) => !bNoiseOnly;
    static bool CommandType(string __0, ref Type __result) {
        __result = typeof(FlyToAutoPilot).Assembly.GetType("Ostranauts.Ships.Commands." + __0, true);
        return false;
    }

    // This candidate is deliberately scoped to the real private withdrawal
    // method. It is installed only in this offline process, never the plugin.
    static void CancelWithdrawn(AIShip __0) {
        if (__0?.Ship?.objSS != null) __0.Ship.objSS.ResetNavData();
    }

    static bool CancelRegisteredOnly(AIShip __0) {
        if (__0 == null || !RegisteredIdentity(__0)) return false;
        __0.Ship?.objSS?.ResetNavData();
        return true;
    }

    static IEnumerable<CodeInstruction> EngageOrderCandidate(IEnumerable<CodeInstruction> instructions) {
        var code = new List<CodeInstruction>(instructions);
        var plan = AccessTools.Field(typeof(ShipSitu), nameof(ShipSitu.NavData));
        int store = code.FindIndex(instruction => instruction.StoresField(plan));
        if (store < 6 || code.FindLastIndex(instruction => instruction.StoresField(plan)) != store ||
            code[store - 6].opcode != OpCodes.Ldarg_0 ||
            !code[store - 1].Calls(AccessTools.PropertyGetter(typeof(NavModCoursePlot), nameof(NavModCoursePlot.NavPlan))))
            throw new InvalidOperationException("Unknown course publication sequence");
        var assignment = code.GetRange(store - 6, 7);
        // The native null-plan branch lands at the original assignment. It must
        // now land at withdrawal, not bypass withdrawal to the moved assignment.
        assignment[0].MoveLabelsTo(code[store + 1]);
        assignment[0].MoveBlocksTo(code[store + 1]);
        code.RemoveRange(store - 6, 7);
        int withdraw = code.FindIndex(instruction => instruction.Calls(
            AccessTools.Method(typeof(AIShipManager), nameof(AIShipManager.UnregisterShip), new[] { typeof(Ship) })));
        if (withdraw < 0) throw new InvalidOperationException("Missing native controller withdrawal");
        code.InsertRange(withdraw + 1, assignment);
        return code;
    }

    static void ClearControllers() {
        AIShipManager.dictAIs.Clear();
        AIShipManager.dictAIs["fixture"] = new List<AIShip>();
        ((Ostranauts.Ships.AIShipQueue)AccessTools.Field(typeof(AIShipManager), "_aIQueue").GetValue(null)).Clear();
    }

    static AIShip NativeAuto(Ship ship, string command) => AIShipManager.AddAIToShip(ship, AIType.Auto,
        "INTERREGIONAL", new JsonAIShipSave {
            strRegId = ship.strRegID,
            strHomeStation = "OKLG",
            enumAIType = AIType.Auto,
            strActiveCommand = command,
            strActiveCommandPayload = command == "FlyToAutoPilot" ? new[] { "1" } : Array.Empty<string>()
        });

    static CondOwner ConsoleOwner(Ship ship) {
        var co = Empty<CondOwner>(); co.ship = ship;
        co.mapGUIPropMaps = new Dictionary<string, Dictionary<string, string>>();
        return co;
    }

    static GUIOrbitDraw OrbitConsole(Ship ship) {
        var console = Empty<GUIOrbitDraw>();
        Set(console, "coSelfTemp", ConsoleOwner(ship));
        Set(console, "dictPropMap", new Dictionary<string, string>());
        console.strCOKey = "Panel A";
        Set(console, "ledWLock", Empty<GUILamp>());
        return console;
    }

    static void CourseEngagement(int mode) {
        foreach (string previous in new[] { "none", "civilian", "auto" }) {
            foreach (bool torch in new[] { false, true }) {
                ClearControllers(); StarSystem.fEpoch = 1000;
                var ship = Hull("engage-" + mode + "-" + previous + "-" + torch);
                ship.IsAIShip = false;
                AIShip old = previous == "none" ? null : previous == "auto" ? NativeAuto(ship, "FlyToAutoPilot") : Register(ship);
                var nav = Course(ship, torch);
                // Preview creation must not itself depend on an active course.
                ship.objSS.NavData = null;
                var console = Empty<NavModCoursePlot>();
                Set(console, "COSelf", ConsoleOwner(ship));
                console.NavPlan = nav;
                AccessTools.Method(typeof(NavModCoursePlot), "EngageTrip").Invoke(console, null);
                var current = AIShipManager.GetAIShipByRegID(ship.strRegID);
                Check(current != null && current.AIType == AIType.Auto && !ReferenceEquals(current, old), "EngageTrip did not create native replacement Auto");
                Check(current.ActiveCommandName == "FlyToAutoPilot", "EngageTrip lost the native queued autopilot leaf");
                bool retained = mode != 1 || old == null;
                Check(ReferenceEquals(ship.objSS.NavData, nav) == retained, "Unexpected new-plan survival for withdrawal candidate");
                Check(ship.objSS.vVelX == 2e-8 && ship.objSS.vVelY == -1e-8, "EngageTrip changed momentum");
                if (retained) {
                    StarSystem.fEpoch = 1050;
                    Check(nav.TimeAdvance(ship.objSS), "Engaged course was not executable by native playback");
                    Check(ship.objSS.vVelX > 2e-8, "Engaged course did not advance");
                }
            }
        }
    }

    static void ControlModeHandoffs(int mode) {
        foreach (bool stationKeeping in new[] { false, true }) {
            ClearControllers(); StarSystem.fEpoch = 1000;
            var ship = Hull("control-mode-" + mode + "-" + stationKeeping);
            ship.IsAIShip = false;
            NativeAuto(ship, "FlyToAutoPilot");
            var oldPlan = Course(ship, true);
            var console = OrbitConsole(ship);
            var station = Hull("control-reference");
            var poi = Empty<NavPOI>(); Set(poi, "_ship", station);
            GUIOrbitDraw.CrossHairTarget = poi;
            string method = stationKeeping ? "ToggleStationKeeping" : "ToggleHoldThrust";
            string command = stationKeeping ? "HoldStationAutoPilot" : "HoldThrustAutoPilot";
            AccessTools.Method(typeof(GUIOrbitDraw), method).Invoke(console, new object[] { true });
            var current = AIShipManager.GetAIShipByRegID(ship.strRegID);
            Check(current != null && current.AIType == AIType.Auto && current.ActiveCommandName == command,
                "Native control-mode replacement failed");
            Check(mode == 0 ? ReferenceEquals(ship.objSS.NavData, oldPlan) : ship.objSS.NavData == null,
                "Unexpected abandoned old course during control-mode replacement");
            if (stationKeeping) Check(ReferenceEquals(ship.shipStationKeepingTarget, station), "Station-keeping reference was erased");
            Check(ship.objSS.vVelX == 2e-8 && ship.objSS.vVelY == -1e-8, "Control-mode switch changed momentum");
            StarSystem.fEpoch += 2;
            AccessTools.Method(typeof(GUIOrbitDraw), method).Invoke(console, new object[] { false });
            Check(AIShipManager.GetAIShipByRegID(ship.strRegID) == null, "Native mode disable did not remove its Auto controller");
        }
    }

    static void RetirementAndTerminalLeaves(int mode) {
        foreach (string reason in new[] { "station-retirement", "remove-leaf", "blank-expiry", "missile-fuel", "fuel-distress" }) {
            ClearControllers(); StarSystem.fEpoch = 1000;
            var ship = Hull("terminal-" + mode + "-" + reason);
            var ai = Register(ship);
            var nav = Course(ship, true);
            var pilot = (Pilot)ai.AICharacter;
            var spin = ship.objSS.fW;
            if (reason == "station-retirement") {
                Set(ship, "aPeople", new List<PersonSpec>());
                var station = Hull("retirement-station");
                CrewSim.DockAndDespawn(ship, station);
                Check(ship.HideFromSystem && ship.strAIDespawnedAt == station.strRegID, "Native retirement disposition changed");
                Check(!ship.bDestroyed && CrewSim.system.dictShips.ContainsKey(ship.strRegID), "Native reserve hull was deleted");
            } else if (reason == "remove-leaf") {
                Check(new RemoveAI(pilot).RunCommand(pilot.Blackboard).Status == CommandCode.Finished,
                    "Native RemoveAI continuation changed");
            } else if (reason == "blank-expiry") {
                var blank = new Blank(pilot);
                blank.SaveData = new[] { "0" };
                Check(blank.RunCommand(pilot.Blackboard).Status == CommandCode.Finished,
                    "Native Blank terminal continuation changed");
            } else if (reason == "missile-fuel") {
                ship.fShallowRCSRemass = 0;
                Check(new FlyToMissile(pilot).RunCommand(pilot.Blackboard).Status == CommandCode.Failure,
                    "Native missile fuel failure changed");
            } else {
                CrewSim.coPlayer = ConsoleOwner(Hull("distress-player-context"));
                ship.fLastVisit = 1; // Keep the separate distant stale-cleanup policy outside this case.
                Check(new SosOutOfFuel(pilot).RunCommand(pilot.Blackboard).Status == CommandCode.Finished,
                    "Native civilian fuel-distress continuation changed");
            }
            Check(AIShipManager.GetAIShipByRegID(ship.strRegID) == null, "Terminal native caller did not withdraw its controller");
            Check(mode == 0 ? ReferenceEquals(ship.objSS.NavData, nav) : ship.objSS.NavData == null,
                "Unexpected terminal caller course ownership");
            Check(ship.objSS.vAccIn.x == 0 && ship.objSS.vAccRCS.x == 0, "Native terminal stop changed");
            Check(ship.objSS.vVelX == 2e-8 && ship.objSS.vVelY == -1e-8 && ship.objSS.fW == spin,
                "Terminal cancellation changed momentum or spin");
            Check(ship.fShallowFusionRemain == 1000, "Terminal cancellation changed fusion fuel");
        }
    }

    static void PlayerDisengagement(int mode) {
        ClearControllers();
        var ship = Hull("player-disengage-" + mode); ship.IsAIShip = false;
        NativeAuto(ship, "FlyToAutoPilot"); Course(ship, true);
        var console = Empty<NavModCoursePlot>();
        Set(console, "COSelf", ConsoleOwner(ship));
        Set(console, "_guiOrbitDraw", OrbitConsole(ship));
        console.chkEngage = Empty<UnityEngine.UI.Toggle>();
        AccessTools.Method(typeof(NavModCoursePlot), "Engage").Invoke(console, null);
        Check(AIShipManager.GetAIShipByRegID(ship.strRegID) == null && ship.objSS.NavData == null,
            "Native player disengagement did not release Auto and its course");
        Check(ship.objSS.vAccIn.x == 0 && ship.objSS.vAccRCS.x == 0 && ship.objSS.vVelX == 2e-8,
            "Native disengagement stop/momentum changed");
    }

    static void RejectedModeChangesPreserveNavigation(int mode) {
        foreach (string previous in new[] { "none", "FlyToAutoPilot", "HoldStationAutoPilot" }) {
            ClearControllers(); StarSystem.fEpoch = 1000;
            var ship = Hull("invalid-reference-" + mode + "-" + previous);
            var old = previous == "none" ? null : NativeAuto(ship, previous);
            var nav = Course(ship, true);
            var console = OrbitConsole(ship);
            Set(console, "chkStationKeeping", Empty<UnityEngine.UI.Toggle>());
            GUIOrbitDraw.CrossHairTarget = null;
            AccessTools.Method(typeof(GUIOrbitDraw), "ToggleStationKeeping").Invoke(console, new object[] { true });
            bool withdrawn = previous == "HoldStationAutoPilot";
            Check(ReferenceEquals(AIShipManager.GetAIShipByRegID(ship.strRegID), withdrawn ? null : old),
                "Invalid station reference removed an unrelated mode");
            Check(ReferenceEquals(ship.objSS.NavData, nav) == (mode == 0 || !withdrawn),
                "Rejected mode change cancelled navigation without withdrawal");
            Check(ship.objSS.vVelX == 2e-8, "Invalid reference changed momentum");
        }
        foreach (bool pda in new[] { false, true }) {
            ClearControllers(); StarSystem.fEpoch = 1000;
            var ship = Hull("blocked-hold-toggle-" + mode + "-" + pda);
            var ai = NativeAuto(ship, "FlyToAutoPilot"); var nav = Course(ship, true);
            var console = OrbitConsole(ship);
            if (pda) Set(console, "<IsPDANav>k__BackingField", true);
            else Set(console, "_holdthrustTimeStamp", StarSystem.fEpoch);
            AccessTools.Method(typeof(GUIOrbitDraw), "ToggleHoldThrust").Invoke(console, new object[] { true });
            Check(ReferenceEquals(AIShipManager.GetAIShipByRegID(ship.strRegID), ai) && ReferenceEquals(ship.objSS.NavData, nav),
                "PDA/debounce early return changed native Auto navigation");
        }
    }

    static int CandidateIntegration(string[] args) {
        var candidate = new Harmony("offline.caller-audit.integration-candidate");
        bool production = args[0] == "production-integration";
        try {
            if (production) InstallOwnershipProduction(candidate);
            else {
                candidate.Patch(AccessTools.Method(typeof(AIShipManager), "UnregisterShip", new[] { typeof(AIShip) }),
                    prefix: new HarmonyMethod(typeof(Program), nameof(CancelRegisteredOnly)));
                candidate.Patch(AccessTools.Method(typeof(NavModCoursePlot), "EngageTrip"),
                    transpiler: new HarmonyMethod(typeof(Program), nameof(EngageOrderCandidate)));
                candidate.Patch(AccessTools.Method(typeof(AIShipManager), "RunAIQueue"),
                    transpiler: new HarmonyMethod(typeof(Program), nameof(DispatchCandidate)));
                candidate.Patch(AccessTools.Method(typeof(AIShipManager), nameof(AIShipManager.FFWD)),
                    transpiler: new HarmonyMethod(typeof(Program), nameof(DispatchCandidate)));
                if (args[0] == "candidate-terminal-integration")
                    candidate.Patch(AccessTools.Method(typeof(LowFuelRetreat), nameof(LowFuelRetreat.RunCommand)),
                        transpiler: new HarmonyMethod(typeof(Program), nameof(RetreatTerminalCandidate)));
            }
            var target = System.Reflection.Assembly.LoadFrom(System.IO.Path.GetFullPath(args[1]));
            var entry = target.EntryPoint;
            if (entry == null) throw new InvalidOperationException("Integration target has no entry point");
            var tail = new string[args.Length - 2];
            Array.Copy(args, 2, tail, 0, tail.Length);
            object result = entry.Invoke(null, entry.GetParameters().Length == 0 ? null : new object[] { tail });
            foreach (var method in new[] {
                AccessTools.Method(typeof(AIShipManager), "UnregisterShip", new[] { typeof(AIShip) }),
                AccessTools.Method(typeof(NavModCoursePlot), "EngageTrip"),
                AccessTools.Method(typeof(AIShipManager), "RunAIQueue"),
                AccessTools.Method(typeof(AIShipManager), nameof(AIShipManager.FFWD))
            }) {
                var info = Harmony.GetPatchInfo(method);
                bool installed = false;
                if (info != null) foreach (string owner in info.Owners) if (owner == candidate.Id) installed = true;
                if (!installed) throw new InvalidOperationException("Target suite removed ownership candidate from " + method.Name);
            }
            if ((production || args[0] == "candidate-terminal-integration") &&
                !Harmony.GetPatchInfo(AccessTools.Method(typeof(LowFuelRetreat), nameof(LowFuelRetreat.RunCommand))).Owners.Contains(candidate.Id))
                throw new InvalidOperationException("Target suite removed terminal retreat candidate");
            Console.WriteLine(production ? "Production ownership patches verified on all five target methods after the integration suite."
                : "Offline ownership candidate remained installed throughout target suite.");
            return result is int code ? code : 0;
        } catch (System.Reflection.TargetInvocationException error) {
            Console.Error.WriteLine(error.InnerException ?? error);
            return 1;
        } finally { candidate.UnpatchSelf(); }
    }

    static void StalePrivateWithdrawal(int mode) {
        ClearControllers(); StarSystem.fEpoch = 1000;
        var ship = Hull("stale-withdrawal-" + mode);
        var old = Register(ship);
        AIShipManager.UnregisterShip(ship);
        var replacement = NativeAuto(ship, "FlyToAutoPilot");
        var newPlan = Course(ship, true);
        Check(newPlan.TimeAdvance(ship.objSS), "Replacement course did not begin native playback");
        // A saved snapshot can contain the old object after a replacement has
        // already registered. The private overload receives that exact object.
        AccessTools.Method(typeof(AIShipManager), "UnregisterShip", new[] { typeof(AIShip) })
            .Invoke(null, new object[] { old });
        bool survives = mode >= 3;
        Check(ReferenceEquals(AIShipManager.GetAIShipByRegID(ship.strRegID), replacement) == survives,
            "Unexpected stale-withdrawal effect on replacement registration");
        Check(ReferenceEquals(ship.objSS.NavData, newPlan) == (mode == 0 || survives),
            "Unexpected stale-withdrawal effect on replacement plan");
        Check((ship.objSS.vAccIn.x != 0) == survives, "Unexpected stale-withdrawal effect on replacement force");
        Check(ship.objSS.vVelX == 2e-8, "Stale withdrawal changed momentum");
    }

    static void NativeSavedCourseReconstruction(int mode) {
        foreach (bool torch in new[] { false, true }) {
            ClearControllers(); StarSystem.fEpoch = 1000;
            var ship = Hull("saved-course-" + mode + "-" + torch);
            ship.IsAIShip = false;
            NativeAuto(ship, "FlyToAutoPilot");
            var nav = Course(ship, torch);
            string savedPlan = LitJson.JsonMapper.ToJson(nav.GetJSON());
            string savedAI = LitJson.JsonMapper.ToJson(AIShipManager.GetJSONAIShipSave(ship.strRegID));
            AIShipManager.UnregisterShip(ship);
            ship.bDestroyed = true;
            var restoredShip = Hull(ship.strRegID); restoredShip.IsAIShip = false;
            var restoredPlan = new NavData(LitJson.JsonMapper.ToObject<JsonNavData>(savedPlan));
            restoredShip.objSS.NavData = restoredPlan;
            var restoredRecord = LitJson.JsonMapper.ToObject<JsonAIShipSave>(savedAI);
            var restored = AIShipManager.AddAIToShip(restoredShip, AIType.Auto, "INTERREGIONAL", restoredRecord, gameLoad: true);
            Check(restored != null && ReferenceEquals(restored.Ship, restoredShip), "Saved Auto reconstruction did not bind replacement hull");
            Check(ReferenceEquals(restoredShip.objSS.NavData, restoredPlan), "Saved controller registration erased restored course");
            Check(restoredPlan.GetNavDataPoints().Count == 2 && restored.ActiveCommandName == "FlyToAutoPilot",
                "Saved native plan or queued autopilot command was lost");
            StarSystem.fEpoch = 1050;
            Check(restoredPlan.TimeAdvance(restoredShip.objSS) && restoredShip.objSS.vVelX > 2e-8,
                "Saved course failed native playback after reconstruction");
        }
    }

    static void MissingRegistryIsNotWithdrawal() {
        ClearControllers();
        var ship = Hull("missing-registry");
        var ai = Register(ship); var nav = Course(ship, true);
        var registry = AIShipManager.dictAIs;
        AIShipManager.dictAIs = null;
        try {
            AccessTools.Method(typeof(AIShipManager), "UnregisterShip", new[] { typeof(AIShip) })
                .Invoke(null, new object[] { ai });
            Check(ReferenceEquals(ship.objSS.NavData, nav) && ship.objSS.vAccIn.x != 0,
                "Absent registry was mistaken for a successful controller withdrawal");
        } finally { AIShipManager.dictAIs = registry; }
    }

    static void MigrationPreservesControllerIdentity() {
        ClearControllers(); StarSystem.fEpoch = 1000;
        AIShipManager.dictAIs["INTERREGIONAL"] = new List<AIShip>();
        int runs = 0;
        var ship = Hull("migrating-controller");
        var ai = Scheduled(ship, () => runs++);
        var nav = Course(ship, true);
        var first = Scheduled(Hull("migrate-first"), () => {
            AIShipManager.dictAIs["fixture"].Remove(ai);
            AIShipManager.AddAIToDict(ai, "INTERREGIONAL");
        });
        var queue = (Ostranauts.Ships.AIShipQueue)AccessTools.Field(typeof(AIShipManager), "_aIQueue").GetValue(null);
        queue.Enqueue(first); queue.PrioritizeShip(ai); queue.PrioritizeShip(first);
        AccessTools.Method(typeof(AIShipManager), "RunAIQueue").Invoke(null, null);
        Check(runs == 1 && RegisteredIdentity(ai), "Region migration was mistaken for controller withdrawal");
        Check(ReferenceEquals(ship.objSS.NavData, nav), "Region migration cancelled legitimate navigation");
    }

    static void SetupCallerFixtures(Harmony fixture) {
        void Patch(Type type, string name, string handler, Type[] parameters = null) => fixture.Patch(
            AccessTools.Method(type, name, parameters), prefix: new HarmonyMethod(typeof(Program), handler));
        Patch(typeof(BaseCharacter), "AssignCaptain", nameof(Quiet));
        Patch(typeof(DataHandler), nameof(DataHandler.GetString), nameof(TextKey), new[] { typeof(string), typeof(bool) });
        Patch(typeof(DataHandler), nameof(DataHandler.GetCommand), nameof(CommandType));
        Patch(typeof(Ship), nameof(Ship.LogAdd), nameof(Quiet));
        Patch(typeof(Ship), nameof(Ship.ToggleVis), nameof(Quiet));
        Patch(typeof(Ship), nameof(Ship.StopManeuver), nameof(NoiseOnly));
        Patch(typeof(GUILamp), "SetImage", nameof(Quiet));
        Patch(typeof(GUIOrbitDraw), "ToggleOrbitalMode", nameof(Quiet));
        Patch(typeof(GUIOrbitDraw), nameof(GUIOrbitDraw.ResetTime), nameof(Quiet));
        Patch(typeof(GUIOrbitDraw), nameof(GUIOrbitDraw.CGClampWarning), nameof(Quiet));
        Patch(typeof(UnityEngine.Debug), nameof(UnityEngine.Debug.Log), nameof(Quiet), new[] { typeof(object) });
        AIShipManager.strATCLast = "fixture";
    }

    static void CallerAudit(Harmony fixture) {
        SetupCallerFixtures(fixture);
        var withdrawal = new Harmony("offline.caller-audit.withdrawal-candidate");
        var order = new Harmony("offline.caller-audit.publication-order-candidate");
        try {
            for (int mode = 0; mode < 4; mode++) {
                if (mode == 1) withdrawal.Patch(AccessTools.Method(typeof(AIShipManager), "UnregisterShip", new[] { typeof(AIShip) }),
                    prefix: new HarmonyMethod(typeof(Program), nameof(CancelWithdrawn)));
                if (mode == 2) order.Patch(AccessTools.Method(typeof(NavModCoursePlot), "EngageTrip"),
                    transpiler: new HarmonyMethod(typeof(Program), nameof(EngageOrderCandidate)));
                if (mode == 3) {
                    withdrawal.UnpatchSelf();
                    withdrawal.Patch(AccessTools.Method(typeof(AIShipManager), "UnregisterShip", new[] { typeof(AIShip) }),
                        prefix: new HarmonyMethod(typeof(Program), nameof(CancelRegisteredOnly)));
                }
                CourseEngagement(mode);
                ControlModeHandoffs(mode);
                PlayerDisengagement(mode);
                RejectedModeChangesPreserveNavigation(mode);
                RetirementAndTerminalLeaves(mode);
                StalePrivateWithdrawal(mode);
                NativeSavedCourseReconstruction(mode);
                MigrationPreservesControllerIdentity();
                if (mode == 0 || mode == 3) MissingRegistryIsNotWithdrawal();
                Console.WriteLine("Native callers, mode " + mode + ": 0=baseline, 1=unsafe withdrawal alone, 2=publication-order candidate, 3=exact registered withdrawal plus publication order.");
            }
        } finally {
            order.UnpatchSelf(); withdrawal.UnpatchSelf();
            GUIOrbitDraw.CrossHairTarget = null;
            CrewSim.coPlayer = null;
        }
    }
}
