using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Ostranauts.Ships;
using Ostranauts.Core.Models;

partial class Program {
    static readonly Dictionary<string, double> masses = new Dictionary<string, double>();
    static string massWriteThrows;
    static bool ReadMass(CondOwner __instance, string strName, ref double __result) {
        __result = strName == "StatMass" && masses.TryGetValue(__instance.strID, out double mass) ? mass : 0;
        return false;
    }
    static bool StoreMass(CondOwner __instance, string strName, double fAmount) {
        Check(strName == "StatMass", "Unexpected condition mutation in mass fixture");
        if (__instance.strID == massWriteThrows) throw new InvalidOperationException("fixture mass write failure");
        masses[__instance.strID] += fAmount;
        return false;
    }
    static void MassDiagnosis(Harmony fixture) {
        // Restore native mass arithmetic; substitute only condition scalar storage and engine access.
        fixture.Unpatch(AccessTools.Method(typeof(CondOwner), "AddMass"), HarmonyPatchType.Prefix, fixture.Id);
        fixture.Unpatch(AccessTools.Method(typeof(CondOwner), "GetTotalMass"), HarmonyPatchType.Prefix, fixture.Id);
        fixture.Unpatch(AccessTools.Method(typeof(CondOwner), "AddCondAmount"), HarmonyPatchType.Prefix, fixture.Id);
        var massFixture = new Harmony("offline.barter.mass.storage");
        massFixture.Patch(AccessTools.Method(typeof(CondOwner), "GetCondAmount", new[] { typeof(string) }), prefix: new HarmonyMethod(typeof(Program), nameof(ReadMass)));
        massFixture.Patch(AccessTools.Method(typeof(CondOwner), "AddCondAmount"), prefix: new HarmonyMethod(typeof(Program), nameof(StoreMass)));
        var previousSaves = DataHandler.dictCOSaves;
        try {
            foreach (bool frozen in new[] { false, true }) {
                masses.Clear();
                var dollyItem = new JsonItem { strID = "mass-dolly", strName = "ItmDolly01" };
                var cargoItem = new JsonItem { strID = "mass-cargo", strName = "fixture-cargo", strParentID = dollyItem.strID };
                var source = new[] { dollyItem, cargoItem };
                var barter = Fixture(source, source);
                var map = (DictionaryArray<string, CondOwner>)AccessTools.Field(typeof(Ship), "mapICOs").GetValue(barter);
                var dolly = map[dollyItem.strID];
                var cargo = map[cargoItem.strID];
                dolly.mapInfo = new Dictionary<string, string>();
                dolly.bFreezeConds = frozen;
                masses[dolly.strID] = 714;
                masses[cargo.strID] = 611;
                var container = Contents(dolly, cargo);
                var savedDolly = new JsonCondOwnerSave { strID = dolly.strID, strCODef = dolly.strCODef, aConds = new[] { "StatMass=1.0x714" } };
                DataHandler.dictCOSaves = new Dictionary<string, JsonCondOwnerSave> { [dolly.strID] = savedDolly };
                container.RemoveCOSimple(cargo); // actual native method + production barter dispatch correction
                Check(container.ContainedCOs.Count == 0 && !barter.json.aItems.Any(x => x.strID == cargo.strID), "Sale did not remove real cargo before checking mass");
                Check(dolly.GetTotalMass() == (frozen ? 714 : 103), "Unexpected native AddMass behavior for frozen/unfrozen parent");
                var original = Empty<Ship>(); original.json = Json(source.ToArray());
                barter.SaveChangedCOs(ref original); // actual native save-back, not substituted
                Check(!original.json.aItems.Any(x => x.strID == cargo.strID), "Sale item survived source layout save-back");
                Check(ReferenceEquals(DataHandler.dictCOSaves[dolly.strID], savedDolly) && savedDolly.aConds.Single() == "StatMass=1.0x714", "Existing container mass was unexpectedly refreshed");
                Console.WriteLine("MASS " + (frozen ? "REMOTE FROZEN" : "UNFROZEN CONTROL") + ": empty dolly runtime=" + dolly.GetTotalMass() + " kg; persisted mass=714 kg; expected empty mass=103 kg.");
            }
            MassPrevention();
        } finally {
            DataHandler.dictCOSaves = previousSaves;
            massFixture.UnpatchSelf();
            masses.Clear();
        }
    }
}
