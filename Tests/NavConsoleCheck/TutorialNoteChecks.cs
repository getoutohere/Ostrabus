using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Serialization;
using BepInEx.Configuration;
using HarmonyLib;
using Ostrabus.NavConsole;

internal static class TutorialNoteChecks
{
    static int checks;
    static readonly Action<GUIOrbitDraw, bool> SetInitial = AccessTools.MethodDelegate<Action<GUIOrbitDraw, bool>>(
        AccessTools.Method(typeof(GUIOrbitDraw), "SetNoteInitial"));

    static void Check(bool result, string message)
    {
        checks++;
        if (!result) throw new Exception(message);
    }

    static GUIOrbitDraw Console(bool open)
    {
        var nav = (GUIOrbitDraw)FormatterServices.GetUninitializedObject(typeof(GUIOrbitDraw));
        SetInitial(nav, open);
        return nav;
    }

    // Only the scene-heavy Init body is substituted. The production postfix and
    // the actual native note setter/getter run, including the missing-button case.
    static void NativeRestore(GUIOrbitDraw nav, Dictionary<string, string> properties)
    {
        bool open = !properties.TryGetValue("bNote", out var text) || bool.Parse(text);
        SetInitial(nav, open);
    }

    static IEnumerable<CodeInstruction> SceneBoundary(IEnumerable<CodeInstruction> instructions)
    {
        yield return new CodeInstruction(OpCodes.Ldarg_0);
        yield return new CodeInstruction(OpCodes.Ldarg_2);
        yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(TutorialNoteChecks), nameof(NativeRestore)));
        yield return new CodeInstruction(OpCodes.Ret);
    }

    internal static void Run()
    {
        var previous = TutorialNote.HideFlag;
        var harmony = new Harmony("offline.navconsole.tutorial-note");
        var configPath = System.IO.Path.GetFullPath("obj/nav-note-test.cfg");
        AccessTools.Field(typeof(BepInEx.Paths), "<BepInExConfigPath>k__BackingField").SetValue(null, configPath + ".core");
        var config = new ConfigFile(configPath, false);
        config.SaveOnConfigSet = false;
        try
        {
            TutorialNote.HideFlag = null;
            var nav = Console(true);
            TutorialNote.Apply(nav);
            Check(nav.NoteShowing, "Uninitialized hide setting changed the native note.");

            TutorialNote.HideFlag = config.Bind("NavConsole", "HideTutorialNote", true);
            TutorialNote.HideFlag.Value = true;
            TutorialNote.Apply(nav);
            Check(!nav.NoteShowing, "Hidden paper still blocks native map clicks.");
            TutorialNote.Apply(nav);
            Check(!nav.NoteShowing, "Repeated application reopened the hidden paper.");

            TutorialNote.HideFlag.Value = false;
            SetInitial(nav, true);
            TutorialNote.Apply(nav);
            Check(nav.NoteShowing, "Visible paper no longer preserves native input blocking.");
            SetInitial(nav, false);
            TutorialNote.Apply(nav);
            Check(!nav.NoteShowing, "Disabling the setting reopened a folded paper.");

            var init = AccessTools.Method(typeof(GUIOrbitDraw), nameof(GUIOrbitDraw.Init), new[] {
                typeof(CondOwner), typeof(Dictionary<string, string>), typeof(string)
            });
            harmony.Patch(init, transpiler: new HarmonyMethod(typeof(TutorialNoteChecks), nameof(SceneBoundary)));
            harmony.CreateClassProcessor(typeof(TutorialNoteRestorePatch)).Patch();
            Check(Harmony.GetPatchInfo(init).Postfixes.Any(patch => patch.owner == harmony.Id),
                "Restore postfix did not bind to the native console Init method.");

            foreach (bool hidden in new[] { true, false })
            {
                TutorialNote.HideFlag.Value = hidden;
                foreach (string saved in new[] { null, "true", "false" })
                {
                    var properties = new Dictionary<string, string> { ["sentinel"] = "keep" };
                    if (saved != null) properties["bNote"] = saved;
                    nav = Console(true);
                    TutorialNote.Apply(nav); // Awake application can precede native restoration.
                    init.Invoke(nav, new object[] { null, properties, "Panel A" });
                    Check(nav.NoteShowing == (!hidden && saved != "false"),
                        "New/saved console restored an inconsistent hidden-note input state.");
                    Check(properties["sentinel"] == "keep" &&
                        (saved == null ? !properties.ContainsKey("bNote") : properties["bNote"] == saved),
                        "Note visibility repair rewrote the supplied property map.");
                }
            }
        }
        finally
        {
            harmony.UnpatchSelf();
            TutorialNote.HideFlag = previous;
        }
        System.Console.WriteLine(checks + " tutorial-note native-state/restore regression checks passed.");
    }
}
