using System;
using System.Collections.Generic;
using System.Globalization;
using HarmonyLib;

namespace Ostrabus.QoL.WreckSeverity;

// Optional observations of the reviewed award sites. No bonus policy, random
// draws, actor registry or saved fields. Release state exists only on its stack.
internal static class WreckBonusLog {
    internal const string Condition = "IsDueBonusDerelict";
    internal struct ReleaseState {
        internal bool Active, PlotSeen, PlotResult, TapeSeen, TapeResult;
        internal float PlotBefore, PlotAfter, TapeBefore, TapeAfter;
    }
    [ThreadStatic] internal static ReleaseState Release;
    static readonly AccessTools.FieldRef<float> Roll = AccessTools.StaticFieldRefAccess<float>(AccessTools.Field(typeof(BeatManager), "fRoll"));
    static readonly AccessTools.FieldRef<Dictionary<string, float>> Chances =
        AccessTools.StaticFieldRefAccess<Dictionary<string, float>>(AccessTools.Field(typeof(BeatManager), "dictEventChances"));
    static readonly Func<bool, bool> NativePlot = AccessTools.MethodDelegate<Func<bool, bool>>(AccessTools.Method(typeof(BeatManager), "Plot"));
    static readonly Func<bool> NativeTape = AccessTools.MethodDelegate<Func<bool>>(AccessTools.Method(typeof(BeatManager), "SpawnVideotape"));
    static readonly Func<bool> NativeBonus = AccessTools.MethodDelegate<Func<bool>>(AccessTools.Method(typeof(BeatManager), "BonusDerelict"));
    static string N(double value) => value.ToString("0.####", CultureInfo.InvariantCulture);
    internal static double Count(CondOwner actor) => actor?.GetCondAmount(Condition) ?? 0;
    internal static bool Pending(CondOwner actor) => actor?.HasCond(Condition) == true;

    internal static void Write(string message) {
        try {
            ConsoleToGUI.instance?.LogInfo(message);
            OstrabusPlugin.Logger?.LogInfo(message);
        } catch (Exception) { /* Diagnostics cannot replace a gameplay result. */ }
    }

    internal static bool Plot(bool tension) {
        float before = Roll();
        bool result = NativePlot(tension);
        if (WreckDamageLog.Enabled && Release.Active) {
            Release.PlotSeen = true; Release.PlotBefore = before; Release.PlotAfter = Roll(); Release.PlotResult = result;
        }
        return result;
    }
    internal static bool Videotape() {
        float before = Roll();
        bool result = NativeTape();
        if (WreckDamageLog.Enabled && Release.Active) {
            Release.TapeSeen = true; Release.TapeBefore = before; Release.TapeAfter = Roll(); Release.TapeResult = result;
        }
        return result;
    }
    internal static bool Bonus() {
        var actor = CrewSim.coPlayer;
        double before = Count(actor); float roll = Roll();
        float threshold = 0; Chances()?.TryGetValue("release_bonus_derelict", out threshold);
        bool result = NativeBonus(); // Native grant and chance consumption, once.
        if (WreckDamageLog.Enabled) {
            double after = Count(actor);
            if (after > before) {
                string context = Release.Active
                    ? "; plot=" + Decision(Release.PlotSeen, Release.PlotResult, Release.PlotBefore, Release.PlotAfter) +
                      "; videotape=" + Decision(Release.TapeSeen, Release.TapeResult, Release.TapeBefore, Release.TapeAfter)
                    : "; release-context=unavailable";
                Award(actor, before, after, "BeatManager.BonusDerelict; roll=" + N(roll) + "; threshold=" + N(threshold) +
                    "; negative-roll=" + (roll < 0 ? "true" : "false") + context);
            }
        }
        return result;
    }
    static string Decision(bool seen, bool success, float before, float after) =>
        seen ? (success ? "success" : "no-event") + "(" + N(before) + "->" + N(after) + ")" : "not-reached";

    internal static void ApplyTrigger(CondTrigger trigger, bool add, CondOwner actor, float coefficient, float age,
        Loot loot, Interaction interaction) {
        bool observe = WreckDamageLog.Enabled && trigger?.strCondName == Condition;
        double before = observe ? Count(actor) : 0;
        trigger.ApplyChanceID(add, actor, coefficient, age); // Preserve native application and any exception.
        if (observe) {
            double after = Count(actor);
            if (after > before) Award(actor, before, after, "Interaction.ApplyLootCT; interaction=" + interaction?.strName +
                "; loot=" + loot?.strName + "; trigger=" + trigger.strName);
        }
    }
    static void Award(CondOwner actor, double before, double after, string cause) =>
        Write("[WreckBonus] award epoch=" + N(StarSystem.fEpoch) + "; actor=" + actor?.strID +
            "; ship=" + actor?.ship?.strRegID + "; count=" + N(before) + "->" + N(after) + "; cause=" + cause);

    internal static string Consumption(Ship ship, double epoch, double before, double after, float curve, float final, Exception failure,
        bool presentAfter = false) =>
        "[WreckBonus " + ship?.strRegID + "] initialization epoch=" + N(epoch) + "; count=" + N(before) + "->" + N(after) +
        "; severity=" + N(curve) + "->" + N(final) + "; cause=Ship.BreakIn; count-cleared=" + (after == 0 ? "true" : "false") +
        "; condition-present-after=" + (presentAfter ? "true" : "false") +
        (failure == null ? "" : "; initialization-failed=" + failure.GetType().Name);
}
