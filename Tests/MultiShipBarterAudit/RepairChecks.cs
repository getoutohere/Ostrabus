using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Ostrabus.MiscBugFixes;
using Ostrabus.Optimizations;
using Ostrabus.QoL.BarterZones;
using Ostranauts.Ships;
using Ostranauts.ShipGUIs.Trade;
using Ostranauts.ShipGUIs.Trade.Models;
using TMPro;
using UnityEngine;

static partial class Program {
    static string headerText;
    static bool HeaderText(string value) { headerText = value; return false; }
    static bool StartJob(ref Coroutine __result) { __result = Empty<Coroutine>(); return false; }

    static void CheckRepairedRows() {
        var production = new Harmony("offline.multi-barter.zone-repairs");
        var ui = new Harmony("offline.multi-barter.header-fixture");
        try {
            // TMP static initialization has already been isolated by the native
            // transaction fixture. No real Unity text object is created here.
            ui.Patch(AccessTools.PropertySetter(typeof(TMP_Text), "text"), prefix: new HarmonyMethod(typeof(Program), nameof(HeaderText)));
            ui.Patch(AccessTools.Method(typeof(GUITradeRowContainer), "SetHeader"), prefix: new HarmonyMethod(typeof(Program), nameof(Skip)));
            production.CreateClassProcessor(typeof(BarterZoneListPatch)).Patch();
            production.CreateClassProcessor(typeof(BarterZoneHeaderPatch)).Patch();
            var a = new JsonZone { strName = "Cargo", strRegID = "O-A" };
            var b = new JsonZone { strName = "Cargo", strRegID = "O-B" };
            var a2 = new JsonZone { strName = "Ammo", strRegID = "O-A" };
            var pending = new JsonZone { strName = "Remote ships still loading" };
            var input = new List<TradeRowData> {
                new TradeRowData(b, null, null), new TradeRowData(a, null, null),
                new TradeRowData(pending, null, null), new TradeRowData(a2, null, null)
            };
            var gui = Empty<GUITrade>();
            var rows = new Dictionary<string, GUITradeRowBase>();
            Set(gui, "_dictTradeRows", rows);
            rowsCreated = warnings = 0;
            AccessTools.Method(typeof(GUITrade), "BuildSellList").Invoke(gui, new object[] { input });
            Check(rows.Count == 4 && rowsCreated == 4 && warnings == 0, "Same-named zone groups remain hidden after repair.");
            Check(input.Select(row => row.JsonZone).SequenceEqual(new[] { a2, a, b, pending }), "Zone groups are not ordered by callsign then name, with loading notice last.");
            Check(a.strName == "Cargo" && b.strName == "Cargo", "Zone names were changed in saved data.");
            Check(BarterZones.Key(a) != BarterZones.Key(b), "Zone keys collide across ships.");
            Check(BarterZones.Key(new JsonZone { strRegID = "A", strName = "BC" }) != BarterZones.Key(new JsonZone { strRegID = "AB", strName = "C" }), "Composite zone keys have ambiguous boundaries.");

            var firstPersonal = Empty<TradeRowData>();
            var secondPersonal = Empty<TradeRowData>();
            Set(firstPersonal, "CoItem", Empty<CondOwner>());
            Set(secondPersonal, "CoItem", Empty<CondOwner>());
            var mixed = new List<TradeRowData> { input[2], firstPersonal, input[0], secondPersonal };
            BarterZones.Sort(mixed);
            Check(ReferenceEquals(mixed[0], firstPersonal) && ReferenceEquals(mixed[1], secondPersonal), "Personal inventory order was changed.");
            Check(mixed[2].JsonZone == a2 && mixed[3].JsonZone == b, "Mixed-input zone grouping failed.");

            var header = Empty<GUITradeRowContainer>();
            Set(header, "txtCoName", Empty<TextMeshProUGUI>());
            foreach (var zone in new[] { a, b, pending }) {
                AccessTools.Method(typeof(GUITradeRowContainer), "SetHeader").Invoke(header, new object[] { new TradeRowData(zone, null, null), zone });
                Check(headerText == (zone.strRegID == null ? zone.strName : zone.strRegID + " / " + zone.strName), "Production zone header did not use callsign presentation.");
            }
            Console.WriteLine("FIXED: all ships' barter zones remain visible and sorted; callsign headers preserve saved names and personal inventory ordering.");
        } finally { production.UnpatchSelf(); ui.UnpatchSelf(); }
    }

