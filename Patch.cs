using HarmonyLib;
using System;
using System.Text;
using TMPro;
using UnityEngine;
using System.Collections.Generic;
using UnityEngine.EventSystems;
using System.Xml.Schema;
using System.Runtime.InteropServices;
using System.Linq;
using Ostranauts.Core.Models;
using Ostranauts.Tools.ExtensionMethods;
using Ostranauts.ShipGUIs.Jobs;
using Ostranauts.UI.MegaToolTip;
using Ostranauts.Core.Tutorials;

namespace Ostrabus;

[HarmonyPatch]
class Patch
{
    [HarmonyPatch(typeof(GUIBountyDetails), nameof(GUIBountyDetails.Init))]
    public static class GUIBountyDetailsPatch
    {
        private static readonly AccessTools.FieldRef<GUIBountyDetails, TMP_Text> TxtLocationField =
            AccessTools.FieldRefAccess<GUIBountyDetails, TMP_Text>("txtLocation");

        [HarmonyPostfix]
        public static void Init_Postfix(GUIBountyDetails __instance, JsonBountySave bountySave) {
            if (__instance == null || bountySave == null) return;
            try {
                CondOwner co = bountySave.GetCO();
                TMP_Text txtLocation = TxtLocationField(__instance);
                if (co == null && txtLocation != null) { txtLocation.text = $"{txtLocation.text} (MIA)"; }
            } catch (Exception ex) {
                ConsoleToGUI.instance?.LogInfo($"[BountyRow] Exception trying to modify UI text: {ex}");
            }
        }
    }
    
    [HarmonyPatch(typeof(GUIBountyRowSingle), nameof(GUIBountyRowSingle.Init))]
    public static class GUIBountyRowSinglePatch
    {
        private static readonly AccessTools.FieldRef<GUIBountyRowSingle, TMP_Text> TxtLocationField =
            AccessTools.FieldRefAccess<GUIBountyRowSingle, TMP_Text>("txtLocation");

        [HarmonyPostfix]
        public static void Init_Postfix(GUIBountyRowSingle __instance, JsonBountySave bountySave){
            if (__instance == null || bountySave == null) return;
            try {
                CondOwner co = bountySave.GetCO();
                TMP_Text txtLocation = TxtLocationField(__instance);
                if (co == null && txtLocation != null) { txtLocation.text = "(MIA)"; }
            } catch (Exception ex) {
                ConsoleToGUI.instance?.LogInfo($"[BountyRow] Exception trying to modify UI text: {ex}");
            }
        }
    }

