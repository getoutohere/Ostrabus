using BepInEx.Configuration;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Ostrabus.Hotkeys;

internal static class FunctionalHotkeys {
    internal static ConfigEntry<bool> Flag;
    internal static bool Enabled => Flag != null && Flag.Value;
    internal static void Tick() {
        if (!Enabled || !Application.isFocused) return;
        Keyboard keys = Keyboard.current;
        if (keys == null || !keys.shiftKey.isPressed || keys.ctrlKey.isPressed || keys.altKey.isPressed || keys.leftMetaKey.isPressed || keys.rightMetaKey.isPressed) return;
        bool airlock = keys.rKey.wasPressedThisFrame, disembark = keys.tKey.wasPressedThisFrame;
        if (!airlock && !disembark) return;
        CrewSim sim = CrewSim.objInstance;
        if (sim == null || !sim.FinishedLoading || CrewSim.Typing || CrewSim.bRaiseUI || CrewSim.bShipEdit || CanvasManager.instance == null || CanvasManager.instance.State != CanvasManager.GUIState.NORMAL) return;
        // R is vanilla's rotate/attitude key. Leave item placement and ship UI alone.
        if (sim.goSelPart != null || CrewSim.inventoryGUI != null && CrewSim.inventoryGUI.Selected != null || TextFocused()) return;
        CondOwner selected = SelectedCrew();
        if (airlock) ToggleAirlock(selected);
        if (disembark) ToggleDisembark(selected);
    }
    static bool TextFocused() {
        var events = EventSystem.current;
        if (events == null || events.currentSelectedGameObject == null) return false;
        GameObject selected = events.currentSelectedGameObject;
        var text = selected.GetComponentInParent<TMP_InputField>();
        if (text != null && text.isFocused) return true;
        var legacy = selected.GetComponentInParent<InputField>();
        return legacy != null && legacy.isFocused;
    }
    internal static CondOwner SelectedCrew() {
        var selection = CrewSim.aSelected;
        if (selection == null || selection.Count == 0) return CrewSim.coPlayer;
        // GetSelectedCrew falls back to the captain for selected non-human objects.
        // Never change the captain's permission when a different object is selected.
        return selection.Count == 1 ? selection[0] : null;
    }
    internal static bool ToggleAirlock(CondOwner selected) => TogglePermission(selected, true);
    internal static bool ToggleDisembark(CondOwner selected) => TogglePermission(selected, false);
    static bool TogglePermission(CondOwner selected, bool airlock) {
        CondOwner player = CrewSim.coPlayer;
        if (!Enabled || player == null || selected == null || selected.bDestroyed || !selected.bAlive || !selected.IsHumanOrRobot ||
            selected.ship == null || selected.ship.LoadState < Ship.Loaded.Edit || selected.Company == null || selected.Company != player.Company ||
            (selected != player && !selected.HasCond("IsPlayerCrew")) || string.IsNullOrEmpty(selected.strID)) return false;
        JsonCompany company = player.Company;
        if (company.mapRoster == null || !company.mapRoster.TryGetValue(selected.strID, out JsonCompanyRules rules) || rules == null) return false;
        bool allowed = !(airlock ? rules.bAirlockPermission : rules.bShoreLeave);
        if (airlock) company.SetPermissionAirlock(selected.strID, allowed);
        else company.SetPermissionShore(selected.strID, allowed);
        selected.LogMessage((airlock ? "Open airlock" : "Disembark") + " permission: " + (allowed ? "ALLOWED." : "DISALLOWED."), "Neutral", selected.strID);
        return true;
    }
}
