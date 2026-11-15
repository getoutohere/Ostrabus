using System;
using System.Collections.Generic;
using HarmonyLib;
using Newtonsoft.Json;
using Ostranauts.InputControl;
using Ostranauts.ShipGUIs.NavStation;
using Ostranauts.ShipGUIs.Utilities;
using Ostranauts.Ships.Sensors;
using UnityEngine;
using UnityEngine.UI;

namespace Ostrabus.NavConsole.Rotation;

internal sealed class RotationModule : NavModBase {
    internal const string PrefabName = "NavModOstrabusRotation";
    internal const string ItemName = "ItmOstrabusNavRotation";
    internal const string LastCommandKey = "Ostrabus.Rotation.Last";
    static readonly AccessTools.FieldRef<GUIOrbitDraw, bool> RcsMode = AccessTools.FieldRefAccess<GUIOrbitDraw, bool>("bRCS");
    static readonly AccessTools.FieldRef<GUIOrbitDraw, Dictionary<string, GUIBtnPressHold>> Buttons = AccessTools.FieldRefAccess<GUIOrbitDraw, Dictionary<string, GUIBtnPressHold>>("dictWASD");
    static readonly string BothKey = (string)AccessTools.Field(typeof(GUIOrbitDraw), "strQEKey").GetValue(null);
    static readonly AccessTools.FieldRef<GUIOrbitDraw, IInputCommand> CounterClockwise = AccessTools.FieldRefAccess<GUIOrbitDraw, IInputCommand>("_commandShipCCW");
    static readonly AccessTools.FieldRef<GUIOrbitDraw, IInputCommand> Clockwise = AccessTools.FieldRefAccess<GUIOrbitDraw, IInputCommand>("_commandShipCW");
    static readonly AccessTools.FieldRef<GUIOrbitDraw, IInputCommand> Attitude = AccessTools.FieldRefAccess<GUIOrbitDraw, IInputCommand>("_commandShipAttitude");
    static readonly AccessTools.FieldRef<GUIDockSys, Dictionary<string, GUIBtnPressHold>> DockButtons = AccessTools.FieldRefAccess<GUIDockSys, Dictionary<string, GUIBtnPressHold>>("dictWASD");
    static readonly AccessTools.FieldRef<GUIDockSys, IInputCommand> DockCounterClockwise = AccessTools.FieldRefAccess<GUIDockSys, IInputCommand>("_commandShipCCW");
    static readonly AccessTools.FieldRef<GUIDockSys, IInputCommand> DockClockwise = AccessTools.FieldRefAccess<GUIDockSys, IInputCommand>("_commandShipCW");
    static readonly AccessTools.FieldRef<GUIDockSys, IInputCommand> DockAttitude = AccessTools.FieldRefAccess<GUIDockSys, IInputCommand>("_commandShipAttitude");
    static readonly Action<GUIOrbitDraw, bool> OrbitalMode = AccessTools.MethodDelegate<Action<GUIOrbitDraw, bool>>(AccessTools.Method(typeof(GUIOrbitDraw), "ToggleOrbitalMode"));
    internal CondOwner Hardware;
    internal Toggle HoldButton;
    internal GameObject HoldOnLabel, HoldOffLabel;
    internal Button[] Arrows;
    internal bool Damaged;

    internal void Bind(GUIOrbitDraw owner, CondOwner console, CondOwner hardware) {
        _guiOrbitDraw = owner;
        COSelf = console;
        Hardware = hardware;
        dictPropMap = console.mapGUIPropMaps["Panel A"];
    }
    protected override void Init() => Sync();
    protected override void UpdateUI() => Sync();
    bool Available() => isActiveAndEnabled && !Damaged && COSelf != null && !COSelf.bDestroyed && COSelf.ship?.objSS != null &&
        !COSelf.HasCond("IsOff") && !COSelf.HasCond("IsDamagedSoftware") && !CrewSim.bShipEdit &&
        Hardware != null && !Hardware.bDestroyed && !Hardware.HasCond("IsDamaged");

    internal void Command(RotationDirection direction) {
        if (!CanTakeControl()) return;
        Ship ship = COSelf.ship;
        var selected = GUIOrbitDraw.CrossHairTarget;
        string kind = null, id = null;
        double offsetX = 0, offsetY = 0;
        if (selected?.Ship != null && selected.Ship != ship) {
            var draw = _guiOrbitDraw.FindShipDraw(selected.Ship);
            if (draw != null && draw.SignalVisibility != SignalVisibility.None) {
                kind = "ship"; id = selected.Ship.strRegID;
                var offset = draw.GetPositionOffset(); offsetX = offset.X; offsetY = offset.Y;
            }
        } else if (selected?.bodyOrbit != null) { kind = "body"; id = selected.bodyOrbit.strName; }
        else if (selected?.stellarObj != null) { kind = "object"; id = selected.stellarObj.strID; }
        bool hold = RotationRuntime.Find(ship)?.Order.Hold ?? false;
        TakeControl();
        RotationRuntime.Start(ship, COSelf, Hardware, direction, kind, id, offsetX, offsetY, hold);
        Sync();
    }

