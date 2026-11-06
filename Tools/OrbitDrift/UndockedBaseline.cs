using System.Text.Json;
using System.Text.Json.Nodes;

static partial class Drift {
    internal static void UndockedBaseline(string root,double? skipStart=null) {
        string directory=Path.Combine(root,"Orbital/LiveTest/SFF-UNDOCKED");
        var baseline=JsonNode.Parse(File.ReadAllText(Path.Combine(directory,"BASELINE.json")))!;
        var bodies=baseline["excursion"]!["data"]!["bodies"]!;
        var sun=Orbit.Parse(bodies["Sol"]!,null);var venus=Orbit.Parse(bodies["Venus"]!,sun);
        var station=Orbit.Parse(bodies["VORB"]!,venus);
        var ship=baseline["excursion"]!["data"]!["ships"]!.AsArray().First(s=>s?["id"]?.GetValue<string>()=="O-GXD")!;
        var motion=ship["motion"]!;var orbit=Orbit.Parse(motion["referenceBody"]!,venus);
        double epoch=skipStart??D(motion,"captureEpoch"),end=epoch+21600,radius=D(bodies["Venus"]!,"radiusAU")*Orbit.AU;
        var initial=orbit.At(epoch,true);var final=orbit.At(end,true);var stationEnd=station.At(end,true);
        var offset=new V(D(motion,"bodyOffsetXAU"),D(motion,"bodyOffsetYAU"))*Orbit.AU;
        initial=initial with { P=initial.P+offset };final=final with { P=final.P+offset };
        var report=new { startEpoch=epoch,endEpoch=end,nativeOrbitLocked=motion["orbitLocked"]!.GetValue<bool>(),
            playerPeriodMinutes=orbit.Period/60,stationPeriodMinutes=station.Period/60,
            periapsisAltitudeKm=D(motion["referenceBody"]!,"periapsisAU")*Orbit.AU-radius,
            apoapsisAltitudeKm=D(motion["referenceBody"]!,"apoapsisAU")*Orbit.AU-radius,
            initialSeparationKm=(initial.P-station.At(epoch,true).P).Length,
            predictedFinalSeparationKm=(final.P-stationEnd.P).Length,
            predictedFinalAltitudeKm=(final.P-venus.At(end,true).P).Length-radius,
            predictedPlayerPositionKm=new {x=final.P.X,y=final.P.Y},
            note="Native prescribed orbital playback prediction, using captured elements and native Newton tolerance. Locked player is excluded from custom SFF translation. Station separation can change despite near-zero initial relative speed." };
        File.WriteAllText(Path.Combine(directory,"PREDICTION.json"),JsonSerializer.Serialize(report,new JsonSerializerOptions {WriteIndented=true}));
        Console.WriteLine(JsonSerializer.Serialize(report,new JsonSerializerOptions {WriteIndented=true}));
    }
}
