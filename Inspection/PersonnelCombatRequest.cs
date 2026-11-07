using System;
using System.Globalization;

namespace Ostrabus.Inspection;

internal static class PersonnelCombatRequest {
    internal static bool IsControl(string path) => path == "/combat/personnel/trace/start" || path == "/combat/personnel/trace/stop";
    internal static bool Parse(InspectionHttp http, out string coId, out int seconds) {
        coId = null; seconds = 120;
        if (http.Url.AbsolutePath.TrimEnd('/') != "/combat/personnel/trace/start") return true;
        string encoded = http.QueryString["coId"], duration = http.QueryString["seconds"];
        if (encoded != null) {
            try { coId = Uri.UnescapeDataString(encoded); }
            catch (UriFormatException) { return false; }
            if (coId.Length == 0 || coId.Length > 64) return false;
            foreach (char c in coId) if (char.IsControl(c)) return false;
        }
        return duration == null || (int.TryParse(duration, NumberStyles.None, CultureInfo.InvariantCulture, out seconds) && seconds >= 1 && seconds <= 300);
    }
}
