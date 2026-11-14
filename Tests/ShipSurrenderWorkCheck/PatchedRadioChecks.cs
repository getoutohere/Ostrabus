using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Ostranauts.Ships.Comms;
using Ostrabus.MiscBugFixes;

internal static partial class Program {
    static Action<Interaction> effectObserver;
    static Action<Loot> commandObserver;
    static float draw = .25f;
    static int draws;
    static bool ObserveEffects(Interaction __instance) { effectObserver?.Invoke(__instance); return false; }
    static bool ObserveCommand(Loot __0) { commandObserver?.Invoke(__0); return false; }
    static bool AlwaysTriggered(ref bool __result) { __result = true; return false; }
    static bool NotPlayerShip(ref bool __result) { __result = false; return false; }
    static bool NoEngineCall() => false;
    static bool Draw(float min, float max, ref float __result) { draws++; __result = min + (max - min) * draw; return false; }

    static int CheckPatchedRadio() {
        string data = Path.Combine(Ostrabus.Development.InstallationPaths.Game, "Ostranauts_Data", "StreamingAssets", "data");
        string stakes = Path.Combine(data, "tsv", "output", "stakes");
        var fixture = new Harmony("offline.radio-surrender.fixture");
        var production = new Harmony("offline.radio-surrender.production");
        var originalTriggers = DataHandler.dictCTs;
        try {
            fixture.Patch(AccessTools.Method(typeof(UnityEngine.Object), "op_Equality"), prefix: new HarmonyMethod(typeof(Program), nameof(Equal)));
            fixture.Patch(AccessTools.Method(typeof(CondOwner), "ValidateParent"), prefix: new HarmonyMethod(typeof(Program), nameof(NoParentValidation)));
            fixture.Patch(AccessTools.Method(typeof(MathUtils), "Rand", new[] { typeof(float), typeof(float), typeof(MathUtils.RandType), typeof(string) }),
                prefix: new HarmonyMethod(typeof(Program), nameof(Draw)));

            var triggers = new Dictionary<string, CondTrigger>();
            foreach (string file in new[] { Path.Combine(data, "condtrigs", "condtrigs.json") }
                .Concat(Directory.GetFiles(Path.Combine(stakes, "condtrigs"), "condTriggers_STKShipSurrender_*.json"))) {
                foreach (JObject json in JArray.Parse(File.ReadAllText(file))) {
                    var trigger = CondTrigger.CloneFromJSON(json.ToObject<JsonCondTrigger>());
                    triggers[trigger.strName] = trigger;
                }
            }
            DataHandler.dictCTs = triggers;
            var never = triggers["TNever"];
            Check(never.aReqs.Intersect(never.aForbids).Any(), "Native TNever is no longer contradictory");
            foreach (bool hasPrisoner in new[] { false, true }) {
                var actor = Empty<CondOwner>(); actor.mapConds = new Dictionary<string, Condition>();
                if (hasPrisoner) actor.mapConds["CareerPrisoner"] = new Condition(null) { strName = "CareerPrisoner", fCount = 1 };
                Check(!never.Triggered(actor, logOutcome: false), "Native no-op trigger can apply effects");
            }

            var loots = new Dictionary<string, Loot>();
            foreach (string file in new[] { Path.Combine(data, "loot", "loot.json") }
                .Concat(Directory.GetFiles(Path.Combine(stakes, "loot"), "loot_STKShipSurrender_*.json"))) {
                foreach (JObject json in JArray.Parse(File.ReadAllText(file))) {
                    var loot = Loot.CloneFromJSON(json.ToObject<JsonLoot>());
                    loots[loot.strName] = loot;
                }
            }
            var relevant = new HashSet<Loot>();
            var interactions = new List<JObject>();
            foreach (string family in new[] { "PlayerDemand", "PlayerOffer", "AIDemand", "AIOffer" }) {
                var definitions = JArray.Parse(File.ReadAllText(Path.Combine(stakes, "interactions", "interactions_STKShipSurrender_" + family + ".json")));
                Check(definitions.Count > 0, "Missing surrender family");
                foreach (JObject ia in definitions) {
                    interactions.Add(ia);
                    foreach (string field in new[] { "LootCondsUs", "LootCondsThem", "LootCTsUs", "LootCTsThem" }) {
                        string id = (string)ia[field];
                        if (id != null && loots.TryGetValue(id, out var loot)) relevant.Add(loot);
                        else Check(id == null, "Unresolved surrender effect " + id);
                    }
                }
            }
            int conditionEdits = 0, triggerEdits = 0;
            foreach (Loot source in relevant) {
                string snapshot = JsonConvert.SerializeObject(source);
                int before = draws;
                Loot filtered = RadioSurrenderEffects.Filter(source);
                Check(draws == before, "Preparing effects consumed a random choice");
                Check(ReferenceEquals(filtered, RadioSurrenderEffects.Filter(source)), "Effect cache allocates every call");
                Check(filtered.aLoots.SequenceEqual(source.aLoots), "Nested or command references changed");
                // The shipped surrender roots use direct condition/trigger edits.
                // Pin that assumption instead of claiming arbitrary nested support.
                Check(source.strType != Loot.TYPE_COND && source.strType != Loot.TYPE_TRIGGER || source.aLoots.Length == 0,
                    "Nested surrender effects require an explicit isolation review: " + source.strName);
                Check(filtered.strType == source.strType && filtered.strName == source.strName && filtered.bSuppress == source.bSuppress,
                    "Effect identity/type changed");
                if (source.strType == Loot.TYPE_COND) {
                    var original = source.GetCondLoot(1, null);
                    var patched = filtered.GetCondLoot(1, null);
                    if (original.Remove("InSocialCombat")) conditionEdits++;
                    Check(!patched.ContainsKey("InSocialCombat"), "Radio condition still edits conversation lock");
                    Check(original.OrderBy(p => p.Key).SequenceEqual(patched.OrderBy(p => p.Key)), "Other condition effects changed");
                } else if (source.strType == Loot.TYPE_TRIGGER) {
                    foreach (float sample in new[] { .01f, .49f, .99f }) {
                        draw = sample;
                        before = draws; var original = source.GetCTLoot(null); int count = draws - before;
                        before = draws; var patched = filtered.GetCTLoot(null);
                        Check(count == draws - before && original.Count == patched.Count, "Trigger selection shape/draw count changed");
                        for (int i = 0; i < original.Count; i++) {
                            if (original[i].strCondName == "InSocialCombat") {
                                triggerEdits++; Check(patched[i].strName == "TNever", "Shared exit still clears another conversation");
                            } else Check(original[i].strName == patched[i].strName && original[i].fCount == patched[i].fCount,
                                "Other trigger selection changed");
                        }
                    }
                } else Check(ReferenceEquals(source, filtered), "Commands/text were cloned or modified");
                Check(JsonConvert.SerializeObject(source) == snapshot, "Shared asset mutated");
            }
            Check(conditionEdits == 146 && triggerEdits > 0, "Expected condition edits/shared exit coverage changed");

            var changing = new Loot { strName = "cache-check", strType = Loot.TYPE_COND, aCOs = new[] { "InSocialCombat=1x1" } };
            Loot initial = RadioSurrenderEffects.Filter(changing);
            changing.aCOs = new[] { "InSocialCombat=1x1", "IsHappy=1x2" };
            Check(!ReferenceEquals(initial, RadioSurrenderEffects.Filter(changing)), "Cache ignored a replacement effect array");

            // Bind the real delivery method. Engine/UI operations are fixtures;
            // the actual transpiled call and try/finally isolation execute.
            fixture.Patch(AccessTools.Method(typeof(Interaction), nameof(Interaction.ApplyEffects)), prefix: new HarmonyMethod(typeof(Program), nameof(ObserveEffects)));
            fixture.Patch(AccessTools.Method(typeof(Interaction), nameof(Interaction.Triggered), new[] { typeof(bool), typeof(bool), typeof(bool) }),
                prefix: new HarmonyMethod(typeof(Program), nameof(AlwaysTriggered)));
            fixture.Patch(AccessTools.Method(typeof(Ship), nameof(Ship.IsPlayerShip)), prefix: new HarmonyMethod(typeof(Program), nameof(NotPlayerShip)));
            foreach (string method in new[] { "TrimMessageLog", "AIReply" })
                fixture.Patch(AccessTools.Method(typeof(Comms), method), prefix: new HarmonyMethod(typeof(Program), nameof(NoEngineCall)));
            fixture.Patch(AccessTools.Method(typeof(Comms), "ApplyLoot"), prefix: new HarmonyMethod(typeof(Program), nameof(ObserveCommand)));
            production.CreateClassProcessor(typeof(RadioSurrenderEffectsPatch)).Patch();
            var receiver = TestShip("receiver");
            var comms = Empty<Comms>();
            AccessTools.Field(typeof(Comms), "_ship").SetValue(comms, receiver);
            AccessTools.Field(typeof(Comms), "_messageLog").SetValue(comms, new List<ShipMessage>());
            var receive = AccessTools.Method(typeof(Comms), "ReceiveMessage");
            foreach (JObject definition in interactions) {
                var ia = Empty<Interaction>(); ia.strName = (string)definition["strName"];
                Loot Resolve(string field) => (string)definition[field] is string id ? loots[id] : null;
                var condUs = ia.LootCondsUs = Resolve("LootCondsUs");
                var condThem = ia.LootCondsThem = Resolve("LootCondsThem");
                var ctUs = ia.LootCTsUs = Resolve("LootCTsUs");
                var ctThem = ia.LootCTsThem = Resolve("LootCTsThem");
                ia.strLootContextUs = "existing-us"; ia.strLootContextThem = "existing-them";
                int applied = 0, commands = 0;
                effectObserver = active => {
                    applied++;
                    Check(active.strLootContextUs == null && active.strLootContextThem == null, "Radio overwrites relationship contexts");
                    Check(ReferenceEquals(active.LootCondsUs, RadioSurrenderEffects.Filter(condUs)) &&
                        ReferenceEquals(active.LootCondsThem, RadioSurrenderEffects.Filter(condThem)) &&
                        ReferenceEquals(active.LootCTsUs, RadioSurrenderEffects.Filter(ctUs)) &&
                        ReferenceEquals(active.LootCTsThem, RadioSurrenderEffects.Filter(ctThem)), "Delivery did not isolate both participants");
                };
                commandObserver = active => { commands++; Check(ReferenceEquals(active, ctThem), "Ship command dispatcher sees substituted effects"); };
                receive.Invoke(comms, new object[] { new ShipMessage { SenderRegId = "sender", ReceiverRegId = receiver.strRegID, Interaction = ia } });
                Check(applied == 1 && commands == 1, "Native delivery bypassed effects or command dispatcher");
                void Restored() => Check(ReferenceEquals(ia.LootCondsUs, condUs) && ReferenceEquals(ia.LootCondsThem, condThem) &&
                    ReferenceEquals(ia.LootCTsUs, ctUs) && ReferenceEquals(ia.LootCTsThem, ctThem) &&
                    ia.strLootContextUs == "existing-us" && ia.strLootContextThem == "existing-them", "Interaction not restored");
                Restored();
                effectObserver = active => { Check(active.strLootContextUs == "existing-us" && ReferenceEquals(active.LootCondsUs, condUs),
                    "Direct on-foot use was changed"); };
                ia.ApplyEffects(); // Direct call must not be intercepted by production.
                effectObserver = _ => throw new InvalidOperationException("fixture effect failure");
                bool threw = false;
                try { RadioSurrenderEffects.Apply(ia, null, false); } catch (InvalidOperationException) { threw = true; }
                Check(threw, "Effect exception swallowed"); Restored();
            }
            var ordinary = Empty<Interaction>(); ordinary.strName = "ordinary-radio"; ordinary.strLootContextUs = "untouched";
            effectObserver = active => Check(active.strLootContextUs == "untouched", "Other radio dialogue changed");
            RadioSurrenderEffects.Apply(ordinary, null, false);
            Console.WriteLine("Covered " + interactions.Count + " radio replies, " + conditionEdits + " condition loots and shared social exits.");
        } finally {
            effectObserver = null; commandObserver = null;
            production.UnpatchSelf(); fixture.UnpatchSelf(); DataHandler.dictCTs = originalTriggers;
        }
        Console.WriteLine(checks + " production radio isolation checks passed.");
        return 0;
    }
}
