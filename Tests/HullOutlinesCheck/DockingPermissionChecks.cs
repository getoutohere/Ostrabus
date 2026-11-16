using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx.Configuration;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using Ostrabus.Development;
using Ostrabus.NavConsole.Outlines;
using Ostrabus.ShipNavigation.Collisions;
using SolarSystem;

static partial class Program {
    static void DockingPermissionCases() {
        int before=checks;
        var oldWorld=CrewSim.system;
        var oldFlag=PhysicalCollisions.Flag;
        var environment=new Harmony("offline.hull.docking-permission.environment");
        var production=new Harmony("offline.hull.docking-permission.production");
        try {
            environment.Patch(AccessTools.PropertyGetter(typeof(Ship),nameof(Ship.Mass)),prefix:new HarmonyMethod(typeof(Program),nameof(DockingMass)));
            HullDisplay.Reset(); PhysicalCollisions.Reset();
            PhysicalCollisions.Flag=Empty<ConfigEntry<bool>>(); Field(PhysicalCollisions.Flag,"_typedValue",true);
            var us=CollisionHull("O-GXD",0,0); var them=CollisionHull("J-YGNB",0,0);
            us.objSS.Size=500; them.objSS.Size=280;
            WarmRangeRadius(us,1145.4256591796875); WarmRangeRadius(them,529.528076171875);
            CrewSim.system=Empty<StarSystem>();
            CrewSim.system.dictShips=new Dictionary<string,Ship>{{us.strRegID,us},{them.strRegID,them}};
            CrewSim.system.aBOs=new Dictionary<string,BodyOrbit>();
            var actor=Empty<CondOwner>(); Field(actor,"_objShip",us);
            var respondent=Empty<CondOwner>(); Field(respondent,"_objShip",them);
            var clearance=Empty<Loot>(); clearance.strName="TXTDockingClearance";
            var assets=JArray.Parse(File.ReadAllText(Path.Combine(InstallationPaths.Data,"interactions/interactions_ship_to_ship.json")));
            var grants=assets.Where(row => (string)row["LootCTsThem"]==clearance.strName && (double?)row["fTargetPointRange"]>0).ToArray();
            Check(grants.Any(row => (string)row["strName"]=="SHIPForceDockAllow") &&
                grants.Any(row => (string)row["strName"]=="SHIPForceDockAllowNoPilot") &&
                grants.Any(row => (string)row["strName"]=="SHIPDockWithShipAllow"),"Authored docking-clearance response contract changed.");
            var gate=AccessTools.Method(typeof(Interaction),"IsWithinShipRange");
            Interaction Response(JToken row) {
                var interaction=Empty<Interaction>(); interaction.strName=(string)row["strName"];
                interaction.fTargetPointRange=(float)row["fTargetPointRange"];
                interaction.objUs=actor; interaction.objThem=respondent; interaction.LootCTsThem=clearance;
                return interaction;
            }
            bool Allowed(Interaction interaction)=>(bool)gate.Invoke(interaction,null);
            double allowance=HullDocking.PairDistanceAU(us,them);
            them.objSS.vPosx=4210*HullZoom.MetresToAU+allowance;
            var forced=Response(grants.Single(row => (string)row["strName"]=="SHIPForceDockAllow"));
            Check(CollisionManager.GetRangeToCollisionKM(us,them)>5 && !Allowed(forced),"Unpatched native gate failed to reproduce rejection at displayed 4.21 km.");
            production.CreateClassProcessor(typeof(HullDockingGeometryPatch)).Patch();
            Check(Allowed(forced),"Production gate still rejects the captured 4.21 km approach.");
            Check(Math.Abs(HullDocking.PermissionRangeKm(us,them,forced)-4.21)<.00001,"Permission clearance differs from the target-data hull allowance.");
            foreach(var row in grants) {
                var interaction=Response(row);
                foreach(double metres in new[]{-1d,1d}) {
                    them.objSS.vPosx=allowance+(interaction.fTargetPointRange*1000+metres)*HullZoom.MetresToAU;
                    Check(Allowed(interaction)==(metres<0),"Permission ignores its authored boundary: "+interaction.strName);
                }
            }
            them.objSS.vPosx=4210*HullZoom.MetresToAU+allowance;
            forced.LootCTsThem=null;
            Check(!Allowed(forced),"Non-clearance interaction acquired the physical approach correction.");
            forced.LootCTsThem=Empty<Loot>(); forced.LootCTsThem.strName="OtherRadioEffect";
            Check(!Allowed(forced),"Unrelated radio effect acquired the docking correction.");
            forced.LootCTsThem=clearance;
            Field(PhysicalCollisions.Flag,"_typedValue",false);
            Check(!Allowed(forced),"Feature-off permission no longer uses native geometry.");
            Field(PhysicalCollisions.Flag,"_typedValue",true);
            them.Classification=Ship.TypeClassification.OrbitalStation; them.objSS.bIsBO=true;
            Check(!Allowed(forced),"Station permission acquired free-hull geometry.");
            them.Classification=Ship.TypeClassification.Default; them.objSS.bIsBO=false;
            var station=CollisionHull("PERMISSION-STATION",0,0); station.Classification=Ship.TypeClassification.OrbitalStation; station.objSS.bIsBO=true;
            var group=new DockGroup(); group.AddShip(them); group.AddShip(station);
            Check(!Allowed(forced),"Station-connected hull acquired free-hull permission geometry.");
            group.RemoveShip(station); group.RemoveShip(them);
            var rock=CollisionHull("PERMISSION-ROCK",0,0); rock.Classification=Ship.TypeClassification.Asteroid;
            group.AddShip(them); group.AddShip(rock,true);
            Check(Allowed(forced),"Moored asteroid replaced the selected target's clearance allowance.");
            group.RemoveShip(rock); group.RemoveShip(them);
            forced.fTargetPointRange=0;
            Check(Allowed(forced),"Unlimited-range native branch changed.");
            forced.fTargetPointRange=-1;
            Check(!Allowed(forced),"Docked-only native branch now accepts undocked ships.");
            group=new DockGroup(); group.AddShip(us); group.AddShip(them);
            var docked=new Dictionary<string,Ship>{{"permission-test",them}};
            Field(us,"aDocked",docked);
            Check(Allowed(forced),"Docked-only native branch rejects an existing group.");
            forced.fTargetPointRange=5;
            Check(!Allowed(forced),"Native already-docked positive-range semantics changed.");
            group.RemoveShip(us); group.RemoveShip(them);
            docked.Clear();
            // The native participant setter intentionally ignores null.
            Field(forced,"objThemTemp",null); Field(forced,"strThemID",null);
            Check(!Allowed(forced),"Missing participant native branch changed.");
            Console.WriteLine("Docking permission range: "+(checks-before)+" checks passed (native rejection, production correction, authored limits and exclusions).");
        } finally {
            production.UnpatchSelf(); environment.UnpatchSelf(); CrewSim.system=oldWorld; PhysicalCollisions.Flag=oldFlag;
            HullDisplay.Reset(); PhysicalCollisions.Reset();
        }
    }
}
