using System;
using System.Collections.Generic;
using HarmonyLib;
using Ostrabus.NavConsole.Rotation;
using Ostranauts.Utils.Models;
using SolarSystem;

static partial class Program {
    static void GroupChecks(Harmony fixture) {
        fixture.Unpatch(AccessTools.Method(typeof(Ship), nameof(Ship.UpdateDockedShips)), AccessTools.Method(typeof(Program), nameof(Quiet)));
        fixture.Patch(AccessTools.PropertySetter(typeof(Ship), nameof(Ship.Gravity)), prefix: new HarmonyMethod(typeof(Program), nameof(Quiet)));
        fixture.Patch(AccessTools.Method(typeof(Ship), nameof(Ship.AccrueTowGroupLoad)), prefix: new HarmonyMethod(typeof(Program), nameof(Quiet)));
        try {
            foreach (double dt in new[] { .02, .1, 60.0 }) {
                RotationRuntime.Cleanup();
                var tug = Hull("group-tug-" + dt);
                var tow = Hull("group-tow-" + dt);
                var third = Hull("group-third-" + dt);
                var group = new DockGroup { Anchor = new ShipSitu(false) };
                var members = new[] { tug, tow, third };
                for (int i = 0; i < members.Length; i++) {
                    var member = members[i];
                    member.objSS._dockGroup = group;
                    member.objSS._dockOffsetPos = new Point((i - 1) * 1e-8, 2e-9);
                    member.objSS._dockOffsetRot = .3f * i;
                    group.DockedShips.Add(new DockGroupMember(member, false));
                    var links = (Dictionary<string, Ship>)AccessTools.Field(typeof(Ship), "aDocked").GetValue(member);
                    if (i > 0) links[members[i - 1].strRegID] = members[i - 1];
                    if (i + 1 < members.Length) links[members[i + 1].strRegID] = members[i + 1];
                }
                group.TimeAdvance(0);
                var order = Order(tow, Math.PI);
                double initialFuel = tow.fShallowRCSRemass;
                for (double t = 0; t < (dt >= 60 ? 180 : 150); t += dt) {
                    StarSystem.fEpoch += dt;
                    double before = tow.fShallowRCSRemass;
                    foreach (var member in members) member.objSS.TimeAdvance(dt);
                    Check(tow.fShallowRCSRemass == before, "Docked member consumed rotation fuel before shared anchor step");
                    group.TimeAdvance(dt);
                    foreach (var member in members) {
                        Check(Math.Abs(RotationPid.Wrap(member.objSS.fRot - group.Anchor.fRot - member.objSS._dockOffsetRot)) < 1e-6,
                            "Shared rotation changed a docked hull's angular offset");
                        double expectedX = group.Anchor.vPosx + member.objSS._dockOffsetPos.X * Math.Cos(group.Anchor.fRot) -
                            member.objSS._dockOffsetPos.Y * Math.Sin(group.Anchor.fRot);
                        Check(Math.Abs(member.objSS.vPosx - expectedX) < 1e-18, "Docked hull separated during rotation");
                        Check(member.objSS.fA == 0 && member.objSS.fW == group.Anchor.fW, "Docked hull retained independent angular thrust");
                    }
                }
                Check(!order.Order.Active && Math.Abs(RotationPid.Wrap(tow.objSS.fRot - Math.PI)) < .02,
                    "Native dock-group turn dt=" + dt + " active=" + order.Order.Active + " heading=" + tow.objSS.fRot +
                    " rate=" + tow.objSS.fW + " fuel=" + tow.fShallowRCSRemass);
                Check(tow.fShallowRCSRemass < initialFuel && tug.fShallowRCSRemass == 10000 && third.fShallowRCSRemass == 10000,
                    "Grouped turn charged the wrong hull's fuel");
            }
            Console.WriteLine("Three-ship native dock-group rotation: frame/SFF convergence, one fuel debit, rigid offsets and angular ownership passed.");
        } finally {
            RotationRuntime.Cleanup();
            fixture.Patch(AccessTools.Method(typeof(Ship), nameof(Ship.UpdateDockedShips)), prefix: new HarmonyMethod(typeof(Program), nameof(Quiet)));
        }
    }
}
