using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using BepInEx.Configuration;
using HarmonyLib;
using Ostranauts.ShipGUIs.MFD;
using Ostranauts.Ships.Comms;

namespace Ostrabus.QoL.Docking;

internal static class SinglePortDocking {
    internal static ConfigEntry<bool> Flag;
    internal static bool Enabled => Flag?.Value ?? true;
    internal static void Initialize(ConfigFile config) => Flag = config.Bind("QoL.Docking", "AutoSelectSinglePort", true,
        "Automatically select the sole eligible target port in the comms docking conversation. Multiple choices and native docking clearance checks are preserved. Toggle live in the Mod Settings PDA app.");
    internal static void SetEnabled(bool enabled) {
        if (Flag == null) throw new InvalidOperationException("Single-port docking is not initialized.");
        Flag.Value = enabled;
    }

    static readonly AccessTools.FieldRef<MFDComms, List<Interaction>> Options = AccessTools.FieldRefAccess<MFDComms, List<Interaction>>("_availableIAs");
    static readonly AccessTools.FieldRef<MFDComms, string> Target = AccessTools.FieldRefAccess<MFDComms, string>("_targetRegID");
    static readonly AccessTools.FieldRef<MFDComms, bool> IsAtc = AccessTools.FieldRefAccess<MFDComms, bool>("_isATCChannel");
    static readonly Func<MFDPage, Ship> ShipUs = AccessTools.MethodDelegate<Func<MFDPage, Ship>>(AccessTools.PropertyGetter(typeof(MFDPage), "ShipUs"));
    static readonly Func<MFDPage, CondOwner> CoUs = AccessTools.MethodDelegate<Func<MFDPage, CondOwner>>(AccessTools.PropertyGetter(typeof(MFDPage), "CoUs"));
    static readonly Action<MFDComms, int> Apply = AccessTools.MethodDelegate<Action<MFDComms, int>>(AccessTools.Method(typeof(MFDComms), "ApplyInteraction"));
    // A single delivered message can update more than one open MFD. Weak keys
    // avoid duplicate requests without retaining conversations, ships or pages.
    static readonly ConditionalWeakTable<ShipMessage, object> Handled = new ConditionalWeakTable<ShipMessage, object>();
    static readonly object Marker = new object();
    [ThreadStatic] static bool applying;

    internal static void Select(MFDComms page, ShipMessage message) {
        if (!Enabled || applying || page == null || IsAtc(page) || message?.Interaction == null || Handled.TryGetValue(message, out _)) return;
        string request;
        switch (message.Interaction.strName) {
            case "SHIPMultiDockSelectAllow": request = "SHIPDock"; break;
            case "SHIPMultiDockSelectForcedAllow": request = "SHIPForceDockNoPilot"; break;
            default: return;
        }
        var ship = ShipUs(page);
        var crew = CoUs(page);
        var target = Target(page);
        if (ship == null || crew == null || string.IsNullOrEmpty(target) ||
            message.ReceiverRegId != ship.strRegID || string.IsNullOrEmpty(message.SenderRegId) ||
            (message.SenderRegId != target && !message.SenderRegId.StartsWith(target + "|", StringComparison.Ordinal))) return;
        var options = Options(page);
        if (options == null || options.Count != 1) return;
        var choice = options[0];
        if (choice?.strName != request || !ReferenceEquals(choice.objUs, crew) ||
            !ReferenceEquals(choice.objThem, message.Interaction.objUs) ||
            !ReferenceEquals(message.Interaction.objThem, crew)) return;
        var port = choice.LootCTsThem;
        if (port?.strName != "PortID" || port.strType != "text" || port.aCOs?.Length != 1) return;
        string selection = port.aCOs[0];
        const string prefix = "Port#", suffix = "=1.0x1";
        if (selection == null || !selection.StartsWith(prefix, StringComparison.Ordinal) ||
            !selection.EndsWith(suffix, StringComparison.Ordinal) || selection.Length < prefix.Length + suffix.Length + 3) return;
        Handled.Add(message, Marker);
        applying = true;
        try {
            // Exactly the same interaction, trigger check, port payload, send,
            // conversation state and log update as clicking the native option.
            Apply(page, 0);
        } finally { applying = false; }
    }
}

[HarmonyPatch(typeof(MFDComms), nameof(MFDComms.OnUIRefresh))]
internal static class SinglePortDockingPatch {
    static void Postfix(MFDComms __instance, ShipMessage shipMessage) => SinglePortDocking.Select(__instance, shipMessage);
}
