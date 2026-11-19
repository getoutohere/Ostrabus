using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using Ionic.Zip;
using Newtonsoft.Json.Linq;
using Ostranauts.Core.Models;

namespace Ostrabus.BoneyardTurnover
{
    internal static class Persistence
    {
        private static readonly ConditionalWeakTable<SaveDto, byte[]> Frozen = new ConditionalWeakTable<SaveDto, byte[]>();
        private static bool loading;
        private static TurnoverState pending;

        internal static void ResetLoad() { loading = true; pending = null; Runtime.Reset(); }
        internal static TurnoverState TakeLoaded()
        {
            var result = loading && Plugin.Enabled ? pending : null;
            loading = false; pending = null;
            return result;
        }

        internal static void Read(string file, Dictionary<string, byte[]> files, JsonGameSave game)
        {
            if (!loading || game == null) return;
            pending = null;
            byte[] bytes = ReadFile(file, files, StateCodec.Entry);
            if (bytes == null) return;
            byte[] infoBytes = ReadFile(file, files, "saveInfo.json");
            if (infoBytes == null) return;
            var info = JArray.Parse(Encoding.UTF8.GetString(infoBytes));
            string seed = info.Count == 1 ? (string)info[0]["seedId"] : null;
            pending = StateCodec.Read(bytes, game, seed);
            if (pending == null) Plugin.Warn("Ignored mismatched or invalid turnover metadata; future departures start fresh.");
        }

        private static byte[] ReadFile(string file, Dictionary<string, byte[]> files, string entry)
        {
            if (files != null)
            {
                // Native memory loading uses archive-relative keys.
                if (!files.TryGetValue(entry, out var bytes)) return null;
                return bytes != null && bytes.Length <= StateCodec.MaxBytes ? bytes : null;
            }
            string path = Path.Combine(Path.GetDirectoryName(file) ?? "", entry);
            if (!File.Exists(path) || new FileInfo(path).Length > StateCodec.MaxBytes) return null;
            return File.ReadAllBytes(path);
        }

        // Called on the main thread immediately before a native save job starts.
        // Only immutable bytes travel into its background ZIP writer.
        internal static void Capture(SaveDto dto)
        {
            if (dto == null) return;
            Frozen.Remove(dto);
            var game = dto.jGameSave?.Item2;
            var info = dto.jSaveInfo?.Item2;
            if (!Runtime.Active || game?.objSystem == null || info == null || string.IsNullOrEmpty(info.seedId) ||
                game.objSystem.dfEpoch != StarSystem.fEpoch || game.objAIShipManager?.strATCLast != Runtime.State.Region) return;
            var state = Runtime.State;
            state.World = info.seedId; state.Player = game.strPlayerCO; state.Epoch = game.objSystem.dfEpoch;
            state.StartEpoch = game.objSystem.dfEpochGameStart;
            byte[] bytes = StateCodec.Write(state);
            if (bytes.Length > StateCodec.MaxBytes) { Plugin.Warn("Turnover metadata exceeded its limit; omitted from this save."); return; }
            Frozen.Add(dto, bytes);
        }

        internal static void Write(SavingJob job, ZipFile zip)
        {
            if (job?.SaveDto != null && Frozen.TryGetValue(job.SaveDto, out var bytes) && zip[StateCodec.Entry] == null)
                zip.AddEntry(StateCodec.Entry, bytes);
        }
    }
}
