using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Ostrabus.NavConsole.Rotation;
using Ostranauts.InputControl;
using UnityEngine.InputSystem;

static partial class Program {
    static InputAction pressedAction;
    static string dockingTarget;
    static float incomingDockYaw;
    static bool NoMarker(ref IntPtr __result) { __result = IntPtr.Zero; return false; }
    static bool InputPressed(InputAction __instance, ref bool __result) { __result = ReferenceEquals(pressedAction, __instance); return false; }
    static bool InputSeconds(ref float __result) { __result = .1f; return false; }
    static bool NotPaused(ref bool __result) { __result = false; return false; }
    static bool TargetId(ref string __result) { __result = dockingTarget; return false; }
    static void DockYaw(float fR) { if (!RotationRuntime.Actuating) incomingDockYaw = fR; }

    sealed class InputStub : IInputCommand {
        public string CommandName => "fixture";
        public string VitalForGroup => "";
        public Func<bool> CanExecute { get; set; }
        public InputAction InputAction { get; } = Empty<InputAction>();
        public void Perform(InputAction.CallbackContext context = default) { }
        public void Reset() { }
        public void Load(JsonBinding binding) { }
        public JsonBinding GetJson() => null;
    }

    static CondOwner Nav(Ship ship) {
        var nav = Empty<CondOwner>(); Set(nav, "_strID", ship.strRegID + "-nav");
        Set(nav, "_objShip", ship);
        nav.mapGUIPropMaps = new Dictionary<string, Dictionary<string, string>> { ["Panel A"] = new Dictionary<string, string>() };
        DataHandler.mapCOs[nav.strID] = nav;
        ship.aNavs = new List<CondOwner> { nav };
        return nav;
    }

