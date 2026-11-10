using System.Text.Json;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

static class Composition {
    internal static string Bootstrap(string original,Feature[] features,Plan plan) {
        var tree=Inventory.Parse(original);var old=tree.DescendantNodes().OfType<ClassDeclarationSyntax>().Single(t=>t.Identifier.ValueText=="OstrabusPlugin");
        string Lines(IEnumerable<string> statements)=>string.Join("\n",statements);
        string fields=plan.Included.Contains("inspection")?"private global::Ostrabus.Inspection.InspectionApi _inspectionApi; private global::Ostrabus.Inspection.InspectionLogHooks _inspectionLogs;":"";
        string hooks=Lines(plan.Patches.Select(h=>$"_harmony.CreateClassProcessor(typeof(global::{h.Type.Replace('+','.')})).Patch();"));
        string ticks=plan.TickCalls>0?"private void Update() {"+Lines(features.SelectMany(f=>f.Tick))+"}":"";
        string cleanup=Lines(features.Reverse().SelectMany(f=>f.Stop));
        string text="""
            [BepInPlugin(PluginInfo.PLUGIN_GUID,PluginInfo.PLUGIN_NAME,PluginInfo.PLUGIN_VERSION)]
            public class OstrabusPlugin : BaseUnityPlugin {
                internal static new ManualLogSource Logger;
                private Harmony _harmony;
            """+fields+"\nprivate void Awake() { Logger=base.Logger; _harmony=new Harmony(PluginInfo.PLUGIN_GUID); try {\n"+
            Lines(features.SelectMany(f=>f.Before))+"\n"+hooks+"\n"+Lines(features.SelectMany(f=>f.After))+
            "\nLogger.LogInfo(\"Ostrabus custom profile: "+plan.Fingerprint[..12]+" ("+string.Join(", ",plan.Included)+")\");\n"+
            "} catch { OnDestroy(); throw; } }\n"+ticks+"\nprivate void OnDestroy() { try {\n"+cleanup+"\n} finally { _harmony?.UnpatchSelf(); } } }";
        var replacement=SyntaxFactory.ParseMemberDeclaration(text)??throw new Exception("Could not generate plugin bootstrap.");
        return tree.ReplaceNode(old,replacement).NormalizeWhitespace().ToFullString()+"\n";
    }
    internal static string Settings(string original,Plan plan) {
        var tree=Inventory.Parse(original);var field=tree.DescendantNodes().OfType<VariableDeclaratorSyntax>().Single(v=>v.Identifier.ValueText=="All"&&v.Ancestors().OfType<ClassDeclarationSyntax>().Any(t=>t.Identifier.ValueText=="SettingsRegistry"));
        var array=(InitializerExpressionSyntax)field.Initializer!.Value;
        var rows=array.Expressions.OfType<ObjectCreationExpressionSyntax>().ToDictionary(r=>r.ArgumentList!.Arguments[0].Expression is LiteralExpressionSyntax l?l.Token.ValueText:throw new Exception("Setting ID must be a literal."),StringComparer.Ordinal);
        foreach(string id in plan.Settings)Files.Need(rows.ContainsKey(id),"Unknown PDA setting: "+id);
        var selected=plan.PdaAssets?array.Expressions.Where(r=>r is ObjectCreationExpressionSyntax o&&o.ArgumentList!.Arguments[0].Expression is LiteralExpressionSyntax id&&plan.Settings.Contains(id.Token.ValueText,StringComparer.Ordinal)):[];
        return tree.ReplaceNode(array,array.WithExpressions(SyntaxFactory.SeparatedList(selected))).NormalizeWhitespace().ToFullString()+"\n";
    }
    internal static void Generate(string root,string output,Catalog catalog,Profile profile,Declaration[] declarations) {
        output=Path.GetFullPath(output);Files.Need(!File.Exists(Path.Combine(output,"Ostrabus.csproj")),"Use a fresh build directory; refusing stale generated sources.");
        Directory.CreateDirectory(output);var features=Inventory.Resolve(catalog,profile);var plan=Inventory.Plan(catalog,profile,declarations);
        var compile=new List<string>();
        foreach(string file in Inventory.Sources(root,catalog)) {
            string text=File.ReadAllText(Path.Combine(root,file));
            text=file switch {
                "OstrabusPlugin.cs"=>Bootstrap(text,features,plan),
                "PdaSettings/SettingsRegistry.cs"=>Settings(text,plan),
                _=>SourcePruner.Apply(file,text,plan.Included)
            };
            string relative="Source/"+file,target=Path.Combine(output,relative);Directory.CreateDirectory(Path.GetDirectoryName(target)!);File.WriteAllText(target,text);compile.Add(relative);
        }
        string info="[assembly: System.Reflection.AssemblyMetadata(\"Ostrabus.BuildKind\",\"Custom\")]\n[assembly: System.Reflection.AssemblyMetadata(\"Ostrabus.Profile\",\""+plan.Fingerprint+"\")]\n";
        File.WriteAllText(Path.Combine(output,"BuildInfo.g.cs"),info);compile.Add("BuildInfo.g.cs");
        var original=XDocument.Load(Path.Combine(root,"Ostrabus.csproj"));
        var project=new XDocument(new XElement("Project",new XAttribute("Sdk","Microsoft.NET.Sdk"),
            new XElement("PropertyGroup",new XElement("TargetFramework","netstandard2.1"),new XElement("AssemblyName","Ostrabus"),new XElement("RootNamespace","Ostrabus"),new XElement("LangVersion","latest"),new XElement("AllowUnsafeBlocks","true"),new XElement("EnableDefaultCompileItems","false"),new XElement("EnableDefaultEmbeddedResourceItems","false"),new XElement("NoWarn","$(NoWarn);0649"),new XElement("PathMap",output+"=/_/OstrabusCustom"),new XElement("Version",original.Descendants("Version").Single().Value)),
            new XElement("ItemGroup",compile.Select(file=>new XElement("Compile",new XAttribute("Include",file)))),
            new XElement("ItemGroup",original.Descendants("Reference").Select(reference=>new XElement(reference)))));
        if(plan.Included.Contains("proximity-range"))project.Root!.Add(new XElement("ItemGroup",new XElement("EmbeddedResource",new XAttribute("Include",Path.Combine(root,"NavConsole","Assets","RoundKnob-v3.png")),new XAttribute("LogicalName","Ostrabus.NavConsole.RoundKnob.png"))));
        project.Save(Path.Combine(output,"Ostrabus.csproj"));Files.Write(Path.Combine(output,"selection.json"),plan);
    }
}
