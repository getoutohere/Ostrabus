using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Text.Json;

sealed record Declaration(string Type,string File,string[] Methods);
static class Inventory {
    internal static string[] Sources(string root,Catalog catalog) {
        var excluded=new HashSet<string>(catalog.ExcludedSources,StringComparer.Ordinal);
        // Discover new module directories too: a new hook must fail validation
        // until it has an owner, rather than quietly disappearing from customs.
        var ignored=new HashSet<string>(new[]{"Tests","Tools","Build","CustomBuilder","bin","obj","dist","OldAnalysisData","vendor"},StringComparer.OrdinalIgnoreCase);
        IEnumerable<string> Walk(string directory) {
            foreach(string path in Directory.EnumerateFiles(directory,"*.cs"))yield return path;
            foreach(string child in Directory.EnumerateDirectories(directory)) {
                string name=Path.GetFileName(child);
                if(name.StartsWith('.')||ignored.Contains(name))continue;
                foreach(string path in Walk(child))yield return path;
            }
        }
        return Walk(root)
            .Select(p=>Path.GetRelativePath(root,p).Replace('\\','/')).Where(p=>!excluded.Contains(p)).Order(StringComparer.OrdinalIgnoreCase).ToArray();
    }
    internal static CompilationUnitSyntax Parse(string text,IEnumerable<string>? defines=null)=>CSharpSyntaxTree.ParseText(text,new CSharpParseOptions(LanguageVersion.Latest,preprocessorSymbols:defines??[])).GetCompilationUnitRoot();
    internal static bool Harmony(AttributeSyntax a,string suffix)=>a.Name.ToString().Split('.').Last() is var name&&(name==suffix||name==suffix+"Attribute");
    internal static string Name(TypeDeclarationSyntax type) {
        var ns=string.Join(".",type.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().Reverse().Select(n=>n.Name.ToString()));
        var names=type.Ancestors().OfType<TypeDeclarationSyntax>().Reverse().Select(t=>t.Identifier.ValueText).Append(type.Identifier.ValueText);
        return (ns.Length==0?"":ns+".")+string.Join("+",names);
    }
    internal static Declaration[] Scan(string root,Catalog catalog) => Sources(root,catalog).SelectMany(file=> {
        var tree=Parse(File.ReadAllText(Path.Combine(root,file)));
        return tree.DescendantNodes().OfType<TypeDeclarationSyntax>().Where(t=>t.AttributeLists.SelectMany(x=>x.Attributes).Any(a=>Harmony(a,"HarmonyPatch")))
            .Select(t=>new Declaration(Name(t),file,t.Members.OfType<MethodDeclarationSyntax>().Where(m=>m.AttributeLists.SelectMany(x=>x.Attributes).Any(a=>Harmony(a,"HarmonyPatch"))).Select(m=>m.Identifier.ValueText).ToArray()));
    }).OrderBy(t=>t.Type,StringComparer.Ordinal).ToArray();
    internal static Hook[] Hooks(Feature feature,Declaration[] declarations)=>feature.Patches.Concat(declarations.Where(d=>feature.PatchFiles.Contains(d.File,StringComparer.Ordinal)).Select(d=>new Hook{Type=d.Type})).ToArray();
    internal static void Validate(Catalog catalog,Declaration[] declarations) {
        Files.Need(catalog.SchemaVersion==1,"Unsupported catalog schema.");
        var ids=new HashSet<string>(StringComparer.Ordinal);var types=declarations.ToDictionary(d=>d.Type,StringComparer.Ordinal);
        foreach(var f in catalog.Features) {
            Files.Need(System.Text.RegularExpressions.Regex.IsMatch(f.Id,"^[a-z][a-z0-9-]*$")&&ids.Add(f.Id),"Invalid/duplicate feature ID: "+f.Id);
            Files.Need(f.Category is "bugfix" or "qol" or "performance" or "simulation" or "developer" or "support","Unknown category: "+f.Id);
            foreach(string path in f.PatchFiles)Files.Need(declarations.Any(d=>d.File==path),"Patch source has no declarations: "+path);
            foreach(var h in f.Patches) {
                Files.Need(types.TryGetValue(h.Type,out var d),"Unknown patch type: "+h.Type);
                if(h.Methods!=null)foreach(string m in h.Methods)Files.Need(d!.Methods.Contains(m,StringComparer.Ordinal),"Unknown method-scoped patch: "+h.Type+"."+m);
            }
        }
        var features=catalog.Features.ToDictionary(f=>f.Id,StringComparer.Ordinal);
        foreach(var f in catalog.Features)foreach(string dep in f.Requires)Files.Need(features.ContainsKey(dep),"Unknown dependency "+dep+" of "+f.Id);
        foreach(var f in catalog.Features)Resolve(catalog,new Profile{Features=[f.Id]});
        var owned=catalog.Features.SelectMany(f=>Hooks(f,declarations).Select(h=>(f.Id,Hook:h))).GroupBy(x=>x.Hook.Type).ToDictionary(g=>g.Key,g=>g.ToArray());
        foreach(var d in declarations) {
            Files.Need(owned.TryGetValue(d.Type,out var owners),"Uncatalogued Harmony patch: "+d.Type+" in "+d.File);
            if(owners!.Length>1&&!catalog.SharedPatches.Contains(d.Type,StringComparer.Ordinal)) {
                Files.Need(owners.All(x=>x.Hook.Methods!=null),"Multiple whole-type owners for "+d.Type);
                var methods=owners.SelectMany(x=>x.Hook.Methods!).ToArray();Files.Need(methods.Distinct(StringComparer.Ordinal).Count()==methods.Length,"Duplicate method ownership for "+d.Type);
            }
            if(owners.All(x=>x.Hook.Methods!=null))foreach(string method in d.Methods)
                Files.Need(owners.Any(x=>x.Hook.Methods!.Contains(method,StringComparer.Ordinal)),"Uncatalogued method patch: "+d.Type+"."+method);
        }
    }
    internal static Feature[] Resolve(Catalog catalog,Profile profile) {
        Files.Need(profile.SchemaVersion==1,"Unsupported profile schema.");
        var map=catalog.Features.ToDictionary(f=>f.Id,StringComparer.Ordinal);var seen=new HashSet<string>();var visiting=new HashSet<string>();var result=new List<Feature>();
        void Visit(string id) {
            Files.Need(map.TryGetValue(id,out var feature),"Unknown feature: "+id);
            if(seen.Contains(id))return;
            Files.Need(visiting.Add(id),"Dependency cycle at "+id);
            foreach(string dep in feature!.Requires.Order(StringComparer.Ordinal))Visit(dep);
            visiting.Remove(id);seen.Add(id);result.Add(feature);
        }
        foreach(string id in profile.Features.Order(StringComparer.Ordinal))Visit(id);
        return result.ToArray();
    }
    internal static Plan Plan(Catalog catalog,Profile profile,Declaration[] declarations) {
        var selected=Resolve(catalog,profile);
        var hooks=selected.SelectMany(f=>Hooks(f,declarations)).GroupBy(h=>h.Type).OrderBy(g=>g.Key,StringComparer.Ordinal).Select(g=>new Hook{Type=g.Key,Methods=g.Any(h=>h.Methods==null)?null:g.SelectMany(h=>h.Methods!).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray()}).ToArray();
        var plan=new Plan{Name=profile.Name,Requested=profile.Features.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),Included=selected.Select(f=>f.Id).ToArray(),Patches=hooks,Settings=selected.SelectMany(f=>f.Settings).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),TickCalls=selected.Sum(f=>f.Tick.Length),PdaAssets=selected.Any(f=>f.Id=="pda-settings")};
        foreach(var f in selected.Where(f=>!plan.Requested.Contains(f.Id,StringComparer.Ordinal)))plan.RequiredBy[f.Id]=selected.Where(p=>p.Requires.Contains(f.Id,StringComparer.Ordinal)).Select(p=>p.Id).ToArray();
        plan.Fingerprint=Files.Hash(JsonSerializer.Serialize(new{profile=plan.Requested,catalog},Files.Json));return plan;
    }
}
