using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using Ostrabus.Inspection;
using Ostranauts.Ships;
using UnityEngine;

static partial class Program {
    static Collider NoCollider(ref RaycastHit hit) => null;
    static IEnumerable<CodeInstruction> RayEngine(IEnumerable<CodeInstruction> code) {
        foreach (var instruction in code) {
            if (instruction.operand is MethodInfo method && method.DeclaringType == typeof(RaycastHit) && method.Name == "get_transform") {
                instruction.opcode = OpCodes.Call;
                instruction.operand = AccessTools.Method(typeof(Program), nameof(HitTransform));
            }
            yield return instruction;
        }
    }
    // Engine substitutes sit inside the observation wrappers, so production
    // DamageRay instrumentation still binds and executes its actual native IL.
    static IEnumerable<CodeInstruction> ObserverEngine(IEnumerable<CodeInstruction> code) {
        foreach (var instruction in code) {
            if (instruction.operand is MethodInfo method) {
                string replacement = null;
                if (method.DeclaringType == typeof(Physics) && method.Name == "RaycastAll") replacement = nameof(Hits);
                else if (method.DeclaringType == typeof(RaycastHit) && method.Name == "get_transform") replacement = nameof(HitTransform);
                else if (method.DeclaringType == typeof(RaycastHit) && method.Name == "get_collider") replacement = nameof(NoCollider);
                else if (method.Name == "GetComponent" && method.IsGenericMethod) {
                    if (method.GetGenericArguments()[0] == typeof(Destructable)) replacement = nameof(Equipment);
                    if (method.GetGenericArguments()[0] == typeof(CondOwner)) replacement = nameof(Person);
                }
                if (replacement != null) { instruction.opcode = OpCodes.Call; instruction.operand = AccessTools.Method(typeof(Program), replacement); }
            }
            yield return instruction;
        }
    }
    static Dictionary<string, object> Trace() => (Dictionary<string, object>)PersonnelCombatInspection.Read(0, 50);
    static List<object> Entries() => (List<object>)Trace()["items"];
    static List<Dictionary<string, object>> Events(object ray) => ((List<object>)((Dictionary<string, object>)ray)["events"]).Cast<Dictionary<string, object>>().ToList();
    static void Detached(object value) {
        if (value == null || value is string || value is bool || value is int || value is long || value is double || value is float) return;
        if (value is Dictionary<string, object> map) { foreach (var item in map.Values) Detached(item); return; }
        if (value is System.Collections.IList list) { foreach (var item in list) Detached(item); return; }
        throw new Exception("Trace retained a non-detached value: " + value.GetType());
    }
    static void PersonnelChecks(Harmony fixture, DamageSystem system, Ship ship, CondOwner target, JArray attacks) {
        int startChecks = checks;
        var native = AccessTools.Method(typeof(DamageSystem), nameof(DamageSystem.DamageRay));
        fixture.Unpatch(native, AccessTools.Method(typeof(Program), nameof(Fixture)));
        // Native DamageRay now has only its always-on self-exclusion patch.
        foreach (string name in new[] { "ObserveRaycast", "ObjectComponent", "CharacterComponent" })
            fixture.Patch(AccessTools.Method(typeof(PersonnelCombatTraceHooks), name), transpiler: new HarmonyMethod(typeof(Program), nameof(ObserverEngine)));
        AccessTools.Field(typeof(CondOwner), "_strID").SetValue(attacker, "offline-shooter");
        AccessTools.Field(typeof(CondOwner), "_strID").SetValue(target, "offline-target");
        attacker.strName = "crew"; target.strName = "target";
        AccessTools.Field(typeof(CondOwner), "_objShip").SetValue(attacker, ship);
        AccessTools.Field(typeof(CondOwner), "_objShip").SetValue(target, ship);
        DataHandler.mapCOs = new Dictionary<string, CondOwner> { [attacker.strID] = attacker, [target.strID] = target };
        var mode = attacks.Single(a => (string)a["strName"] == "AModeRifle02").ToObject<JsonAttackMode>();
        Check(!PersonnelCombatTraceHooks.Installed, "Trace hooks present before opt-in");
        Check(PersonnelCombatInspection.Start("missing", 120).Status == 404 && !PersonnelCombatTraceHooks.Installed, "Missing actor started capture");
        Check(PersonnelCombatInspection.Start(attacker.strID, 0).Status == 400 && !PersonnelCombatTraceHooks.Installed, "Zero duration accepted");
        Check(PersonnelCombatInspection.Start(attacker.strID, 301).Status == 400, "Unbounded duration accepted");
        Check(PersonnelCombatInspection.Start(attacker.strID, 120).Status == 200 && PersonnelCombatTraceHooks.Installed, "Production trace failed to install");
        fixture.Patch(native, transpiler: new HarmonyMethod(typeof(Program), nameof(RayEngine)));
        Check(PersonnelCombatInspection.Start(attacker.strID, 120).Status == 409, "Active trace was silently cleared");
        var interaction = new Interaction { strName = "offline-shot", attackMode = mode };
        AccessTools.Field(typeof(Interaction), "strUsID").SetValue(interaction, attacker.strID);
        AccessTools.Field(typeof(Interaction), "strThemID").SetValue(interaction, target.strID);
        object[] effectsArgs = { interaction, false, null };
        AccessTools.Method(typeof(PersonnelCombatTraceHooks), "EffectsBegin").Invoke(null, effectsArgs);
        Reset(Body(attacker), Body(target));
        Shot(system, mode, attacker);
        AccessTools.Method(typeof(PersonnelCombatTraceHooks), "EffectsEnd").Invoke(null, new object[] { null, effectsArgs[2] });
        Check(damageCalls == 1 && ReferenceEquals(woundReceiver, target), "Tracing broke shooter exclusion or intended damage");
        var ray = (Dictionary<string, object>)Entries().Single();
        Check((bool)ray["completed"] && (int)ray["candidateCount"] == 2 && (string)((Dictionary<string, object>)ray["mode"])["id"] == "AModeRifle02", "Actual ray metadata missing");
        var events = Events(ray);
        Check((string)((Dictionary<string, object>)ray["intent"])["targetId"] == target.strID && PersonnelCombatInspection.Intent == null, "Intended target or interaction context restoration missing");
        Check(events.Count(e => (string)e["stage"] == "damageRoll") == 3, "Trace added/lost native damage draws");
        Check(events.Count(e => (string)e["stage"] == "woundDamage") == 1 && events.Single(e => (string)e["stage"] == "woundDamage")["returned"] is true, "Actual wound invocation/result missing");
        Check((string)Trace()["error"] == null, "Trace capture raised diagnostic error");
        Reset(Body(attacker)); Shot(system, mode, target);
        Check(Entries().Count == 1 && damageCalls == 1, "Shooter filter suppressed another shooter's damage or recorded it");
        var floorScrap = Empty<CondOwner>(); floorScrap.strName = "ItmMeat01Loose";
        Reset(Object(floorScrap), Body(target)); Shot(system, mode, attacker);
        Check(conditionCalls == 0 && damageCalls == 1 && ReferenceEquals(woundReceiver, target), "Tracing made floor meat block shots again");
        Check(Events(Entries().Last()).Any(e => (string)e["stage"] == "objectDamageRemaining" && e["ignoredFloorMeat"] is true && (double)e["value"] == 0),
            "Trace did not report the active floor-meat cover exclusion");
        Reset(Object(target)); Shot(system, mode, attacker);
        Check(conditionCalls == 1 && ReferenceEquals(damagedObject, target), "Trace altered native object damage");
        var objectEvents = Events(Entries().Last());
        Check(objectEvents.Any(e => (string)e["stage"] == "objectDamage" && (double)e["amount"] == mode.fDmgEnv && (string)e["id"] == target.strID), "Native object damage amount/recipient missing");
        Check(objectEvents.Any(e => (string)e["stage"] == "objectDamageRemaining"), "Object remaining durability missing");
        Reset(); Shot(system, mode, attacker);
        Check((int)((Dictionary<string, object>)Entries().Last())["candidateCount"] == 0, "Empty physics result not captured");
        nativeWoundError = new InvalidOperationException("native wound failure");
        Reset(Body(target));
        Exception caught = null;
        try { Shot(system, mode, attacker); }
        catch (Exception ex) { caught = ex; }
        finally { nativeWoundError = null; }
        Check(caught is InvalidOperationException && caught.Message == "native wound failure", "Trace swallowed/replaced a native exception");
        ray = (Dictionary<string, object>)Entries().Last();
        Check(!(bool)ray["completed"] && (string)ray["exception"] == "InvalidOperationException" && PersonnelCombatInspection.Ray == null, "Exceptional ray left stale capture context");
        var page = (Dictionary<string, object>)PersonnelCombatInspection.Read(1, 1);
        Check(((List<object>)page["items"]).Count == 1 && (long)page["nextSince"] == 2, "Trace cursor skipped or duplicated rays");
        Detached(Trace());
        Check(SnapshotJson.Encode(Trace()).Contains("candidateHits"), "Trace not serializable through API encoder");
        PersonnelCombatInspection.Stop();
        Check(!PersonnelCombatTraceHooks.Installed && !PersonnelCombatInspection.Active && Entries().Count == 5, "Stop did not remove hooks/retain primitive history");
        Check(!Harmony.GetPatchInfo(native).Owners.Contains("com.ostranauts.template.inspection.personnel"), "Trace left a permanent native hook");
        PersonnelCombatInspection.Start(attacker.strID, 120);
        Check(Entries().Count == 0, "New session retained previous trace");
        Reset(Enumerable.Range(0, 40).Select(i => Body(attacker)).ToArray());
        Shot(system, mode, attacker);
        ray = (Dictionary<string, object>)Entries().Single();
        Check((bool)ray["candidatesTruncated"] && ((List<object>)ray["candidateHits"]).Count == PersonnelCombatInspection.MaxHits, "Candidate capture unbounded");
        Check((int)ray["eventsDropped"] > 0 && ((List<object>)ray["events"]).Count == PersonnelCombatInspection.MaxEvents, "Processing event capture unbounded");
        Reset();
        for (int i = 1; i < PersonnelCombatInspection.Capacity + 8; i++) Shot(system, mode, attacker);
        Check(!PersonnelCombatInspection.Active && (long)Trace()["latestSequence"] == PersonnelCombatInspection.Capacity && (string)Trace()["stopReason"] == "ray limit", "Ray limit did not stop capture");
        PersonnelCombatInspection.Tick();
        Check(!PersonnelCombatTraceHooks.Installed, "Ray-limit expiry left hooks installed");
        PersonnelCombatInspection.Reset();
        Check(Entries().Count == 0 && Trace()["shooterId"] == null, "Scene reset retained trace state");
        PersonnelCombatInspection.Start(attacker.strID, 120);
        AccessTools.Field(typeof(PersonnelCombatInspection), "deadline").SetValue(null, System.Diagnostics.Stopwatch.GetTimestamp() - 1);
        PersonnelCombatInspection.Tick();
        Check(!PersonnelCombatInspection.Active && !PersonnelCombatTraceHooks.Installed && (string)Trace()["stopReason"] == "timeout", "Wall-time timeout did not remove hooks");
        PersonnelCombatInspection.Reset();
        Detached(PersonnelCombatInspection.Snapshot(attacker));
        RequestChecks();
        // Fail-closed on a changed native IL shape.
        bool rejected = false;
        try { PersonnelCombatTraceHooks.TraceRay(new[] { new CodeInstruction(OpCodes.Ret) }).ToList(); }
        catch (InvalidOperationException) { rejected = true; }
        Check(rejected, "Unknown native method shape installed partial diagnostics");
        Check(!typeof(PersonnelCombatTraceHooks).GetCustomAttributes(typeof(HarmonyPatch), false).Any(), "PatchAll could enable diagnostic hooks");
        fixture.Unpatch(native, AccessTools.Method(typeof(Program), nameof(RayEngine)));
        fixture.Patch(native, transpiler: new HarmonyMethod(typeof(Program), nameof(Fixture)));
        Reset(Body(attacker), Body(target)); Shot(system, mode, attacker);
        Check(damageCalls == 1 && ReferenceEquals(woundReceiver, target), "Removing trace hooks disturbed the permanent self-hit fix");
        Console.WriteLine((checks - startChecks) + " personnel combat tracing checks passed.");
    }
    static void RequestChecks() {
        InspectionHttp Parse(string target) {
            var http = new InspectionHttp();
            AccessTools.Method(typeof(InspectionHttp), "Parse").Invoke(http, new object[] {
                "POST " + target + " HTTP/1.1\r\nHost: 127.0.0.1:18764\r\nContent-Length: 0", 18764 });
            return http;
        }
        var normal = Parse("/combat/personnel/trace/start?coId=Ayden%20Kelly&seconds=120");
        Check(normal.Error == null && PersonnelCombatRequest.Parse(normal, out string id, out int seconds) && id == "Ayden Kelly" && seconds == 120, "Actual HTTP parser rejected trace query");
        var defaults = Parse("/combat/personnel/trace/start");
        Check(PersonnelCombatRequest.Parse(defaults, out id, out seconds) && id == null && seconds == 120, "Trace control defaults changed");
        foreach (string value in new[] { "0", "301", "-1", "1.5", "NaN", "9999999999999999999", "", "%20120" })
            Check(!PersonnelCombatRequest.Parse(Parse("/combat/personnel/trace/start?seconds=" + value), out id, out seconds), "Invalid duration accepted: " + value);
        foreach (string value in new[] { "", "%0A", new string('a', 65) })
            Check(!PersonnelCombatRequest.Parse(Parse("/combat/personnel/trace/start?coId=" + value), out id, out seconds), "Invalid actor ID accepted");
        Check(Parse("/combat/personnel/trace/start?seconds=1&seconds=2").Error != null, "Duplicate control accepted");
        Check(Parse("/combat/personnel/trace/start?execute=anything").Error != null, "Arbitrary control parameter accepted");
        Check(PersonnelCombatRequest.IsControl("/combat/personnel/trace/start") && PersonnelCombatRequest.IsControl("/combat/personnel/trace/stop") &&
            !PersonnelCombatRequest.IsControl("/combat/personnel/trace") && !PersonnelCombatRequest.IsControl("/cos/actor/combat"), "POST allowlist includes observation routes");
    }
}

namespace Ostrabus.Inspection {
    internal sealed class InspectionApi {
        internal sealed class Reply {
            internal int Status;
            internal object Body;
            internal Reply(int status, object body) { Status = status; Body = body; }
        }
    }
}