    [HarmonyPatch(typeof(GUIBounties), "OnBountySelected")]
    public static class GUIBountiesPatch {
        [HarmonyPrefix]
        public static bool OnBountySelected_Prefix(GUIBounties __instance, JsonBountySave bountySave) {
            List<Ship> shipList;
            if (__instance == null || bountySave == null) return true;
            try {
                CondOwner co = bountySave.GetCO();
                if (co == null) { ConsoleToGUI.instance.LogInfo($"[Bounties] CO not found!"); return true; }                              

                ConsoleToGUI.instance.LogInfo($"[Bounties] Selected bounty: {co.strName}");
                var ownShips = getOwnShips();
                shipList = new List<Ship>(CrewSim.system.GetAllLoadedShips());
                foreach (Ship ship in shipList) { 
                    if (ship.GetCOByID(co.strID) != null) { 
                        ConsoleToGUI.instance.LogInfo($"[Bounties] found {co.strID} in ship {ship.strRegID} ({ship.publicName})"); 
                        foreach (var ownShip in ownShips) {
                            if (ownShip == null) continue; 
                            ConsoleToGUI.instance.LogInfo($"[Bounties]\t{ship.strRegID} --> {ownShip.strRegID}: {SpaceDist(ship.GetRangeTo(ownShip))}");
                        }
                    } 
                }    
            }
            catch (Exception ex) {
                ConsoleToGUI.instance?.LogInfo($"[Bounties] Exception in prefix hook: {ex}");
            }
            return true; 
        }
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(GUIPDA), "GetKnownSocialContacts")]
    static List<Relationship> GUIPDA_GetKnownSocialContacts_Postfix(List<Relationship> __result)
    {
        int removed = __result.RemoveAll(QoL.Socials.ContactVisibility.Hide);
        ConsoleToGUI.instance.LogInfo($"[Socials] hiding {removed} missing contacts or construction drones");
        __result.Sort((x, y) => x.pspec.strFirstName.CompareTo(y.pspec.strFirstName));
        ConsoleToGUI.instance.LogInfo($"[Socials] return {__result.Count} contacts");
        return __result;
    }

    private const double KILOMETERS_PER_AU = 149_597_870.7;

    private static readonly AccessTools.FieldRef<GUISocialsRow, string> StrCOIDField = 
            AccessTools.FieldRefAccess<GUISocialsRow, string>("strCOID");

    [HarmonyPrefix]
    [HarmonyPatch(typeof(GUISocialsRow), "OnPointerClick")]
    static bool GUISocialsRow_OnPointerClick_Prefix(GUISocialsRow __instance, PointerEventData eventData)
    {
        List<Ship> shipList;
        string strCOID = StrCOIDField(__instance);
        switch (eventData.clickCount) {
        // Print the distance to an NPC and what ship they're on 
        case 1:
            var ownShips = getOwnShips();
            shipList = new List<Ship>(CrewSim.system.GetAllLoadedShips());
            foreach (Ship ship in shipList) { 
                if (ship.GetCOByID(strCOID) != null) { 
                    ConsoleToGUI.instance.LogInfo($"[Socials] found {strCOID} in ship {ship.strRegID} ({ship.publicName})"); 
                    foreach (var ownShip in ownShips) {
                        if (ownShip == null) continue; 
                        ConsoleToGUI.instance.LogInfo($"[Socials]\t{ship.strRegID} --> {ownShip.strRegID}: {SpaceDist(ship.GetRangeTo(ownShip))}");
                    }
                    break;
                } 
            }
            if (DataHandler.mapCOs != null && DataHandler.mapCOs.TryGetValue(strCOID, out CondOwner value)) {
                // CRITICAL FIX: The game crashes here because it checks 'value.ship' without verifying if 'value' is null first!
                if (value == null) {
                    ConsoleToGUI.instance.LogInfo($"[MyMod] Intercepted click for '{strCOID}', but CondOwner is null (Deceased/Missing contact). Blocking game crash.");
                    return false; 
                }
            }
            else {
                ConsoleToGUI.instance.LogInfo($"[MyMod] strCOID '{strCOID}' not found in mapCOs.");
                return false; 
            }
            return true;
        case 2:
        // Double click on a portrait in Spacebook to delete that NPC (and their ship) from the game if they're labeled "prunable"
            CondTrigger condTrigger = DataHandler.GetCondTrigger("TIsNPCPrunable");
            shipList = new List<Ship>(CrewSim.system.GetAllLoadedShips());
            foreach (Ship ship in shipList) {
                var co = ship.GetCOByID(strCOID);
                if (co != null) { 
                    ConsoleToGUI.instance.LogInfo($"[Socials] found {strCOID} in ship {ship.strRegID} ({ship.publicName})"); 
                    foreach (CondOwner c in ship.GetCOs(condTrigger, bSubObjects: false, bAllowDocked: false, bAllowLocked: false)) {
                        if (c.strName == co.strName) {
                            prunePersonAndMaybeShip(co, ship, ship.GetPeople(bAllowDocked: false).Count);
                            return true;
                        }
                    }
                    ConsoleToGUI.instance.LogInfo($"[Socials] {strCOID} is not eligible for pruning");
                    return true; 
                }
            }
            ConsoleToGUI.instance.LogInfo($"[Socials] could not find {strCOID}"); 
            return true;
        default: return true;
        }
    }

    static List<Ship> getOwnShips() {
        List<Ship> ships = [];
        foreach(var shipName in CrewSim.GetSelectedCrew().GetShipsOwned()) {
            ships.Add(CrewSim.GetLoadedShipByRegId(shipName));
        }
        return ships;
    }

    static void prunePersonAndMaybeShip(CondOwner co, Ship ship, int shipCoCount) {
        co.RemoveFromCurrentHome(bForce: true);
		co.Destroy();
        ConsoleToGUI.instance.LogInfo($"[Socials] Pruned person {co.strName}");
        if (shipCoCount == 1 && ship.IsAIShip) {
            ship.ToggleVis(bShow: false);
			ship.Destroy();
            ConsoleToGUI.instance.LogInfo($"[Socials] Pruned ship {ship.strRegID} ({co.strName} was last occupant)");
        }
    }

    static bool prunePerson(CondOwner co) {
        if (co == null) { return false; }
        if (!co.IsHumanOrRobot) { return false; }
        co.RemoveFromCurrentHome(bForce: true);
        co.Destroy();
        return true;
    }

    static string SpaceDist(double au)
    {
        double kilometers = au * KILOMETERS_PER_AU;
        if (Math.Abs(kilometers) <= 1_000_000) return $"{kilometers:N1} km";
        else return $"{au:F2} AU";
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(ConsoleResolver), "ResolveString")]
    static bool ConsoleResolver_ResolveString_Prefix(ConsoleResolver __instance, ref string strInput) {
        if (strInput == "massfix") {
            MiscBugFixes.MassRepair.Run();
            return false;
        }
        if (strInput == "bingo") {
            FindBingoFuelAI();
            return false;
        }
        if (strInput == "refuelai"){
            var refueled = RefuelAIShips(FindBingoFuelAI());
            ConsoleToGUI.instance.LogInfo($"Refueled {refueled} AI ships.");
            return false;
        }
        if (strInput == "garbagecollect") {
            try
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                strInput += $"\nGC Done!";
                return false;
            }
            catch (Exception ex)
            {
                strInput += $"\nGC error! {ex}";
                return false;
            }
        }
        if (strInput == "cleanc") {
            CondOwner condOwner = null;
            CondOwner selected = GUIMegaToolTip.Selected;
            if (selected == null || !selected.IsHumanOrRobot) {
                ConsoleToGUI.instance.LogInfo($"Thing selected to human or robot!");
                return false;
            }
            condOwner = selected; 
            var name = condOwner.strName;
            if (prunePerson(condOwner)) {
                ConsoleToGUI.instance.LogInfo($"Destroyed person {name}!");
            } else {
                ConsoleToGUI.instance.LogInfo($"Did not destroy selected {name}!");
            }
            return false;
        }
        if (strInput.StartsWith("remove ")) {
            string[] parts = strInput.Split((char[])null);
            if (parts.Length != 2) {
                strInput += $"\ninvalid command: remove [ship regId]";
                return false;
            }
            var target = parts[1];
            strInput += $"\nTargeting {target}";
            var ship = getShipByRegId(target);
            if (ship == null) { 
                strInput += $"\nCouldn't find ship {target}!";
                return false;
            }
            if (ship.IsAIShip == false) {
                strInput += $"\nNot an AI ship; aborting!";
                return false;
            }
            var pruneCount = PruneShip(ship.strRegID, true);
            var remainingPeople = ship.GetPeople(bAllowDocked: false).Count;
            if (remainingPeople == 0) {
                ship.ToggleVis(bShow: false);
			    ship.Destroy();
                ConsoleToGUI.instance.LogInfo($"Removed ship {ship.strRegID}");
            } else {
                strInput += $"\nDid not remove ship {target}, {remainingPeople} people still onboard!";
            }
            return false;
        }
        // Destroy half of prunable NPCs on VORB
        if (strInput == "prunevorb") {
            PruneShip("VORB", false);
            return false;
        }
        if (strInput == "prunevnca") {
            PruneShip("VNCA", false);
            return false;
        }
        if (strInput == "prunemall") {
            PruneShip("VNCA_SD", false);
            return false;
        }
        return true;
    }

    static int PruneShip(string regId, bool pruneAll) {
        int pruned = 0;
        CondTrigger condTrigger = DataHandler.GetCondTrigger("TIsNPCPrunable");
        var ship = getShipByRegId(regId);
        if (ship == null) {
            return 0;
        }
        var cos = ship.GetCOs(condTrigger, bSubObjects: false, bAllowDocked: false, bAllowLocked: false);
        ConsoleToGUI.instance.LogInfo($"[Prune] {cos.Count} people on ship {ship.strRegID}");
        int numToPrune = cos.Count / 2;
        if (pruneAll == true) { numToPrune = cos.Count; }
        ConsoleToGUI.instance.LogInfo($"[Prune] Pruning {numToPrune} people on ship {ship.strRegID}");
        foreach (CondOwner co in cos) {
            if (co == null || pruned > numToPrune) { continue; }
            co.RemoveFromCurrentHome(bForce: true);
            co.Destroy();
            ConsoleToGUI.instance.LogInfo($"[Prune] Pruned person {co.strName} from ship {ship.strRegID}");
            pruned = pruned + 1;
        }
        return pruned;
    }

    static Ship getShipByRegId(string regId) {
        var shipList = new List<Ship>(CrewSim.system.GetAllLoadedShips());
        foreach (Ship ship in shipList) { if (ship.strRegID == regId) { return ship; } }
        return null;
    }

    static List<Ship> FindBingoFuelAI()
    {
        var shipList = new List<Ship>(CrewSim.system.GetAllLoadedShips());
        var totalCount = shipList.Count;
        var removed = shipList.RemoveAll(ship => 
            (ship.IsDerelict() || !ship.IsAIShip || ship.GetPeople(false).Count <= 0) || ship.GetRCSRemain() > 0);
        ConsoleToGUI.instance.LogInfo($"{totalCount - removed} crewed AI ships with bingo fuel (total ships: {totalCount})");
        return shipList;
    }
    static int RefuelAIShips(List<Ship> shipList) {
        int refueled = 0;
        foreach (Ship ship in shipList) {
            try {
                double before = ship.GetRCSRemain();
                double rcsMax = ship.GetRCSMax();
                if (rcsMax <= 0 || double.IsNaN(rcsMax) || double.IsInfinity(rcsMax)) {
                    ConsoleToGUI.instance.LogInfo($"[RefuelAI] {ship.strRegID}: no usable RCS capacity ({rcsMax}); skipped.");
                    continue;
                }

                double amount = Math.Min(rcsMax / 2, Math.Max(0, rcsMax - before));
                ship.RefuelRCS((float)amount);

                double after = ship.GetRCSRemain();
                if (after > before) refueled++;
                ConsoleToGUI.instance.LogInfo($"[RefuelAI] {ship.strRegID} ({ship.LoadState}): RCS {before:N2} -> {after:N2} / {rcsMax:N2} kg.");
            } catch (Exception ex) {
                ConsoleToGUI.instance?.LogInfo($"[RefuelAI] {ship.strRegID}: refueling failed: {ex}");
            }
        }
        return refueled;
    }
 }
