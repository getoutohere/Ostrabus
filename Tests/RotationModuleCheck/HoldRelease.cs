using System;
using HarmonyLib;
using Ostrabus.NavConsole.Rotation;

static partial class Program {
    static int releaseAudioStarts, releaseAudioStops;
    static bool CountReleaseStart(string __0) { if (__0?.StartsWith("ShipRCS", StringComparison.Ordinal) == true) releaseAudioStarts++; return false; }
    static bool CountReleaseStop(string __0) { if (__0?.StartsWith("ShipRCS", StringComparison.Ordinal) == true) releaseAudioStops++; return false; }
    static void HoldRelease() {
        RotationRuntime.Cleanup();
        var ship = Hull("hold-release"); Nav(ship);
        var order = Order(ship, 0, hold: true);
        ship.objSS.fW = .02f;
        double before = ship.fShallowRCSRemass;
        float rate = ship.objSS.fW;
        order.SetHold(false);
        StarSystem.fEpoch += .02;
        ship.objSS.TimeAdvance(.02);
        Check(ship.fShallowRCSRemass == before && ship.objSS.fW == rate && ship.objSS.fA == 0,
            "HOLD off fired a corrective pulse or changed angular momentum");
        Check(!RotationRuntime.HasCommands && RotationRuntime.Find(ship) == null,
            "Released hold kept capturing manual RCS input");
        Check(DataHandler.mapCOs[ship.strRegID + "-nav"].mapGUIPropMaps["Panel A"].ContainsKey(RotationModule.LastCommandKey),
            "Releasing hold discarded the remembered command");
        ship.Maneuver(.1f, 0, 0, 0, .02f);
        Check(ship.fShallowRCSRemass < before && ship.objSS.vAccRCS.x != 0, "Released controller continued intercepting manual translation");
        Console.WriteLine("HOLD release: no RCS pulse/fuel, unchanged spin, immediate controller retirement and remembered command preserved.");
        RotationRuntime.Cleanup();
    }

    static void HoldReleaseAudio(Harmony fixture) {
        var field = AccessTools.Field(typeof(AudioManager), "am");
        var previous = field.GetValue(null);
        field.SetValue(null, Empty<AudioManager>());
        foreach (var method in AccessTools.GetDeclaredMethods(typeof(AudioManager))) {
            if (method.Name == "PlayAudioEmitter") fixture.Patch(method, prefix: new HarmonyMethod(typeof(Program), nameof(CountReleaseStart)));
            if (method.Name == "StopAudioEmitter") fixture.Patch(method, prefix: new HarmonyMethod(typeof(Program), nameof(CountReleaseStop)));
        }
        fixture.Patch(AccessTools.Method(typeof(Ship), nameof(Ship.SetThrust)), prefix: new HarmonyMethod(typeof(Program), nameof(Quiet)));
        try {
            foreach (float spin in new[] { 0f, .02f }) {
                RotationRuntime.Cleanup();
                var ship = Hull("audible-release-" + spin); Nav(ship);
                var command = Order(ship, 0, hold: true);
                Set(ship, "nLoadState", Ship.Loaded.Full); // Exercise native audible StopManeuver.
                ship.objSS.fW = spin;
                releaseAudioStarts = releaseAudioStops = 0;
                command.SetHold(false);
                Check(releaseAudioStarts == 0 && releaseAudioStops == 3, "Native HOLD release started RCS audio instead of stopping its three emitters");
                Check(ship.objSS.fW == spin, "Audible release changed spin");
            }
            Console.WriteLine("Full-ship native release audio: stops all three RCS emitters, starts none, preserves stationary/moving spin.");
        } finally {
            field.SetValue(null, previous);
            RotationRuntime.Cleanup();
        }
    }
}
