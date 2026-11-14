using System;
using System.Collections.Specialized;
using System.Reflection;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using Ostranauts.Ships.AIPilots;
using Ostranauts.Ships.Commands;

static partial class Program {
    static void Transport() {
        var type = typeof(Ostrabus.OstrabusPlugin).Assembly.GetType("Ostrabus.Inspection.InspectionHttp", true);
        var parse = AccessTools.Method(type, "Parse");
        foreach (string method in new[] { "GET", "POST" }) {
            foreach (string reference in new[] { "HQCH", "Mercury", "HQCH-NAV1" }) {
                var http = Activator.CreateInstance(type, true);
                parse.Invoke(http, new object[] { method + " /ships/E-KRQD/match-velocity?referenceId=" + reference + " HTTP/1.1\r\nHost: 127.0.0.1:18764\r\nContent-Length: 0", 18764 });
                Check(AccessTools.Field(type, "Error").GetValue(http) == null, "HTTP transport rejected velocity reference");
                var query = (NameValueCollection)AccessTools.Field(type, "QueryString").GetValue(http);
                Check(query["referenceId"] == reference && (string)AccessTools.Field(type, "HttpMethod").GetValue(http) == method, "HTTP transport lost reference or method");
            }
        }
        foreach (string query in new[] { "referenceId=HQCH&referenceId=Mercury", "referenceId=HQCH&unknown=x", "referenceId=HQCH=Mercury" }) {
            var http = Activator.CreateInstance(type, true);
            parse.Invoke(http, new object[] { "POST /ships/E-KRQD/match-velocity?" + query + " HTTP/1.1\r\nHost: 127.0.0.1:18764\r\nContent-Length: 0", 18764 });
            Check(AccessTools.Field(type, "Error").GetValue(http) != null, "HTTP transport accepted ambiguous/unsupported parameters");
        }
        NetworkRoundTrip();
    }
    static async Task<string> Send(int port, string method, string path = "/ships/wayward/match-velocity?referenceId=HQCH") {
        using var client = new TcpClient(AddressFamily.InterNetwork);
        await client.ConnectAsync(IPAddress.Loopback, port);
        using var stream = client.GetStream();
        byte[] request = Encoding.ASCII.GetBytes(method + " " + path + " HTTP/1.1\r\nHost: 127.0.0.1:" + port + "\r\nContent-Length: 0\r\n\r\n");
        await stream.WriteAsync(request, 0, request.Length);
        using var data = new MemoryStream();
        var buffer = new byte[4096];
        int read;
        while ((read = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0) data.Write(buffer, 0, read);
        return Encoding.UTF8.GetString(data.ToArray());
    }
    static void NetworkRoundTrip() {
        var assembly = typeof(Ostrabus.OstrabusPlugin).Assembly;
        var apiType = assembly.GetType("Ostrabus.Inspection.InspectionApi", true);
        var boundary = new Harmony("offline.ai-velocity.transport-boundaries");
        // Recorder/UI lifecycle work is unrelated to request transport and requires a live scene.
        foreach (var pair in new[] { ("PersonnelCombatInspection", "Tick"), ("AIBehaviorHistory", "Capture") })
            boundary.Patch(AccessTools.Method(assembly.GetType("Ostrabus.Inspection." + pair.Item1, true), pair.Item2), prefix: new HarmonyMethod(typeof(Program), nameof(Skip)));
        boundary.Patch(AccessTools.Method(typeof(CondOwner), nameof(CondOwner.ZeroCondAmount)), prefix: new HarmonyMethod(typeof(Program), nameof(Skip)));
        var ai = AIShipManager.dictAIs["HQCH"][0];
        ai.AICharacter.PriorityQueue = new QueuedCommandsNode("PriorityQueue", ai.AICharacter);
        ai.AICharacter.BehaviorTree = new SelectorCommand("BehaviorTree", ai.AICharacter.PriorityQueue,
            new StickySequenceCommand("Main", new ICommand[] { new TargetNewIfNone(ai.AICharacter) }));
        Set(ai, "_behaviorTree", ai.AICharacter.BehaviorTree);
        ai.Ship.objSS.vVelX = 1e-7;
        CrewSim.objInstance = Empty<CrewSim>(); Set(CrewSim.objInstance, "_finishedLoading", true);
        var api = Activator.CreateInstance(apiType, BindingFlags.Instance | BindingFlags.NonPublic, null, new object[] { 0 }, null);
        var listener = (TcpListener)AccessTools.Field(apiType, "listener").GetValue(api);
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        Set(api, "port", port); Set(api, "system", CrewSim.system);
        var pump = AccessTools.Method(apiType, "Pump");
        try {
            foreach (string method in new[] { "GET", "POST" }) {
                var ship = CrewSim.system.dictShips["wayward"];
                double before = ship.objSS.vVelX;
                var request = Send(port, method);
                var until = DateTime.UtcNow.AddSeconds(5);
                while (!request.IsCompleted && DateTime.UtcNow < until) { pump.Invoke(api, null); Thread.Sleep(1); }
                Check(request.IsCompleted, "HTTP request did not complete");
                string response = request.GetAwaiter().GetResult();
                Check(response.StartsWith("HTTP/1.1 200 ", StringComparison.Ordinal), "HTTP recovery failed: " + response);
                var envelope = JObject.Parse(response.Substring(response.IndexOf("\r\n\r\n", StringComparison.Ordinal) + 4));
                var body = (JObject)envelope["data"];
                Check(body.Value<bool>("eligible") && body.Value<bool>("applied") == (method == "POST") && body.Value<string>("referenceId") == "HQCH", "HTTP dispatch lost method/reference: " + response);
                Check(body.Value<bool>("held") == (method == "POST"), "HTTP match did not establish Hold");
                Check(method == "GET" ? ship.objSS.vVelX == before : ship.objSS.vVelX == CrewSim.system.dictShips["HQCH"].objSS.vVelX, "HTTP preview/mutation behavior incorrect");
            }
            boundary.Patch(AccessTools.Method(typeof(TargetNewIfNone), "FindTarget"), prefix: new HarmonyMethod(typeof(Program), nameof(NoRoute)));
            foreach (bool available in new[] { false, true }) {
                nextTripAvailable = available;
                var request = Send(port, "POST", "/ships/wayward/release-hold");
                var until = DateTime.UtcNow.AddSeconds(5);
                while (!request.IsCompleted && DateTime.UtcNow < until) { pump.Invoke(api, null); Thread.Sleep(1); }
                Check(request.IsCompleted, "HTTP release did not complete");
                string response = request.GetAwaiter().GetResult();
                Check(response.StartsWith("HTTP/1.1 " + (available ? "200 " : "409 "), StringComparison.Ordinal), "HTTP release status incorrect: " + response);
                var envelope = JObject.Parse(response.Substring(response.IndexOf("\r\n\r\n", StringComparison.Ordinal) + 4));
                Check(envelope["data"].Value<bool>("held") == !available && AIShipManager.dictAIs["HQCH"].Count == 1, "HTTP release lost controller or Hold");
            }
        } finally {
            AccessTools.Method(apiType, "DisposeTransport").Invoke(api, null);
            boundary.UnpatchSelf();
        }
    }
}
