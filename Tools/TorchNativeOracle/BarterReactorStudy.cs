using System;
using System.Collections.Generic;
using HarmonyLib;
using Ostranauts.Ships;
using Ostrabus.TorchCalculator;
using UnityEngine;

static partial class Program {
    static readonly List<string> reactorWarnings = new List<string>();
    static void ReactorWarning(object __0) {
        string message = __0?.ToString();
        if (message?.StartsWith("WARNING: Fusion reactor damage! ", StringComparison.Ordinal) == true) reactorWarnings.Add(message);
    }
    static void RunBarterReactorStudy(Harmony h) {
        var run = AccessTools.Method(typeof(FusionIC), "Run");
        h.Patch(AccessTools.Method(typeof(Debug), nameof(Debug.LogWarning), new[] { typeof(object) }),
            postfix: new HarmonyMethod(typeof(Program), nameof(ReactorWarning)));
        // Native mutators short-circuit on frozen copy conditions. Keep the
        // existing read/control/engine fixtures but execute those actual guards.
        foreach (string name in new[] { "AddCondAmount", "SetCondAmount", "ZeroCondAmount" })
            h.Unpatch(AccessTools.Method(typeof(CondOwner), name), AccessTools.Method(typeof(Program), name == "AddCondAmount" ? nameof(Add) : name == "SetCondAmount" ? nameof(Set) : nameof(Zero)));
        foreach (bool missingCoils in new[] { false, true }) {
            var fusion = Make(new ReactorFit(), TorchModel.Ideal, 0, 0);
            ship = Empty<BarterZoneShip>();
            foreach (var copy in Values.Keys) copy.bFreezeConds = true;
            core.mapGUIPropMaps = new Dictionary<string, Dictionary<string, string>> { ["Panel A"] = Controls };
            Values[core]["StatICABLWall"] = 1;
            Controls["knobRatio"] = "0";
            if (missingCoils) Field(fusion, "aCoils", new List<CondOwner>());
            reactorWarnings.Clear();
            run.Invoke(fusion, new object[] { .293617248524018 });
            Check(reactorWarnings.Count == (missingCoils ? 1 : 0), "Native copy reactor warning must depend on missing containment coils");
            Near(Read(core, "StatICABLWall"), 1, "Native frozen condition guard blocks the copy's reactor-wall write");
            Near(Read(core, "StatICCoreTemp"), TorchModel.Ideal, "Native frozen condition guard preserves saved copy temperature");
            if (missingCoils) Console.WriteLine(reactorWarnings[0]);
        }
        Console.WriteLine(checks + " offscreen reactor checks passed: native Run emits missing-coil damage warnings on frozen barter copies; native condition writes remain blocked. Engine/module discovery and other reads are fixture boundaries.");
    }
}
