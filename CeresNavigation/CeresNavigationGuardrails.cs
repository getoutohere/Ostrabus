using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Reflection;
using HarmonyLib;
using Ostranauts.Core;
using Ostranauts.ShipGUIs.Utilities;
using Ostranauts.Ships;
using Ostranauts.Ships.AIPilots;
using Ostranauts.Ships.Commands;
using Ostrabus.ShipNavigation;
using UnityEngine.SceneManagement;

namespace Ostrabus.CeresNavigation;

internal static class CeresNavigationGuardrails {
    const double ReturnCheckInterval = 30;
    internal const double AstronomicalUnitKm = 149597872;
    internal const double LostShipHomeDistanceKm = 200000;
    const string LegacyReturnKey = "CeresNavigation.ReturnRouteQueued";
    internal const string RejectedPlanKey = "CeresNavigation.ReturnAfterRejectedPlan";
    sealed class CheckTime { internal double Next, Last; }
    static ConditionalWeakTable<AIShip, CheckTime> checks = new();
    static readonly List<string> factions = new(1);
    static readonly AccessTools.FieldRef<NavData, List<NavDataPoint>> Points = AccessTools.FieldRefAccess<NavData, List<NavDataPoint>>("_navPoints");
    internal static void OnSceneUnloaded(Scene scene) => ResetRuntimeState();
    internal static void ResetRuntimeState() { checks = new(); factions.Clear(); }
    internal static bool IsCeresFactionShip(Ship ship) {
        if (ship == null || ship.bDestroyed || CrewSim.system == null) return false;
        string owner = CrewSim.system.GetShipOwner(ship.strRegID);
        return owner == "CCRE" || owner == "GalileanConfederacy" || owner == "BeltPirates";
    }
    internal static Ship FindHome(Ship ship, AIShip ai = null) {
        if (ship?.objSS == null || CrewSim.system == null) return null;
        ai ??= NavigationFuel.Controller(ship);
        string owner = CrewSim.system.GetShipOwner(ship.strRegID);
        Ship assigned = string.IsNullOrEmpty(ai?.HomeStation) ? null : CrewSim.system.GetShipByRegID(ai.HomeStation);
        if (assigned != null && assigned != ship && !assigned.bDestroyed && assigned.objSS != null &&
            !assigned.HideFromSystem && CrewSim.system.GetShipOwner(assigned.strRegID) == owner) return assigned;
        factions.Clear(); factions.Add(owner);
        Ship home = CrewSim.system.GetNearestStation(ship.objSS.vPosx, ship.objSS.vPosy, excludeOutposts: false, factions);
        return home != null && home != ship && !home.bDestroyed && !home.HideFromSystem && home.objSS != null ? home : null;
    }
    static bool HasUsableTorch(Ship ship) => ship.fFusionThrustMax > 0 && ship.fShallowFusionRemain > 0;
    internal static bool CannotReturnWithRCS(Ship ship, Ship home, double maxSpeed) {
        if (ship?.objSS == null || home?.objSS == null || HasUsableTorch(ship)) return false;
        double remaining = ship.DeltaVRemainingRCS;
        double required = AIShipManager.GetDeltaVNeededToTargetFullTrip(ship.objSS, home.objSS, maxSpeed, ship.bFusionReactorRunning);
        return !NavigationFuel.Finite(remaining) || !NavigationFuel.Finite(required) || remaining <= required;
    }
    internal static bool LacksRoundTripReserve(Ship ship, ShipSitu target, Ship home, double maxSpeed) {
        if (ship?.objSS == null || target == null || home?.objSS == null) return true;
        double outbound = AIShipManager.GetDeltaVNeededToTargetFullTrip(ship.objSS, target, maxSpeed, ship.bFusionReactorRunning);
        double back = AIShipManager.GetDeltaVNeededToTargetFullTrip(target, home.objSS, maxSpeed, ship.bFusionReactorRunning);
        double remaining = ship.DeltaVRemainingRCS, required = 1.2 * (outbound + back);
        return !NavigationFuel.Finite(remaining) || !NavigationFuel.Finite(required) || remaining <= required;
    }
    internal static bool PlanHasReserve(Ship ship, Ship home, NavData plan, double maxSpeed) {
        if (plan?.Destination?.ObjSS == null || plan.Origin == null || home?.objSS == null) return false;
        if (plan.IsUsingTorchDrive()) return true; // Native torch planning retains fuel authority.
        if (!NavigationFuel.Finite(plan.Destination.ArrivalTime) || !NavigationFuel.Finite(plan.Origin.ArrivalTime) ||
            plan.Destination.ArrivalTime <= plan.Origin.ArrivalTime || plan.Destination.FuelLevel < 0) return false;
        double predictedFuel = plan.Origin.FuelLevel;
        double epoch = StarSystem.fEpoch;
        var points = Points(plan);
        if (epoch >= plan.Destination.ArrivalTime) predictedFuel = plan.Destination.FuelLevel;
        else for (int i = 1; i < points.Count; i++) {
            var previous = points[i - 1]; var next = points[i];
            if (epoch > next.ArrivalTime) continue;
            double duration = next.ArrivalTime - previous.ArrivalTime;
            if (!(duration > 0) || !NavigationFuel.Finite(duration)) return false;
            double progress = Math.Max(0, Math.Min(1, (epoch - previous.ArrivalTime) / duration));
            predictedFuel = previous.FuelLevel + (next.FuelLevel - previous.FuelLevel) * progress;
            break;
        }
        double outbound = predictedFuel - plan.Destination.FuelLevel;
        var predictedHome = new ShipSitu(home.objSS, false);
        if ((predictedHome.bIsBO || predictedHome.bBOLocked || predictedHome.bOrbitLocked) && !string.IsNullOrEmpty(predictedHome.strBOPORShip)) {
            BodyOrbit body = CrewSim.system.GetBO(predictedHome.strBOPORShip);
            if (!global::Ostrabus.Orbital.NativeEphemeris.State(body, plan.Destination.ArrivalTime, out var position, out var velocity)) return false;
            predictedHome.vPosx = position.X + predictedHome.vBOOffsetx; predictedHome.vPosy = position.Y + predictedHome.vBOOffsety;
            predictedHome.vVelX = velocity.X; predictedHome.vVelY = velocity.Y;
        }
        double backDv = AIShipManager.GetDeltaVNeededToTargetFullTrip(plan.Destination.ObjSS, predictedHome, maxSpeed, false);
        double backFuel = ship.CalculateRCSFuelConsumption(backDv);
        double fuel = ship.GetRCSRemain(), required = 1.2 * (Math.Max(0, outbound) + backFuel);
        return NavigationFuel.Finite(outbound) && outbound >= 0 && NavigationFuel.Finite(backFuel) && backFuel >= 0 &&
            NavigationFuel.Finite(fuel) && NavigationFuel.Finite(required) && fuel > required;
    }
    static bool PatrolRole(AIShip ai) => ai.AIType == AIType.Navy || ai.AIType == AIType.Pirate || ai.AIType == AIType.Police;
    internal static void CheckReturn(AIShip ai) {
        Ship ship = ai?.Ship;
        if (ai?.Blackboard == null || ai.AICharacter == null || !IsCeresFactionShip(ship) || !ship.IsAIShip || ship.objSS == null || ship.bDestroyed ||
            ship.HideFromSystem || ship.DMGStatus == Ship.Damage.Derelict || ship.NavPlayerManned ||
            ReferenceEquals(CrewSim.coPlayer?.ship, ship)) return;
        if ((ship.HasDockGroup || ship.IsDocked()) && ship.IsDockedWithPlayer()) return;
        bool legacy = ai.Blackboard.Get<bool>(LegacyReturnKey);
        // Migrate persistent old intent once; it must never act as a dedup latch.
        if (legacy) {
            Ship oldHome = FindHome(ship, ai);
            if (oldHome != null) { StationReturn.Request(ai, oldHome); ai.Blackboard.Remove(LegacyReturnKey); }
        }
        if (StationReturn.Pending(ai) || !PatrolRole(ai)) return;
        var time = checks.GetValue(ai, _ => new CheckTime());
        double epoch = StarSystem.fEpoch;
        if (epoch >= time.Last && epoch < time.Next) return;
        time.Last = epoch; time.Next = epoch + ReturnCheckInterval;
        if (ship.IsStation() || ship.IsStationHidden() || ship.IsDocked() || ship.HasDockGroup) return;
        NavData plan = ship.objSS.NavData;
        if (HasUsableTorch(ship) && plan != null && plan.IsUsingTorchDrive()) return;
        Ship home = FindHome(ship, ai);
        if (home == null || ship.targetRegID == home.strRegID || ship.shipScanTarget == home) return;
        double speed = ai.AICharacter.MaxSpeed(null);
        bool unsafeRoute = plan != null && !PlanHasReserve(ship, home, plan, speed);
        double remaining = ship.DeltaVRemainingRCS;
        if (!unsafeRoute && NavigationFuel.Finite(remaining) && !AIShipManager.BingoFuelCheck(ship, home, speed)) return;
        StationReturn.Request(ai, home);
        Ostrabus.OstrabusPlugin.Logger?.LogInfo($"[CeresNavigation] {ship.strRegID}: returning to {home.strRegID} for fuel reserve.");
    }
    [HarmonyPatch(typeof(AIShip), nameof(AIShip.RunAI))]
    internal static class ReturnGuardPatch {
        [HarmonyPrefix, HarmonyPriority(Priority.High)]
        internal static void Prefix(AIShip __instance) {
            try { CheckReturn(__instance); }
            catch (Exception error) { Ostrabus.OstrabusPlugin.Logger?.LogError("[CeresNavigation] " + error); }
        }
    }
    [HarmonyPatch]
    internal static class PatrolHomePatch {
        static IEnumerable<MethodBase> TargetMethods() {
            yield return AccessTools.Method(typeof(NavyPilot), nameof(NavyPilot.GetTarget));
            yield return AccessTools.Method(typeof(PiratePilot), nameof(PiratePilot.GetTarget));
            yield return AccessTools.Method(typeof(PolicePilot), nameof(PolicePilot.GetTarget));
        }
        [HarmonyPostfix]
        internal static void Postfix(BaseCharacter __instance, ref TargetData __result) {
            if (!IsCeresFactionShip(__instance.ShipUs) || __result != null && !__instance.Blackboard.Get<bool>(RejectedPlanKey)) return;
            Ship home = FindHome(__instance.ShipUs);
            if (home != null) { __result = new TargetData(home); __instance.Blackboard.Remove(RejectedPlanKey); }
        }
    }
    [HarmonyPatch(typeof(TargetNewIfNone), "GenerateNavData")]
    internal static class PlannedReservePatch {
        [HarmonyPostfix]
        internal static void Postfix(TargetNewIfNone __instance, TargetData target, ref NavData __result) {
            Ship ship = __instance.ShipUs;
            if (__result == null || !IsCeresFactionShip(ship) || !ship.IsAIShip) return;
            AIShip ai = NavigationFuel.Controller(ship);
            if (ai == null || !PatrolRole(ai)) return;
            Ship home = FindHome(ship, ai);
            if (home == null || target?.Ship == home || target?.TargetRegId == home.strRegID) return;
            if (!PlanHasReserve(ship, home, __result, ai.AICharacter.MaxSpeed(null))) {
                __result = null;
                ai.Blackboard.Set(RejectedPlanKey, true);
            }
        }
    }
}
[HarmonyPatch(typeof(ConsoleResolver), "ResolveString")]
internal static class CeresNavigationConsoleCommands
{
    [HarmonyPrefix]
    private static bool ResolveStringPrefix(ref string strInput)
    {
        if (strInput != "pruneceres") return true;
        CeresStrandedPruner.Run();
        return false;
    }
}

