using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using HarmonyLib;
using Ionic.Zip;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Ostranauts.Core.Models;

internal static partial class Program
{
    static readonly ConditionalWeakTable<SaveDto, byte[]> frozenRiskState = new ConditionalWeakTable<SaveDto, byte[]>();
    static string failZipPath;
    static bool failOptionalMetadata;
    static int metadataWriterThread, metadataWriteFailures;

    static bool BeforeRiskZipSave(string __0)
    {
        if (failZipPath != null && Path.GetFullPath(__0) == failZipPath) throw new IOException("Offline controlled pre-commit failure.");
        return true;
    }
    static void WriteFrozenRiskMetadata(SavingJob __instance, ZipFile zip)
    {
        metadataWriterThread = Thread.CurrentThread.ManagedThreadId;
        try
        {
            if (failOptionalMetadata) throw new IOException("Offline optional metadata failure.");
            if (frozenRiskState.TryGetValue(__instance.SaveDto, out var bytes) && zip[MetadataEntry] == null)
                zip.AddEntry(MetadataEntry, bytes);
        }
        catch { metadataWriteFailures++; } // Metadata cannot fail the native save.
    }
    static void CaptureRiskMetadata(SaveDto dto, JObject live)
    {
        frozenRiskState.Remove(dto);
        frozenRiskState.Add(dto, Encoding.UTF8.GetBytes(live.ToString(Formatting.None)));
    }
    static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    static JObject ReadRiskMetadata(byte[] bytes, double nativeEpoch, string nativeWorld)
    {
        if (bytes == null || bytes.Length > 1024 * 1024) return null;
        try
        {
            using var input = new JsonTextReader(new StringReader(Encoding.UTF8.GetString(bytes))) { MaxDepth = 16, DateParseHandling = DateParseHandling.None };
            var state = JObject.Load(input);
            if ((int?)state["schema"] != 1 || (string)state["world"] != nativeWorld ||
                (double?)state["epoch"] != nativeEpoch || !Finite(nativeEpoch) || state["away"]?.Type != JTokenType.Boolean) return null;
            double departure = (double)state["departure"];
            if (!Finite(departure) || departure > nativeEpoch || !CurvePrototype.Valid((double)state["halfLife"]) ||
                !CurvePrototype.Valid((double)state["shape"])) return null;
            if ((bool)state["away"] && string.IsNullOrEmpty((string)state["trip"])) return null;
            if (!(state["members"] is JArray members) || members.Count > 4096) return null;
            var ids = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
            foreach (var member in members)
            {
                if (!(member is JObject)) return null;
                string id = (string)member["id"];
                double age = (double)member["age"];
                if (string.IsNullOrEmpty(id) || id.Length > 256 || !ids.Add(id) || !Finite(age) || age < 0) return null;
            }
            return state;
        }
        catch (Exception error) when (error is JsonException || error is InvalidCastException || error is FormatException ||
            error is ArgumentException || error is InvalidOperationException || error is OverflowException)
        {
            return null;
        }
    }
    static byte[] RiskMetadataBytes(string archive)
    {
        using var zip = ZipFile.Read(archive);
        var entry = zip[MetadataEntry];
        if (entry == null) return null;
        using var data = new MemoryStream();
        entry.Extract(data);
        return data.ToArray();
    }
    static bool ConsumeRiskReturn(JObject field)
    {
        if (!(bool)field["away"]) return false;
        field["away"] = false;
        field["trip"] = "";
        return true;
    }
    static SaveDto RiskSave(string work, string slot)
    {
        string root = Path.GetFullPath(work).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string folder = Path.GetFullPath(Path.Combine(root, slot)).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        // Native cancellation recursively deletes this folder. Verify that its
        // resolved absolute path is inside this explicitly named private fixture.
        if (!folder.StartsWith(root, StringComparison.OrdinalIgnoreCase) || folder == root)
            throw new InvalidOperationException("Unsafe native cancellation fixture path.");
        Directory.CreateDirectory(folder);
        var dto = new SaveDto { filepath = folder, persistenpath = work };
        dto.AddGameSave(new JsonGameSave { strName = "risk-player", objSystem = new JsonStarSystemSave { dfEpoch = 1000 } }, folder + "risk.json");
        dto.AddSaveInfo(new JsonSaveInfo { strName = slot, playerName = "risk", seedId = "risk-world" }, folder + "saveInfo.json");
        return dto;
    }
    static string RiskArchive(SaveDto dto) => Path.Combine(dto.filepath, new DirectoryInfo(dto.filepath).Name + ".zip");
    static string HashRiskFile(string path)
    {
        using var input = File.OpenRead(path);
        using var hash = SHA256.Create();
        return BitConverter.ToString(hash.ComputeHash(input));
    }
    static void MetadataRiskChecks(Harmony h, string work)
    {
        var write = AccessTools.Method(typeof(SavingJob), "WriteSaveImagesToZip");
        h.Patch(write, postfix: new HarmonyMethod(typeof(Program), nameof(WriteFrozenRiskMetadata)));
        h.Patch(AccessTools.Method(typeof(ZipFile), nameof(ZipFile.Save), new[] { typeof(string) }),
            prefix: new HarmonyMethod(typeof(Program), nameof(BeforeRiskZipSave)));
        var state = JObject.Parse("{schema:1,world:'risk-world',epoch:1000,away:true,departure:900,trip:'first-trip',halfLife:7,shape:1,members:[{id:'old-wreck',age:2}]}");
        var dto = RiskSave(work, "risk-committed");
        CaptureRiskMetadata(dto, state);
        int captureThread = Thread.CurrentThread.ManagedThreadId;
        state["trip"] = "later-live-trip";
        state["halfLife"] = 99;
        state["members"][0]["id"] = "later-live-id";
        var job = new SavingJob { SaveDto = dto };
        job.Start();
        job.JoinThread();
        Check(job.IsDone && job.Exception == null && metadataWriterThread != captureThread, "Actual worker-thread metadata save failed.");
        string archive = RiskArchive(dto);
        byte[] committed = RiskMetadataBytes(archive);
        var loaded = ReadRiskMetadata(committed, 1000, "risk-world");
        Check(loaded != null && (string)loaded["trip"] == "first-trip" && (double)loaded["halfLife"] == 7 &&
            (string)loaded["members"][0]["id"] == "old-wreck", "Worker metadata read the changed live state instead of immutable snapshot bytes.");
        Check(ConsumeRiskReturn(loaded) && !ConsumeRiskReturn(loaded), "Repeated entry applied the same prototype departure twice.");
        var after = Encoding.UTF8.GetBytes(loaded.ToString(Formatting.None));
        Check(!ConsumeRiskReturn(ReadRiskMetadata(after, 1000, "risk-world")), "After-return metadata replayed its completed trip.");
        Check(ConsumeRiskReturn(ReadRiskMetadata(committed, 1000, "risk-world")), "Rollback to the earlier snapshot failed to restore its earlier pending trip.");

        foreach (string scenario in new[] { "world", "epoch", "schema", "future", "duplicate", "age", "shape", "trip", "truncated", "oversize", "missing", "member-type", "age-type", "schema-overflow", "world-type" })
        {
            var corrupt = JObject.Parse(Encoding.UTF8.GetString(committed));
            if (scenario == "world") corrupt["world"] = "other-save";
            if (scenario == "epoch") corrupt["epoch"] = 1001;
            if (scenario == "schema") corrupt["schema"] = 99;
            if (scenario == "future") corrupt["departure"] = 2000;
            if (scenario == "duplicate") ((JArray)corrupt["members"]).Add(corrupt["members"][0].DeepClone());
            if (scenario == "age") corrupt["members"][0]["age"] = -1;
            if (scenario == "shape") corrupt["shape"] = 0;
            if (scenario == "trip") corrupt["trip"] = "";
            if (scenario == "missing") corrupt.Remove("departure");
            if (scenario == "member-type") corrupt["members"] = new JArray(1);
            if (scenario == "age-type") corrupt["members"][0]["age"] = "not a number";
            if (scenario == "schema-overflow") corrupt["schema"] = long.MaxValue;
            if (scenario == "world-type") corrupt["world"] = new JObject();
            byte[] bytes = scenario == "truncated" ? new byte[] { (byte)'{' } :
                scenario == "oversize" ? new byte[1024 * 1024 + 1] : Encoding.UTF8.GetBytes(corrupt.ToString(Formatting.None));
            Check(ReadRiskMetadata(bytes, 1000, "risk-world") == null, "Invalid/cross-save metadata was admitted: " + scenario);
        }
        Check(ReadRiskMetadata(null, 1000, "risk-world") == null, "Missing metadata invented an absence.");

        string before = HashRiskFile(archive);
        var next = JObject.Parse(Encoding.UTF8.GetString(committed));
        next["trip"] = "new-uncommitted-trip";
        CaptureRiskMetadata(dto, next);
        failZipPath = Path.GetFullPath(archive);
        job.StartWithoutThreading();
        failZipPath = null;
        Check(job.Exception is IOException && HashRiskFile(archive) == before,
            "Controlled pre-commit native failure changed the previously committed save/metadata.");

        var cancelledDto = RiskSave(work, "risk-cancelled");
        CaptureRiskMetadata(cancelledDto, next);
        var cancelled = new SavingJob { SaveDto = cancelledDto };
        Field(cancelled, "_stop", true); // Same flag set by native AbortSafe on a running job.
        cancelled.StartWithoutThreading();
        Check(cancelled.IsDone && cancelled.WasCancelled && !Directory.Exists(cancelledDto.filepath) && HashRiskFile(archive) == before,
            "Native cancellation published metadata or altered the unrelated committed fixture.");

        var optionalDto = RiskSave(work, "risk-optional-failure");
        CaptureRiskMetadata(optionalDto, next);
        var optional = new SavingJob { SaveDto = optionalDto };
        failOptionalMetadata = true;
        optional.StartWithoutThreading();
        failOptionalMetadata = false;
        Check(optional.Exception == null && metadataWriteFailures > 0 && RiskMetadataBytes(RiskArchive(optionalDto)) == null,
            "Optional metadata failure prevented a native save or preserved stale metadata.");
        Console.WriteLine("Metadata risk prototype: frozen bytes survive actual worker saving; rollback/completed-trip replay, cross-save/corrupt data, pre-commit I/O failure, native cancellation and optional-write failure controls pass.");
    }
}
