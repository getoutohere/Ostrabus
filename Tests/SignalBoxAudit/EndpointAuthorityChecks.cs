using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Emit;
using HarmonyLib;
using Ostranauts.Electrical;
using Ostrabus.Development;

static partial class Program {
    static void CheckEndpointAuthority() {
        int before = checks;
        foreach (Ship.Loaded load in new[] { Ship.Loaded.None, Ship.Loaded.Shallow, Ship.Loaded.Edit, Ship.Loaded.Full })
            foreach (bool done in new[] { false, true }) foreach (bool teardown in new[] { false, true })
                foreach (int endpointState in new[] { 0, 1, 2 }) foreach (bool switching in new[] { false, true }) {
            var source = Box(); var target = Device(); string id = target.coSelf.strID;
            Set(source.coShip, "nLoadState", load); CompletedElectricalLoad(source.coShip) = done; source.coShip.bDestroyed = teardown;
            source.coShip.json = new JsonShip { aItems = new[] { new JsonItem { strID = id, strName = "stale-or-loading-endpoint" } } };
            Input(source, id, true); source.outputConnections[id] = new ElectricalConnection(id, SignalType.On); source.SetGPM();
            if (endpointState == 0) registry.Remove(id);
            if (endpointState == 1) Set(target.coSelf, "elec", null);
            if (switching) SwitchBegin(source.coShip, id);
            try {
                source.AuditConnections();
                bool retain = endpointState == 2 || load != Ship.Loaded.Full || !done || teardown || switching;
                Check(source.inputConnections.ContainsKey(id) == retain, "Audit input classification follows completed scene generation, not saved JSON presence");
                Check(source.outputConnections.ContainsKey(id) == retain, "Audit output classification follows completed scene generation");
            } finally { if (switching) SwitchEnd(source.coShip, id); }
        }
        foreach (bool returns in new[] { false, true }) {
            var source = Box(); var target = Device(); string id = target.coSelf.strID;
            source.SetUpConnection(target.coSelf); Tick(source); Tick(target);
            registry.Remove(id); CompletedElectricalLoad(source.coShip) = false;
            source.RemoveConnection(id); Tick(source);
            Check(Queue(source, "sendQueue").Count == 1, "Exactly one durable disconnect while reconstruction is incomplete");
            int lookupsBeforeWaiting = lookupCalls;
            for (int i = 0; i < 20; i++) { StarSystem.fEpoch += 1; source.UpdateManual(); }
            Check(lookupCalls == lookupsBeforeWaiting && !Get<bool>(source, "bNeedsCheck"), "Dormant lifecycle wait does not poll runtime lookup");
            for (int i = 0; i < 20; i++) Tick(source);
            Check(Queue(source, "sendQueue").Count == 1, "Pending disconnect does not multiply during unavailable retries");
            if (returns) registry[id] = target.coSelf;
            CompletedElectricalLoad(source.coShip) = true;
            WakeCandidate(source); StarSystem.fEpoch += .2; source.UpdateManual(); if (returns) Tick(target);
            Check(Queue(source, "sendQueue").Count == 0 && Gpm(source)["sendQueue"] == "", "Completed reconstruction either delivers or retires the terminal signal");
            if (returns) Check(!target.inputConnections.ContainsKey(source.coSelf.strID), "Rematerialized receiver consumes the delayed disconnection");
            for (int i = 0; i < 10; i++) Tick(source);
            Check(Queue(source, "sendQueue").Count == 0 && !Get<bool>(source, "bNeedsCheck"), "Retired/delivered disconnect does not cause perpetual retry");
        }
        // Native explicitly preserves Electrical -> Electrical conversions.
        var upstream = Box(); var old = Box(); upstream.SetUpConnection(old.coSelf); Tick(upstream); Tick(old);
        var next = Device(); string persistentId = old.coSelf.strID;
        SwitchBegin(upstream.coShip, persistentId); SwitchBegin(upstream.coShip, persistentId);
        old.CleanUp(false); // Exact native mode-switch branch when replacement has Electrical.
        registry.Remove(persistentId); upstream.AuditConnections();
        Check(upstream.outputConnections.ContainsKey(persistentId), "Mode-switch gap does not erase a preserved relationship");
        SwitchEnd(upstream.coShip, persistentId);
        Check(EndpointTemporary(upstream.coShip, persistentId), "Nested switch scope remains active until outer completion");
        Set(next.coSelf, "_strID", persistentId); conditions[persistentId] = conditions[old.coSelf.strID];
        Set(next, "mapGPM", Gpm(old)); next.coSelf.mapGUIPropMaps["Electrical"] = Gpm(old);
        registry[persistentId] = next.coSelf; Init(next);
        SwitchEnd(upstream.coShip, persistentId); upstream.AuditConnections();
        Check(upstream.outputConnections.ContainsKey(persistentId) && next.inputConnections.ContainsKey(upstream.coSelf.strID), "Same-ID/GPM native reconstruction preserves both relationship ends");
        Check(!old.needsCleanup, "Later destruction of old Electrical component does not disconnect replacement");
        var doomed = Box(); var peer = Device(); doomed.SetUpConnection(peer.coSelf); Tick(doomed); Tick(peer);
        doomed.CleanUp(true); Tick(peer);
        Check(!peer.inputConnections.ContainsKey(doomed.coSelf.strID), "Native non-preserving conversion/permanent cleanup still disconnects");
        // Ship generation, not registration string, owns authority and switch state.
        var oldShip = upstream.coShip; oldShip.strRegID = "SAME-REG";
        var newShip = Box().coShip; newShip.strRegID = oldShip.strRegID;
        CrewSim.system.dictShips[newShip.strRegID] = newShip;
        SwitchBegin(oldShip, "id");
        Check(EndpointTemporary(oldShip, "id") && !EndpointTemporary(newShip, "id"), "Old generation state cannot poison a reconstructed ship with the same registration");
        SwitchEnd(oldShip, "id");
        CheckDefinitionPreservationRules();
        var reader = typeof(PatchProcessor).GetMethods().Single(m => m.Name == "GetOriginalInstructions" && m.GetParameters().Length == 2 && !m.GetParameters()[1].ParameterType.IsByRef);
        var loadingCode = ((IEnumerable<CodeInstruction>)reader.Invoke(null, new object[] { AccessTools.Method(typeof(Ship), nameof(Ship.InitShip)), null })).ToList();
        var doneFlag = AccessTools.Field(typeof(Ship), "_bDoneLoading");
        int firstFalse = loadingCode.FindIndex(i => i.opcode == OpCodes.Stfld && Equals(i.operand, doneFlag));
        int finalTrue = loadingCode.FindLastIndex(i => i.opcode == OpCodes.Stfld && Equals(i.operand, doneFlag));
        int firstSpawn = loadingCode.FindIndex(i => i.operand is System.Reflection.MethodInfo m && m.Name == "SpawnItems");
        int lastSpawn = loadingCode.FindLastIndex(i => i.operand is System.Reflection.MethodInfo m && m.Name == "SpawnItems");
        Check(firstFalse > 0 && loadingCode[firstFalse-1].opcode == OpCodes.Ldc_I4_0 && firstFalse < firstSpawn, "Native InitShip marks its map incomplete before item spawning");
        Check(finalTrue > lastSpawn && loadingCode[finalTrue-1].opcode == OpCodes.Ldc_I4_1, "Native InitShip marks completion only after spawning");
        // A ready owner can still be holding a cached ship from before a transfer.
        var moved = Box(); var movedTarget = Device(); moved.SetUpConnection(movedTarget.coSelf); Tick(moved); Tick(movedTarget);
        var newOwner = Box().coShip; Set(moved.coSelf, "_objShip", newOwner);
        int lookupsBefore = lookupCalls;
        moved.AuditConnections(); moved.RemoveConnection(movedTarget.coSelf.strID); Tick(moved);
        Check(lookupCalls == lookupsBefore && Queue(moved, "sendQueue").Count == 1, "Stale cached owner defers without querying the old runtime map");
        moved.coShip = newOwner; Tick(moved); Tick(movedTarget);
        Check(Queue(moved, "sendQueue").Count == 0 && !movedTarget.inputConnections.ContainsKey(moved.coSelf.strID), "Delivery resumes after owner binding is settled");
        var initializingSource = Box(); var initializingTarget = Device();
        initializingSource.SetUpConnection(initializingTarget.coSelf); Tick(initializingSource); Tick(initializingTarget);
        initializingTarget.isInitialising = true; initializingSource.RemoveConnection(initializingTarget.coSelf.strID); Tick(initializingSource);
        Check(Queue(initializingSource, "sendQueue").Count == 1, "Existing receiver defers terminal delivery until its GPM initialization finishes");
        initializingTarget.isInitialising = false; Tick(initializingSource); Tick(initializingTarget);
        Check(!initializingTarget.inputConnections.ContainsKey(initializingSource.coSelf.strID), "Receiver consumes terminal delivery after initialization");
        Console.WriteLine("Endpoint authority: " + (checks-before) + " checks passed.");
    }
    static void CheckDefinitionPreservationRules() {
        var definitions = Directory.GetFiles(Path.Combine(InstallationPaths.Data, "condowners"), "*.json")
            .SelectMany(path => LitJson.JsonMapper.ToObject<JsonCondOwner[]>(File.ReadAllText(path)))
            .Where(def => def != null && def.strName != null).GroupBy(def => def.strName).ToDictionary(group => group.Key, group => group.First());
        bool ElectricalDef(string name) => definitions[name].aUpdateCommands?.Any(command => command.StartsWith("Electrical,")) == true;
        Check(ElectricalDef("ItmElectricalBox01") && !ElectricalDef("ItmElectricalBox01Loose"), "Shipped signal-box loose conversion deliberately loses Electrical");
        Check(ElectricalDef("ItmFusionLaserArray01Off") && !ElectricalDef("ItmFusionLaserArray01Loose"), "Shipped fusion-array loose conversion deliberately loses Electrical");
        var preservedLoose = definitions.Keys.Where(name => name.EndsWith("Loose") && ElectricalDef(name)).ToArray();
        foreach (string loose in preservedLoose) {
            string installed = loose.Substring(0, loose.Length-5);
            Check(definitions.ContainsKey(installed) && ElectricalDef(installed), "Preserving loose asset has an Electrical installed counterpart: " + loose);
            var old = Box(); var replacement = Device(); var source = Device();
            Input(old, source.coSelf.strID, false); old.gateMode = GateMode.NAND; old.SetSlider(.05f); old.ToggleOverride(); old.SetGPM();
            string id = old.coSelf.strID; old.CleanUp(false);
            Set(replacement.coSelf, "_strID", id); Set(replacement, "mapGPM", Gpm(old)); replacement.coSelf.mapGUIPropMaps["Electrical"] = Gpm(old); Init(replacement);
            Check(replacement.inputConnections.ContainsKey(source.coSelf.strID) && replacement.gateMode == GateMode.NAND && !replacement.bOverride && replacement.sliderPercent == old.sliderPercent,
                "Native preserved GPM retains wire, gate, override and delay for asset pair: " + loose);
        }
        File.WriteAllLines("obj/electrical-preserving-loose-definitions.txt", preservedLoose);
        Console.WriteLine("ASSET RULE: " + preservedLoose.Length + " loose definitions retain Electrical; list in obj/electrical-preserving-loose-definitions.txt.");
    }
}
