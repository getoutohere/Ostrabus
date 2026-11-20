using System;
using System.IO;
using Newtonsoft.Json.Linq;
using Ostrabus.ShipNavigation;

static partial class Program {
    // Diagnostic replay, not a fixture asserting that every approach from
    // behind is safe. Recorded steering is already evasive; the coasting case
    // deliberately isolates radius policy using the captured position/velocity.
    static int AsternReplay(string directory) {
        var us = JObject.Parse(File.ReadAllText(Path.Combine(directory, "_ships_V-HZ0GD.json")));
        var player = JObject.Parse(File.ReadAllText(Path.Combine(directory, "_ships_O-GXD.json")));
        Check((double)us["epoch"] == (double)player["epoch"], "Replay needs an atomic-epoch pair");
        var a = us["data"]["navigation"]["currentMotion"];
        var b = player["data"]["navigation"]["currentMotion"];
        double x = (double)a["positionAU"]["x"] - (double)b["positionAU"]["x"];
        double y = (double)a["positionAU"]["y"] - (double)b["positionAU"]["y"];
        double vx = (double)a["velocityAUPerSecond"]["x"] - (double)b["velocityAUPerSecond"]["x"];
        double vy = (double)a["velocityAUPerSecond"]["y"] - (double)b["velocityAUPerSecond"]["y"];
        // Native radii are read from the pre-encounter save. The exterior radii
        // were rebuilt with the production snapshot/worker from that save.
        int nativeUs = (int)JObject.Parse(File.ReadAllText(Path.Combine(directory, "saved-target-header.json")))["fields"]["objSS"]["size"];
        int nativeThem = (int)JObject.Parse(File.ReadAllText(Path.Combine(directory, "saved-player-header.json")))["fields"]["objSS"]["size"];
        const double hullUs = 1046.52, hullThem = 1145.43;
        var self = new ShipSitu(false) { vPosx = x, vPosy = y, vVelX = vx, vVelY = vy, Size = nativeUs * 2 };
        var other = new ShipSitu(false) { Size = nativeThem };
        bool native = CollisionManager.WillCollide(self, other, 30);
        self.Size = Math.Max(nativeUs * 2, (int)Math.Ceiling(hullUs * 2));
        other.Size = Math.Max(nativeThem, (int)Math.Ceiling(hullThem));
        bool padded = AvoidancePrediction.Collision(self, other, 30);
        self.Size = (int)Math.Ceiling(hullUs);
        bool physical = AvoidancePrediction.Collision(self, other, 30);
        double min = Math.Sqrt(Minimum(x * RepairAU, y * RepairAU, vx * RepairAU, vy * RepairAU, 0, 0, 30));
        Check(!native && padded && !physical, "Captured-velocity coasting control no longer isolates excessive circular clearance");
        Check(min > hullUs + hullThem, "Coasting control is not proven clear of both enclosing hull circles");
        Console.WriteLine("Captured position/velocity with both craft coasting: native warning=" + native +
            "; current padded warning=" + padded + "; enclosing physical-hull warning=" + physical +
            "; minimum centres=" + min + "m; physical radius sum=" + (hullUs + hullThem) + "m.");
        self.vPosx = 5000 / RepairAU; self.vPosy = 0;
        self.vVelX = -200 / RepairAU; self.vVelY = 0;
        Check(AvoidancePrediction.Collision(self, other, 30), "Real rear-end collision was accidentally exempted");
        Console.WriteLine("Real rear-end control remains hazardous. This does not reconstruct the uncaptured first dodge decision.");
        Console.WriteLine(checks + " astern-approach diagnostic checks passed.");
        return 0;
    }
}
