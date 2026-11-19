using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

// Patch only explicitly selected UTF-8 token ranges. In particular, never
// deserialize/reserialize the global CO table, inventories or other numbers.
internal static class ExactNativeRepair
{
    private const string Kind = "mercury-original-attachment-v1";
    private const string Metadata = "boneyard-turnover.state.json";
    private sealed class Range
    {
        internal required string Path;
        internal long Start, Length;
        internal required JToken Old, Next;
        internal byte[] Bytes => Encoding.UTF8.GetBytes(Next.ToString(Newtonsoft.Json.Formatting.None));
    }
    private sealed class Frame
    {
        internal required string Path;
        internal bool Array;
        internal string? Property;
        internal int Index;
        internal Range? Range;
    }
    private static string Hash(string path)
    {
        using var input = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(input));
    }
    private static string Pointer(string name) => name.Replace("~", "~0").Replace("/", "~1");
    private static Range[] Locate(string path, JObject[] changes)
    {
        var requested = changes.ToDictionary(x => (string)x["path"]!, x => x, StringComparer.Ordinal);
        var found = new Dictionary<string, Range>(StringComparer.Ordinal);
        var stack = new Stack<Frame>();
        byte[] buffer = new byte[128 * 1024];
        int count = 0;
        long offset = 0;
        bool final = false;
        var state = new JsonReaderState(new JsonReaderOptions { MaxDepth = 256 });
        using var input = File.OpenRead(path);
        string ValuePath()
        {
            if (stack.Count == 0) return "";
            var parent = stack.Peek();
            if (parent.Array) return parent.Path + "/" + parent.Index++;
            string key = parent.Property ?? throw new InvalidDataException("Object value lacks a property name.");
            parent.Property = null;
            return parent.Path + "/" + Pointer(key);
        }
        while (true)
        {
            if (!final && count < buffer.Length)
            {
                int read = input.Read(buffer, count, buffer.Length - count);
                if (read == 0) final = true;
                count += read;
            }
            var reader = new Utf8JsonReader(buffer.AsSpan(0, count), final, state);
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.PropertyName) { stack.Peek().Property = reader.GetString(); continue; }
                if (reader.TokenType == JsonTokenType.EndArray || reader.TokenType == JsonTokenType.EndObject)
                {
                    var ended = stack.Pop();
                    if (ended.Range != null) ended.Range.Length = offset + reader.BytesConsumed - ended.Range.Start;
                    continue;
                }
                string pointer = ValuePath();
                Range? target = null;
                if (requested.TryGetValue(pointer, out var change))
                {
                    if (found.ContainsKey(pointer)) throw new InvalidDataException("Duplicate target property: " + pointer);
                    target = new Range { Path = pointer, Start = offset + reader.TokenStartIndex,
                        Old = change["old"]!, Next = change["new"]! };
                    found.Add(pointer, target);
                }
                if (reader.TokenType == JsonTokenType.StartObject || reader.TokenType == JsonTokenType.StartArray)
                    stack.Push(new Frame { Path = pointer, Array = reader.TokenType == JsonTokenType.StartArray, Range = target });
                else if (target != null) target.Length = offset + reader.BytesConsumed - target.Start;
            }
            int consumed = checked((int)reader.BytesConsumed);
            state = reader.CurrentState;
            if (final)
            {
                if (consumed != count || stack.Count != 0) throw new InvalidDataException("Truncated native JSON.");
                break;
            }
            Buffer.BlockCopy(buffer, consumed, buffer, 0, count - consumed);
            count -= consumed; offset += consumed;
            if (count == buffer.Length)
            {
                if (buffer.Length >= 64 * 1024 * 1024) throw new InvalidDataException("Oversized native JSON token.");
                Array.Resize(ref buffer, buffer.Length * 2);
            }
        }
        if (found.Count != requested.Count) throw new InvalidDataException("One or more exact repair paths are absent.");
        var ranges = found.Values.OrderBy(x => x.Start).ToArray();
        long end = 0;
        foreach (var range in ranges)
        {
            if (range.Start < end || range.Length <= 0 || range.Length > 1024 * 1024) throw new InvalidDataException("Overlapping or oversized replacement ranges.");
            input.Position = range.Start;
            byte[] bytes = new byte[checked((int)range.Length)];
            input.ReadExactly(bytes);
            if (!JToken.DeepEquals(JToken.Parse(Encoding.UTF8.GetString(bytes)), range.Old))
                throw new InvalidDataException("Native value differs from the plan: " + range.Path);
            end = range.Start + range.Length;
        }
        return ranges;
    }
    private static void CopyCount(Stream input, Stream output, long count)
    {
        byte[] buffer = new byte[128 * 1024];
        while (count > 0)
        {
            int read = input.Read(buffer, 0, (int)Math.Min(count, buffer.Length));
            if (read == 0) throw new EndOfStreamException();
            output.Write(buffer, 0, read); count -= read;
        }
    }
    private static void Transform(string original, Stream output, Range[] ranges)
    {
        using var input = File.OpenRead(original);
        foreach (var range in ranges)
        {
            CopyCount(input, output, range.Start - input.Position);
            input.Position += range.Length;
            output.Write(range.Bytes);
        }
        input.CopyTo(output);
    }
    private static void EqualCount(Stream a, Stream b, long count)
    {
        byte[] left = new byte[128 * 1024], right = new byte[left.Length];
        while (count > 0)
        {
            int length = (int)Math.Min(count, left.Length);
            a.ReadExactly(left, 0, length); b.ReadExactly(right, 0, length);
            if (!left.AsSpan(0, length).SequenceEqual(right.AsSpan(0, length)))
                throw new InvalidDataException("Unapproved native bytes changed.");
            count -= length;
        }
    }
    private static void Verify(string original, Stream actual, Range[] ranges)
    {
        using var input = File.OpenRead(original);
        foreach (var range in ranges)
        {
            EqualCount(input, actual, range.Start - input.Position);
            input.Position += range.Length;
            using var expected = new MemoryStream(range.Bytes);
            EqualCount(expected, actual, expected.Length);
        }
        EqualCount(input, actual, input.Length - input.Position);
        if (actual.ReadByte() != -1) throw new InvalidDataException("Prepared native entry has unapproved trailing bytes.");
    }
    private static void Scope(JObject plan)
    {
        if ((string?)plan["repairKind"] != Kind || (bool?)plan["productionPlanAccepted"] != true)
            throw new InvalidDataException("Not an accepted Mercury plan.");
        var ids = plan["candidateIds"]!.Values<string>().ToHashSet(StringComparer.Ordinal);
        double age = (double?)plan["creditedHours"] ?? double.NaN;
        if (ids.Count == 0 || ids.Count > 12 || !double.IsFinite(age) || age < 120 || (double?)plan["assignedDays"] != 5)
            throw new InvalidDataException("Unexpected repair scope.");
        foreach (var change in plan["changes"]!.Cast<JObject>())
        {
            string id = (string)change["id"]!, entry = (string)change["entry"]!, path = (string)change["path"]!;
            if (!ids.Contains(id)) throw new InvalidDataException("Change lies outside the accepted cohort.");
            bool physical = entry == "ships/" + id + ".json";
            string key = path[(path.LastIndexOf('/') + 1)..];
            bool valid = physical
                ? Regex.IsMatch(path, "^/0/objSS/(boPORShip|vBOOffsetx|vBOOffsety|vPosx|vPosy|vVelX|vVelY|fOrbitW|fOrbitTimeLast|aPathRecentT|aPathRecentX|aPathRecentY)$")
                : entry == (string?)plan["rootEntry"] && Regex.IsMatch(path,
                    "^/0/objSystem/aStellarDerelicts/[0-9]+/(strParent|vBOOffsetx|vBOOffsety|fOrbitW|fOrbitTimeLast)$");
            if (!valid) throw new InvalidDataException("Unapproved native repair path: " + entry + path);
            var next = change["new"]!;
            if (key == "boPORShip" || key == "strParent") valid = (string?)next == "HQCH_DET";
            else if (key.StartsWith("aPathRecent", StringComparison.Ordinal)) valid = next is JArray array && array.Count == 0;
            else
            {
                valid = (next.Type == JTokenType.Float || next.Type == JTokenType.Integer) && double.IsFinite((double)next);
                if (key == "fOrbitW") valid &= (double)next == 0;
                if (key == "fOrbitTimeLast") valid &= (double)next == (double)plan["epoch"]!;
            }
            if (!valid) throw new InvalidDataException("Unexpected proposed value: " + path);
        }
    }
    internal static void Prepare(string source, string planPath, string output)
    {
        output = Path.GetFullPath(output);
        if (File.Exists(output) || File.Exists(output + ".report.json") || output.Equals(Path.GetFullPath(source), StringComparison.OrdinalIgnoreCase))
            throw new IOException("Output must be a new archive.");
        var plan = JObject.Parse(File.ReadAllText(planPath));
        Scope(plan);
        string sourceHash = Hash(source), planHash = Hash(planPath);
        if (sourceHash != (string?)plan["currentSourceSHA256"]) throw new IOException("Current save changed after planning.");
        var groups = plan["changes"]!.Cast<JObject>().GroupBy(x => (string)x["entry"]!).ToDictionary(x => x.Key, x => x.ToArray());
        string temporary = Path.Combine(Path.GetDirectoryName(output)!, "native-repair-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        var files = new Dictionary<string, string>();
        var ranges = new Dictionary<string, Range[]>();
        try
        {
            using var original = ZipFile.OpenRead(source);
            if (original.Entries.Select(x => x.FullName).Distinct().Count() != original.Entries.Count || original.GetEntry(Metadata) == null)
                throw new InvalidDataException("Native archive has duplicate entries or no existing turnover metadata.");
            foreach (var group in groups)
            {
                string file = Path.Combine(temporary, Guid.NewGuid().ToString("N"));
                files.Add(group.Key, file);
                using (var target = new FileStream(file, FileMode.CreateNew))
                using (var entry = (original.GetEntry(group.Key) ?? throw new InvalidDataException("Native entry absent.")).Open()) entry.CopyTo(target);
                ranges.Add(group.Key, Locate(file, group.Value));
            }
            byte[] state = Encoding.UTF8.GetBytes(plan["turnoverState"]!.ToString(Newtonsoft.Json.Formatting.None));
            using (var file = new FileStream(output, FileMode.CreateNew))
            using (var zip = new ZipArchive(file, ZipArchiveMode.Create))
            foreach (var entry in original.Entries)
            {
                var copy = zip.CreateEntry(entry.FullName, CompressionLevel.Optimal);
                copy.LastWriteTime = entry.LastWriteTime;
                using var target = copy.Open();
                if (entry.FullName == Metadata) target.Write(state);
                else if (files.TryGetValue(entry.FullName, out var native)) Transform(native, target, ranges[entry.FullName]);
                else { using var input = entry.Open(); input.CopyTo(target); }
            }
            int verified = 0;
            using (var copy = ZipFile.OpenRead(output))
            {
                if (copy.Entries.Count != original.Entries.Count) throw new InvalidDataException("Archive membership changed.");
                foreach (var entry in original.Entries)
                {
                    using var actual = (copy.GetEntry(entry.FullName) ?? throw new InvalidDataException("Missing prepared entry.")).Open();
                    if (entry.FullName == Metadata)
                    {
                        using var expected = new MemoryStream(state);
                        EqualCount(expected, actual, state.Length);
                        if (actual.ReadByte() != -1) throw new InvalidDataException("Unexpected turnover bytes.");
                        continue;
                    }
                    if (files.TryGetValue(entry.FullName, out var native)) Verify(native, actual, ranges[entry.FullName]);
                    else
                    {
                        using var input = entry.Open();
                        if (!SHA256.HashData(input).SequenceEqual(SHA256.HashData(actual))) throw new InvalidDataException("Unapproved native payload changed: " + entry.FullName);
                    }
                    verified++;
                }
            }
            if (Hash(source) != sourceHash || Hash(planPath) != planHash) throw new IOException("Source or plan changed during preparation.");
            var report = new JObject { ["repairKind"] = Kind, ["currentSourceSHA256"] = sourceHash, ["repairedSHA256"] = Hash(output),
                ["planSHA256"] = planHash, ["requiredTurnoverSHA256"] = plan["requiredTurnoverSHA256"],
                ["originalsUnchanged"] = true, ["productionReaderAccepted"] = false, ["exactNativeChangesVerified"] = true,
                ["nativePayloadsHashVerified"] = verified, ["changedNativePayloads"] = files.Count,
                ["unchangedNativePayloads"] = verified - files.Count, ["changedNativeEntries"] = new JArray(files.Keys),
                ["approvedNativePropertyChanges"] = plan["changes"], ["candidateIds"] = plan["candidateIds"], ["excluded"] = plan["excluded"],
                ["creditedHours"] = plan["creditedHours"], ["changedEntry"] = Metadata, ["installed"] = false,
                ["verification"] = "Unchanged entries SHA-256 matched. Changed native entries matched byte for byte outside the exact approved UTF-8 token ranges. Production native loading verification is still required." };
            File.WriteAllText(output + ".report.json", report.ToString());
            Console.WriteLine("Prepared copy: " + files.Count + " native entries changed only at approved tokens; " + (verified - files.Count) + " native entries identical. Native reader verification pending.");
        }
        finally
        {
            foreach (string file in files.Values) if (File.Exists(file)) File.Delete(file);
            Directory.Delete(temporary);
        }
    }

    internal static void Test()
    {
        string directory = Path.Combine(Path.GetTempPath(), "exact-native-repair-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string source = Path.Combine(directory, "source.json");
        int checks = 0;
        void Check(bool value, string message) { checks++; if (!value) throw new InvalidDataException(message); }
        bool Refuses(Action action) { try { action(); return false; } catch (InvalidDataException) { return true; } catch (System.Text.Json.JsonException) { return true; } }
        try
        {
            string prefix = "[{\"other\":{\"\":1.000000000000000000001,\"unicode\":\"\u03bb" + new string('x', 150000) + "\"},\"objSS\":{\"boPORShip\":\"Mercury\",\"vBOOffsetx\":1.1234567890123456e-7,\"aPathRecentT\":[1,2,3]}}]";
            File.WriteAllText(source, prefix, new UTF8Encoding(false));
            JObject Change(string path, JToken old, JToken next) => new JObject { ["path"] = path, ["old"] = old, ["new"] = next };
            var changes = new[] { Change("/0/objSS/boPORShip", "Mercury", "HQCH_DET"), Change("/0/objSS/aPathRecentT", new JArray(1, 2, 3), new JArray()) };
            var ranges = Locate(source, changes);
            using var prepared = new MemoryStream(); Transform(source, prepared, ranges);
            prepared.Position = 0; Verify(source, prepared, ranges);
            string actual = Encoding.UTF8.GetString(prepared.ToArray());
            Check(actual == prefix.Replace("\"Mercury\"", "\"HQCH_DET\"").Replace("[1,2,3]", "[]"), "Unknown numeric precision or UTF-8 bytes changed.");
            Check(File.ReadAllText(source) == prefix, "Original was modified.");
            Check(Refuses(() => Locate(source, new[] { Change("/0/objSS/boPORShip", "wrong", "HQCH_DET") })), "Wrong original value accepted.");
            Check(Refuses(() => Locate(source, new[] { Change("/0/absent", 0, 1) })), "Absent property accepted.");
            byte[] altered = prepared.ToArray(); altered[12] ^= 1;
            Check(Refuses(() => Verify(source, new MemoryStream(altered), ranges)), "Unapproved byte mutation accepted.");
            File.WriteAllText(source, "[{\"objSS\":{\"boPORShip\":\"Mercury\",\"boPORShip\":\"Mercury\"}}]");
            Check(Refuses(() => Locate(source, changes.Take(1).ToArray())), "Duplicate target accepted.");
            File.WriteAllText(source, "[{\"objSS\":{\"boPORShip\":\"Mercury\"");
            Check(Refuses(() => Locate(source, changes.Take(1).ToArray())), "Truncated JSON accepted.");
            var invalidPlan = new JObject { ["repairKind"] = Kind, ["productionPlanAccepted"] = true, ["candidateIds"] = new JArray("E-XMH"),
                ["creditedHours"] = 120, ["assignedDays"] = 5, ["changes"] = new JArray(new JObject { ["id"] = "E-XMH", ["entry"] = "ships/E-XMH.json",
                    ["path"] = "/0/aItems", ["old"] = new JArray(), ["new"] = new JArray() }) };
            Check(Refuses(() => Scope(invalidPlan)), "Inventory edit accepted by narrow repair scope.");
            Console.WriteLine(checks + " exact JSON repair checks passed, including unknown precision, long UTF-8 tokens, protected byte spans and refusal controls.");
        }
        finally { if (File.Exists(source)) File.Delete(source); Directory.Delete(directory); }
    }
}
