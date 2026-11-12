using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using HarmonyLib;
using LitJson;
using Ostranauts.Core;
using Ostranauts.Ships;
using Ostranauts.Ships.AIPilots;
using Ostranauts.Ships.Commands;
using Ostranauts.ShipGUIs.Utilities;

static partial class TrafficChecks {
    // Investigation only: not linked into Ostrabus or installed by the plugin.
    static bool CandidateVisibleVisit(Ship shipTarget, ref bool __result) {
        if (!shipTarget.HideFromSystem) return true;
        __result = false;
        return false;
    }
    static bool Friendly(ref float __result) { __result = 50; return false; }
    static bool hiddenPorts = true;
    static bool VisitPorts(ref bool __result) { __result = hiddenPorts; return false; }
    static CondOwner hiddenSelected;
    static bool VisitSelected(ref CondOwner __result) { __result = hiddenSelected; return false; }
    sealed class ReusePositioned : Exception {}
    static bool StopBeforeCrew() => throw new ReusePositioned();

    static void HiddenVisitInvestigation(Harmony outer, CivilianPilot civilian, Ship visitor, Ship reserve) {
        int before = checks;
        var h = new Harmony("offline.hidden-visit.candidate");
        var boundary = new Harmony("offline.hidden-visit.boundaries");
        var eligibility = AccessTools.Method(typeof(CivilianPilot), "CanDockWith");
        var ports = AccessTools.PropertyGetter(typeof(Ship), nameof(Ship.HasDockingPorts));
        bool Accept(Ship hull) => (bool)eligibility.Invoke(civilian, new object[] { visitor, hull });
        try {
            boundary.Patch(AccessTools.Method(typeof(CondOwner), nameof(CondOwner.GetFactionScore), new[] { typeof(string) }), prefix: new HarmonyMethod(typeof(TrafficChecks), nameof(Friendly)));
            outer.Unpatch(ports, AccessTools.Method(typeof(TrafficChecks), nameof(Yes)));
            boundary.Patch(ports, prefix: new HarmonyMethod(typeof(TrafficChecks), nameof(VisitPorts)));
            reserve.DMGStatus = Ship.Damage.Used;
            reserve.objSS.vPosx = visitor.objSS.vPosx + 1e-7;
            var conditions = new[] { "ordinary", "derelict", "no-ports", "full", "active-ai", "attached", "self", "asteroid-no-ports" };
            var native = new Dictionary<string,bool>();
            foreach (bool patched in new[] { false, true }) {
                if (patched) h.Patch(eligibility, prefix: new HarmonyMethod(typeof(TrafficChecks), nameof(CandidateVisibleVisit)));
                foreach (bool hidden in new[] { false, true }) foreach (string condition in conditions) {
                    hiddenPorts = condition != "no-ports" && condition != "asteroid-no-ports";
                    reserve.HideFromSystem = hidden;
                    reserve.DMGStatus = condition == "derelict" ? Ship.Damage.Derelict : Ship.Damage.Used;
                    reserve.Classification = condition == "asteroid-no-ports" ? Ship.TypeClassification.Asteroid : Ship.TypeClassification.GroundStation;
                    Set(reserve, "nLoadState", condition == "full" ? Ship.Loaded.Full : Ship.Loaded.Shallow);
                    var links = (Dictionary<string,Ship>)AccessTools.Field(typeof(Ship), "aDocked").GetValue(reserve);
                    links.Clear(); reserve.CachedAllDockedShips = null;
                    if (condition == "attached") links["fixture-port"] = home;
                    AIShipManager.dictAIs[AIShipManager.strATCLast].Clear();
                    if (condition == "active-ai") {
                        var ai = Empty<AIShip>(); Set(ai, "_shipUs", reserve);
                        Set(ai, "_aiCharacter", new Program.Pilot { ShipUs = reserve, Role = AIType.Civilian });
                        AIShipManager.dictAIs[AIShipManager.strATCLast].Add(ai);
                    }
                    bool accepted = Accept(condition == "self" ? visitor : reserve);
                    string key = hidden + "/" + condition;
                    if (!patched) {
                        native[key] = accepted;
                        Check(accepted == (condition == "ordinary" || condition == "derelict" || condition == "asteroid-no-ports"), "Native eligibility differs: " + key);
                    } else Check(accepted == ((!hidden || condition == "self") && native[key]), "Candidate changed eligibility beyond hidden roaming destinations: " + key);
                }
                AIShipManager.dictAIs[AIShipManager.strATCLast].Clear();
                ((Dictionary<string,Ship>)AccessTools.Field(typeof(Ship), "aDocked").GetValue(reserve)).Clear();
                reserve.CachedAllDockedShips = null;
                reserve.Classification = Ship.TypeClassification.GroundStation;
                reserve.DMGStatus = Ship.Damage.Used; Set(reserve, "nLoadState", Ship.Loaded.Shallow); hiddenPorts = true;
                reserve.HideFromSystem = true;
                // Native RNG's ship-choice draw is fixed at .6; no alternate
                // visible vessel exists here, so the candidate must fall through.
                Ship chosen = civilian.GetTarget().Ship;
                Check(patched ? chosen != reserve && chosen.IsStation() : chosen == reserve, "Native new-target fallback did not match candidate scope");
                var visible = Hull("visible-visit-control"); visible.DMGStatus = Ship.Damage.Used;
                Check(civilian.GetTarget().Ship == (patched ? visible : reserve), "Visible crewless hull lost its visit eligibility");
                CrewSim.system.dictShips.Remove(visible.strRegID);
                Check(AIShip.FindShipForFaction("owner-faction") == reserve, "Candidate blocked the native reserve supply");
                // Explicit cargo destinations and remembered civilian partners
                // do not call the private roaming predicate, and must survive it.
                var cargoHull = Hull("hidden-cargo-control"); cargoHull.shipScanTarget = reserve;
                var cargo = new HaulerCargoPilot(home.strRegID, cargoHull, "unused-fixture-loot");
                Check(cargo.GetTarget().Ship == reserve, "Candidate changed an explicit freight/PASS target");
                CrewSim.system.dictShips.Remove(cargoHull.strRegID);
                visitor.shipUndock = reserve; instantResult = CommandCode.Finished;
                civilian.FFWD(new Undock(civilian), civilian.Blackboard);
                Check(visitor.shipScanTarget == reserve, "Candidate changed remembered-partner FFWD");
            }
            // A selection-only correction intentionally does not rewrite already
            // assigned or serialized targets, including a later hidden transition.
            reserve.HideFromSystem = false;
            var visibleChoice = civilian.GetTarget();
            Check(visibleChoice.Ship == reserve, "Candidate rejected a real visible rendezvous before retirement");
            visibleChoice.ApplyToShip(visitor);
            reserve.HideFromSystem = true;
            civilian.Blackboard.Set("TripFuelPrediction", "old-visit&12");
            var route = new NavData(visitor); visitor.objSS.NavData = route;
            var choose = new TargetNewIfNone(civilian);
            Check(choose.RunCommand(civilian.Blackboard).Status == CommandCode.Finished && visitor.shipScanTarget == reserve &&
                ReferenceEquals(visitor.objSS.NavData, route) && civilian.Blackboard.Get<string>("TripFuelPrediction") == "old-visit&12",
                "Selection-only candidate unexpectedly migrated an existing hidden visit");
            var saved = new JsonShip { strRegID = visitor.strRegID, strScanTargetID = reserve.strRegID,
                strTargetRegID = reserve.strRegID, objSituScanTarget = reserve.objSS.GetJSON() };
            visitor.json = JsonMapper.ToObject<JsonShip>(JsonMapper.ToJson(saved));
            visitor.ClearShipTarget(); visitor.CheckTargets();
            Check(visitor.shipScanTarget == reserve && visitor.targetRegID == reserve.strRegID &&
                !ReferenceEquals(visitor.shipSituTarget, reserve.objSS), "Native saved-target restoration did not reproduce independent target snapshot");
            choose.RunCommand(civilian.Blackboard);
            Check(visitor.shipScanTarget == reserve && ReferenceEquals(visitor.shipSituTarget, reserve.objSS) &&
                ReferenceEquals(visitor.objSS.NavData, route), "Native target continuation did not retain/refresh an already-saved hidden destination");

            // Execute native reserve lookup/reset/reposition in the creator, and
            // stop before crew/asset creation. Faction/damage presentation and
            // stationary TimeAdvance are boundaries; actual PlaceOrbitPosition runs.
            h.UnpatchSelf();
            boundary.Patch(AccessTools.Method(typeof(CrewSim), nameof(CrewSim.GetSelectedCrew)), prefix: new HarmonyMethod(typeof(TrafficChecks), nameof(VisitSelected)));
            hiddenSelected = Empty<CondOwner>(); hiddenSelected.ship = home;
            boundary.Patch(AccessTools.Method(typeof(Ship), nameof(Ship.SetFactions)), prefix: new HarmonyMethod(typeof(TrafficChecks), nameof(Quiet)));
            boundary.Patch(AccessTools.Method(typeof(ShipSitu), nameof(ShipSitu.TimeAdvance)), prefix: new HarmonyMethod(typeof(TrafficChecks), nameof(Quiet)));
            var damageType = AccessTools.Property(typeof(Ship), nameof(Ship.DamageSystem)).PropertyType;
            Set(reserve, "<DamageSystem>k__BackingField", FormatterServices.GetUninitializedObject(damageType));
            boundary.Patch(AccessTools.Method(damageType, "RemoveShallowDamage"), prefix: new HarmonyMethod(typeof(TrafficChecks), nameof(Quiet)));
            boundary.Patch(AccessTools.Method(typeof(StarSystem), nameof(StarSystem.GetFaction)), prefix: new HarmonyMethod(typeof(TrafficChecks), nameof(VisitFaction)));
            boundary.Patch(AccessTools.Method(typeof(AIShip), nameof(AIShip.AddCrew), new[] { typeof(string), typeof(Ship), typeof(string), typeof(bool) }), prefix: new HarmonyMethod(typeof(TrafficChecks), nameof(StopBeforeCrew)));
            home.objSS.vPosx = visitor.objSS.vPosx + 5e-5; home.objSS.vVelX = 7e-9;
            double previousX = reserve.objSS.vPosx;
            var targetSitu = visitor.shipSituTarget;
            try { AccessTools.Method(typeof(CivilianPilot), "CreateAIShip").Invoke(civilian, new object[] { home.strRegID }); throw new Exception("Creator did not reach the expected crew boundary"); }
            catch (TargetInvocationException error) when (error.InnerException is ReusePositioned) {}
            Check(reserve.objSS.vPosx != previousX && !reserve.HideFromSystem && reserve.IsAIShip, "Native creator did not reuse/reposition the hidden selected hull");
            Check(ReferenceEquals(visitor.shipScanTarget, reserve) && ReferenceEquals(visitor.shipSituTarget, targetSitu) &&
                ReferenceEquals(targetSitu, reserve.objSS) && targetSitu.vPosx == reserve.objSS.vPosx,
                "Reuse does not retain the shared live target situation as expected");
            Check(ReferenceEquals(visitor.objSS.NavData, route) && civilian.Blackboard.Get<string>("TripFuelPrediction") == "old-visit&12",
                "Native reuse unexpectedly invalidated the visitor's existing route/fuel estimate");
            Console.WriteLine((checks - before) + " hidden-visit investigation checks passed: native selection/reuse eligibility, candidate exclusions/fallback, preserved visible visits/explicit destinations, old-target limit and actual creator repositioning. Crew creation and physical navigation are explicit boundaries.");
        } finally {
            h.UnpatchSelf(); boundary.UnpatchSelf(); hiddenSelected = null; hiddenPorts = true;
            outer.Patch(ports, prefix: new HarmonyMethod(typeof(TrafficChecks), nameof(Yes)));
            visitor.objSS.NavData = null; visitor.shipUndock = null;
        }
    }
    static bool VisitFaction(ref JsonFaction __result) { __result = new JsonFaction { strName = "owner-faction" }; return false; }
}
