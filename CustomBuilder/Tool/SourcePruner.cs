using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

// Build-time syntax edits only. The maintained full source/deploy path is never
// rewritten. Each edit is anchored to the current owning type/method, and the
// resulting assemblies are independently verified and bound before packaging.
sealed class SourcePruner(string file,IEnumerable<string> included) : CSharpSyntaxRewriter {
    readonly HashSet<string> features=new(included,StringComparer.Ordinal);
    bool Has(string id)=>features.Contains(id);
    static string Type(SyntaxNode node)=>node.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault()?.Identifier.ValueText??"";
    static bool Call(ExpressionSyntax expression,string suffix)=>expression is InvocationExpressionSyntax i&&i.Expression.ToString().EndsWith(suffix,StringComparison.Ordinal);
    static BlockSyntax Body(string text)=>(BlockSyntax)SyntaxFactory.ParseStatement("{"+text+"}");
    public override SyntaxNode? VisitExpressionStatement(ExpressionStatementSyntax node) {
        if(!Has("venus-navigation")&&(Call(node.Expression,"VorbNavPlanGuard.TargetChanged")||Call(node.Expression,"VorbNavPlanGuard.RecordFuelAdded")))return null;
        if(file=="Orbital/OrbitalPatches.cs"&&!Has("coast")&&Call(node.Expression,"CoastIntegration.Selected"))return null;
        if(!Has("hull-collisions")&&(Call(node.Expression,"Ostrabus.ShipNavigation.Collisions.PhysicalCollisions.Reset")||file=="NavConsole/Outlines/ContourBuilder.cs"&&Call(node.Expression,"collision.Release")))return null;
        return base.VisitExpressionStatement(node);
    }
    public override SyntaxNode? VisitIfStatement(IfStatementSyntax node) {
        if(file=="Navigation/RcsReplenishment.cs"&&!Has("venus-navigation")&&node.Statement is ExpressionStatementSyntax e&&Call(e.Expression,"VorbNavPlanGuard.RecordFuelAdded"))
            return SyntaxFactory.ExpressionStatement(node.Condition); // Rebase remains authoritative.
        if(file=="Navigation/AIShipAvoidancePatch.cs"&&!Has("station-docked")&&node.Condition.ToString()=="instruction.Calls(hidden)")return Visit(node.Else!.Statement);
        if(file=="Navigation/Collisions/PhysicalCollisions.cs"&&!Has("station-docked")&&node.Condition.ToString().Contains("StationDockedCollisions.ShipCollision",StringComparison.Ordinal))return null;
        return base.VisitIfStatement(node);
    }
    public override SyntaxNode? VisitFieldDeclaration(FieldDeclarationSyntax node) {
        if(file=="NavConsole/Outlines/ContourBuilder.cs"&&!Has("hull-collisions")&&node.Declaration.Variables.Any(v=>v.Identifier.ValueText=="collision"))return null;
        return base.VisitFieldDeclaration(node);
    }
    public override SyntaxNode? VisitObjectCreationExpression(ObjectCreationExpressionSyntax node) {
        if(file=="NavConsole/Outlines/ContourBuilder.cs"&&!Has("hull-collisions")&&node.Type.ToString()=="Envelope"&&node.ArgumentList?.Arguments.Count==3)
            return node.WithArgumentList(node.ArgumentList.WithArguments(SyntaxFactory.SeparatedList(node.ArgumentList.Arguments.Take(2))));
        return base.VisitObjectCreationExpression(node);
    }
    public override SyntaxNode? VisitInitializerExpression(InitializerExpressionSyntax node) {
        if(file=="NavConsole/Outlines/HullDisplay.cs"&&!Has("hull-collisions"))
            node=node.WithExpressions(SyntaxFactory.SeparatedList(node.Expressions.Where(e=>!(e is AssignmentExpressionSyntax a&&a.Left is ImplicitElementAccessSyntax key&&key.ArgumentList.Arguments[0].Expression is LiteralExpressionSyntax l&&l.Token.ValueText=="physicalCollisions"))));
        return base.VisitInitializerExpression(node);
    }
    public override SyntaxNode? VisitMethodDeclaration(MethodDeclarationSyntax node) {
        string owner=Type(node),name=node.Identifier.ValueText;
        if(file=="Orbital/OrbitalPatches.cs"&&owner=="OrbitalGravityPatch"&&name=="Prefix"&&!Has("sff"))return null;
        if(file=="NavConsole/ProximityCollisionPatch.cs") {
            if(!Has("proximity-range")&&name is "Prefix" or "Finalizer")return null;
            if(name=="Transpiler") {
                var statements=node.Body!.Statements.ToList();
                int hidden=statements.FindIndex(s=>s is LocalDeclarationStatementSyntax d&&d.Declaration.Variables.Any(v=>v.Identifier.ValueText=="hidden"));
                int starts=statements.FindIndex(s=>s is LocalDeclarationStatementSyntax d&&d.Declaration.Variables.Any(v=>v.Identifier.ValueText=="starts"));
                Files.Need(hidden>=0&&starts>hidden,"Proximity source layout changed; custom composition requires review.");
                if(!Has("proximity-range"))statements=statements.Take(Has("station-docked")?starts:hidden).Append(SyntaxFactory.ParseStatement("return code;")).ToList();
                else if(!Has("station-docked"))statements.RemoveRange(hidden,starts-hidden);
                return node.WithBody(node.Body.WithStatements(SyntaxFactory.List(statements)));
            }
        }
        if(file=="Orbital/OrbitalPatches.cs"&&owner=="OrbitalStepObservationPatch"&&name=="Prefix") {
            if(Has("economy-sff")&&Has("sff"))return base.VisitMethodDeclaration(node);
            string text="OrbitalSimulation.ObserveUpdate(__instance,fTimeDelta); __state=null;";
            if(Has("sff")) {
                text+="if(!GUIFFWD.Active||fTimeDelta<60)return;";
                if(Has("economy-sff"))text+="var economy=Economy.SffEconomy.Begin(__instance,fTimeDelta,true);";
                text+="var motion=SffSimulation.Begin(__instance,fTimeDelta,true);";
                text+=Has("economy-sff")?"if(economy!=null||motion!=null)__state=new Step{Motion=motion,Economy=economy};":"if(motion!=null)__state=new Step{Motion=motion};";
            }
            return node.WithBody(Body(text));
        }
        if(file=="Orbital/OrbitalPatches.cs"&&owner=="OrbitalConsolePatch"&&name=="Prefix") {
            if(Has("sff")&&Has("coast"))return base.VisitMethodDeclaration(node);
            string text="if(strInput==null)return true; string command=strInput.Trim().ToLowerInvariant(); if(command!=\"orbital\"&&!command.StartsWith(\"orbital \"))return true; string argument=command.Length>7?command.Substring(7).Trim():\"status\"; if(argument==\"on\")OrbitalSimulation.SetEnabled(true); else if(argument==\"off\")OrbitalSimulation.SetEnabled(false); else if(argument==\"toggle\")OrbitalSimulation.SetEnabled(!OrbitalSimulation.Enabled);";
            if(Has("sff"))text+="else if(argument==\"sff on\"||argument==\"sff off\")SffSimulation.Enabled=argument==\"sff on\";";
            if(Has("coast"))text+="else if(argument==\"coast on\"||argument==\"coast off\")CoastIntegration.SetEnabled(argument==\"coast on\");";
            text+="else if(argument!=\"status\"&&argument.Length!=0){ConsoleToGUI.instance?.LogInfo(\"Unknown orbital command in this build.\");return false;} OrbitalSimulation.Status();return false;";
            return node.WithBody(Body(text));
        }
        return base.VisitMethodDeclaration(node);
    }
    internal static string Apply(string file,string source,string[] features) {
        var tree=Inventory.Parse(source);var result=new SourcePruner(file,features).Visit(tree)!;
        return result.NormalizeWhitespace().ToFullString()+"\n";
    }
}