    bool CanTakeControl() {
        if (!Available()) return false;
        Ship ship = COSelf.ship;
        if (ship.IsStation() || ship.IsStationHidden() || ship.objSS.bIsBO) return false;
        var anchor = ship.objSS._dockGroup?.Anchor;
        if (anchor != null && (anchor.bBOLocked || anchor.bIsBO)) return false;
        if (_guiOrbitDraw.HoldingThrustActive) {
            ConsoleToGUI.instance?.LogInfo("[Rotation] Release held thrust before commanding rotation.");
            return false;
        }
        if (ship.objSS.bBOLocked) foreach (Ship docked in ship.GetAllDockedShips())
            if (docked?.objSS != null && docked.objSS.bIsBO) return false;
        return true;
    }

    void TakeControl() {
        Ship ship = COSelf.ship;
        _guiOrbitDraw.chkStationKeeping.isOn = false;
        _guiOrbitDraw.SetPropMapData("chkEngage", "false");
        foreach (var course in _guiOrbitDraw.GetComponentsInChildren<NavModCoursePlot>(true)) {
            var toggle = AccessTools.Field(typeof(NavModCoursePlot), "chkEngage").GetValue(course) as Toggle;
            if (toggle != null) toggle.isOn = false;
        }
        RotationRuntime.Cancel(ship);
        AIShipManager.UnregisterShip(ship);
        ship.objSS.ResetNavData();
        ship.ClearShipTarget();
        CrewSim.GetSelectedCrew()?.ClaimShip(ship.strRegID);
        if (ship.objSS.bBOLocked && !ship.objSS.bIsBO) ship.objSS.UnlockFromBO();
        OrbitalMode(_guiOrbitDraw, false);
        foreach (var engines in _guiOrbitDraw.GetComponentsInChildren<NavModEngineMode>(true)) {
            var toggle = AccessTools.Field(typeof(NavModEngineMode), "chkEngineMode").GetValue(engines) as Toggle;
            if (toggle != null) toggle.isOn = false;
        }
        _guiOrbitDraw.SetPropMapData("nKnobEngineMode", "1");
    }

    internal void ToggleHold(bool enabled) {
        if (!Available()) return;
        RefreshMap();
        var pilot = RotationRuntime.Find(COSelf.ship);
        if (pilot == null && enabled && dictPropMap.TryGetValue(LastCommandKey, out string saved)) {
            try {
                var payload = JsonConvert.DeserializeObject<string[]>(saved);
                if (!OstrabusRotationPilot.ValidPayload(payload)) dictPropMap.Remove(LastCommandKey);
                else if (CanTakeControl()) {
                    TakeControl();
                    pilot = RotationRuntime.Resume(COSelf.ship, payload, COSelf, Hardware);
                }
            }
            catch (JsonException) { dictPropMap.Remove(LastCommandKey); }
        }
        pilot?.SetHold(enabled);
        Sync();
    }

    void Sync() {
        RefreshMap();
        var pilot = COSelf?.ship != null ? RotationRuntime.Find(COSelf.ship) : null;
        if (HoldButton != null) {
            HoldButton.interactable = Available() && (pilot != null || dictPropMap.ContainsKey(LastCommandKey));
            bool holding = pilot?.Order.Hold ?? false;
            HoldButton.SetIsOnWithoutNotify(holding);
            // Quiet synchronization bypasses the native lit-rim callback.
            // Keep one caption visible without firing another command.
            if (HoldOnLabel != null) HoldOnLabel.SetActive(holding);
            if (HoldOffLabel != null) HoldOffLabel.SetActive(!holding);
        }
        if (Arrows != null) foreach (Button arrow in Arrows) if (arrow != null) arrow.interactable = Available();
    }

    static bool Pressed(IInputCommand command) => command?.InputAction != null && command.InputAction.IsPressed();
    internal static bool ManualRotation(GUIOrbitDraw owner) {
        if (owner == null || !RcsMode(owner) || CrewSim.Typing) return false;
        if (Pressed(CounterClockwise(owner)) || Pressed(Clockwise(owner)) || Pressed(Attitude(owner))) return true;
        var buttons = Buttons(owner);
        bool Down(string key) => key != null && buttons != null && buttons.TryGetValue(key, out var button) && button != null && button.bPressed;
        return Down("Q") || Down("E") || Down(BothKey);
    }

    internal static bool ManualRotation(GUIDockSys owner) {
        if (owner == null || CrewSim.Typing) return false;
        if (Pressed(DockCounterClockwise(owner)) || Pressed(DockClockwise(owner)) || Pressed(DockAttitude(owner))) return true;
        var buttons = DockButtons(owner);
        bool Down(string key) => buttons != null && buttons.TryGetValue(key, out var button) && button != null && button.bPressed;
        return Down("Q") || Down("E");
    }

    void RefreshMap() {
        if (COSelf?.mapGUIPropMaps != null && COSelf.mapGUIPropMaps.TryGetValue("Panel A", out var map)) dictPropMap = map;
    }
}