internal static class CeresStrandedPruner
{
    private static bool IsEligible(Ship ship, bool includeDocked)
    {
        return ship != null && !ship.bDestroyed && ship.IsAIShip && ship.LoadState == Ship.Loaded.Shallow &&
            ship.DMGStatus != Ship.Damage.Derelict && !ship.IsStation() && !ship.IsStationHidden() &&
            (includeDocked || !ship.IsDocked()) && CeresNavigationGuardrails.IsCeresFactionShip(ship) &&
            CrewSim.coPlayer?.ship != ship && !ship.IsDockedWithPlayer();
    }

    private static bool TryPrune(Ship ship, Ship home, CondTrigger prunableTrigger, HashSet<string> skippedCrewShips,
        ConsoleToGUI console, ref int removedShips, ref int removedCrew, ref int skippedCrew, string reason)
    {
        bool wasDocked = ship.IsDocked();
        List<CondOwner> crew = ship.GetPeople(bAllowDocked: false);
        if (crew == null || crew.Count == 0) return false;
        bool allPrunable = true;
        for (int i = 0; i < crew.Count; i++)
        {
            CondOwner person = crew[i];
            if (person == null || !prunableTrigger.Triggered(person, logOutcome: false))
            {
                allPrunable = false;
                break;
            }
        }
        if (!allPrunable)
        {
            if (skippedCrewShips.Add(ship.strRegID)) skippedCrew++;
            return false;
        }

        for (int i = 0; i < crew.Count; i++)
        {
            CondOwner person = crew[i];
            person.RemoveFromCurrentHome(bForce: true);
            person.Destroy();
            removedCrew++;
        }

        if (ship.GetPeople(bAllowDocked: false).Count != 0)
        {
            console?.LogInfo($"[PruneCeres] {ship.strRegID}: crew remained after pruning; hull kept.");
            return false;
        }

        AIShipManager.UnregisterShip(ship);
        ship.ToggleVis(bShow: false);
        ship.Destroy(isDespawning: !wasDocked);
        removedShips++;
        console?.LogInfo($"[PruneCeres] Removed {reason} {ship.strRegID} ({crew.Count} pruneable crew; home {home.strRegID}).");
        return true;
    }

