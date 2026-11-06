using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using HarmonyLib;

static class Program {
    static int checks, creaks, hits;
    static float modifier;
    static readonly List<CondOwner> parts = new List<CondOwner>();
    static T Empty<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    static void Field(object target, string name, object value) => AccessTools.Field(target.GetType(), name).SetValue(target, value);
    static float Wear(Ship ship) => (float)AccessTools.Field(typeof(Ship), "fWearManeuver").GetValue(ship);
    static void Check(bool ok, string message) { checks++; if (!ok) throw new Exception(message); }
    static bool Quiet() => false;
    static bool Creak() { creaks++; return false; }
    static bool Parts(ref List<CondOwner> __result) { __result = parts; return false; }
    static bool Damage(float fModifier) { hits++; modifier = fModifier; return false; }
    static bool Equal(UnityEngine.Object __0, UnityEngine.Object __1, ref bool __result) { __result = ReferenceEquals(__0, __1); return false; }
    static Ship NewShip(int stabilizers = 0) {
        var ship = Empty<Ship>();
        Field(ship, "aPeople", new List<PersonSpec>()); Field(ship, "nLoadState", Ship.Loaded.Edit);
        Field(ship, "nActiveStabilizers", stabilizers); Field(ship, "fGravAmountLast", 0d);
        return ship;
    }
    static int Main() {
        var h = new Harmony("offline.maneuverwear");
        h.Patch(AccessTools.Method(typeof(UnityEngine.Object), "op_Equality"), prefix: new HarmonyMethod(typeof(Program), nameof(Equal)));
        h.Patch(AccessTools.Method(typeof(CrewSim), "CamShake"), prefix: new HarmonyMethod(typeof(Program), nameof(Quiet)));
        h.Patch(AccessTools.Method(typeof(AudioManager), "PlayCreakAudio"), prefix: new HarmonyMethod(typeof(Program), nameof(Creak)));
        h.Patch(AccessTools.Method(typeof(TileUtils), "EVAFallAwayCheck"), prefix: new HarmonyMethod(typeof(Program), nameof(Quiet)));
        h.Patch(AccessTools.Method(typeof(Ship), "GetCOs", new[] { typeof(CondTrigger), typeof(bool), typeof(bool), typeof(bool) }), prefix: new HarmonyMethod(typeof(Program), nameof(Parts)));
        h.Patch(AccessTools.Method(typeof(Ship), "ManeuverDamagePart"), prefix: new HarmonyMethod(typeof(Program), nameof(Damage)));
        CrewSim.objInstance = Empty<CrewSim>(); AudioManager.am = Empty<AudioManager>();
        parts.Add(Empty<CondOwner>());
        var ship = NewShip();
        ship.Gravity = 0.29;
        Check(creaks == 0 && Wear(ship) > 0.28f, "0.29g did not silently accumulate wear");
        float before = Wear(ship); ship.Gravity = 0.29;
        Check(Wear(ship) == before, "Constant acceleration added sudden-change wear");
        for (int i = 0; i < 51; i++) { ship.Gravity = 0; ship.Gravity = 0.29; }
        Check(creaks == 0 && hits == 0, "Silent cycles unexpectedly creaked or crossed 30 before 104 changes");
        ship.Gravity = 0;
        Check(hits == 75 && Wear(ship) == 0, "Sub-threshold changes did not eventually apply wear");
        Console.WriteLine("0.29g: no creak, but damage batch after 105 acceleration changes; 75 part hits.");
        foreach (int n in new[] { 0, 1, 2, 3 }) {
            creaks = hits = 0; ship = NewShip(n); ship.Gravity = 0.3;
            Check(creaks == 1, "0.3g sound threshold unexpectedly changed with stabilizers");
            Field(ship, "fWearManeuver", 30.1f);
            ship.Gravity = 0.3;
            float expected = n == 0 ? 10f : 10f * (0.3f + 1f / (float)Math.Pow(2, n));
            Check(hits == 75 && Math.Abs(modifier - expected) < 0.0001f, "Unexpected stabilizer damage multiplier");
            Console.WriteLine(n + " stabilizers: creak at 0.3g; part-damage multiplier " + (modifier / 10f));
        }
        h.CreateClassProcessor(typeof(Ostrabus.MiscBugFixes.StabilizerCreakPatch)).Patch();
        foreach (int n in new[] { 0, 1, 2, 3, 10 }) {
            double threshold = Ostrabus.MiscBugFixes.StabilizerCreakPatch.Threshold(NewShip(n));
            foreach (double change in new[] { 0.29, 0.3, 0.374, 0.376, 0.544, 0.546, 0.705, 0.707, 1.1 }) {
                creaks = hits = 0; ship = NewShip(n);
                ship.Gravity = change;
                Check(creaks == ((float)change >= threshold ? 1 : 0), "Patched sound boundary incorrect for " + n + " stabilizers, " + change + "g");
                Check(Math.Abs(Wear(ship) - (float)change) < 0.000001f, "Audio patch changed accumulated wear");
                ship.Gravity = 0;
                Check(creaks == ((float)change >= threshold ? 2 : 0), "Negative acceleration change did not use same sound threshold");
            }
            hits = 0; ship = NewShip(n); Field(ship, "fWearManeuver", 30.1f);
            ship.Gravity = 0.29;
            float expected = n == 0 ? 10f : 10f * (0.3f + 1f / (float)Math.Pow(2, n));
            Check(hits == 75 && Math.Abs(modifier - expected) < 0.0001f && Wear(ship) == 0, "Audio patch changed damage application");
        }
        creaks = 0; ship = NewShip(2); Field(ship, "bNeedsGravSet", true); ship.Gravity = 2;
        Check(creaks == 0 && Wear(ship) == 0, "Initial gravity assignment creaked or accumulated wear");
        AccessTools.Field(typeof(GUIFFWD), "bFFWDActive").SetValue(null, true);
        try {
            ship = NewShip(2); ship.Gravity = 2;
            Check(creaks == 0 && Wear(ship) == 0, "SFF gravity assignment creaked or accumulated wear");
        } finally { AccessTools.Field(typeof(GUIFFWD), "bFFWDActive").SetValue(null, false); }
        h.UnpatchSelf();
        Console.WriteLine(checks + " native maneuver wear checks passed. Only engine/UI/audio output, part enumeration and final damage writes are fixtures.");
        return 0;
    }
}

namespace Ostrabus {
    internal static class OstrabusPlugin { internal static readonly TestLog Logger = new TestLog(); }
    internal sealed class TestLog { internal void LogWarning(string message) { throw new Exception(message); } }
}
