using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using Ostranauts.Core;
using Ostranauts.Ships;
using Ostranauts.Ships.AIPilots;
using Ostranauts.Ships.Commands;
using Ostranauts.Ships.Comms;

internal static partial class Program {
    static bool sendSucceeds;
    static int attempts;
    static Func<string, bool> sendOutcome;
    static readonly Dictionary<string, AIShip> controllers = new();
    static int controllerLookups;
    static bool Unequal(UnityEngine.Object __0, UnityEngine.Object __1, ref bool __result) {
        __result = !ReferenceEquals(__0, __1);
        return false;
    }
    static bool Send(string __1, ref bool __result) { attempts++; __result = sendOutcome?.Invoke(__1) ?? sendSucceeds; return false; }
    static bool NoController(string __0, ref AIShip __result) { controllerLookups++; controllers.TryGetValue(__0, out __result); return false; }

    static int CheckCombatLoop() {
        var fixture = new Harmony("offline.surrender-combat-loop");
        fixture.Patch(AccessTools.Method(typeof(UnityEngine.Object), "op_Equality"),
            prefix: new HarmonyMethod(typeof(Program), nameof(Equal)));
        fixture.Patch(AccessTools.Method(typeof(UnityEngine.Object), "op_Inequality"),
            prefix: new HarmonyMethod(typeof(Program), nameof(Unequal)));
        fixture.Patch(AccessTools.Method(typeof(Comms), "SendMessage", new[] { typeof(string), typeof(string), typeof(string) }),
            prefix: new HarmonyMethod(typeof(Program), nameof(Send)));
        fixture.Patch(AccessTools.Method(typeof(AIShipManager), "GetAIShipByRegID"),
            prefix: new HarmonyMethod(typeof(Program), nameof(NoController)));
        try {
            T Empty<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
            var pirate = Empty<Ship>(); pirate.strRegID = "O-DW74";
            var player = Empty<Ship>(); player.strRegID = "O-GXD";
            var formerOpponent = Empty<Ship>(); formerOpponent.strRegID = "O-4XDP";
            AccessTools.Field(typeof(Ship), "<objSS>k__BackingField").SetValue(player, Empty<ShipSitu>());
            AccessTools.Field(typeof(Ship), "<Comms>k__BackingField").SetValue(pirate, Empty<Comms>());
            var surrender = Empty<SurrenderOffer>();
            var initiate = Empty<InitiateAgainstBiggestThreat>();
            AccessTools.Field(typeof(BaseCommand), "_shipUs").SetValue(surrender, pirate);
            AccessTools.Field(typeof(BaseCommand), "_shipUs").SetValue(initiate, pirate);
            var board = new Blackboard();
            board.Set("Enemies", new List<Ship> { player });
            board.Set("ClosestEnemy", player);
            board.Set("NeedsRepairs", true);
            board.Set("SurrenderedTo", new List<Ship> { formerOpponent });
            player.shipCombatTarget = pirate;

            sendSucceeds = false;
            for (int cycle = 0; cycle < 6; cycle++) {
                Check(initiate.RunCommand(board).Status == CommandCode.Finished && pirate.shipCombatTarget == player,
                    "Native threat initiation did not acquire the player");
                Check(board.Get<bool>("InCombat") && board.Get<List<Ship>>("Combatants").Contains(player),
                    "Initiation failed to restore combat state");
                int before = attempts;
                Check(surrender.RunCommand(board).Status == CommandCode.Finished && attempts == before + 1,
                    "Failed native surrender did not report success");
                Check(pirate.shipCombatTarget == null && !board.Get<bool>("InCombat") && !board.HasKey("Combatants"),
                    "Failed surrender did not erase the engagement");
                Check(!board.Get<List<Ship>>("SurrenderedTo").Contains(player) && board.Get<List<Ship>>("Enemies").Contains(player),
                    "Failed surrender unexpectedly recorded the player as a surrendered-to ship or removed the threat");
            }

            sendSucceeds = true;
            initiate.RunCommand(board);
            Check(surrender.RunCommand(board).Status == CommandCode.Finished && board.Get<List<Ship>>("SurrenderedTo").Contains(player),
                "Successful-message control did not record the offer");

            string data = Path.Combine(Ostrabus.Development.InstallationPaths.Game, "Ostranauts_Data", "StreamingAssets", "data");
            var trigger = Definition(Path.Combine(data, "tsv", "output", "stakes", "condtrigs", "condTriggers_STKShipSurrender_AIOffer.json"),
                "CT_STKShipSurrender_AIOffer_Intro_Us");
            Check(((JArray)trigger["aForbids"]).ToObject<string[]>() is string[] forbids &&
                Array.IndexOf(forbids, "IsSTKShipSurrender_AIOffer_Cooldown") >= 0,
                "Authored surrender cooldown no longer rejects a fresh offer");
        } finally {
            fixture.UnpatchSelf();
        }
        Console.WriteLine(checks + " native surrender combat-loop checks passed; no Ostrabus gameplay patches loaded.");
        return 0;
    }
}
