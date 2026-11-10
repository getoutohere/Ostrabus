using Microsoft.CodeAnalysis.CSharp.Syntax;

static class SelfTest {
    internal static void Run(string root,Catalog catalog,Declaration[] declarations) {
        int checks=0;
        void Check(bool value,string name){checks++;Files.Need(value,"Self-test failed: "+name);}
        void Reject(Action action,string name){bool rejected=false;try{action();}catch{rejected=true;}Check(rejected,name);}
        Plan Make(params string[] ids)=>Inventory.Plan(catalog,new Profile{Features=ids},declarations);
        string Read(string file)=>File.ReadAllText(Path.Combine(root,file));
        Reject(()=>Make("not-a-feature"),"unknown feature");
        Reject(()=>Inventory.Resolve(new Catalog{Features=[new Feature{Id="a",Requires=["b"]},new Feature{Id="b",Requires=["a"]}]},new Profile{Features=["a"]}),"cycles");
        Reject(()=>Inventory.Validate(new Catalog{Features=[new Feature{Id="a",Category="bugfix"},new Feature{Id="a",Category="bugfix"}]},[]),"duplicate IDs");
        Reject(()=>Inventory.Validate(new Catalog(),[new Declaration("NewPatch","new.cs",[])]),"unowned patch");
        foreach(var f in catalog.Features) {
            var plan=Make(f.Id);Check(plan.Included.Contains(f.Id),f.Id+" included");
            foreach(var id in plan.Included)foreach(var dep in catalog.Features.Single(x=>x.Id==id).Requires)Check(plan.Included.Contains(dep),"closure "+id+" -> "+dep);
        }
        foreach(string preset in Presets.Names) {
            var profile=Presets.Create(catalog,preset);Check(profile.Features.All(id=>!catalog.Features.Single(f=>f.Id==id).Experimental),"preset excludes experiments");
            if(preset!="developer")Check(profile.Features.All(id=>catalog.Features.Single(f=>f.Id==id).Category!="developer"),"preset excludes diagnostics");
            _=Inventory.Plan(catalog,profile,declarations);
        }
        string plugin=Read("OstrabusPlugin.cs");
        foreach(string[] ids in new[]{Array.Empty<string>(),new[]{"door-autoclose"},new[]{"crew-hotkeys"},new[]{"altwork"},new[]{"inspection"}}) {
            var profile=new Profile{Features=ids};var plan=Make(ids);
            var generated=Inventory.Parse(Composition.Bootstrap(plugin,Inventory.Resolve(catalog,profile),plan));
            Check(!generated.ContainsDiagnostics,"bootstrap parses");
            Check(generated.DescendantNodes().OfType<MethodDeclarationSyntax>().Any(m=>m.Identifier.ValueText=="Update")== (plan.TickCalls>0),"no unused Update");
            Check(!generated.ToString().Contains("PatchAll("),"no blanket patching");
            Check(generated.DescendantNodes().OfType<InvocationExpressionSyntax>().Count(i=>i.Expression.ToString().EndsWith(".CreateClassProcessor"))==plan.Patches.Length,"explicit registration count");
        }
        string registry=Read("PdaSettings/SettingsRegistry.cs");
        foreach(string[] ids in new[]{new[]{"pda-settings","crew-hotkeys"},new[]{"pda-settings"},new[]{"crew-hotkeys"}}) {
            var plan=Make(ids);string generated=Composition.Settings(registry,plan);
            var rows=Inventory.Parse(generated).DescendantNodes().OfType<ObjectCreationExpressionSyntax>().Where(o=>o.Type.ToString()=="Setting").Select(o=>o.ArgumentList!.Arguments[0].Expression.ToString().Trim('"')).ToArray();
            Check(rows.Order().SequenceEqual((plan.PdaAssets?plan.Settings:[]).Order()),"PDA rows reflect selection");
        }
        string Prune(string file,params string[] ids)=>SourcePruner.Apply(file,Read(file),Make(ids).Included);
        string proximity="NavConsole/ProximityCollisionPatch.cs";
        Check(!Prune(proximity,"proximity-range").Contains("IgnoreAsObstacle"),"range alone preserves collisions");
        string station=Prune(proximity,"station-docked");
        Check(station.Contains("IgnoreAsObstacle")&&!station.Contains("static void Prefix("),"station alone omits range callback");
        Check(!Prune("Navigation/AIShipAvoidancePatch.cs","ai-avoidance").Contains("nameof(StationDockedCollisions.IgnoreAsObstacle)"),"avoidance alone preserves parked hulls");
        Check(!Prune("Navigation/Collisions/PhysicalCollisions.cs","hull-collisions").Contains("StationDockedCollisions.ShipCollision(a, b)"),"physical collision policy independent");
        Check(!Prune("NavConsole/Outlines/ContourBuilder.cs","hull-outlines").Contains("new CollisionShape.Builder"),"outlines omit collision covers");
        string refill=Prune("Navigation/RcsReplenishment.cs","rcs-refill");
        Check(refill.Contains("Rebase(ship, capture.Plan, added);")&&!refill.Contains("RecordFuelAdded"),"retain refill without Venus observer");
        string orbit=Prune("Orbital/OrbitalPatches.cs","orbital");
        Check(!orbit.Contains("SffEconomy.Begin(")&&!orbit.Contains("SffSimulation.Begin("),"orbital alone omits SFF launch");
        Check(!Prune("Orbital/OrbitalPatches.cs","sff").Contains("SffEconomy.Begin("),"SFF alone omits economy launch");
        Check(Prune("Orbital/OrbitalPatches.cs","economy-sff").Contains("SffEconomy.Begin("),"economy launch included");
        foreach(string file in Inventory.Sources(root,catalog))Check(!Inventory.Parse(Prune(file)).ContainsDiagnostics,"empty composition syntax "+file);
        Check(Make("station-docked","proximity-range").Patches.Count(h=>h.Type=="Ostrabus.NavConsole.ProximityCollisionPatch")==1,"shared patch deduplicated");
        Check(Make("social-filter").Patches.Single().Methods!.SequenceEqual(new[]{"GUIPDA_GetKnownSocialContacts_Postfix"}),"mixed file method selection");
        Check(Make("door-autoclose","door-autoclose").Fingerprint==Make("door-autoclose").Fingerprint,"duplicate requests normalized");
        Console.WriteLine($"Custom builder self-test: {checks} checks passed.");
    }
}
