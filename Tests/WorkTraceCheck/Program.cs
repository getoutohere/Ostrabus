using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Ostrabus.Diagnostics;

static partial class Program {
    const string Owner = "com.ostranauts.template.diagnostics.work";
    static int checks;
    static void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
    static int Count() => Harmony.GetAllPatchedMethods().Count(m => Harmony.GetPatchInfo(m).Owners.Contains(Owner));
    static bool OtherHook(ref bool __result) { __result = true; return false; }
    sealed class Steps : IEnumerator, IDisposable {
        int step;
        public bool Disposed;
        public object Current => "sentinel";
        public bool MoveNext() => step++ == 0;
        public void Reset() => throw new NotSupportedException();
        public void Dispose() { Disposed = true; }
    }
    public static void Main(string[] args) {
        if (args.Length > 0 && args[0] == "scheduler") { SchedulerTiming.Run(); return; }
        Check(!WorkTrace.Enabled && Count() == 0, "Diagnostics should start without simulation hooks");
        foreach (string name in new[] { "ACTAlarmO2Install", "ACTTV01OffUninstall", "ACTFeedItemTV", "ACTReloadItemN2", "ACTHaulItem", "EquipItem" }) {
            var interaction = (Interaction)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(Interaction));
            interaction.strName = name;
            Check(WorkTrace.Relevant(interaction), "Required work event filtered: " + name);
        }
        foreach (string name in new[] { "ACTMelee;AModePunch", "SOCDarkJoke", "ACTCombatDodge" }) {
            var interaction = (Interaction)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(Interaction));
            interaction.strName = name;
            Check(!WorkTrace.Relevant(interaction), "Unrelated menu/social event retained: " + name);
        }
        var foreign = new Harmony("offline.worktrace.foreign");
        var ledger = AccessTools.Method(typeof(Interaction), "ValidateLedger");
        foreign.Patch(ledger, prefix: new HarmonyMethod(typeof(Program), nameof(OtherHook)));
        WorkTrace.SetEnabled(true);
        Check(WorkTrace.Enabled && Count() == 15, "Expected fifteen native methods to be instrumented");
        WorkTrace.SetEnabled(true);
        Check(Count() == 15, "Repeated enable duplicated hooks");
        CheckTimingEvents();
        var result = new IteratorResult<bool> { Value = true };
        var steps = new Steps();
        var wrapper = (IEnumerator)Activator.CreateInstance(typeof(WorkTrace).GetNestedType("ValidationIterator", BindingFlags.NonPublic),
            BindingFlags.Instance | BindingFlags.NonPublic, null, new object[] { steps, result, null, null, null, "fixture", -1 }, null);
        Check(wrapper.MoveNext() && (string)wrapper.Current == "sentinel", "Wrapper changed a native yield");
        WorkTrace.SetEnabled(false);
        Check(!WorkTrace.Enabled && Count() == 0, "Off must remove all diagnostic simulation hooks");
        Check(Harmony.GetPatchInfo(ledger).Owners.Contains("offline.worktrace.foreign"), "Off removed another owner's patch");
        Check(!wrapper.MoveNext() && result.Value, "In-flight wrapper changed native completion/result after disabling");
        ((IDisposable)wrapper).Dispose();
        Check(steps.Disposed, "Wrapper did not forward native disposal");
        WorkTrace.SetEnabled(true);
        Check(Count() == 15, "Re-enable failed after unpatching");
        WorkTrace.Stop();
        Check(Count() == 0, "Shutdown left diagnostic hooks installed");
        foreign.UnpatchSelf();
        var profiles = typeof(Ostrabus.Optimizations.LoadProfiling).Assembly.GetTypes()
            .Where(t => t.Namespace == "Ostrabus.Optimizations" && t.Name.EndsWith("ProfilePatch")).ToArray();
        Check(profiles.Length == 12, "Profiling hook fixture should cover all twelve classes");
        Ostrabus.Optimizations.LoadProfiling.Enabled = false;
        var profileOwner = new Harmony("offline.worktrace.profile");
        foreach (var profile in profiles) profileOwner.CreateClassProcessor(profile).Patch();
        Check(!Harmony.GetAllPatchedMethods().Any(m => Harmony.GetPatchInfo(m).Owners.Contains(profileOwner.Id)), "Disabled profiling installed hooks");
        Ostrabus.Optimizations.SavedJsonLoading.Enabled = true;
        foreach (var profile in profiles) {
            bool prepared = (bool)AccessTools.Method(profile, "Prepare").Invoke(null, null);
            Check(prepared == (profile.Name == "ShipFileProfilePatch"), "Experimental parser must retain only shared file routing");
        }
        Ostrabus.Optimizations.LoadProfiling.Enabled = true;
        foreach (var profile in profiles) Check((bool)AccessTools.Method(profile, "Prepare").Invoke(null, null), "Enabled profiling gate rejected a class");
        profileOwner.UnpatchSelf();
        Console.WriteLine("WorkTrace checks passed: " + checks);
    }
}
