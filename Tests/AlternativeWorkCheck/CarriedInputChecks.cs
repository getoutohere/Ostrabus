using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using Ostrabus.AlternativeWork;

static partial class Program {
    static List<CondOwner> carriedInputs, widerInputs;
    static int inputRolls, shipVisits, inputQueues, requiredInputCount = 1;
    static string selectedInput;
    static bool inputsStored;
    static bool InputLoot(ref Loot __result) { __result = Empty<Loot>(); return false; }
    static bool InputRequirements(ref List<CondTrigger> __result) {
        inputRolls++;
        __result = new List<CondTrigger>();
        for (int i = 0; i < requiredInputCount; i++) __result.Add(new CondTrigger { strName = "floor", fCount = 1 });
        return false;
    }
    static bool StoredRequirements(List<CondTrigger> __1) { if (inputsStored) __1.Clear(); return false; }
    static bool RemainingRequirements(List<CondTrigger> __0, ref List<CondTrigger> __result) { __result = __0; return false; }
    static bool InventoryRequirements(CondOwnerVisitor visitor, bool bAllowLocked) {
        Check(!bAllowLocked, "Carried probe accessed locked inventory");
        foreach (var co in carriedInputs) ((CondOwnerVisitorLazyOrCondTrigger)visitor).subVisitor.Visit(co);
        return false;
    }
    static IEnumerator WiderRequirements(CondOwnerVisitor visitor) {
        shipVisits++;
        foreach (var co in widerInputs) ((CondOwnerVisitorLazyOrCondTrigger)visitor).subVisitor.Visit(co);
        yield return null;
    }
    static bool VisitWiderRequirements(CondOwnerVisitor visitor, bool bSubObjects, bool bAllowDocked, bool bAllowLocked, ref IEnumerator __result) {
        Check(bSubObjects && bAllowDocked && !bAllowLocked, "Changed native wider search traversal flags");
        __result = WiderRequirements(visitor); return false;
    }
    static bool InputPathfinder(ref Pathfinder __result) { __result = null; return false; }
    static bool UsTrigger(ref CondTrigger __result) { __result = new CondTrigger { strName = "TIs[us]" }; return false; }
    static IEnumerator NearestInput(IteratorResult<Ostranauts.Core.Models.Tuple<CondOwner, List<string>>> result, List<CondOwner> available, string persistent) {
        // Controlled reachability/condition lookup; native CheckItemsAvailableDeferred
        // still performs its requirement loop and removes selected candidates.
        foreach (var co in available) if (co.strID.StartsWith("floor", StringComparison.Ordinal)) {
            result.Value.Item1 = co;
            selectedInput = co.strID;
            break;
        }
        yield break;
    }
    static bool FindInput(IteratorResult<Ostranauts.Core.Models.Tuple<CondOwner, List<string>>> result, List<CondOwner> aCOsHayStack, string persistentCO, ref IEnumerator __result) {
        __result = NearestInput(result, aCOsHayStack, persistentCO); return false;
    }
    static bool QueueInput() { inputQueues++; return false; }
    sealed class UnusedValidation : IEnumerator, IDisposable {
        internal bool Disposed;
        public object Current => null;
        public bool MoveNext() => throw new Exception("Replayed original validator and rolled inputs twice");
        public void Reset() => throw new NotSupportedException();
        public void Dispose() { Disposed = true; }
    }
    static void RunInputCase(Interaction interaction, CondOwner co, CondOwner target, bool fetch, bool expected, int visits, int queues) {
        inputRolls = shipVisits = inputQueues = 0;
        var original = new UnusedValidation();
        var result = new IteratorResult<bool>();
        var args = new object[] { interaction, result, co, target, false, fetch, original };
        AccessTools.Method(typeof(WorkCarriedInputsPatch), "Postfix").Invoke(null, args);
        var steps = (IEnumerator)args[6];
        bool verbose = interaction.bVerboseTrigger;
        while (steps.MoveNext()) Check(interaction.bVerboseTrigger == verbose, "Carried probe leaked verbosity across a yield");
        Check(interaction.bVerboseTrigger == verbose, "Carried probe changed native verbosity");
        Check(result.Value == expected, "Wrong carried/wider material validation result");
        Check(inputRolls == 1, "Required input loot was rolled more than once");
        Check(shipVisits == visits && inputQueues == queues, "Wrong wider search or prerequisite dispatch");
        Check(original.Disposed, "Validator retained the unused native enumerator");
    }
    static void CarriedFixture(Harmony fixture) {
            fixture.Patch(AccessTools.Method(typeof(UnityEngine.Object), "op_Equality"), prefix: new HarmonyMethod(typeof(Program), nameof(Equal)));
            fixture.Patch(AccessTools.Method(typeof(UnityEngine.Object), "op_Inequality"), prefix: new HarmonyMethod(typeof(Program), nameof(Unequal)));
            fixture.Patch(AccessTools.Method(typeof(DataHandler), "GetLoot"), prefix: new HarmonyMethod(typeof(Program), nameof(InputLoot)));
            fixture.Patch(AccessTools.Method(typeof(Loot), "GetCTLoot"), prefix: new HarmonyMethod(typeof(Program), nameof(InputRequirements)));
            fixture.Patch(AccessTools.Method(typeof(Interaction), "CountStoredInputs"), prefix: new HarmonyMethod(typeof(Program), nameof(StoredRequirements)));
            fixture.Patch(AccessTools.Method(typeof(Interaction), "GetRemainingInputTriggers"), prefix: new HarmonyMethod(typeof(Program), nameof(RemainingRequirements)));
            fixture.Patch(AccessTools.Method(typeof(CondOwner), "VisitCOs"), prefix: new HarmonyMethod(typeof(Program), nameof(InventoryRequirements)));
            fixture.Patch(AccessTools.Method(typeof(Ship), "VisitCOsDeferred"), prefix: new HarmonyMethod(typeof(Program), nameof(VisitWiderRequirements)));
            fixture.Patch(AccessTools.PropertyGetter(typeof(CondOwner), "Pathfinder"), prefix: new HarmonyMethod(typeof(Program), nameof(InputPathfinder)));
            fixture.Patch(AccessTools.Method(typeof(DataHandler), "GetCondTrigger", new[] { typeof(string) }), prefix: new HarmonyMethod(typeof(Program), nameof(UsTrigger)));
            fixture.Patch(AccessTools.Method(typeof(CondOwner), "GetNearestTriggeredDeferred"), prefix: new HarmonyMethod(typeof(Program), nameof(FindInput)));
    }
    static void CheckCarriedInputs() {
        var fixture = new Harmony("altwork.carried.fixture");
        var oldFlag = AlternativeWorkSystem.Flag;
        var oldContext = AlternativeWorkSystem.Context;
        var oldCOs = DataHandler.mapCOs;
        try {
            CarriedFixture(fixture);
            fixture.Patch(AccessTools.Method(typeof(Interaction), "QueueMissingInputDeliveries"), prefix: new HarmonyMethod(typeof(Program), nameof(QueueInput)) { priority = Priority.Last });
            AlternativeWorkSystem.Flag = Empty<ConfigEntry<bool>>(); MaterialModuleEnabled(true);
            DataHandler.mapCOs = new Dictionary<string, CondOwner>();
            var co = MaterialCO("carried-worker");
            Field(co, "_objShip", Empty<Ship>());
            var target = MaterialCO("blueprint");
            var wm = Empty<WorkManager>();
            var upstream = new Task2 { strTargetCOID = target.strID };
            wm.taskUpstream = upstream;
            AlternativeWorkSystem.Context = new AlternativeWorkSystem.SearchContext { Crew = co, Manager = wm };
            var interaction = Empty<Interaction>(); interaction.strLootItmInputs = "floor-inputs";
            // The real WorkManager sets this for ALL selections, not only debug requests.
            interaction.bVerboseTrigger = true;
            carriedInputs = new List<CondOwner> { MaterialCO("floor-in-carried-container") };
            widerInputs = new List<CondOwner>();
            RunInputCase(interaction, co, target, false, true, 0, 0);
            RunInputCase(interaction, co, target, true, false, 0, 1);
            Check(ReferenceEquals(AlternativeWorkSystem.Context.MaterialPrerequisite, upstream), "Carried fast path lost native material-prerequisite signal");
            carriedInputs.Clear(); widerInputs.Add(MaterialCO("floor-on-ship"));
            RunInputCase(interaction, co, target, false, true, 1, 0);
            RunInputCase(interaction, co, target, true, false, 1, 1);
            widerInputs.Clear();
            RunInputCase(interaction, co, target, false, false, 1, 0);
            carriedInputs.Add(MaterialCO("irrelevant-item"));
            RunInputCase(interaction, co, target, false, false, 1, 0);
            inputsStored = true;
            RunInputCase(interaction, co, target, true, true, 0, 0);
            inputsStored = false;
            carriedInputs.Clear(); carriedInputs.Add(MaterialCO("floor-in-carried-container"));
            target.strPersistentCO = "floor-on-ship";
            widerInputs.Add(MaterialCO(target.strPersistentCO));
            DataHandler.mapCOs[target.strPersistentCO] = widerInputs[0];
            RunInputCase(interaction, co, target, false, true, 1, 0);
            Check(selectedInput == target.strPersistentCO, "Wider search ignored persistent material preference");
            carriedInputs.Add(widerInputs[0]); widerInputs.Clear();
            RunInputCase(interaction, co, target, false, true, 0, 0);
            Check(selectedInput == target.strPersistentCO, "Carried search ignored persistent material preference");
            target.strPersistentCO = null;
            requiredInputCount = 2;
            carriedInputs.RemoveAt(1);
            RunInputCase(interaction, co, target, false, false, 1, 0);
            widerInputs.Add(MaterialCO("floor-second"));
            RunInputCase(interaction, co, target, false, true, 1, 0);
            RunInputCase(interaction, co, target, true, false, 1, 1);
            carriedInputs.Add(widerInputs[0]); widerInputs.Clear();
            RunInputCase(interaction, co, target, false, true, 0, 0);
            foreach (string gate in new[] { "off", "outside", "stats", "vanilla" }) {
                var context = AlternativeWorkSystem.Context;
                MaterialModuleEnabled(gate != "off");
                if (gate == "outside") AlternativeWorkSystem.Context = null;
                context.VanillaCollection = gate == "vanilla";
                var original = new UnusedValidation();
                var args = new object[] { interaction, new IteratorResult<bool>(), co, target, gate == "stats", true, original };
                AccessTools.Method(typeof(WorkCarriedInputsPatch), "Postfix").Invoke(null, args);
                Check(ReferenceEquals(args[6], original), "Carried fast path escaped its scope: " + gate);
                AlternativeWorkSystem.Context = context;
            }
        } finally {
            inputsStored = false; requiredInputCount = 1; selectedInput = null; carriedInputs = widerInputs = null;
            AlternativeWorkSystem.Context = oldContext;
            AlternativeWorkSystem.Flag = oldFlag;
            DataHandler.mapCOs = oldCOs;
            fixture.UnpatchSelf();
        }
    }
}