    static void DockingInput(Harmony fixture, Harmony production) {
        var profiler = typeof(UnityEngine.Object).Assembly.GetType("Unity.Profiling.LowLevel.Unsafe.ProfilerUnsafeUtility", true);
        var marker = Array.Find(profiler.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static),
            method => method.Name == "CreateMarker" && method.GetParameters()[0].ParameterType == typeof(string));
        fixture.Patch(marker, prefix: new HarmonyMethod(typeof(Program), nameof(NoMarker)));
        fixture.Patch(AccessTools.Method(typeof(InputAction), nameof(InputAction.IsPressed)), prefix: new HarmonyMethod(typeof(Program), nameof(InputPressed)));
        fixture.Patch(AccessTools.Method(typeof(CrewSim), nameof(CrewSim.TimeElapsedScaled)), prefix: new HarmonyMethod(typeof(Program), nameof(InputSeconds)));
        fixture.Patch(AccessTools.PropertyGetter(typeof(CrewSim), nameof(CrewSim.Paused)), prefix: new HarmonyMethod(typeof(Program), nameof(NotPaused)));
        fixture.Patch(AccessTools.PropertyGetter(typeof(GUIDockSys), "DockingTarget"), prefix: new HarmonyMethod(typeof(Program), nameof(TargetId)));
        fixture.Patch(AccessTools.Method(typeof(Ship), nameof(Ship.Maneuver)), prefix: new HarmonyMethod(typeof(Program), nameof(DockYaw)));
        var target = Hull("docking-input-target"); target.objSS.vPosx = .001;
        dockingTarget = target.strRegID;
        var handler = AccessTools.Method(typeof(GUIDockSys), "KeyHandler");
        var fields = new[] { "_commandFlyUp", "_commandFlyDown", "_commandFlyLeft", "_commandFlyRight", "_commandShipCCW", "_commandShipCW", "_commandShipAttitude" };
        for (int pass = 0; pass < 8; pass++) {
            RotationRuntime.Cleanup();
            var ship = Hull("dock-input-" + pass); var nav = Nav(ship);
            var order = Order(ship, 0, hold: true);
            var ui = Empty<GUIDockSys>(); Set(ui, "coSelfTemp", nav); Set(ui, "strCoSelfID", nav.strID); Set(ui, "objSS", ship.objSS);
            var actions = new InputStub[fields.Length];
            for (int i = 0; i < fields.Length; i++) { actions[i] = new InputStub(); Set(ui, fields[i], actions[i]); }
            var buttons = new Dictionary<string, GUIBtnPressHold>();
            foreach (string key in new[] { "W", "S", "A", "D", "Q", "E" }) buttons[key] = Empty<GUIBtnPressHold>();
            Set(ui, "dictWASD", buttons);
            // First reproduce the old failure using native sideways input with
            // no UI-scoping repair. The actual native handler invents nonzero yaw.
            int input = pass == 0 ? 2 : pass - 1;
            pressedAction = actions[input].InputAction;
            incomingDockYaw = 0;
            handler.Invoke(ui, null);
            if (pass == 0) {
                Check(incomingDockYaw != 0 && !order.Order.Active, "Native sideways thrust did not reproduce false manual-yaw cancellation");
                production.CreateClassProcessor(typeof(RotationDockInputPatch)).Patch();
            } else if (input < 4) {
                Check(order.Order.Active && order.Order.Hold, "Native translation canceled bearing hold input=" + input);
                if (input >= 2) Check(incomingDockYaw != 0, "Sideways docking fixture failed to generate native assisted yaw");
                Check(RotationRuntime.DockInputShip == null, "Docking input scope escaped its original call");
                StarSystem.fEpoch += .1; ship.objSS.TimeAdvance(.1);
                Check(ship.objSS.vAccRCS.magnitude > 0 && order.Order.Hold, "Held rotation swallowed native translation");
            } else Check(!order.Order.Active, "Explicit docking yaw/attitude input failed to cancel hold");
        }
        pressedAction = null;
        RotationRuntime.Cleanup();
        Console.WriteLine("Native docking input: assisted-yaw cancellation reproduced; all four translations preserve hold, explicit yaw/attitude cancel it.");
    }

    static void RememberedCommand() {
        RotationRuntime.Cleanup();
        var ship = Hull("remembered-command"); var nav = Nav(ship);
        var originalTarget = Hull("remembered-original-target"); originalTarget.objSS.vPosy = .001;
        var ui = Empty<GUIOrbitDraw>();
        Set(ui, "coSelfTemp", nav); Set(ui, "strCoSelfID", nav.strID); Set(ui, "strCOKey", "Panel A");
        var working = new Dictionary<string, string>(); Set(ui, "dictPropMap", working);
        GUIOrbitDraw.Instance = ui;
        try {
            var command = Order(ship, 0, hold: true, target: originalTarget.strRegID);
            var hardware = Empty<CondOwner>(); Set(hardware, "_strID", ship.strRegID + "-module");
            command.Remember();
            string saved = nav.mapGUIPropMaps["Panel A"][RotationModule.LastCommandKey];
            Check(working[RotationModule.LastCommandKey] == saved, "Remembered arrow command absent from native UI working copy");
            for (int i = 0; i < 3; i++) {
                command.SetHold(false);
                Check(nav.mapGUIPropMaps["Panel A"].ContainsKey(RotationModule.LastCommandKey), "HOLD off forgot last arrow command");
                command = RotationRuntime.Resume(ship,
                    Newtonsoft.Json.JsonConvert.DeserializeObject<string[]>(working[RotationModule.LastCommandKey]), nav, hardware);
                command.SetHold(true);
                Check(command.Order.Active && command.Order.Hold, "HOLD on failed to resume last arrow command");
            }
            command.SetHold(false);
            ship.objSS.TimeAdvance(.1);
            Check(!command.Order.Active, "Released settled hold did not complete");
            // Same publication as GUIOrbitDraw.SaveAndClose, without running its
            // rendering teardown. Reopening must retain a usable original order.
            nav.mapGUIPropMaps["Panel A"] = working;
            Check(OstrabusRotationPilot.ValidPayload(Newtonsoft.Json.JsonConvert.DeserializeObject<string[]>(working[RotationModule.LastCommandKey])),
                "Closing the UI discarded its resumable arrow command");
            command = RotationRuntime.Resume(ship,
                Newtonsoft.Json.JsonConvert.DeserializeObject<string[]>(working[RotationModule.LastCommandKey]), nav, hardware);
            command.SetHold(true);
            ship.Maneuver(0, 0, .1f, 0, .1f);
            Check(!command.Order.Active && working.ContainsKey(RotationModule.LastCommandKey), "Manual yaw erased the remembered command");
            var payload = Newtonsoft.Json.JsonConvert.DeserializeObject<string[]>(working[RotationModule.LastCommandKey]);
            var resumed = RotationRuntime.Resume(ship, payload, nav, hardware);
            Check(resumed != null && resumed.Order.TargetId == originalTarget.strRegID, "Re-enabling hold lost the last arrow's original target");
            resumed.SetHold(true);
            Check(resumed.Order.Active && resumed.Order.Hold && RotationRuntime.Find(ship) == resumed,
                "Native Auto registration failed to resume the remembered command after cancellation");
        } finally { GUIOrbitDraw.Instance = null; RotationRuntime.Cleanup(); }
        Console.WriteLine("Remembered arrow command survives HOLD off/on, completion, native UI map publication and manual yaw cancellation.");
    }
}
