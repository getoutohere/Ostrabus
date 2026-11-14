using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Ostrabus.ShipNavigation;
using Ostrabus.MiscBugFixes;
using Ostranauts.ShipGUIs.NavStation;
using Ostranauts.Ships.AIPilots;
using Ostranauts.Ships.Commands;

static partial class Program {
    static void InstallOwnershipProduction(Harmony harmony) {
        harmony.CreateClassProcessor(typeof(FlightControllerWithdrawalPatch)).Patch();
        harmony.CreateClassProcessor(typeof(FlightControllerDispatchPatch)).Patch();
        harmony.CreateClassProcessor(typeof(CourseEngagementOrderPatch)).Patch();
        harmony.CreateClassProcessor(typeof(RetiredRetreatTerminalPatch)).Patch();
    }

    static List<CodeInstruction> NativeInstructions(MethodBase method) {
        var reader = AccessTools.GetDeclaredMethods(typeof(PatchProcessor)).Single(candidate =>
            candidate.Name == "GetOriginalInstructions" && candidate.GetParameters().Length == 2 &&
            candidate.GetParameters()[0].ParameterType == typeof(MethodBase) && !candidate.GetParameters()[1].ParameterType.IsByRef);
        var arguments = new object[reader.GetParameters().Length];
        arguments[0] = method;
        return new List<CodeInstruction>((IEnumerable<CodeInstruction>)reader.Invoke(null, arguments));
    }

    static void ExpectUnknownIl(Action action, string message) {
        bool rejected = false;
        try { action(); } catch (InvalidOperationException) { rejected = true; }
        Check(rejected, message);
    }

    static void VersionGuards() {
        var engage = AccessTools.Method(typeof(NavModCoursePlot), "EngageTrip");
        var retreat = AccessTools.Method(typeof(LowFuelRetreat), nameof(LowFuelRetreat.RunCommand));
        var remove = AccessTools.Method(typeof(AIShipManager), nameof(AIShipManager.UnregisterShip), new[] { typeof(Ship) });
        var navField = AccessTools.Field(typeof(ShipSitu), nameof(ShipSitu.NavData));
        var labels = AccessTools.Field(typeof(CodeInstruction), "labels");

        Check(CourseEngagementOrderPatch.Transpiler(NativeInstructions(engage)).Any(), "Production engagement transpiler rejected native IL");
        Check(RetiredRetreatTerminalPatch.Transpiler(NativeInstructions(retreat)).Any(), "Production terminal transpiler rejected native IL");
        ExpectUnknownIl(() => CourseEngagementOrderPatch.Transpiler(Array.Empty<CodeInstruction>()).ToList(), "Empty engagement IL was accepted");
        foreach (string mutation in new[] { "extra-store", "missing-owner", "gap", "internal-label", "exception-boundary", "missing-withdrawal" }) {
            var code = NativeInstructions(engage);
            int store = code.FindIndex(instruction => instruction.StoresField(navField));
            switch (mutation) {
                case "extra-store": code.Add(new CodeInstruction(OpCodes.Stfld, navField)); break;
                case "missing-owner": code[store - 5].operand = null; break;
                case "gap": code.Insert(store + 1, new CodeInstruction(OpCodes.Nop)); break;
                case "internal-label":
                    object label = code.First(instruction => instruction.opcode.FlowControl == FlowControl.Cond_Branch).operand;
                    ((IList)labels.GetValue(code[store - 1])).Add(label);
                    break;
                case "exception-boundary": code[store].blocks.Add(new ExceptionBlock(ExceptionBlockType.BeginExceptionBlock)); break;
                default: code.RemoveAll(instruction => instruction.Calls(remove)); break;
            }
            ExpectUnknownIl(() => CourseEngagementOrderPatch.Transpiler(code).ToList(), "Unknown engagement IL accepted: " + mutation);
        }
        foreach (string mutation in new[] { "extra-withdrawal", "changed-return", "missing-ret", "exception-boundary" }) {
            var code = NativeInstructions(retreat);
            int site = code.FindIndex(instruction => instruction.Calls(remove));
            switch (mutation) {
                case "extra-withdrawal": code.Add(new CodeInstruction(OpCodes.Call, remove)); break;
                case "changed-return": code[site + 2].operand = AccessTools.PropertyGetter(typeof(BaseCommand), "Success"); break;
                case "missing-ret": code[site + 3].opcode = OpCodes.Nop; break;
                default: code[site + 2].blocks.Add(new ExceptionBlock(ExceptionBlockType.BeginExceptionBlock)); break;
            }
            ExpectUnknownIl(() => RetiredRetreatTerminalPatch.Transpiler(code).ToList(), "Unknown terminal IL accepted: " + mutation);
        }
        foreach (string name in new[] { "RunAIQueue", "FFWD" }) {
            var method = AccessTools.Method(typeof(AIShipManager), name);
            Check(FlightControllerDispatchPatch.Transpiler(NativeInstructions(method), method).Any(), "Native dispatch IL rejected");
            ExpectUnknownIl(() => FlightControllerDispatchPatch.Transpiler(Array.Empty<CodeInstruction>(), method).ToList(), "Missing dispatch call accepted");
        }
        ExpectUnknownIl(() => FlightControllerDispatchPatch.Transpiler(NativeInstructions(engage), engage).ToList(), "Unknown dispatch method accepted");
    }

