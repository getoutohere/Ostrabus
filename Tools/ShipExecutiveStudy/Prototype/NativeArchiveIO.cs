using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Ionic.Zip;
using LitJson;
using Ostranauts.Core.Models;

namespace ShipExecutivePrototype;

// Preserve native wire data until validation. Native LitJson normalizes some
// empty collections to null on read; hashing reconstructed DTOs would reject a
// genuine native archive. Both identity validation and DTO parsing use these
// same immutable source strings, not separately supplied world/metadata files.
sealed class NativeArchiveImage
{
    readonly Dictionary<string, string> entries;
    internal NativeArchiveImage(Dictionary<string, string> entries)
    {
        this.entries = new Dictionary<string, string>(entries);
    }
    internal SaveDto Read()
    {
        var restored = new SaveDto();
        foreach (var entry in entries)
            if (entry.Key.StartsWith("ships/", StringComparison.Ordinal))
                restored.AddShip(JsonMapper.ToObject<JsonShip[]>(entry.Value).Single(), entry.Key);
            else
                restored.AddGameSave(JsonMapper.ToObject<JsonGameSave[]>(entry.Value).Single(), entry.Key);
        return restored;
    }
    internal JObject Content()
    {
        var game = (JObject)JArray.Parse(entries.Single(e => !e.Key.StartsWith("ships/", StringComparison.Ordinal)).Value).Single();
        var ships = new JArray(entries.Where(e => e.Key.StartsWith("ships/", StringComparison.Ordinal)).Select(e => JArray.Parse(e.Value).Single()));
        return NativeSavePort.Content(game, ships);
    }
}

static class NativeArchiveIO
{
    internal static int ThreadedWrites, SynchronousWrites;
    internal static string Folder() => "obj/ShipExecutiveNativeSaves/" + Guid.NewGuid().ToString("N") + "/";

    internal static NativeArchiveImage RoundTrip(SaveDto dto, string folder, bool threaded)
    {
        string target = Path.GetFullPath(folder);
        string allowed = Path.GetFullPath("obj/ShipExecutiveNativeSaves") + Path.DirectorySeparatorChar;
        if (!target.StartsWith(allowed, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Offline archive escaped its workspace directory");
        Directory.CreateDirectory(target);
        dto.filepath = folder;
        dto.persistenpath = Path.GetFullPath(".");
        dto.AddSaveInfo(new JsonSaveInfo { strName = "offline-executive" }, folder + "saveInfo.json");
        var job = new SavingJob { SaveDto = dto };
        if (threaded)
        {
            job.Start();
            if (!job._thread.Join(10000))
                throw new TimeoutException("Native offline save worker did not finish");
        }
        else
            job.StartWithoutThreading();
        if (job.Exception != null)
            throw new InvalidOperationException("Native save writer failed", job.Exception);
        if (!job.IsDone)
            throw new InvalidOperationException("Native save writer did not complete");
        if (threaded)
            ThreadedWrites++;
        else
            SynchronousWrites++;
        var entries = new Dictionary<string, string>();
        string archive = Path.Combine(target, new DirectoryInfo(target).Name + ".zip");
        using (var zip = ZipFile.Read(archive))
        {
            foreach (var entry in zip.Entries.Where(e => e.FileName.EndsWith(".json", StringComparison.Ordinal)))
            {
                using (var stream = new MemoryStream())
                {
                    entry.Extract(stream);
                    string json = Encoding.UTF8.GetString(stream.ToArray());
                    if (entry.FileName != "saveInfo.json")
                        entries.Add(entry.FileName, json);
                }
            }
        }
        return new NativeArchiveImage(entries);
    }
}
