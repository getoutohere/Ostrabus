using System;
using HarmonyLib;
using Ostrabus.MiscBugFixes;
using Ostrabus.ShipNavigation.TorchCourses;
using Ostrabus.ShipNavigation.Towing;
using Ostranauts.ShipGUIs.Utilities;
using Ostranauts.Ships.Commands;
using Ostranauts.Utils.Models;

static partial class Program {
    static void RunTransferPlaybackChecks() {
        int before = checks;
        var patch = new Harmony("offline.torch.transfer-playback");
        patch.CreateClassProcessor(typeof(TorchTransferKernelPatch)).Patch();
        patch.CreateClassProcessor(typeof(DockedCourseMemberPatch)).Patch();
        patch.CreateClassProcessor(typeof(DockedCourseGroupPatch)).Patch();
        patch.CreateClassProcessor(typeof(TorchTowMassPatch)).Patch();
        patch.Patch(AccessTools.PropertySetter(typeof(Ship),nameof(Ship.Gravity)),prefix:new HarmonyMethod(typeof(Program),nameof(Skip)));
        patch.Patch(AccessTools.Method(typeof(Ship),nameof(Ship.AccrueTowGroupLoad)),prefix:new HarmonyMethod(typeof(Program),nameof(Skip)));
        try {
            foreach (double pilot in new[] { 1d,.75 }) foreach (double sideways in new[] { 0d,5000 })
                foreach (double cap in new[] { 5000d,1e8 }) foreach (bool sff in new[] { false,true }) foreach (bool towing in new[] { false,true }) {
                    SetupCourse(false,pilot);
                    var group = towing ? TowGroup() : null;
                    var nav = Kernel(TransferPath(),10000,new Point(10000,sideways)*(1/TransferAu),default,.2f,true,cap:cap/TransferAu);
                    ship.objSS.vPosx = nav.Origin.ObjSS.vPosx; ship.objSS.vPosy = nav.Origin.ObjSS.vPosy;
                    ship.objSS.vVelX = nav.Origin.ObjSS.vVelX; ship.objSS.vVelY = nav.Origin.ObjSS.vVelY;
                    ship.objSS.NavData = nav;
                    object[] attach = { nav }; AccessTools.Method(typeof(TorchCoursePlanningPatch),"Postfix").Invoke(null,attach);
                    Check(ReferenceEquals(attach[0],nav),"Valid new course retained by UI postfix");
                    var profile = TorchCourseFuel.Find(nav,false);
                    Check(profile != null,"Momentum-aware waypoints receive existing fuel/control correction");
                    double start = fixtureEpoch, end = nav.Destination.ArrivalTime;
                    Point endPosition = nav.Destination.ObjSS.vPos, endVelocity = nav.Destination.ObjSS.vVel;
                    double initialHe3 = Read(he3[0],"StatSolidHe3"), finalFuel = nav.Destination.TorchFuelLevel;
                    double expectedUse = (nav.Origin.TorchFuelLevel-finalFuel)*profile.Usage;
                    Field(nav,"fEpochLast",fixtureEpoch);
                    var active = AccessTools.Field(typeof(GUIFFWD),"bFFWDActive"); active.SetValue(null,sff);
                    var autopilot = Empty<FlyToAutoPilot>(); Field(autopilot,"_shipUs",ship);
                    bool restored = false;
                    try {
                        while (fixtureEpoch < end) {
                            double next = Math.Min(end,fixtureEpoch+(sff ? 1700 : .27));
                            Check((bool)AccessTools.Method(typeof(FlyToAutoPilot),"RequirementsMet").Invoke(autopilot,null),"Native affordability guard accepts new transfer");
                            if (group == null) Check(AdvanceCourse(nav,next),"Native playback advances new transfer");
                            else {
                                double dt = next-fixtureEpoch;
                                fixtureEpoch = next; StarSystem.fEpoch = next;
                                ship.objSS.TimeAdvance(dt); group.TimeAdvance(dt);
                                Check(ReferenceEquals(ship.objSS.NavData,nav) || (ship.objSS.NavData == null && next >= end-1e-6),
                                    "Grouped native playback advances new transfer (JSON endpoint rounding may finish at arrival)");
                                Check(Math.Abs((ship.objSS.vPos-group.Anchor.vPos).magnitude-1e-8) < 1e-13,"New transfer keeps rigid tow offset");
                            }
                            Check(!profile.Invalid,"New transfer keeps valid reactor profile");
                            if (sff && !restored && fixtureEpoch >= (start+end)*.5 && fixtureEpoch < end) {
                                var json = LitJson.JsonMapper.ToJson(nav.GetJSON());
                                nav = new NavData(LitJson.JsonMapper.ToObject<JsonNavData>(json));
                                ship.objSS.NavData = nav;
                                profile = TorchCourseFuel.Find(nav);
                                Check(profile != null,"New trajectory restores through ordinary native save data");
                                restored = true;
                            }
                        }
                        Check((ship.objSS.vPos-endPosition).magnitude < 1e-15,"New trajectory reaches its planned endpoint");
                        Check((ship.objSS.vVel-endVelocity).magnitude < 1e-18,"New trajectory reaches its planned arrival velocity");
                        Near(initialHe3-Read(he3[0],"StatSolidHe3"),expectedUse,"New trajectory fuel matches preview",3e-6);
                        Near(ship.fShallowFusionRemain,finalFuel,"New trajectory reconciles physical reactants",3e-6);
                        AdvanceCourse(nav,end+1);
                        Check(!ship.objSS.HasNavData(),"New trajectory disengages on arrival");
                        Check(double.Parse(Controls["slidCycle"]) == 0 && double.Parse(Controls["slidFlow"]) == 0,"New trajectory idles on arrival");
                        Check(TorchCourseFuel.Playing == null,"New trajectory releases playback scope");
                    } finally { active.SetValue(null,false); }
                }
            Console.WriteLine("Momentum-aware playback: " + (checks-before) + " checks across 32 scenarios; native playback, tow groups, pilot modifiers, normal/SFF schedules, JSON reload, preview and physical fuel.");
        } finally { patch.UnpatchSelf(); }
    }
}
