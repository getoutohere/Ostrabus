using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Serialization;
using System.Threading;
using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Ostrabus.Diagnostics.Obituaries;
using Ostrabus.Inspection;
using Ostranauts.Core;
using Condowner.Utils;

// Native death setter and fatal-condition evaluator execute unchanged except for
// scene/rendering boundaries. Destructive world/actor teardown is a fixture;
// its observer is still installed on the real native method.
static class Program {
    static int checks, cancels, validations;
    static bool failAnimation, failDestroy;
    static Action population, replacement, loadStep, materialize, disposal;
    static readonly string DirectoryPath = Path.GetFullPath("obj/ObituaryCheck/" + Guid.NewGuid().ToString("N"));
    static readonly Dictionary<string, Condition> definitions = new Dictionary<string, Condition>();
    static T Empty<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    static void Set(object obj, string field, object value) => AccessTools.Field(obj.GetType(), field).SetValue(obj, value);
    static void Check(bool condition, string why) { checks++; if (!condition) throw new Exception(why); }
    static bool Quiet() => false;
    static bool Equal(UnityEngine.Object __0, UnityEngine.Object __1, ref bool __result) { __result = ReferenceEquals(__0, __1); return false; }
    static bool Unequal(UnityEngine.Object __0, UnityEngine.Object __1, ref bool __result) { __result = !ReferenceEquals(__0, __1); return false; }
    static bool Selected(ref CondOwner __result) { __result = null; return false; }
    static bool Cancel(CondOwner __instance) { cancels++; __instance.aQueue.Clear(); return false; }
    static bool Validate() { validations++; return false; }
    static bool Animate() { if (failAnimation) throw new InvalidOperationException("native animation fixture failure"); return false; }
    static T NoComponent<T>(UnityEngine.Component instance) where T : class => null;
    static IEnumerable<CodeInstruction> SceneComponents(IEnumerable<CodeInstruction> code) {
        foreach (var instruction in code) {
            if (instruction.operand is MethodInfo method && method.DeclaringType == typeof(UnityEngine.Component) && method.Name == "GetComponent" && method.IsGenericMethod) {
                instruction.opcode = OpCodes.Call;
                instruction.operand = AccessTools.Method(typeof(Program), nameof(NoComponent)).MakeGenericMethod(method.GetGenericArguments());
            }
            yield return instruction;
        }
    }
    static bool SpecialCondition(CondOwner __instance, string strName, double fAmount) {
        if (strName != "IsDead" && strName != "DisallowPaperDoll" && strName != "OrdinaryUpdate") return true;
        if (fAmount > 0) __instance.mapConds[strName] = new Condition(null) { strName = strName, fCount = fAmount };
        else __instance.mapConds.Remove(strName);
        return false;
    }
    static bool Definition(string __0, ref Condition __result) { __result = definitions[__0]; return false; }
    static bool DestroyActor(CondOwner __instance) {
        if (failDestroy) throw new InvalidOperationException("native destroy fixture failure");
        __instance.bDestroyed = true; __instance.ship = null; __instance.mapConds = null;
        DataHandler.mapCOs.Remove(__instance.strID); return false;
    }
    static bool Detach(CondOwner __instance, ref Ship __result) { __result = __instance.ship; __instance.ship = null; return false; }
    static bool DestroyShip(Ship __instance, bool isDespawning) {
        Set(__instance, "<objSS>k__BackingField", null); __instance.bDestroyed = true;
        foreach (var person in __instance.People) {
            if (!DataHandler.mapCOs.TryGetValue(person.strCO, out var co)) continue;
            co.ship = null;
            if (!isDespawning) co.Kill = true;
            co.Destroy();
        }
        return false;
    }
    static bool Population() { population?.Invoke(); return false; }
    static bool Replacement() { replacement?.Invoke(); return false; }
    static bool Restore(CondOwner __instance, JsonCondOwnerSave jCOSIn) {
        if (loadStep != null) loadStep();
        __instance.bAlive = jCOSIn?.bAlive ?? true; return false;
    }
    static bool Materialize() { materialize?.Invoke(); return false; }
    static bool DisposeWorld() { disposal?.Invoke(); return false; }
    static bool Score(ref float __result) { __result = -100; return false; }
    static bool NullTrigger(ref CondTrigger __result) { __result = null; return false; }
    static bool Factions(ref List<JsonFaction> __result) { __result = new List<JsonFaction>(); return false; }
    static bool Text(string __0, ref string __result) { __result = __0; return false; }
    static Ship Hull(string id = "O-HULL") {
        var ship = Empty<Ship>(); ship.strRegID = id; ship.publicName = "Fixture vessel"; ship.Classification = Ship.TypeClassification.Default;
        Set(ship, "<objSS>k__BackingField", Empty<ShipSitu>()); ship.objSS.vVelX = 1.25; ship.objSS.strBOPORShip = "HQCH"; ship.objSS.bOrbitLocked = true;
        Set(ship, "aPeople", new List<PersonSpec>()); return ship;
    }
    static CondOwner Person(string id, Ship ship = null) {
        var co = Empty<CondOwner>(); Set(co, "_strID", id); co.strName = id; co.strNameFriendly = "NPC " + id;
        co.bAlive = true; co.bFreezeCondRules = true; co.Clock = Empty<Clock>();
        co.mapConds = new Dictionary<string, Condition> { ["IsHuman"] = new Condition(null) { strName = "IsHuman", fCount = 1 } };
        co.aQueue = new List<Interaction> { new Interaction { strName = "WanderSoon" } };
        co.aDestructableConds = new List<string>(); co.dictAddCondEvents = new Dictionary<string, string[]>(); co.dictRemoveCondEvents = new Dictionary<string, string[]>();
        Set(co, "aCondsTimed", new List<Condition>()); Set(co, "aCondZeroes", new List<string>());
        Set(co, "aFactions", new List<string> { "BELT_PIRATES" });
        co.ship = ship;
        DataHandler.mapCOs[id] = co;
        if (ship != null) { var person = Empty<PersonSpec>(); person.strCO = id; person.bAlive = true; ship.People.Add(person); }
        return co;
    }
    static JObject Status(ObituaryStore store) => JObject.FromObject(store.Status());
    static void Drain(ObituaryStore store) => Check(SpinWait.SpinUntil(() => (long)Status(store)["accepted"] == (long)Status(store)["written"] || Status(store)["error"].Type != JTokenType.Null, 5000), "Writer did not drain");
    static JArray Events(ObituaryStore store, string kind = null) {
        Drain(store); return (JArray)JObject.FromObject(store.Read(null, null, kind, null, 0, 50, null, null))["events"];
    }
    static void Finish(ObituaryStore store) { store.Stop(); Check(store.Completion.Wait(5000), "Writer did not finish"); }
    static JObject Last(string kind) => (JObject)Events(ObituaryRecorder.Store, kind).Last;
    static int Count(string kind) => Events(ObituaryRecorder.Store, kind).Count;
    static void Lifecycle(Harmony production) {
        var ship = Hull();
        var baseline = Person("baseline", ship); cancels = validations = 0;
        baseline.Kill = true;
        var baselineConds = baseline.mapConds.Keys.OrderBy(x => x).ToArray();
        int baseCancel = cancels, baseValidate = validations;
        ObituaryRecorder.Enabled = true;
        foreach (var type in typeof(ObituaryRecorder).Assembly.GetTypes().Where(t => t.Namespace == typeof(ObituaryDeathPatch).Namespace && t.GetCustomAttributes(typeof(HarmonyPatch), false).Length > 0))
            production.CreateClassProcessor(type).Patch();
        Check(production.GetPatchedMethods().Count() == 12, "Enabled obituary native target count changed");
        foreach(var classification in new[]{Ship.TypeClassification.Default,Ship.TypeClassification.Projectile}) {
            var empty=Hull("O-EMPTY"); empty.Classification=classification;
            var parent=new ObituaryRecorder.Scope("outer-operation");
            try {
                Check(ObituaryRecorder.BeginShip(empty,false)==null && ReferenceEquals(ObituaryRecorder.Current,parent),
                    "Crewless destruction allocated a vessel snapshot/context");
            } finally {parent.Close();}
            empty.Destroy(false);
            Check(empty.bDestroyed && ObituaryRecorder.Current==null,"Crewless teardown changed native destruction or leaked context");
        }
        var floor = Empty<CondOwner>();
        loadStep = () => Check(ObituaryRecorder.Current == null, "Saved floor allocated a person restoration scope");
        floor.SetData(new JsonCondOwner(), false, new JsonCondOwnerSave { aConds = new[] { "IsFloor=1x1", "StatMass=7x1" } }); loadStep = null;
        Check(ObituaryRecorder.PersonData(floor, null, new JsonCondOwnerSave { aConds = new[] { "IsRobot=1x1" } }), "Saved robot identity not recognized before initialization");
        var actor = Person("native-death", ship); cancels = validations = 0;
        actor.Kill = true;
        Check(!actor.bAlive && actor.mapConds.Keys.OrderBy(x => x).SequenceEqual(baselineConds) && cancels == baseCancel && validations == baseValidate && actor.aQueue.Count == 0,
            "Recorder changed native death result/conditions/cleanup");
        Check(Count("death") == 1 && (string)Last("death")["npcId"] == "native-death", "Native death not recorded exactly once");
        Check((string)Last("death")["evidence"]["before"]["location"]["motion"]["bodyReference"] == "HQCH", "Manufactured reference lost");
        actor.Kill = true; Check(Count("death") == 1, "Repeated death counted twice");
        actor.Kill = false; actor.Kill = true; Check(Count("death") == 2, "Resurrection followed by new death was lost");
        actor = Person("immune"); actor.bIgnoreKill = true; actor.Kill = true;
        Check(actor.bAlive && Count("death") == 2, "Ignored native kill became an obituary");
        actor = Person("asphyxiation", ship);
        actor.AddCondAmount("DcOxygen04", 1);
        Check(!actor.bAlive && Last("death")["evidence"]["before"]["conditions"]["selected"].Any(c => (string)c["id"] == "DcOxygen04" && (bool)c["fatal"]), "Native fatal condition missing from death evidence");
        Check(actor.mapConds["DcOxygen04"].bFatal, "Native asset fatal condition was not applied");
        actor = Person("animation-failure"); failAnimation = true;
        try { actor.Kill = true; Check(false, "Native exception was swallowed"); }
        catch (InvalidOperationException e) { Check(e.Message == "native animation fixture failure", "Native exception changed"); }
        finally { failAnimation = false; }
        Check((string)Last("death")["npcId"] == actor.strID && ((string)Last("death")["evidence"]["exception"]).Contains("animation fixture failure"), "Partially completed death lost");
        actor = Person("population-prune", ship);
        population = () => { actor.RemoveFromCurrentHome(true); actor.Destroy(); };
        AccessTools.Method(typeof(AIShipManager), "SpawnPassShip").Invoke(null, null);
        var removed = Last("living-removal");
        Check((string)removed["npcId"] == actor.strID && (string)removed["shipId"] == ship.strRegID && (string)removed["mechanism"] == "station-population", "Population removal lost ship/cause");
        Check((bool)removed["evidence"]["aliveAfter"], "Living removal mislabeled a physiological death");
        var arrivingShip = Hull("O-ARRIVAL"); actor = Person("arrival-rejected", arrivingShip);
        CrewSim.DockAndDespawn(arrivingShip, Hull("STATION"));
        removed = Last("living-removal");
        Check((string)removed["npcId"] == "arrival-rejected" && (string)removed["mechanism"] == "station-arrival" && arrivingShip.HideFromSystem,
            "Native docking rejection not recorded, or native retirement changed");
        actor = Person("destroy-exception"); failDestroy = true;
        try { actor.Destroy(); Check(false, "Destroy exception swallowed"); } catch (InvalidOperationException e) { Check(e.Message.Contains("destroy fixture"), "Destroy exception changed"); }
        finally { failDestroy = false; }
        Check((string)Last("operation-failed")["npcId"] == actor.strID && !actor.bDestroyed, "Failed destroy reported as a removal");
        int deaths = Count("death"), removals = Count("living-removal");
        actor = Person("already-dead");
        var saved = new JsonCondOwnerSave { bAlive = false };
        loadStep = () => { actor.Kill = true; };
        actor.SetData(new JsonCondOwner(), false, saved); loadStep = null;
        Check(Count("death") == deaths && !actor.bAlive, "Restored corpse counted as new death");
        var alreadyDead = Person("person-spec-dead"); var spec = Empty<PersonSpec>(); spec.strCO = alreadyDead.strID; spec.bAlive = false; alreadyDead.pspec = spec;
        var unrelated = Person("unrelated-during-materialization");
        materialize = () => { alreadyDead.Kill = true; unrelated.Kill = true; };
        spec.MakeCondOwner(default, null); materialize = null;
        Check(Count("death") == deaths + 1 && (string)Last("death")["npcId"] == unrelated.strID, "Person-spec restore suppressed an unrelated real death");
        deaths++;
        actor = Person("catch-up-death"); loadStep = () => { actor.Kill = true; throw new InvalidOperationException("catch-up failed"); };
        try { actor.SetData(new JsonCondOwner(), false, new JsonCondOwnerSave { bAlive = true }); } catch (InvalidOperationException) { }
        loadStep = null;
        Check(Count("death") == deaths + 1 && ObituaryRecorder.Current == null, "Real initialization death suppressed or scope leaked");
        var replacingShip = Hull("O-REPLACE"); Person("surviving-replacement", replacingShip);
        replacement = () => replacingShip.Destroy();
        AccessTools.Method(typeof(CrewSim), "DestroyAndReload").Invoke(Empty<CrewSim>(), new object[] { replacingShip, new JsonShip() });
        Check(Count("living-removal") == removals && ObituaryRecorder.Current == null, "Shallow replacement emitted false removals or leaked scope");
        actor = Person("world-unload-disposal"); disposal = () => actor.Destroy(); Empty<CrewSim>().EmptyScene(); disposal = null;
        Check(Count("living-removal") == removals && ObituaryRecorder.Current == null, "Normal world teardown generated an obituary");
        var loss = Hull("O-LOSS"); Person("lost-crew", loss); loss.Destroy(false);
        var death = Last("death"); var vessel = Last("vessel-destruction-begin");
        Check((string)death["shipId"] == "O-LOSS" && (string)death["vesselEvent"] == (string)vessel["vesselEvent"], "Crew death not linked to pre-destruction vessel event");
        Check((double)vessel["evidence"]["ship"]["motion"]["vxAUPerSecond"] == 1.25 && loss.objSS == null, "Pre-destruction motion lost");
        Check(ObituaryRecorder.Current == null, "Ship destruction scope leaked");
        var malformed = Hull("O-MALFORMED-ROUTE"); malformed.objSS.NavData = Empty<Ostranauts.ShipGUIs.Utilities.NavData>();
        actor = Person("malformed-route", malformed); actor.Kill = true;
        Check((string)Last("death")["npcId"] == actor.strID && Last("death")["evidence"]["before"]["location"]["motion"]["routeEndEpoch"].Type == JTokenType.Null,
            "Malformed navigation evidence prevented death recording");
        actor = Person("unregistered-preview"); DataHandler.mapCOs.Remove(actor.strID); deaths = Count("death"); actor.Kill = true;
        Check(Count("death") == deaths, "Unregistered preview generated obituary");
        ObituaryRecorder.BeginLoad("example-save.zip"); var firstBranch = (string)JObject.FromObject(ObituaryRecorder.Status())["branch"];
        StarSystem.fEpoch -= 5000; ObituaryRecorder.BeginLoad("example-save.zip");
        Check(firstBranch != (string)JObject.FromObject(ObituaryRecorder.Status())["branch"], "Reverted save reused obituary timeline");
        var query = new NameValueCollection { ["coId"] = "lost-crew", ["kind"] = "death", ["limit"] = "5" };
        var reply = ObituaryInspection.Read("/obituaries/events", query);
        Check(reply.Status == 200 && ((JArray)JObject.FromObject(reply.Body)["events"]).Count == 1, "Inspection filtering failed");
        var wire = JObject.Parse(SnapshotJson.Encode(reply.Body));
        Check((string)wire["events"][0]["npcId"] == "lost-crew" && wire["events"][0]["evidence"]["before"]["conditions"]["selected"] is JArray,
            "Production wire encoder rejected or changed nested obituary evidence");
        string utc = (string)JObject.FromObject(reply.Body)["events"][0]["utc"];
        Check(utc.Contains("T") && utc.EndsWith("Z", StringComparison.Ordinal), "Wire conversion changed the recorded UTC timestamp format");
        Check((bool)JObject.Parse(SnapshotJson.Encode(ObituaryInspection.Read("/obituaries", new NameValueCollection()).Body))["enabled"],
            "Production wire encoder rejected recorder status");
        query["since"] = "1"; Check(ObituaryInspection.Read("/obituaries/events", query).Status == 400, "Ambiguous cross-session cursor accepted");
        Check(JObject.Parse(SnapshotJson.Encode(ObituaryInspection.Read("/obituaries/events", query).Body))["error"].Type == JTokenType.String,
            "Invalid obituary request could not be encoded");
        query["session"] = (string)JObject.FromObject(ObituaryRecorder.Status())["session"];
        Check(ObituaryInspection.Read("/obituaries/events", query).Status == 200, "Session cursor rejected");
        query["afterEpoch"] = "NaN"; Check(ObituaryInspection.Read("/obituaries/events", query).Status == 400, "Invalid epoch accepted");
        actor = Person("many-conditions");
        for (int i = 0; i < 80; i++) actor.mapConds["DcExtra" + i] = new Condition(null) { strName = "DcExtra" + i, fCount = 1 };
        actor.AddCondAmount("DcOxygen04", 1);
        Check((bool)Last("death")["evidence"]["before"]["conditions"]["selected"][0]["fatal"] &&
            (int)Last("death")["evidence"]["before"]["conditions"]["omitted"] > 0, "Capped condition evidence omitted the fatal condition");
        actor = Person("id with space"); actor.Kill = true; Drain(ObituaryRecorder.Store);
        var http = new InspectionHttp();
        AccessTools.Method(typeof(InspectionHttp), "Parse").Invoke(http, new object[] {
            "GET /obituaries/events?coId=id%20with%20space&kind=death&limit=1 HTTP/1.1\r\nHost: 127.0.0.1:18764", 18764 });
        Check(http.Error == null && ((JArray)JObject.FromObject(ObituaryInspection.Read(http).Body)["events"]).Count == 1, "Real HTTP query parser rejected new filters/encoded identity");
        deaths = Count("death");
        ObituaryRecorder.Enabled = false; actor = Person("disabled"); actor.Kill = true; Check(Count("death") == deaths, "Disabled recorder still recorded");
        ObituaryRecorder.Enabled = true;
    }
    static Dictionary<string, object> Row(int n, int padding = 512) => ObituaryRecorder.Map("session", "store-test", "sequence", n, "kind", "death", "npcId", "npc-" + n,
        "epoch", (double)n, "evidence", new string('x', padding));
    static void Storage() {
        var path = Path.Combine(DirectoryPath, "rotation"); var store = new ObituaryStore(path, 4096);
        for (int i = 0; i < 64; i++) { store.Enqueue(Row(i)); Drain(store); }
        Finish(store);
        var files = Directory.GetFiles(path, "*.jsonl");
        Check(files.Length == 4 && files.Sum(p => new FileInfo(p).Length) <= 4 * 4096 && files.All(p => new FileInfo(p).Length <= 4096), "Rotation violated total disk cap");
        Check(Events(store).Count > 0 && Events(store).Count < 64, "Rotation did not retain recent events");
        store = new ObituaryStore(path, 4096); store.Enqueue(Row(100)); Drain(store); Finish(store);
        Check(Directory.GetFiles(path).Length == 4 && Events(store).Any(r => (string)r["npcId"] == "npc-100"), "Restart lost history or multiplied files");
        path = Path.Combine(DirectoryPath, "oversize"); store = new ObituaryStore(path); store.Enqueue(Row(1, 50000)); Drain(store); Finish(store);
        Check((bool)Events(store)[0]["evidenceTruncated"] && new FileInfo(Directory.GetFiles(path)[0]).Length <= ObituaryStore.RecordBytes, "Oversize evidence was not bounded/marked");
        File.AppendAllText(Directory.GetFiles(path)[0], "partial crash tail");
        Check((long)JObject.FromObject(store.Read(null, null, null, null, 0, 50, null, null))["corruptLines"] == 1, "Interrupted final record not reported");
        store = new ObituaryStore(path); store.Enqueue(Row(2)); Drain(store); Finish(store);
        Check(Events(store).Count == 2 && (long)JObject.FromObject(store.Read(null, null, null, null, 0, 50, null, null))["corruptLines"] == 1,
            "Appending after a crash corrupted the next event or hid the broken tail");
        File.AppendAllText(Path.Combine(path, "obituaries-0.jsonl"), "{}\n");
        Check(Events(store).Count == 2 && (long)JObject.FromObject(store.Read(null, null, null, null, 0, 50, null, null))["corruptLines"] == 2,
            "Malformed event identity was returned as a valid obituary");
        string bad = Path.Combine(DirectoryPath, "file-not-directory"); File.WriteAllText(bad, "occupied");
        store = new ObituaryStore(bad); store.Enqueue(Row(1)); Check(store.Completion.Wait(5000), "Disk error hung writer");
        Check(Status(store)["error"].Type == JTokenType.String && (long)Status(store)["dropped"] > 0, "Disk failure was silent");
        Check(JObject.Parse(SnapshotJson.Encode(store.Read(null, null, null, null, 0, 25, null, null)))["status"]["error"].Type == JTokenType.String,
            "Storage failure status could not be encoded by production transport");
        var original = ObituaryRecorder.Store; ObituaryRecorder.Store = store;
        var actor = Person("disk-error-still-dies"); actor.Kill = true;
        Check(!actor.bAlive, "Disk failure changed native death"); ObituaryRecorder.Store = original;
        store = new ObituaryStore(Path.Combine(DirectoryPath, "burst"));
        var watch = Stopwatch.StartNew(); for (int i = 0; i < 50000; i++) store.Enqueue(Row(i, 64)); watch.Stop();
        Finish(store);
        Check((long)Status(store)["dropped"] > 0 && (long)Status(store)["accepted"] + (long)Status(store)["dropped"] == 50000, "Burst accounting or backpressure failed");
        Console.WriteLine("Bounded enqueue stress: 50000 attempts in " + watch.ElapsedMilliseconds + " ms; " + Status(store).ToString(Formatting.None));
    }
    static int Main() {
        Directory.CreateDirectory(DirectoryPath); DataHandler.mapCOs = new Dictionary<string, CondOwner>();
        var fixture = new Harmony("offline.obituary.scene"); var production = new Harmony("offline.obituary.production");
        void Prefix(Type type, string method, string helper, Type[] signature = null) => fixture.Patch(AccessTools.Method(type, method, signature), prefix: new HarmonyMethod(typeof(Program), helper) { priority = Priority.Last });
        try {
            Prefix(typeof(UnityEngine.Object), "op_Equality", nameof(Equal)); Prefix(typeof(UnityEngine.Object), "op_Inequality", nameof(Unequal));
            Prefix(typeof(UnityEngine.Debug), "Log", nameof(Quiet), new[] { typeof(object) });
            Prefix(typeof(CondOwner), "AICancelAll", nameof(Cancel)); Prefix(typeof(CondOwner), "SetAnimState", nameof(Animate));
            Prefix(typeof(CrewSim), "GetSelectedCrew", nameof(Selected)); Prefix(typeof(AIShipManager), "ValidateCrew", nameof(Validate), new[] { typeof(CondOwner) });
            fixture.Patch(AccessTools.PropertySetter(typeof(CondOwner), "Kill"), transpiler: new HarmonyMethod(typeof(Program), nameof(SceneComponents)));
            Prefix(typeof(CondOwner), "AddCondAmount", nameof(SpecialCondition)); Prefix(typeof(DataHandler), "GetCond", nameof(Definition));
            Prefix(typeof(CondOwner), "UpdatePriority", nameof(Quiet));
            Prefix(typeof(GUIRenderTargets), "UpdateFaces", nameof(Quiet));
            AccessTools.Field(typeof(MonoSingleton<GUIRenderTargets>), "_instance").SetValue(null, Empty<GUIRenderTargets>());
            Prefix(typeof(CondOwner), "Destroy", nameof(DestroyActor)); Prefix(typeof(CondOwner), "RemoveFromCurrentHome", nameof(Detach));
            Prefix(typeof(Ship), "Destroy", nameof(DestroyShip)); Prefix(typeof(AIShipManager), "SpawnPassShip", nameof(Population));
            Prefix(typeof(CrewSim), "DestroyAndReload", nameof(Replacement)); Prefix(typeof(CondOwner), "SetData", nameof(Restore));
            Prefix(typeof(PersonSpec), "MakeCondOwner", nameof(Materialize));
            Prefix(typeof(CrewSim), "EmptyScene", nameof(DisposeWorld)); Prefix(typeof(CondOwner), "CatchUp", nameof(Quiet));
            Prefix(typeof(CondOwner), "UnclaimShip", nameof(Quiet)); Prefix(typeof(CondOwner), "GetFactionScore", nameof(Score), new[] { typeof(List<string>) });
            Prefix(typeof(DataHandler), "GetCondTrigger", nameof(NullTrigger), new[] { typeof(string) }); Prefix(typeof(DataHandler), "GetString", nameof(Text));
            Prefix(typeof(Ship), "GetShipFactions", nameof(Factions)); Prefix(typeof(Ship), "LogAdd", nameof(Quiet)); Prefix(typeof(Ship), "ToggleVis", nameof(Quiet));
            AIShipManager.dictAIs = new Dictionary<string, List<Ostranauts.Ships.AIPilots.AIShip>>();
            var assets = JArray.Parse(File.ReadAllText(Path.Combine(Ostrabus.Development.InstallationPaths.Data, "conditions", "conditions.json")));
            var fatal = assets.Single(x => (string)x["strName"] == "DcOxygen04").ToObject<JsonCond>();
            definitions["DcOxygen04"] = new Condition(fatal);
            Check(definitions["DcOxygen04"].bFatal, "Installed fatal asset changed");
            var disabled = new Harmony("offline.obituary.disabled");
            foreach (var type in typeof(ObituaryRecorder).Assembly.GetTypes().Where(t => t.Namespace == typeof(ObituaryDeathPatch).Namespace && t.GetCustomAttributes(typeof(HarmonyPatch), false).Length > 0))
                disabled.CreateClassProcessor(type).Patch();
            Check(!disabled.GetPatchedMethods().Any(), "Disabled startup installed diagnostic hooks");
            ObituaryRecorder.Store = new ObituaryStore(Path.Combine(DirectoryPath, "lifecycle"));
            Lifecycle(production); Storage(); Finish(ObituaryRecorder.Store);
            Check((long)JObject.FromObject(ObituaryRecorder.Status())["captureErrors"] == 0, "Capture failures were hidden by tests");
            Console.WriteLine("Obituaries: " + checks + " native death/condition, lifecycle, storage and inspection checks passed. " + DirectoryPath);
            return 0;
        } catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { ObituaryRecorder.Enabled = false; ObituaryRecorder.Store?.Stop(); production.UnpatchSelf(); fixture.UnpatchSelf(); }
    }
}
