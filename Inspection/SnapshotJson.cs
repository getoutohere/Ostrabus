using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Ostrabus.Inspection;

// Deliberately accepts only detached snapshot data; never reflects over objects.
internal static class SnapshotJson {
    internal static Dictionary<string, object> Map(params object[] pairs) {
        var result = new Dictionary<string, object>();
        for (int i = 0; i < pairs.Length; i += 2) {
            object value = pairs[i + 1];
            if (value is string text && text.Length > 1024) { value = text.Substring(0, 1024) + "[truncated]"; }
            result.Add((string)pairs[i], value);
        }
        return result;
    }

    internal static string Encode(object value) {
        var output = new StringBuilder();
        Write(output, value);
        return output.ToString();
    }

    static void Quote(StringBuilder output, string value) {
        output.Append('"');
        foreach (char c in value) {
            if (c == '"' || c == '\\') { output.Append('\\').Append(c); }
            else if (c < 32 || char.IsSurrogate(c)) { output.Append("\\u").Append(((int)c).ToString("x4")); }
            else { output.Append(c); }
        }
        output.Append('"');
    }

    static void Write(StringBuilder output, object value) {
        if (value == null) { output.Append("null"); return; }
        if (value is string text) { Quote(output, text); return; }
        if (value is bool flag) { output.Append(flag ? "true" : "false"); return; }
        if (value is double number) {
            if (double.IsNaN(number) || double.IsInfinity(number)) { Quote(output, number.ToString(CultureInfo.InvariantCulture)); }
            else { output.Append(number.ToString("R", CultureInfo.InvariantCulture)); }
            return;
        }
        if (value is float single) { Write(output, (double)single); return; }
        if (value is int || value is long) { output.Append(Convert.ToString(value, CultureInfo.InvariantCulture)); return; }
        bool first = true;
        if (value is Dictionary<string, object> map) {
            output.Append('{');
            foreach (var pair in map) {
                if (!first) { output.Append(','); } first = false;
                Quote(output, pair.Key); output.Append(':'); Write(output, pair.Value);
            }
            output.Append('}'); return;
        }
        if (value is IList list) {
            output.Append('[');
            foreach (object item in list) {
                if (!first) { output.Append(','); } first = false;
                Write(output, item);
            }
            output.Append(']'); return;
        }
        throw new InvalidOperationException("Snapshot contains a non-primitive value.");
    }
}
