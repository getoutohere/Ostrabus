using System;
using System.Collections.Specialized;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace Ostrabus.Inspection;

// One bounded HTTP request per connection; no keepalive, bodies or chunking.
internal sealed class InspectionHttp {
    internal readonly NameValueCollection Headers = new NameValueCollection();
    internal readonly NameValueCollection QueryString = new NameValueCollection();
    internal string HttpMethod, Error;
    internal int ErrorStatus = 400;
    internal Uri Url;

    internal static async Task<InspectionHttp> Read(NetworkStream stream, int port) {
        var result = new InspectionHttp();
        byte[] buffer = new byte[4096];
        int length = 0;
        while (length < buffer.Length) {
            int count = await stream.ReadAsync(buffer, length, buffer.Length - length).ConfigureAwait(false);
            if (count == 0) { throw new System.IO.IOException("Disconnected"); }
            length += count;
            for (int i = Math.Max(0, length - count - 3); i <= length - 4; i++) {
                if (buffer[i] != 13 || buffer[i + 1] != 10 || buffer[i + 2] != 13 || buffer[i + 3] != 10) { continue; }
                for (int j = 0; j < i; j++) {
                    byte b = buffer[j];
                    if (b > 126 || (b < 32 && b != 13 && b != 10 && b != 9)) { result.Error = "Invalid header bytes."; return result; }
                }
                result.Parse(Encoding.ASCII.GetString(buffer, 0, i), port);
                return result;
            }
        }
        result.Error = "HTTP headers exceed 4096 bytes.";
        return result;
    }

    void Parse(string text, int port) {
        string[] lines = text.Split(new[] { "\r\n" }, StringSplitOptions.None);
        string[] first = lines[0].Split(' ');
        if (first.Length != 3 || (first[2] != "HTTP/1.1" && first[2] != "HTTP/1.0")) { Error = "Invalid request line."; return; }
        HttpMethod = first[0];
        string target = first[1];
        if (target.Length > 512 || !target.StartsWith("/", StringComparison.Ordinal) || target.StartsWith("//", StringComparison.Ordinal)) { Error = "Invalid request target."; return; }
        for (int i = 1; i < lines.Length; i++) {
            int split = lines[i].IndexOf(':');
            if (split <= 0) { Error = "Invalid header."; return; }
            string key = lines[i].Substring(0, split);
            if (Headers[key] != null) { Error = "Duplicate header."; return; }
            Headers.Add(key, lines[i].Substring(split + 1).Trim());
        }
        if (Headers["Host"] != $"127.0.0.1:{port}") { ErrorStatus = 403; Error = "Use the numeric loopback address and configured port."; return; }
        if (Headers["Transfer-Encoding"] != null || (Headers["Content-Length"] != null && Headers["Content-Length"] != "0")) { Error = "Request bodies are not supported."; return; }
        if (!Uri.TryCreate($"http://127.0.0.1:{port}" + target, UriKind.Absolute, out Url)) { Error = "Invalid URL."; return; }
        if (Url.Query.Length > 0) {
            foreach (string part in Url.Query.Substring(1).Split('&')) {
                string[] pair = part.Split('=');
                if (pair.Length != 2 || (pair[0] != "offset" && pair[0] != "limit" && pair[0] != "radiusKm" && pair[0] != "since" && pair[0] != "shipId" && pair[0] != "stationId" && pair[0] != "minDistanceKm" && pair[0] != "maxShips" && pair[0] != "confirm" && pair[0] != "dryRun" && pair[0] != "force" && pair[0] != "targetKg" && pair[0] != "coId" && pair[0] != "seconds") || QueryString[pair[0]] != null) { Error = "Expected unique supported query parameters."; return; }
                QueryString.Add(pair[0], pair[1]);
            }
        }
    }

    internal static async Task Write(NetworkStream stream, int status, byte[] body) {
        string reason = status == 200 ? "OK" : "Inspection Error";
        byte[] head = Encoding.ASCII.GetBytes($"HTTP/1.1 {status} {reason}\r\nContent-Type: application/json; charset=utf-8\r\nCache-Control: no-store\r\nConnection: close\r\nContent-Length: {body.Length}\r\n\r\n");
        await stream.WriteAsync(head, 0, head.Length).ConfigureAwait(false);
        await stream.WriteAsync(body, 0, body.Length).ConfigureAwait(false);
    }
}
