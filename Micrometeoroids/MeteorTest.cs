using System;
using System.Reflection;
using HarmonyLib;

namespace Ostrabus.Micrometeoroids;

// Called only by an explicit main-thread API request. No timers or persistent hooks.
internal static class MeteorTest {
    internal const string Route = "/micrometeoroids/strike";
    static readonly FieldInfo Roll = AccessTools.Field(typeof(BeatManager), "fRoll");
    static readonly FieldInfo OnStation = AccessTools.Field(typeof(BeatManager), "bOnStation");
    static readonly FieldInfo Docked = AccessTools.Field(typeof(BeatManager), "bDockedWithStation");
    static readonly MethodInfo Event = AccessTools.Method(typeof(BeatManager), "Micrometeoroid");
    [ThreadStatic] static Result current;
    internal sealed class Result {
        internal bool Triggered;
        internal string ShipId;
        internal double? DetectionProbability;
        internal float? Angle;
        internal bool? Detected, Intercepted;
        internal int Bursts, Rounds;
    }
    internal static void Target(Ship ship) {
        if (current != null) current.ShipId = ship?.strRegID;
    }
    internal static void Defense(float angle, double probability, bool detected, bool intercepted, int bursts, int rounds) {
        if (current == null) return;
        current.Angle = angle; current.DetectionProbability = probability;
        current.Detected = detected; current.Intercepted = intercepted;
        current.Bursts = bursts; current.Rounds = rounds;
    }
    internal static Result Trigger() {
        if (CrewSim.coPlayer == null || CrewSim.coPlayer.ship?.objSS == null || CrewSim.system == null)
            throw new InvalidOperationException("A loaded player ship and simulation are required.");
        var result = new Result();
        object previousRoll = Roll.GetValue(null), previousStation = OnStation.GetValue(null), previousDocked = Docked.GetValue(null);
        var previous = current;
        try {
            current = result;
            // GenerateTension normally refreshes these cached flags. Refresh them
            // for this direct event call without changing timers or event weights.
            OnStation.SetValue(null, CrewSim.coPlayer.ship.objSS.bIsBO);
            Docked.SetValue(null, CrewSim.coPlayer.ship.objSS.bBOLocked);
            Roll.SetValue(null, float.NegativeInfinity); // Request this event; keep native eligibility.
            result.Triggered = (bool)Event.Invoke(null, null);
            return result;
        } finally {
            Roll.SetValue(null, previousRoll); OnStation.SetValue(null, previousStation); Docked.SetValue(null, previousDocked);
            current = previous;
        }
    }
}