    static AsyncShipLoader LoadingPair(out BarterZoneShip a, out BarterZoneShip b, out List<Coroutine> jobs) {
        var loader = Empty<AsyncShipLoader>();
        a = Copy("CANCEL-A");
        b = Copy("CANCEL-B");
        jobs = new List<Coroutine> { AsyncShipJobs.Start(loader, a.Init(10)), AsyncShipJobs.Start(loader, b.Init(10)) };
        Set(loader, "_loadStateDict", new Dictionary<string, IAsyncLoadable> { [a.strRegID] = a, [b.strRegID] = b });
        Set(loader, "_loadingCoroutine", jobs);
        return loader;
    }
    static void CheckRepairedCancellation() {
        var production = new Harmony("offline.multi-barter.loader-repairs");
        var engine = new Harmony("offline.multi-barter.start-fixture");
        try {
            engine.Patch(AccessTools.Method(typeof(MonoBehaviour), "StartCoroutine", new[] { typeof(IEnumerator) }), prefix: new HarmonyMethod(typeof(Program), nameof(StartJob)));
            production.CreateClassProcessor(typeof(AsyncShipJobStartPatch)).Patch();
            production.CreateClassProcessor(typeof(AsyncShipUnloadPatch)).Patch();
            stopped.Clear();
            var loader = LoadingPair(out var a, out var b, out var jobs);
            var aJob = jobs[0];
            var bJob = jobs[1];
            Check(AsyncShipJobs.Count(loader) == 2, "Native iterator ownership was not registered.");
            loader.Unload("NOT-LOADED");
            Check(stopped.Count == 0 && jobs.Count == 2, "Unknown target cancelled unrelated work.");
            loader.Unload(a.strRegID);
            Check(stopped.SequenceEqual(new[] { aJob }) && jobs.SequenceEqual(new[] { bJob }), "Targeted unload did not preserve unrelated coroutine.");
            Check(AsyncShipJobs.Count(loader) == 1, "Targeted cancellation retained job ownership metadata.");
            Set(b, "_fullyLoaded", true);
            Check(loader.GetShip(b.strRegID, out var ready) && ReferenceEquals(ready, b), "Surviving ship could not finish loading normally.");
            loader.Unload(b.strRegID);
            Check(stopped.Count == 2 && jobs.Count == 0 && AsyncShipJobs.Count(loader) == 0, "Final unload left dangling job metadata.");
            stopped.Clear();
            loader = LoadingPair(out a, out b, out jobs);
            loader.Unload();
            Check(stopped.Count == 2 && jobs.Count == 0 && AsyncShipJobs.Count(loader) == 0, "Global unload no longer cancels every job.");
            Check(!loader.IsShipLoading(a.strRegID) && !loader.IsShipLoading(b.strRegID), "Global unload retained ship cache entries.");
            var preview = Empty<AsyncPreviewShip>();
            preview.strRegID = "PREVIEW-OWNER";
            var rating = Empty<AsyncRatingShip>();
            rating.strRegID = "RATING-OWNER";
            jobs.Add(AsyncShipJobs.Start(loader, preview.Init(10)));
            jobs.Add(AsyncShipJobs.Start(loader, rating.Init(10, null)));
            Check(AsyncShipJobs.Count(loader) == 2, "Preview/rating iterators did not identify their owning ships.");
            AsyncShipJobs.Clear(jobs, loader, null);
            Check(jobs.Count == 0 && AsyncShipJobs.Count(loader) == 0, "Preview/rating ownership metadata was not released.");
            production.CreateClassProcessor(typeof(BarterInitPatch)).Patch();
            jobs.Add(AsyncShipJobs.Start(loader, a.Init(10)));
            Check(AsyncShipJobs.Count(loader) == 1, "Paced barter initialization iterator lost its ship identity.");
            AsyncShipJobs.Clear(jobs, loader, null);
            Check(AsyncShipJobs.Count(loader) == 0, "Paced barter iterator metadata was retained.");
            Console.WriteLine("FIXED: targeted unload preserves unrelated loaders, global unload still cancels all, and metadata is released.");
        } finally { production.UnpatchSelf(); engine.UnpatchSelf(); stopped.Clear(); }
    }
}