    internal static void Run()
    {
        ConsoleToGUI console = ConsoleToGUI.instance;
        int scanned = 0, strandedMatches = 0, distant = 0, distantRemoved = 0, removedShips = 0, removedCrew = 0, skippedCrew = 0;
        try
        {
            if (CrewSim.system == null)
            {
                console?.LogInfo("[PruneCeres] No active simulation.");
                return;
            }

            CondTrigger prunableTrigger = DataHandler.GetCondTrigger("TIsNPCPrunable");
            List<Ship> ships = new(CrewSim.system.GetAllLoadedShips());
            HashSet<string> skippedCrewShips = new(StringComparer.Ordinal);
            for (int i = 0; i < ships.Count; i++)
            {
                Ship ship = ships[i];
                if (!IsEligible(ship, includeDocked: false)) continue;

                AIShip ai = AIShipManager.GetAIShipByRegID(ship.strRegID);
                if (ai == null) continue;
                Ship home = CeresNavigationGuardrails.FindHome(ship);
                if (home == null || home == ship) continue;
                scanned++;

                double maxSpeed = AIShip.CalculateMaxSpeed(ship, null);
                if (!CeresNavigationGuardrails.CannotReturnWithRCS(ship, home, maxSpeed)) continue;
                strandedMatches++;
                TryPrune(ship, home, prunableTrigger, skippedCrewShips, console, ref removedShips, ref removedCrew, ref skippedCrew, "stranded");
            }

            for (int i = 0; i < ships.Count; i++)
            {
                Ship ship = ships[i];
                if (!IsEligible(ship, includeDocked: true)) continue;
                Ship home = CeresNavigationGuardrails.FindHome(ship);
                if (home == null || home == ship) continue;
                double distanceKm = ship.GetRangeTo(home) * CeresNavigationGuardrails.AstronomicalUnitKm;
                if (double.IsNaN(distanceKm) || double.IsInfinity(distanceKm) || distanceKm <= CeresNavigationGuardrails.LostShipHomeDistanceKm) continue;
                distant++;
                if (TryPrune(ship, home, prunableTrigger, skippedCrewShips, console, ref removedShips, ref removedCrew, ref skippedCrew,
                    $"lost ({distanceKm:N0} km from home)")) distantRemoved++;
            }
            console?.LogInfo($"[PruneCeres] Scanned {scanned} eligible CCRE/Galcon/Belt Pirates AI ships; stranded pass found {strandedMatches}; distant pass found {distant} over {CeresNavigationGuardrails.LostShipHomeDistanceKm:N0} km from home and removed {distantRemoved}; total removed {removedShips} ships and {removedCrew} crew; {skippedCrew} ships had non-pruneable crew.");
        }
        catch (Exception ex)
        {
            console?.LogInfo($"[PruneCeres] Aborted after removing {removedShips} ships and {removedCrew} crew: {ex.Message}");
            Ostrabus.OstrabusPlugin.Logger?.LogError($"[PruneCeres] {ex}");
        }
    }
}


