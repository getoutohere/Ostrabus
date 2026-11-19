using System;
using System.Globalization;
using Ostrabus.Diagnostics.Obituaries;

namespace Ostrabus.Inspection;

internal static class ObituaryInspection {
    internal static InspectionApi.Reply Read(InspectionHttp request) {
        return Read(request.Url.AbsolutePath.TrimEnd('/'), request.QueryString);
    }
    internal static InspectionApi.Reply Read(string path, System.Collections.Specialized.NameValueCollection query) {
        if (path == "/obituaries") return new InspectionApi.Reply(200, ObituaryRecorder.Status());
        string Decode(string text) => text == null ? null : Uri.UnescapeDataString(text);
        string npc = Decode(query["coId"]), ship = Decode(query["shipId"]), kind = Decode(query["kind"]), session = Decode(query["session"]);
        bool Text(string value) => value == null || value.Length <= 128 && value.IndexOfAny(new[] { '\r', '\n', '\0' }) < 0;
        bool Number(string value, out double? parsed) {
            parsed = null;
            if (value == null) return true;
            if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double number) || double.IsNaN(number) || double.IsInfinity(number)) return false;
            parsed = number; return true;
        }
        if (!Text(npc) || !Text(ship) || !Text(kind) || !Text(session) ||
            !long.TryParse(query["since"] ?? "0", NumberStyles.None, CultureInfo.InvariantCulture, out long since) ||
            !int.TryParse(query["limit"] ?? "25", NumberStyles.None, CultureInfo.InvariantCulture, out int limit) || limit < 1 || limit > 50 ||
            !Number(query["afterEpoch"], out var after) || !Number(query["beforeEpoch"], out var before) ||
            after.HasValue && before.HasValue && after.Value > before.Value || since > 0 && session == null)
            return new InspectionApi.Reply(400, SnapshotJson.Map("error", "Invalid obituary filter. limit: 1..50; finite epoch range; nonnegative since requires session; text filters <=128 characters."));
        if (ObituaryRecorder.Store == null) return new InspectionApi.Reply(200, SnapshotJson.Map("events", new object[0], "status", ObituaryRecorder.Status()));
        return new InspectionApi.Reply(200, ObituaryRecorder.Store.Read(npc, ship, kind, session, since, limit, after, before));
    }
}
