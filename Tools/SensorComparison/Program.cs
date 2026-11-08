using System.Globalization;
using System.Text;
using System.Text.Json;
using Ostrabus.Sensors.Model;
using Ostrabus.SensorComparison;

if(args.Length!=2)throw new ArgumentException("Usage: SensorComparison <native-baseline.jsonl> <output-directory>");
int checks=ModelChecks.Run();
var native=File.ReadLines(args[0]).Select(line=>JsonSerializer.Deserialize<NativeRow>(line)).ToDictionary(row=>row.Id);
var rows=new List<ComparisonRow>();var settings=ModelSettings.Default;
foreach(var scenario in ScenarioCatalog.All()) {
    if(!native.TryGetValue(scenario.Id,out var baseline))throw new Exception("Missing native result: "+scenario.Id);
    var resolution=ContactModel.Evaluate(scenario.Sensors,scenario.Target,settings);
    var previousScatter=ContactModel.Evaluate(scenario.Sensors,scenario.Target,new ModelSettings(true,opticalScatterArcsec:2160));
    if(Math.Abs(resolution.RangeSigmaM-baseline.ProposedRangeSigmaM)>1e-9*Math.Max(1,resolution.RangeSigmaM) ||
        Math.Abs(resolution.CrossRangeSigmaM-baseline.ProposedCrossSigmaM)>1e-9*Math.Max(1,resolution.CrossRangeSigmaM))
        throw new Exception("Bundled Mono/.NET model mismatch: "+scenario.Id);
    checks+=2;
    if(baseline.CorrectedSignal+1e-12<baseline.Signal)throw new Exception("Inverse-square EM must not reduce detection strength: "+scenario.Id);
    checks++;
    if (!baseline.Visible && scenario.Target.SampleAgeSeconds == 0 && scenario.PackageId == "starter-passive") {
        if (resolution.PositionRmsM > baseline.RmsErrorM) throw new Exception("Starter fresh-contact accuracy worse than vanilla: " + scenario.Id);
        checks++;
    }
    rows.Add(new ComparisonRow(scenario.Id,scenario.PackageId,scenario.TargetId,scenario.Target.RangeKm,
        baseline.Signal,baseline.Visible,baseline.MaximumErrorM,baseline.RmsErrorM,resolution.RangeSigmaM,resolution.CrossRangeSigmaM,
        resolution.PositionRmsM,resolution.ObservationAgeSeconds,resolution.RoundTripSeconds,resolution.Sources.ToString(),
        scenario.Target.TransponderBroadcasting,scenario.Target.KnownHull,baseline.ExactCutoffKm,
        baseline.CorrectedSignal,baseline.CorrectedVisible,baseline.CorrectedVisible?0:scenario.Target.RangeKm*200*Math.Sqrt(8d/15),baseline.CorrectedCutoffKm,
        TransponderReception.Received(scenario.Sensors,scenario.Target),scenario.Target.TransponderBroadcasting?TransponderReception.Signal(scenario.Sensors,scenario.Target.RangeKm):0,
        previousScatter.RangeSigmaM,previousScatter.CrossRangeSigmaM));
}
Directory.CreateDirectory(args[1]);
File.Copy(args[0]+".metadata.json",Path.Combine(args[1],"native-metadata.json"),true);
File.WriteAllText(Path.Combine(args[1],"model-settings.json"),JsonSerializer.Serialize(settings,new JsonSerializerOptions{IncludeFields=true,WriteIndented=true}));
File.WriteAllText(Path.Combine(args[1],"comparisons.json"),JsonSerializer.Serialize(rows,new JsonSerializerOptions{WriteIndented=true}));
using(var csv=new StreamWriter(Path.Combine(args[1],"comparisons.csv"))) {
    csv.WriteLine("package,target,range_km,native_signal,native_exact,native_max_m,native_rms_m,model_range_sigma_m,model_cross_sigma_m,model_rms_m,state_age_s,round_trip_s,sources,transponder_broadcasting,bearing_sigma_arcsec,inverse_square_signal,inverse_square_exact,inverse_square_rms_m,timestamp_received,transponder_em_signal");
    foreach(var row in rows)csv.WriteLine(string.Join(",",row.Package,row.Target,F(row.RangeKm),F(row.NativeSignal),row.NativeExact,F(row.NativeMaxM),F(row.NativeRmsM),F(row.RangeSigmaM),F(row.CrossSigmaM),F(row.ModelRmsM),F(row.StateAgeSeconds),F(row.RoundTripSeconds),"\""+row.Sources+"\"",row.TransponderBroadcasting,F(row.BearingSigmaArcsec),F(row.CorrectedSignal),row.CorrectedExact,F(row.CorrectedRmsM),row.TimestampReceived,F(row.TransponderSignal)));
}
var report=new StringBuilder("# Sensor model comparison\n\nThree configurations: vanilla; vanilla with inverse-square EM; overhaul with inverse-square EM and EM-only timestamp acquisition. No live patch. Native scores use shipped methods; revised EM uses validated arithmetic. Offset statistics are analytic; model axes are one-sigma scales, not maximum bounds. Broadcasting is a scenario input; packet reception is calculated from transponder-only EM strength against the native threshold.\n\n");
foreach(string target in new[]{"unknown-dark-wreck","cooperative-cold","known-silent-wreck","stale-60s"}) {
    report.AppendLine("## "+target+"\n\n| Package | Range km | Vanilla RMS m | EM fix RMS m | Overhaul RMS m | Timestamp received |\n|---|---:|---:|---:|---:|---|");
    foreach(var row in rows.Where(r=>r.Target==target && new[]{30d,120,180,240,250,500,1000,1400,10000,100000}.Contains(r.RangeKm)))
        report.AppendLine($"| {row.Package} | {F(row.RangeKm)} | {F(row.NativeRmsM)} | {F(row.CorrectedRmsM)} | {F(row.ModelRmsM)} | {row.TimestampReceived} |");
    report.AppendLine();
}
report.AppendLine($"{checks} model checks passed. Further tuning and live integration remain separate work. The passive local-navigation prior deliberately preserves starter gameplay; it is not a claim that one optical bearing yields this range accuracy.");
File.WriteAllText(Path.Combine(args[1],"comparison.md"),report.ToString());
string template=File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"report.html"));
File.WriteAllText(Path.Combine(args[1],"index.html"),template.Replace("/*DATA*/[]",JsonSerializer.Serialize(rows)));
Console.WriteLine($"{checks} checks passed; {rows.Count} matched native/model comparisons. Report: {Path.Combine(args[1],"index.html")}");
foreach(var row in rows.Where(r=>r.Target=="unknown-dark-wreck" && (r.Package=="starter-passive" || r.Package=="stellar-magnificence") && new[]{30d,1000,1400,10000}.Contains(r.RangeKm)))
    Console.WriteLine($"{row.Package} {row.RangeKm} km dark wreck: native RMS {row.NativeRmsM:F2} m; model RMS {row.ModelRmsM:F2} m (radial {row.RangeSigmaM:F2}, cross {row.CrossSigmaM:F2})");
static string F(double x)=>x.ToString("G8",CultureInfo.InvariantCulture);
record NativeRow(string Id,double Signal,bool Visible,double MaximumErrorM,double MeanErrorM,double RmsErrorM,double MaximumOffsetIntervalSeconds,double ExactCutoffKm,double ProposedRangeSigmaM,double ProposedCrossSigmaM,double CorrectedSignal,bool CorrectedVisible,double CorrectedCutoffKm);
record ComparisonRow(string Id,string Package,string Target,double RangeKm,double NativeSignal,bool NativeExact,double NativeMaxM,double NativeRmsM,double RangeSigmaM,double CrossSigmaM,double ModelRmsM,double StateAgeSeconds,double RoundTripSeconds,string Sources,bool TransponderBroadcasting,bool KnownHull,double NativeCutoffKm,double CorrectedSignal,bool CorrectedExact,double CorrectedRmsM,double CorrectedCutoffKm,bool TimestampReceived,double TransponderSignal,double PreviousScatterRangeSigmaM,double PreviousScatterCrossSigmaM) {
    public double BearingSigmaArcsec => CrossSigmaM / Math.Max(1, RangeKm * 1000) / ContactModel.ArcsecondRadians;
}
