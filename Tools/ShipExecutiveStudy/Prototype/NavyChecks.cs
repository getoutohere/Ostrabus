using System;
using System.IO;
using HarmonyLib;
using MissionContracts;
using Ostranauts.Ships.Commands;

namespace ShipExecutivePrototype;

static class NavyChecks
{
    static void Forbidden() => throw new InvalidOperationException("Opaque combat/retreat controller was invoked by replacement");
    internal static void Run(Action<string, Action> scenario, Action<bool, string> check)
    {
        var model = Definition.Load("Navigation/Architecture/Replacement/mission-model.json");
        NavyAssetAudit.Run(scenario, check);
        using (var fixtures = new NavyFixtures())
        {
            var guard = new Harmony("offline.navy.forbid-opaque");
            foreach (var type in new[] { typeof(LowFuelRetreat), typeof(KeepWithinFiringDistance), typeof(KeepAtDistance) })
                guard.Patch(AccessTools.Method(type, "RunCommand"), prefix: new HarmonyMethod(typeof(NavyChecks), nameof(Forbidden)));
            try
            {
                scenario("Navy native policy: patrol, combat, timed dodge, resume, retreat and arrival", () =>
                {
                    var w = new NativeWorld();
                    var actor = new NativeNavyActor(w, model);
                    actor.Tick();
                    check(actor.Machine.State.Active?.Kind == "Patrol" && w.Subject.objSS.NavData != null, "Native main policy did not start patrol");
                    w.Threat = true;
                    actor.Tick();
                    actor.Tick();
                    check(actor.Machine.State.Active.Kind == "Combat" && actor.CombatCalls > 0 && w.NativeShotRequests > 0, "Native threat/engagement rules did not produce combat");
                    check(actor.Dodge("ENEMY", 1), "Native urgent dodge was refused");
                    for (int i = 0; i < 11; i++)
                    {
                        StarSystem.fEpoch++;
                        actor.Tick(1);
                    }
                    actor.Tick();
                    check(actor.ReactionCalls > 1 && actor.Machine.State.Active.Kind == "Combat", "Timed native dodge did not finish and return to combat policy");
                    w.LowFuel = true;
                    actor.Tick();
                    check(actor.Machine.State.Active.Kind == "Return", "Native tactical fuel predicate did not emit explicit return");
                    check(actor.Travel.Advance(60, "SFF") && w.Retired && w.ArrivalCalls == 1, "Explicit combat return did not arrive once");
                });
                scenario("Navy native damage and surrender policy chooses return", () =>
                {
                    var w = new NativeWorld { Threat = true };
                    var actor = new NativeNavyActor(w, model);
                    actor.Tick();
                    actor.Tick();
                    w.Damaged = true;
                    actor.Tick();
                    check(fixtures.Sent.Contains("STKShipSurrender_AIOffer_Intro") && actor.Machine.State.Active.Kind == "Return", "Authored surrender offer was bypassed");
                });
                scenario("Navy live dodge checkpoint preserves remaining simulation duration", () =>
                {
                    var w = new NativeWorld { Threat = true };
                    var actor = new NativeNavyActor(w, model);
                    actor.Tick();
                    actor.Tick();
                    actor.Dodge("ENEMY", -1);
                    StarSystem.fEpoch += 3;
                    actor.Tick(3);
                    double remaining = actor.State.ReactionRemaining;
                    string saved = actor.Save();
                    var loaded = NativeNavyActor.Restore(model, saved);
                    check(loaded.State.ReactionRemaining == remaining && loaded.State.ReactionStarted, "Reaction timer was reconstructed from scratch");
                    for (int i = 0; i < 8; i++)
                    {
                        StarSystem.fEpoch++;
                        loaded.Tick(1);
                    }
                    loaded.Tick();
                    check(!loaded.State.ReactionStarted && loaded.Machine.State.Active.Kind == "Combat" && !loaded.World.Subject.objSS.bIgnoreGrav, "Reloaded dodge retained gravity suppression or lost combat");
                });
                scenario("Navy actual world entrypoints: normal, SFF iterator and PASS iterator", () =>
                {
                    foreach (string source in new[] { "normal", "SFF", "PASS" })
                    {
                        var w = new NativeWorld { LowFuel = true };
                        var actor = new NativeNavyActor(w, model);
                        actor.Travel.BeginNativeLowFuelReturn();
                        using (var bridge = new NativeWorldTimeBridge(actor))
                        {
                            if (source == "normal")
                                bridge.Normal(60);
                            else if (source == "SFF")
                                bridge.Sff(60);
                            else
                                bridge.Pass(60);
                            check(w.Retired && w.ArrivalCalls == 1 && StarSystem.fEpoch == 1060, "Native entrypoint failed arrival/residual time: " + source);
                            check(bridge.NativeDispatches > 0 && Math.Abs(w.Subject.fShallowRCSRemass - 295) < 1e-6, "Native queue or fuel settlement bypassed: " + source);
                            check(bridge.Segments == 2 && bridge.EntryCalls.Contains(source == "SFF" ? "GUIFFWD" : source), "Native entrypoint did not split at route arrival: " + source);
                        }
                    }
                });
                NavyInputChecks.Run(scenario, check, model, fixtures);
                NavyFuelChecks.Run(scenario, check, model);
                NavyDialogueChecks.Run(scenario, check, model);
                NativeArrivalChecks.Run(scenario, check, model);
                NativeSaveChecks.Run(scenario, check, model);
                NavyTimeChecks.Run(scenario, check, model);
                WorldServiceChecks.Run(scenario, check, model);
                scenario("Navy native long-range geometry becomes an explicit tactical waypoint", () =>
                {
                    fixtures.EquipmentProfile = 2;
                    fixtures.LocalBody = NativeWorld.Empty<BodyOrbit>();
                    fixtures.LocalBody.strName = "HOME";
                    fixtures.LocalBody.dXReal = -1;
                    try
                    {
                        var w = new NativeWorld { Threat = true };
                        var actor = new NativeNavyActor(w, model);
                        actor.Tick();
                        actor.Tick();
                        check(actor.State.CombatMode == "Long" && actor.State.TacticalGoal != null && new ShipSitu(actor.State.TacticalGoal).strBOPORShip == "HOME", "Native long-range waypoint construction was bypassed");
                        check(w.Subject.shipScanTarget == null && w.Subject.objSS.NavData != null && w.ArrivalCalls == 0, "Long-range combat retained an implicit hostile dock stage");
                        var restored = NativeNavyActor.Restore(model, actor.Save());
                        restored.Tick();
                        check(restored.Machine.State.Active.Kind == "Combat" && restored.State.TacticalFlightPayload != null && restored.World.ArrivalCalls == 0, "Reload lost tactical state or initiated hostile docking");
                    }
                    finally { fixtures.EquipmentProfile = 1; fixtures.LocalBody = null; }
                });
                scenario("Navy missing long-range geometry falls through native flee policy", () =>
                {
                    fixtures.EquipmentProfile = 2;
                    try
                    {
                        var w = new NativeWorld { Threat = true };
                        var actor = new NativeNavyActor(w, model);
                        actor.Tick();
                        actor.Tick();
                        check(actor.Machine.State.Active.Phase == "AwaitingPolicy" && w.ArrivalCalls == 0, "Missing tactical point became hostile docking");
                        actor.Tick();
                        check(actor.Machine.State.Active.Kind == "Return" && w.Subject.shipScanTarget == w.Home, "Failed tactical steering endlessly retried instead of native flee fallback");
                    }
                    finally { fixtures.EquipmentProfile = 1; }
                });
            }
            finally { guard.UnpatchSelf(); }
        }
    }
}
