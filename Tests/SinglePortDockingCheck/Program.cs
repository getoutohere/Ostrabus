using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using BepInEx.Configuration;
using HarmonyLib;
using LitJson;
using Ostrabus.QoL.Docking;
using Ostranauts.Core;
using Ostranauts.ShipGUIs.MFD;
using Ostranauts.ShipGUIs.NavStation;
using Ostranauts.Ships.Comms;

static class Program {
    static int checks, sends, triggerChecks;
    static bool allowed = true;
    static Ship us, target;
    static CondOwner crew, captain;
    static Interaction sent;
    static List<(string, string)> pairs;
    static T Empty<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    static void Set(object obj, string name, object value) => AccessTools.Field(obj.GetType(), name).SetValue(obj, value);
    static void Check(bool condition, string message) { checks++; if (!condition) throw new Exception(message); }
    static bool Equal(UnityEngine.Object __0, UnityEngine.Object __1, ref bool __result) { __result = ReferenceEquals(__0, __1); return false; }
    static bool Unequal(UnityEngine.Object __0, UnityEngine.Object __1, ref bool __result) { __result = !ReferenceEquals(__0, __1); return false; }
    static bool Quiet() => false;
    static bool Ports(bool earlyOut, ref List<(string, string)> __result) {
        Check(!earlyOut, "Native UI must enumerate all compatible port pairs"); __result = pairs; return false;
    }
    static bool Captain(ref CondOwner captain, ref bool __result) { captain = Program.captain; __result = true; return false; }
    static bool PortName(ref string __result) { __result = "Airlock"; return false; }
    static bool Action(string inv, CondOwner coThem, ref Interaction __result) {
        __result = new Interaction { strName = inv, objUs = crew, objThem = coThem }; return false;
    }
    static bool Trigger(ref bool __result) { triggerChecks++; __result = allowed; return false; }
    static bool Send(Interaction ia, ref ShipMessage __result) {
        sends++; sent = ia; __result = new ShipMessage { SenderRegId = us.strRegID, ReceiverRegId = target.strRegID, Interaction = ia }; return false;
    }
    static MFDComms Page(bool atc = false, string id = "O-TARGET") {
        var page = Empty<MFDComms>(); Set(page, "_targetRegID", id); Set(page, "_isATCChannel", atc);
        Set(page, "_coUs", crew); Set(page, "_shipUs", us); Set(page, "_availableIAs", new List<Interaction>());
        return page;
    }
    static ShipMessage Prompt(bool forced = false, string sender = "O-TARGET") => new ShipMessage {
        SenderRegId = sender, ReceiverRegId = us.strRegID,
        Interaction = new Interaction { strName = forced ? "SHIPMultiDockSelectForcedAllow" : "SHIPMultiDockSelectAllow",
            objUs = captain, objThem = crew, aInverse = new[] { forced ? "them#SHIPForceDockNoPilot" : "them#SHIPDock" } }
    };
    static List<Interaction> Options(MFDComms page) => (List<Interaction>)AccessTools.Field(typeof(MFDComms), "_availableIAs").GetValue(page);
    static int Main() {
        string configPath = Path.GetFullPath("obj/SinglePortDockingCheck/test.cfg");
        AccessTools.Field(typeof(BepInEx.Paths), "<BepInExConfigPath>k__BackingField").SetValue(null, configPath + ".core");
        var config = new ConfigFile(configPath, false); config.SaveOnConfigSet = false;
        SinglePortDocking.Initialize(config);
        Check(SinglePortDocking.Enabled && SinglePortDocking.Flag.Definition.Section == "QoL.Docking" &&
            SinglePortDocking.Flag.Definition.Key == "AutoSelectSinglePort", "Saved CFG/default contract incorrect");
        var data = JsonMapper.ToObject<JsonInteraction[]>(File.ReadAllText(Ostrabus.Development.InstallationPaths.Data + "interactions/interactions_ship_to_ship.json"));
        Check(data.Single(x => x.strName == "SHIPMultiDockSelectAllow").aInverse.SequenceEqual(new[] { "them#SHIPDock" }), "Native normal port prompt contract changed");
        Check(data.Single(x => x.strName == "SHIPMultiDockSelectForcedAllow").aInverse.SequenceEqual(new[] { "them#SHIPForceDockNoPilot" }), "Native forced port prompt contract changed");
        var fixture = new Harmony("offline.single-port.scene");
        var production = new Harmony("offline.single-port.production");
        void Prefix(Type type, string method, string helper) => fixture.Patch(AccessTools.Method(type, method), prefix: new HarmonyMethod(typeof(Program), helper));
        try {
            Prefix(typeof(UnityEngine.Object), "op_Equality", nameof(Equal)); Prefix(typeof(UnityEngine.Object), "op_Inequality", nameof(Unequal));
            fixture.Patch(AccessTools.Method(typeof(UnityEngine.Debug), nameof(UnityEngine.Debug.LogWarning), new[] { typeof(object) }), prefix: new HarmonyMethod(typeof(Program), nameof(Quiet)));
            Prefix(typeof(Ship), nameof(Ship.GetAvailableDockingPorts), nameof(Ports)); Prefix(typeof(Ship), nameof(Ship.GetPortName), nameof(PortName));
            Prefix(typeof(Comms), nameof(Comms.GetCaptain), nameof(Captain)); Prefix(typeof(MFDComms), "GetInterAction", nameof(Action));
            Prefix(typeof(MFDComms), "ShowOptions", nameof(Quiet)); Prefix(typeof(MFDComms), "ShowWaitingForResponse", nameof(Quiet));
            Prefix(typeof(GUIMessageDisplay), nameof(GUIMessageDisplay.AddMessage), nameof(Quiet));
            fixture.Patch(AccessTools.Method(typeof(Interaction), nameof(Interaction.Triggered), new[] { typeof(bool), typeof(bool), typeof(bool) }), prefix: new HarmonyMethod(typeof(Program), nameof(Trigger)));
            fixture.Patch(AccessTools.Method(typeof(Comms), nameof(Comms.SendMessage), new[] { typeof(Interaction), typeof(string) }), prefix: new HarmonyMethod(typeof(Program), nameof(Send)));
            AccessTools.Field(typeof(MonoSingleton<GUIMessageDisplay>), "_instance").SetValue(null, Empty<GUIMessageDisplay>());
            us = Empty<Ship>(); us.strRegID = "O-US"; us.PrimaryDockingPortID = "own-port";
            target = Empty<Ship>(); target.strRegID = "O-TARGET";
            Set(us, "<Comms>k__BackingField", Empty<Comms>()); Set(target, "<Comms>k__BackingField", Empty<Comms>());
            crew = Empty<CondOwner>(); captain = Empty<CondOwner>(); Set(crew, "_objShip", us); Set(captain, "_objShip", target);
            production.CreateClassProcessor(typeof(SinglePortDockingPatch)).Patch();
            Check(Harmony.GetPatchInfo(AccessTools.Method(typeof(MFDComms), nameof(MFDComms.OnUIRefresh))).Postfixes.Any(p => p.owner == production.Id), "UI hook did not bind");
            foreach (bool forced in new[] { false, true }) {
                foreach (int count in new[] { 0, 1, 2 }) {
                    pairs = Enumerable.Range(0, count).Select(i => ("target-port-" + i, "own-port")).ToList();
                    var page = Page(); int before = sends;
                    page.OnUIRefresh(Prompt(forced));
                    Check(sends == before + (count == 1 ? 1 : 0), "Wrong zero/one/multiple port behavior");
                    if (count == 1) {
                        Check(sent.strName == (forced ? "SHIPForceDockNoPilot" : "SHIPDock") &&
                            sent.LootCTsThem.aCOs.SequenceEqual(new[] { "Port#target-port-0=1.0x1" }), "Native selected port payload changed");
                        Check(Options(page).Count == 0 && us.PrimaryDockingPortID == "own-port", "Native send cleanup or own active port changed");
                    }
                }
            }
            // One valid option for the active own port, despite a second raw
            // pairing with another own port. Native filtering owns this choice.
            pairs = new List<(string, string)> { ("chosen-target", "own-port"), ("other-target", "other-own-port") };
            int initial = sends; Page().OnUIRefresh(Prompt());
            Check(sends == initial + 1 && sent.LootCTsThem.aCOs[0] == "Port#chosen-target=1.0x1", "Active own port filtering was bypassed");
            var message = Prompt(); Page().OnUIRefresh(message); int once = sends;
            Page().OnUIRefresh(message); Check(sends == once, "Two MFDs submitted the same port prompt twice");
            SinglePortDocking.SetEnabled(false); initial = sends;
            var disabled = Page(); disabled.OnUIRefresh(Prompt()); Check(sends == initial && Options(disabled).Count == 1, "Disabled shortcut did not retain native menu");
            SinglePortDocking.SetEnabled(true);
            allowed = false; triggerChecks = 0;
            var denied = Page(); denied.OnUIRefresh(Prompt());
            Check(sends == initial && Options(denied).Count == 1 && triggerChecks == 1, "Shortcut bypassed native ApplyInteraction trigger check");
            allowed = true;
            var unrelated = Prompt(); unrelated.Interaction.strName = "SHIPDockAllow";
            Page().OnUIRefresh(unrelated); Check(sends == initial, "Clearance or unrelated replies were auto-selected");
            Page(true).OnUIRefresh(Prompt()); Check(sends == initial, "Public ATC listening page sent a request");
            Page().OnUIRefresh(Prompt(sender: "OTHER|FACILITY")); Check(sends == initial, "Unrelated facility message was auto-selected");
            Page().OnUIRefresh(Prompt(sender: "O-TARGET|FACILITY")); Check(sends == initial + 1, "Native target facility was excluded");
            var stale = Page(); var staleMessage = Prompt();
            // Opening/rebuilding a historical conversation does not invoke our
            // hook. It must not send messages just because a menu was queried.
            AccessTools.Method(typeof(MFDComms), "BuildInvIas").Invoke(stale, new object[] { staleMessage.Interaction, captain });
            Check(sends == initial + 1, "Building historical options sent a message");
            Console.WriteLine("Single-port docking: " + checks + " native UI/selection/config/asset-contract checks passed.");
            return 0;
        } finally { production.UnpatchSelf(); fixture.UnpatchSelf(); SinglePortDocking.Flag = null; }
    }
}