    static void ProductionChecks(Harmony fixture, string externalPlugin = null) {
        SetupCallerFixtures(fixture);
        fixture.Patch(AccessTools.Method(typeof(CondOwner), nameof(CondOwner.HasCond), new[] { typeof(string), typeof(bool) }),
            prefix: new HarmonyMethod(typeof(Program), nameof(NoConditions)));
        var production = new Harmony("offline.controller-lifetime.production");
        var crew = new Harmony("offline.controller-lifetime.existing-crew");
        var progress = new Harmony("offline.controller-lifetime.existing-retreat-progress");
        try {
            Func<AIShip, bool> registered = FlightControllerLifetime.Registered;
            if (externalPlugin == null) {
                InstallOwnershipProduction(production);
                production.CreateClassProcessor(typeof(FlightControlHandoffPatch)).Patch();
                production.CreateClassProcessor(typeof(ShallowTorchShutdownPatch)).Patch();
                production.CreateClassProcessor(typeof(ShallowRcsTorchReleasePatch)).Patch();
            } else {
                var assembly = Assembly.LoadFrom(System.IO.Path.GetFullPath(externalPlugin));
                foreach (string name in new[] {
                    "FlightControllerWithdrawalPatch", "FlightControllerDispatchPatch", "CourseEngagementOrderPatch", "RetiredRetreatTerminalPatch",
                    "FlightControlHandoffPatch", "ShallowTorchShutdownPatch", "ShallowRcsTorchReleasePatch"
                }) production.CreateClassProcessor(assembly.GetType("Ostrabus.ShipNavigation." + name, true)).Patch();
                registered = (Func<AIShip, bool>)Delegate.CreateDelegate(typeof(Func<AIShip, bool>),
                    AccessTools.Method(assembly.GetType("Ostrabus.ShipNavigation.FlightControllerLifetime", true), "Registered"));
            }
            crew.CreateClassProcessor(typeof(CrewedAIExecutionPatch)).Patch();

            foreach (bool torch in new[] { false, true }) foreach (bool hidden in new[] { false, true })
                Scenario(torch, cancelBeforeUnregister: false, hidden, productionRepair: true);
            CourseEngagement(4);
            ControlModeHandoffs(4);
            PlayerDisengagement(4);
            RejectedModeChangesPreserveNavigation(4);
            RetirementAndTerminalLeaves(4);
            StalePrivateWithdrawal(4);
            NativeSavedCourseReconstruction(4);
            MigrationPreservesControllerIdentity();
            MissingRegistryIsNotWithdrawal();
            ClearControllers();
            DispatchAfterWithdrawal(true);
            FastForwardAfterWithdrawal(true);
            ReplacementCourseCallback();
            PreservationControls();

            SetupContinuationFixtures(fixture);
            foreach (bool existingProgress in new[] { false, true }) {
                if (existingProgress) progress.CreateClassProcessor(typeof(LowFuelRetreatProgressPatch)).Patch();
                foreach (bool longRange in new[] { false, true }) foreach (bool retires in new[] { false, true })
                    RetreatConsumer(longRange, retires, repaired: true);
                RealFailedTargetContinuation(false);
                FailedTargetParent(false);
            }
            ClearControllers();
            var ai = Register(Hull("allocation-control"));
            for (int i = 0; i < 100; i++) registered(ai);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 5000; i++) registered(ai);
            Check(GC.GetAllocatedBytesForCurrentThread() == before, "Registration checks allocate after warmup");
            VersionGuards();
        } finally {
            progress.UnpatchSelf(); crew.UnpatchSelf(); production.UnpatchSelf();
            GUIOrbitDraw.CrossHairTarget = null;
            CrewSim.coPlayer = null;
        }
    }
}
