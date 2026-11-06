using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

string output = Path.GetFullPath("obj/excursion-recorder-check.jsonl");
if (File.Exists(output)) File.Delete(output);
var listener = new TcpListener(IPAddress.Loopback, 0);
listener.Start();
int port = ((IPEndPoint)listener.LocalEndpoint).Port;
var requests = new List<string>();
using var stop = new CancellationTokenSource();
var server = Task.Run(async () => {
    try {
        while (!stop.IsCancellationRequested) {
            using var client = await listener.AcceptTcpClientAsync(stop.Token);
            using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, true);
            string line = await reader.ReadLineAsync() ?? "";
            requests.Add(line);
            while (!string.IsNullOrEmpty(await reader.ReadLineAsync())) {}
            int n = requests.Count;
            var body = new { generation = n < 3 ? 1 : 2, data = new { session = n < 3 ? 1 : 2, nextSince = n == 1 || n >= 3 ? 1 : 2, latestSequence = n < 3 ? 2 : 1, cursorAhead = n == 3, cursorOverrun = false, events = new[] { new { sequence = n } } } };
            byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(body);
            byte[] header = Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {bytes.Length}\r\nConnection: close\r\n\r\n");
            await stream.WriteAsync(header); await stream.WriteAsync(bytes);
        }
    } catch (OperationCanceledException) {}
});
var start = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
foreach (string arg in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", Path.GetFullPath("Record-Excursion.ps1"), "-OutputPath", output, "-Port", port.ToString(), "-DurationMinutes", "0.02", "-IntervalSeconds", "0.1", "-ShipId", "wreck ID" }) start.ArgumentList.Add(arg);
using var process = Process.Start(start)!;
try {
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
    await process.WaitForExitAsync(timeout.Token);
    string errors = await process.StandardError.ReadToEndAsync();
    if (process.ExitCode != 0) throw new Exception(errors);
} finally { if (!process.HasExited) process.Kill(true); stop.Cancel(); listener.Stop(); await server; }
if (requests.Count < 4) throw new Exception("Recorder did not drain/reset cursors");
string[] expected = { "since=0", "since=1", "since=2", "since=0" };
for (int i = 0; i < expected.Length; i++) if (!requests[i].Contains(expected[i])) throw new Exception("Wrong cursor: " + requests[i]);
if (!requests[0].Contains("shipId=wreck%20ID")) throw new Exception("Ship ID not URL encoded");
var lines = File.ReadAllLines(output);
foreach (string line in lines) using (JsonDocument.Parse(line)) {}
if (lines.Length != requests.Count) throw new Exception("Missing JSONL readings");
Console.WriteLine($"Recorder passed: {lines.Length} valid JSONL readings, encoded target, page draining and generation/session cursor reset.");
