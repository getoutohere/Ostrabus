using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

if (args.Length != 1) throw new ArgumentException("Usage: FlightJournalAnalysis <journal.jsonl>");
const double kmAu = 149597870.7;
double N(JToken? t) => t == null || t.Type == JTokenType.Null ? double.NaN : (double)t;
string? S(JToken? t) => (string?)t;
double Distance(JToken? a, JToken? b) => Math.Sqrt(Math.Pow(N(a?["XAU"]) - N(b?["XAU"]), 2) + Math.Pow(N(a?["YAU"]) - N(b?["YAU"]), 2)) * kmAu;
double Speed(JToken? a, JToken? b) => Math.Sqrt(Math.Pow(N(a?["VxAUPerSecond"]) - N(b?["VxAUPerSecond"]), 2) + Math.Pow(N(a?["VyAUPerSecond"]) - N(b?["VyAUPerSecond"]), 2)) * kmAu;
var events = new List<JObject>(); int partial = 0;
foreach (string line in File.ReadLines(args[0])) {
    try { var row = JObject.Parse(line); if (row["Kind"] != null) events.Add(row); }
    catch (JsonReaderException) { partial++; }
}
if (events.Count == 0) throw new Exception("No event records");
var start = events.First(); var end = events.Last(e => e["Ships"] is JArray a && a.Count > 0);
var beforeShips = ((JArray)start["Ships"]!).ToDictionary(s => S(s["Id"])!);
var finalShips = ((JArray)end["Ships"]!).ToDictionary(s => S(s["Id"])!);
var sffs = events.Where(e => S(e["Kind"]) == "sff-world-after").ToArray();
var regions = events.Where(e => S(e["Kind"])!.StartsWith("atc-")).Select(e => new {
    kind = S(e["Kind"]), region = S(e["Region"]), epoch = N(e["Epoch"]), ships = N(e["TotalShips"]), ais = N(e["TotalAis"]), error = S(e["Error"])
}).ToArray();
var sffRows = sffs.Select(e => new {
    epoch = N(e["Epoch"]), hours = N(e["SkipSeconds"]) / 3600,
    newReport = (bool?)e["NewSffReport"], failures = e["Orbital"]?["lastSff"]?["failures"],
    nativeCourses = e["Orbital"]?["lastSff"]?["nativeCourses"],
    impacts = e["Orbital"]?["lastSff"]?["items"]?.Where(i => i["impactBody"]?.Type == JTokenType.String).ToArray(),
    fallbacks = e["Orbital"]?["lastSff"]?["items"]?.Where(i => (bool?)i["budgetFallback"] == true).ToArray(),
    error = S(e["Error"])
}).ToArray();
var playback = sffs.Select(e => {
    var points = ((JArray?)e["PlayerCourse"])?.ToArray() ?? Array.Empty<JToken>();
    var actual = e["Ships"]!.FirstOrDefault(s => S(s["Id"]) == S(e["PlayerShipId"]));
    if (points.Length < 2 || actual == null) return null;
    double epoch = N(e["Epoch"]); int left = 0, right = points.Length - 1;
    for (int i = 1; i < points.Length; i++) if (epoch <= N(points[i]["ArrivalEpoch"])) { left = i - 1; right = i; break; }
    float span = (float)(N(points[right]["ArrivalEpoch"]) - N(points[left]["ArrivalEpoch"]));
    float elapsed = (float)(epoch - N(points[left]["ArrivalEpoch"]));
    float t = span == 0 ? 0 : Math.Clamp(elapsed / span, 0, 1);
    double Lerp(string key) => N(points[left]["Motion"]?[key]) + (N(points[right]["Motion"]?[key]) - N(points[left]["Motion"]?[key])) * t;
    double position = Math.Sqrt(Math.Pow(Lerp("XAU") - N(actual["Motion"]?["XAU"]), 2) + Math.Pow(Lerp("YAU") - N(actual["Motion"]?["YAU"]), 2)) * kmAu;
    double velocity = Math.Sqrt(Math.Pow(Lerp("VxAUPerSecond") - N(actual["Motion"]?["VxAUPerSecond"]), 2) + Math.Pow(Lerp("VyAUPerSecond") - N(actual["Motion"]?["VyAUPerSecond"]), 2)) * kmAu;
    return new { epoch, left, right, positionErrorKm = position, velocityErrorKmS = velocity, points = points.Length };
}).Where(r => r != null).ToArray();
object[] Survey(Dictionary<string, JToken> ships) => ships.Values.Where(s => s["Home"]?.Type == JTokenType.String).Select(s => {
    ships.TryGetValue(S(s["Home"])!, out var home); ships.TryGetValue(S(s["TargetId"]) ?? "", out var target);
    return (object)new { id = S(s["Id"]), home = S(s["Home"]), role = S(s["PilotType"]), command = S(s["Command"]),
        homeRangeKm = Distance(s["Motion"], home?["Motion"]), homeVrelKmS = Speed(s["Motion"], home?["Motion"]),
        target = S(s["TargetId"]), targetRangeKm = Distance(s["Motion"], target?["Motion"]),
        fuelKg = N(s["CachedRcsKg"]), fuelCurrent = (bool?)s["FuelCachesCurrent"],
        fusionThrust = N(s["CachedFusionThrustMax"]), hasPlan = (bool?)s["HasPlan"], planArrivalEpoch = N(s["PlanArrivalEpoch"]),
        targetReferenceMatches = (bool?)s["TargetReferenceMatches"], targetObjectCurrent = (bool?)s["TargetObjectIsCurrent"] };
}).ToArray();
var player = finalShips[S(end["PlayerShipId"])!];
var output = new {
    events = events.Count, partialLines = partial, gameHours = (N(end["Epoch"]) - N(start["Epoch"])) / 3600,
    startRegion = S(start["Region"]), finalRegion = S(end["Region"]), startShips = beforeShips.Count, finalShips = finalShips.Count,
    removedIds = beforeShips.Keys.Except(finalShips.Keys).ToArray(), newIds = finalShips.Keys.Except(beforeShips.Keys).ToArray(),
    eventCounts = events.GroupBy(e => S(e["Kind"])!).ToDictionary(g => g.Key, g => g.Count()),
    truncations = events.Count(e => (bool?)e["ShipsTruncated"] == true || (bool?)e["BodiesTruncated"] == true || (bool?)e["AisTruncated"] == true || (bool?)e["PlayerCourseTruncated"] == true),
    maximumCaptureMs = events.Max(e => N(e["CaptureMs"])), maximumStaleAiReferences = events.Max(e => N(e["StaleAiReferences"])),
    regions, sffs = sffRows, nativeCoursePlayback = playback,
    player = new { id = S(player["Id"]), target = S(player["TargetId"]), planPoints = N(player["PlanPoints"]),
        remainingMinutes = (N(player["PlanArrivalEpoch"]) - N(end["Epoch"])) / 60,
        targetRangeKm = Distance(player["Motion"], player["TargetMotion"]), targetVrelKmS = Speed(player["Motion"], player["TargetMotion"]) },
    initialAiSurvey = Survey(beforeShips), finalAiSurvey = Survey(finalShips),
    warnings = events.Where(e => S(e["Kind"]) == "log").Select(e => new { epoch = N(e["Epoch"]), level = S(e["Level"]), message = S(e["Message"]) }).ToArray()
};
Console.WriteLine(JsonConvert.SerializeObject(output, Formatting.Indented));
