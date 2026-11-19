using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using HarmonyLib;
using Ionic.Zip;
using Newtonsoft.Json.Linq;
using Ostrabus.BoneyardTurnover;
using Ostranauts.Core.Models;
using BTRuntime = Ostrabus.BoneyardTurnover.Runtime;

internal static partial class Program
{
    private static void FractionalTimestampChecks(Harmony harmony, string work)
    {
        // These clock values reproduce the real "ai" save. Native LitJson
        // stores 65635026132.7693; Newtonsoft preserves 65635026132.769279.
        const double epoch = 65635026132.769279;
        const double start = 65627498854.1278;
        ProductionField();
        StarSystem.fEpoch = epoch;
        AIShipManager.strATCLast = "MSUZ";
        BTRuntime.Loaded(null);
        BTRuntime.State.Fields.Add(new FieldState
        {
            Key = "EJDR|EJDR|RandomDerelictEJDR_GEO|EJDR_GEO", Region = "EJDR", Definition = "fixture-definition",
            Away = true, Departure = epoch - 2 * 86400, HalfLife = 7, Shape = 1, Seed = 123,
            Ages = new Dictionary<string, double> { ["BT-SURVIVOR"] = 4 }
        });
        var dto = RiskSave(work, "fractional-timestamp");
        dto.jGameSave.Item2.objSystem.dfEpoch = epoch;
        dto.jGameSave.Item2.objSystem.dfEpochGameStart = start;
        dto.jGameSave.Item2.strPlayerCO = "Ayden Kelly";
        dto.jGameSave.Item2.objAIShipManager = new JsonAIShipManagerSave { strATCLast = "MSUZ" };
        harmony.CreateClassProcessor(typeof(SaveStartHook)).Patch();
        harmony.CreateClassProcessor(typeof(SaveWriteHook)).Patch();
        try
        {
            var job = new SavingJob { SaveDto = dto };
            job.StartWithoutThreading();
            Check(job.Exception == null, "Fractional-clock native saving failed: " + job.Exception);
            using var zip = ZipFile.Read(RiskArchive(dto));
            using var nativeBytes = new MemoryStream();
            zip["risk.json"].Extract(nativeBytes);
            var loaded = LitJson.JsonMapper.ToObject<JsonGameSave[]>(Encoding.UTF8.GetString(nativeBytes.ToArray()))[0];
            Check(loaded.objSystem.dfEpoch != epoch && loaded.objSystem.dfEpoch == 65635026132.7693,
                "Native writer no longer reproduces the real save's clock rounding.");
            byte[] metadata = RiskMetadataBytes(RiskArchive(dto));
            var restored = StateCodec.Read(metadata, loaded, "risk-world");
            Check(restored != null, "Native timestamp rounding discarded valid departure metadata.");
            Check(restored.Fields.Count == 1 && restored.Fields[0].Away && restored.Fields[0].Ages["BT-SURVIVOR"] == 4 &&
                restored.Fields[0].Departure == epoch - 2 * 86400, "Fractional-clock restore changed pending departure state.");

            // Keep the metadata format and its original precision. In particular,
            // this reader must accept already-written 0.1.0 metadata unchanged.
            Check((double)JObject.Parse(Encoding.UTF8.GetString(metadata))["Epoch"] == epoch,
                "Metadata was rewritten to conceal the native precision mismatch.");
            var files = new Dictionary<string, byte[]> { [StateCodec.Entry] = metadata };
            using var info = new MemoryStream();
            zip["saveInfo.json"].Extract(info);
            files["saveInfo.json"] = info.ToArray();
            Persistence.ResetLoad();
            Persistence.Read("risk.json", files, loaded);
            BTRuntime.Loaded(Persistence.TakeLoaded());
            Check(BTRuntime.State.Fields.Count == 1 && BTRuntime.State.Fields[0].Away,
                "Production memory loading discarded a valid fractional-clock departure.");
            BTRuntime.Enter("EJDR");
            Check(BTRuntime.State.Fields[0].Away, "Reload itself consumed the pending return.");

            var culture = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
                Check(StateCodec.Read(metadata, loaded, "risk-world") != null, "Timestamp matching depends on system culture.");
            }
            finally { CultureInfo.CurrentCulture = culture; }

            loaded.objSystem.dfEpoch += 1;
            Check(StateCodec.Read(metadata, loaded, "risk-world") == null, "A genuinely different native save epoch was admitted.");
            loaded.objSystem.dfEpoch -= 1;
            loaded.objSystem.dfEpochGameStart += 1;
            Check(StateCodec.Read(metadata, loaded, "risk-world") == null, "A genuinely different world-start epoch was admitted.");
            loaded.objSystem.dfEpochGameStart -= 1;
            loaded.strPlayerCO = "different-player";
            Check(StateCodec.Read(metadata, loaded, "risk-world") == null, "A different player was admitted.");
            loaded.strPlayerCO = "Ayden Kelly";
            loaded.objAIShipManager.strATCLast = "different-region";
            Check(StateCodec.Read(metadata, loaded, "risk-world") == null, "A different native region was admitted.");
            loaded.objAIShipManager.strATCLast = "MSUZ";
            Check(StateCodec.Read(metadata, loaded, "different-world") == null, "A different world was admitted.");

            var state = JObject.Parse(Encoding.UTF8.GetString(metadata));
            const double preciseStart = 65627498854.127823;
            state["StartEpoch"] = preciseStart;
            var nativeStart = LitJson.JsonMapper.ToObject<JsonStarSystemSave>(
                LitJson.JsonMapper.ToJson(new JsonStarSystemSave { dfEpochGameStart = preciseStart })).dfEpochGameStart;
            loaded.objSystem.dfEpochGameStart = nativeStart;
            Check(nativeStart != preciseStart && StateCodec.Read(Encoding.UTF8.GetBytes(state.ToString()), loaded, "risk-world") != null,
                "Native rounding of the world-start clock still discards valid metadata.");
            loaded.objSystem.dfEpoch = double.NaN;
            Check(StateCodec.Read(metadata, loaded, "risk-world") == null, "Non-finite native clock was admitted.");
            RoundedDownResave(work);
            Console.WriteLine("Fractional timestamps: real-save clock reproduced through native SavingJob/LitJson; pending tracking restored, native precision/culture handled, different save identities still rejected.");
        }
        finally
        {
            harmony.Unpatch(AccessTools.Method(typeof(SavingJob), nameof(SavingJob.Start)), HarmonyPatchType.All, harmony.Id);
            harmony.Unpatch(AccessTools.Method(typeof(SavingJob), nameof(SavingJob.StartWithoutThreading)), HarmonyPatchType.All, harmony.Id);
            harmony.Unpatch(AccessTools.Method(typeof(SavingJob), "WriteSaveImagesToZip"), HarmonyPatchType.All, harmony.Id);
            BTRuntime.Reset();
        }
    }

    private static JsonGameSave ReadNativeFixture(SaveDto dto)
    {
        using var zip = ZipFile.Read(RiskArchive(dto));
        using var bytes = new MemoryStream();
        zip["risk.json"].Extract(bytes);
        return LitJson.JsonMapper.ToObject<JsonGameSave[]>(Encoding.UTF8.GetString(bytes.ToArray()))[0];
    }

    private static void RoundedDownResave(string work)
    {
        const double original = 65635026132.769333;
        const double start = 65627498854.1278;
        StarSystem.fEpoch = original;
        BTRuntime.State.Fields[0].Departure = original;
        var dto = RiskSave(work, "fractional-round-down");
        dto.jGameSave.Item2.objSystem.dfEpoch = original;
        dto.jGameSave.Item2.objSystem.dfEpochGameStart = start;
        dto.jGameSave.Item2.objAIShipManager = new JsonAIShipManagerSave { strATCLast = "MSUZ" };
        dto.jGameSave.Item2.strPlayerCO = "Ayden Kelly";
        var job = new SavingJob { SaveDto = dto };
        job.StartWithoutThreading();
        Check(job.Exception == null, "Native round-down save failed.");
        var game = ReadNativeFixture(dto);
        Check(game.objSystem.dfEpoch < original, "Fixture did not round the native clock downward.");
        var restored = StateCodec.Read(RiskMetadataBytes(RiskArchive(dto)), game, "risk-world");
        Check(restored != null && restored.Fields[0].Away, "Rounded-down native clock discarded pending tracking.");
        BTRuntime.Loaded(restored);
        StarSystem.fEpoch = game.objSystem.dfEpoch;

        // Resaving immediately after reload captures the lower native clock,
        // while departure retains its original high-precision metadata value.
        var resave = RiskSave(work, "fractional-immediate-resave");
        resave.jGameSave.Item2.objSystem = game.objSystem;
        resave.jGameSave.Item2.objAIShipManager = game.objAIShipManager;
        resave.jGameSave.Item2.strPlayerCO = game.strPlayerCO;
        job = new SavingJob { SaveDto = resave };
        job.StartWithoutThreading();
        Check(job.Exception == null, "Immediate native resave failed.");
        var reloaded = StateCodec.Read(RiskMetadataBytes(RiskArchive(resave)), ReadNativeFixture(resave), "risk-world");
        Check(reloaded != null && reloaded.Fields[0].Departure > reloaded.Epoch && reloaded.Fields[0].Away,
            "Same native instant was rejected as a future departure after resaving.");
        reloaded.Fields[0].Departure = reloaded.Epoch + 1;
        Check(StateCodec.Read(StateCodec.Write(reloaded), ReadNativeFixture(resave), "risk-world") == null,
            "A genuinely future departure was admitted.");

        var field = ProductionField();
        StarSystem.fEpoch = game.objSystem.dfEpoch;
        field.Anchor.dTimeCalcLast = field.Anchor.dVelCalcLast = StarSystem.fEpoch;
        var candidate = ProductionDeferred("BT-ROUND-DOWN");
        var record = new FieldState
        {
            Departure = original, HalfLife = 7, Shape = 1,
            Ages = new Dictionary<string, double> { [candidate.Id] = 4 }
        };
        Check(BTRuntime.Apply(field, record) && record.Ages.Count == 1 && record.Ages[candidate.Id] == 4 &&
            CrewSim.system.dictStellarObjects.ContainsKey(candidate.Id),
            "Rounding within one native instant cleared or aged a survivor cohort.");
    }
}
