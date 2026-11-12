using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

static class Program {
    static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    static string root = "", folder = "";
    static bool query;
    static T Read<T>(string file) => JsonSerializer.Deserialize<T>(File.ReadAllText(file), Json) ?? throw new Exception("Empty document: "+file);
    static T Copy<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, Json), Json)!;
    static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    static void Main(string[] args) {
        try {
            query = args.FirstOrDefault() is "scenario" or "search" or "node" or "source" or "evidence" or "patch";
            root = Path.GetFullPath(Environment.CurrentDirectory);
            while (!File.Exists(Path.Combine(root, "Ostrabus.csproj"))) root = Directory.GetParent(root)?.FullName ?? throw new Exception("Run from within the repository.");
            if (args.FirstOrDefault() == "coverage") {
                BehaviorCoverage.Run(root, args);
                return;
            }
            folder = Path.Combine(root, "Navigation", "BehaviorGraph");
            string modelName=args.Contains("--model") ? args.ElementAtOrDefault(Array.IndexOf(args,"--model")+1) ?? throw new Exception("Missing model name.") : "retriever";
            Check(modelName is "retriever" or "scavenger" or "deployer" or "target-lifecycle" or "traffic","Unknown model: "+modelName);
            bool retriever=modelName=="retriever";
            Check(retriever || args.FirstOrDefault()!="assembly-self-test","Run assembly-self-test on the default retriever fixture model; slice audits use --model.");
            if(!retriever)folder=Path.Combine(folder,modelName switch { "scavenger" => "Scavenger", "deployer" => "Deployer", "traffic" => "Traffic", _ => "TargetLifecycle" });
            var vanilla = Read<Model>(Path.Combine(folder, retriever ? "retriever.vanilla.json" : "vanilla.json"));
            var overlay = Read<Overlay>(Path.Combine(folder, retriever ? "ostrabus.overlay.json" : "overlay.json"));
            bool assemblyCommand=args.FirstOrDefault()?.StartsWith("assembly-",StringComparison.Ordinal)==true;
            Validate(vanilla,!assemblyCommand); Check(overlay.Base == vanilla.Id && overlay.SchemaVersion == 1, "Overlay/base mismatch.");
            var effective = Apply(vanilla, overlay); Validate(effective,!assemblyCommand);
            if(assemblyCommand) {AssemblyReview.Run(args,root,folder,vanilla,effective,overlay);return;}
            string? gameRoot = null;
            if (args.Contains("--game-source")) {
                int at = Array.IndexOf(args, "--game-source");
                gameRoot = Path.GetFullPath(args.ElementAtOrDefault(at+1) ?? throw new Exception("Missing game source root."));
            }
            VerifySources(vanilla, gameRoot);
            if (modelName == "traffic") TrafficTrees.Verify(folder, vanilla);
            if(args.Contains("--assembly")) {
                string file=args.ElementAtOrDefault(Array.IndexOf(args,"--assembly")+1) ?? throw new Exception("Missing assembly path.");
                Check(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))).Equals(vanilla.AssemblySha256,StringComparison.OrdinalIgnoreCase),"Game assembly fingerprint differs; review model against this version.");
                Console.Error.WriteLine("Pinned game assembly fingerprint matches.");
            }
            int tested = Test(vanilla, effective);
            string command = args.FirstOrDefault() ?? "validate";
            if (command == "generate") {
                File.WriteAllText(Path.Combine(folder, retriever ? "retriever.effective.json" : "effective.json"), JsonSerializer.Serialize(effective, Json)+"\n");
                File.WriteAllText(Path.Combine(folder, "VANILLA.md"), Render(vanilla).TrimEnd()+"\n");
                File.WriteAllText(Path.Combine(folder, "EFFECTIVE.md"), Render(effective).TrimEnd()+"\n");
                File.WriteAllText(Path.Combine(folder, "AUDIT.md"), Audit(vanilla, effective, overlay).TrimEnd()+"\n");
                if (modelName == "traffic") File.WriteAllText(Path.Combine(folder, "CONTROL-TREES.md"), TrafficTrees.Render(folder).TrimEnd()+"\n");
                Console.WriteLine("Generated effective graph, vanilla/effective diagrams and audit.");
            } else if (command == "scenario") {
                string id = args.ElementAtOrDefault(1) ?? throw new Exception("scenario requires an ID.");
                var scenario = vanilla.Scenarios.Single(s => s.Id == id);
                Console.WriteLine(JsonSerializer.Serialize(new[] { ("vanilla", vanilla), ("effective", effective) }.Select(mode=>
                    new {mode=mode.Item1,scenario=id,result=Run(mode.Item2,scenario)}).ToArray(),Json));
            } else if (command == "node") {
                string id = args.ElementAtOrDefault(1) ?? throw new Exception("node requires an ID.");
                Check(effective.Nodes.ContainsKey(id), "Unknown node: "+id);
                var incoming=effective.Edges.Where(e=>e.To==id).ToArray();var outgoing=effective.Edges.Where(e=>e.From==id).ToArray();
                var refs=effective.Nodes[id].Sources.Concat(incoming.Concat(outgoing).SelectMany(e=>e.Sources)).Distinct().ToDictionary(key=>key,key=>effective.Sources[key]);
                Console.WriteLine(JsonSerializer.Serialize(new { node = effective.Nodes[id], incoming, outgoing, references=refs }, Json));
            } else if (command == "source") {
                string id = args.ElementAtOrDefault(1) ?? throw new Exception("source requires an ID.");
                Console.WriteLine(JsonSerializer.Serialize(vanilla.Sources[id], Json));
            } else if (command == "evidence") {
                Console.WriteLine(JsonSerializer.Serialize(vanilla.Evidence[args.ElementAtOrDefault(1) ?? throw new Exception("evidence requires an ID.")],Json));
            } else if (command == "patch") {
                string id=args.ElementAtOrDefault(1) ?? throw new Exception("patch requires an ID.");
                Console.WriteLine(JsonSerializer.Serialize(new { patch=overlay.Patches.Single(p=>p.Id==id), edges=effective.Edges.Where(e=>e.Patch==id) },Json));
            } else if (command == "search") {
                var scenario=vanilla.Scenarios.Single(s=>s.Id==(args.ElementAtOrDefault(1) ?? vanilla.Scenarios[0].Id));
                string finding=args.ElementAtOrDefault(2) ?? vanilla.Findings[0].Id;
                int bound=args.Length>3 && !args[3].StartsWith("--") ? int.Parse(args[3]) : 8;
                Check(bound>=1 && bound<=16,"Search bound must be 1..16.");
                Console.WriteLine(JsonSerializer.Serialize(new[]{("vanilla",vanilla),("effective",effective)}.Select(mode=>
                    new{mode=mode.Item1,result=Search(mode.Item2,scenario,finding,bound)}).ToArray(),Json));
            } else if (command != "validate") throw new Exception("Commands: validate, generate, scenario ID, search [SCENARIO FINDING BOUND], node ID, source ID, evidence ID, patch ID.");
            string summary=$"Validated {vanilla.Nodes.Count} control points, {vanilla.Edges.Count} vanilla / {effective.Edges.Count} effective edges; {tested} conditional scenario expectations and model safety checks passed.";
            if(query)Console.Error.WriteLine(summary);else Console.WriteLine(summary);
        } catch (Exception error) { Console.Error.WriteLine(error.Message); Environment.ExitCode = 1; }
    }
    static void Validate(Model m,bool checkPaths=true) {
        Check(m.SchemaVersion == 1, "Unknown schema version.");
        Check(m.Edges.Select(e => e.Id).Distinct().Count() == m.Edges.Count, "Duplicate edge ID.");
        foreach (var (id, source) in m.Sources) {
            Check(source.Origin is "game" or "repo", "Invalid source origin: "+id);
            Check(!Path.IsPathRooted(source.Path) && !source.Path.Contains(':') && !source.Path.Split('/').Contains(".."), "Source path must be relative: "+id);
            Check(source.Anchors.Length > 0 && source.Symbol.Length > 0, "Missing method anchor: "+id);
            if (checkPaths && source.Origin == "repo") Check(File.Exists(Path.Combine(root, source.Path)), "Missing repository source: "+source.Path);
        }
        void Refs(IEnumerable<string> refs) { foreach (string id in refs) Check(m.Sources.ContainsKey(id), "Unknown source: "+id); }
        void Condition(Condition c, HashSet<string> stack) {
            foreach (var (fact, values) in c.Facts) {
                Check(m.Variables.TryGetValue(fact, out var variable), "Unknown fact: "+fact);
                Check(values.Length > 0 && values.All(v => variable!.Values.Contains(v)), "Invalid domain in condition: "+fact);
            }
            foreach (string id in c.Require.Concat(c.Forbid)) {
                Check(m.Predicates.ContainsKey(id), "Unknown predicate: "+id);
                Check(stack.Add(id), "Recursive predicate: "+id); Condition(m.Predicates[id], stack); stack.Remove(id);
            }
        }
        void Facts(Dictionary<string,string> facts) {
            foreach (var (id, value) in facts) Check(m.Variables.TryGetValue(id, out var variable) && variable.Values.Contains(value), "Unknown fact/value: "+id+"="+value);
        }
        foreach (var predicate in m.Predicates.Values) Condition(predicate, new());
        foreach (var node in m.Nodes.Values) {Check(node.Sources.Length>0,"Node has no source binding.");Refs(node.Sources);}
        foreach (var edge in m.Edges) {
            Check(m.Nodes.ContainsKey(edge.From) && m.Nodes.ContainsKey(edge.To), "Unknown node in edge: "+edge.Id);
            Check(edge.Sources.Length>0,"Edge has no source binding: "+edge.Id);
            Condition(edge.When, new()); Facts(edge.Set); Refs(edge.Sources);
            foreach (var guard in edge.Guards) { Check(m.Predicates.ContainsKey(guard.Predicate), "Unknown guard predicate."); Facts(guard.ProbeSet); }
            foreach (string id in edge.Evidence) Check(m.Evidence.ContainsKey(id), "Unknown evidence: "+id);
        }
        foreach (var finding in m.Findings) { Condition(finding.When, new()); Refs(finding.Sources); }
        if(checkPaths)foreach (var evidence in m.Evidence.Values) Check(File.Exists(Path.Combine(root, evidence.Artifact)), "Missing evidence artifact: "+evidence.Artifact);
        foreach (var scenario in m.Scenarios) { Check(m.Nodes.ContainsKey(scenario.Start), "Unknown scenario entry."); Facts(scenario.Facts); }
    }
    static Model Apply(Model vanilla, Overlay overlay) {
        var m = Copy(vanilla); m.Id = overlay.Id;
        foreach (var patch in overlay.Patches) {
            Check(patch.Removes.Length > 0 && patch.Preserves.Length > 0 && patch.Classification.Length > 0, "Patch lacks behavioral tradeoff: "+patch.Id);
            foreach (var op in patch.Operations) {
                Check(op.Phase is "before" or "after","Unknown interception phase: "+op.Phase);
                if (op.Kind == "add") {
                    m.Edges.Add(new Edge { Id=op.Id, From=op.From, To=op.To, Event=op.Event, When=new() { Require=new[] { op.Predicate } }, Set=op.Set,
                        Sources=patch.Sources, Evidence=patch.Evidence, Note=patch.Summary, Patch=patch.Id });
                    continue;
                }
                var original = m.Edges.Single(e => e.Id == op.Edge);
                var changed = Copy(original); changed.Id=op.Id; changed.To=op.To; changed.Patch=patch.Id;
                changed.Sources=original.Sources.Concat(patch.Sources).Distinct().ToArray(); changed.Evidence=original.Evidence.Concat(patch.Evidence).Distinct().ToArray(); changed.Note=patch.Summary;
                foreach(string key in op.RemoveSet)Check(changed.Set.Remove(key),"Cannot remove absent effect: "+key);
                foreach (var effect in op.Set) changed.Set[effect.Key]=effect.Value;
                if (op.Kind == "replace") { m.Edges.Remove(original); m.Edges.Add(changed); }
                else if (op.Kind == "intercept") {
                    // A postfix sees native effects first; a prefix sees entry facts.
                    // ProbeSet is a counterfactual guard evaluation, not an extra write.
                    var probe = op.Phase == "after" ? new Dictionary<string,string>(original.Set) : new();
                    original.Guards.Add(new() { Predicate=op.Predicate, Negated=true, ProbeSet=probe });
                    changed.Guards.Add(new() { Predicate=op.Predicate, ProbeSet=probe }); m.Edges.Add(changed);
                } else throw new Exception("Unknown overlay operation: "+op.Kind);
            }
        }
        return m;
    }
    static bool? Evaluate(Model m, Condition condition, Dictionary<string,string> facts) {
        bool unknown = false;
        foreach (var (key, allowed) in condition.Facts) {
            if (!facts.TryGetValue(key, out string? value)) unknown=true;
            else if (!allowed.Contains(value)) return false;
        }
        foreach (string id in condition.Require) { var value=Evaluate(m,m.Predicates[id],facts); if(value==false)return false; if(value==null)unknown=true; }
        foreach (string id in condition.Forbid) { var value=Evaluate(m,m.Predicates[id],facts); if(value==true)return false; if(value==null)unknown=true; }
        return unknown ? null : true;
    }
    static bool? Enabled(Model m, Edge e, Dictionary<string,string> facts) {
        bool unknown=Evaluate(m,e.When,facts)==null;
        if(Evaluate(m,e.When,facts)==false)return false;
        foreach(var guard in e.Guards) {
            var probe=new Dictionary<string,string>(facts); foreach(var effect in guard.ProbeSet)probe[effect.Key]=effect.Value;
            var value=Evaluate(m,m.Predicates[guard.Predicate],probe);
            if(value==null)unknown=true;
            else if(value==guard.Negated)return false;
        }
        return unknown ? null : true;
    }
    internal static Trace Run(Model m, Scenario s) {
        var result=new Trace { Node=s.Start, Facts=new(s.Facts) };
        void Findings() {
            foreach(var f in m.Findings) {
                var value=Evaluate(m,f.When,result.Facts);
                if(value==true && !result.Findings.Contains(f.Id))result.Findings.Add(f.Id);
                if(value==null && !result.UnknownChecks.Contains(f.Id))result.UnknownChecks.Add(f.Id);
            }
        }
        Findings();
        foreach(string evt in s.Events) {
            var candidates=m.Edges.Where(e=>e.From==result.Node && e.Event==evt).Select(e=>(Edge:e,Enabled:Enabled(m,e,result.Facts))).ToArray();
            var yes=candidates.Where(c=>c.Enabled==true).ToArray();
            if(candidates.Any(c=>c.Enabled==null)) {result.Stop="unknown guards at "+evt;break;}
            if(yes.Length!=1) {result.Stop=(yes.Length==0 ? "blocked" : "ambiguous")+" event "+evt;break;}
            var edge=yes[0].Edge; foreach(var effect in edge.Set)result.Facts[effect.Key]=effect.Value;
            result.Node=edge.To;result.Edges.Add(edge.Id);Findings();
        }
        return result;
    }
    static int Test(Model vanilla, Model effective) {
        int count=0;
        foreach(var scenario in vanilla.Scenarios) foreach(var (mode,expect) in scenario.Expect) {
            var result=Run(mode=="vanilla" ? vanilla : effective,scenario);
            if(expect.Node!=null) { Check(result.Node==expect.Node, scenario.Id+" "+mode+": expected node "+expect.Node+", got "+result.Node+" ("+result.Stop+")");count++; }
            if(expect.Finding!=null) {Check(result.Findings.Contains(expect.Finding),scenario.Id+": expected finding missing: "+expect.Finding);count++;}
            if(expect.Without!=null) {Check(!result.Findings.Contains(expect.Without),scenario.Id+": unexpected finding: "+expect.Without);count++;}
            if(expect.Fact!=null) {Check(result.Facts.GetValueOrDefault(expect.Fact)==expect.Value,scenario.Id+": unexpected final fact.");count++;}
            if(expect.Stop!=null) {Check(result.Stop==expect.Stop,scenario.Id+": unexpected trace termination: "+result.Stop);count++;}
        }
        if(vanilla.Edges.Any(e=>e.Id=="sff.pickup")) {
            var absent=new Scenario { Start="fly", Facts=new(), Events=new[] { "sff_pickup" } };
            Check(Run(effective,absent).Stop.StartsWith("unknown"),"Unknown facts were silently treated as false.");count++;
            Check(vanilla.Edges.Single(e=>e.Id=="sff.pickup").Guards.Count==0,"Overlay mutated the vanilla document.");count++;
        } else {
            var conditional=vanilla.Edges.First(e=>e.When.Facts.Count>0);
            var absent=new Scenario {Start=conditional.From,Events=new[]{conditional.Event}};
            Check(Run(effective,absent).Stop.StartsWith("unknown"),"Unknown slice facts were silently treated as false.");count++;
        }
        var typo=Copy(vanilla);typo.Edges[0].Set["invented_fact"]="true";
        bool rejected=false;try { Validate(typo); } catch { rejected=true; }Check(rejected,"Unknown state writes accepted.");count++;
        var cycle=Copy(vanilla);cycle.Predicates["self_cycle"]=new() { Require=new[]{"self_cycle"} };
        rejected=false;try {Validate(cycle);} catch {rejected=true;}Check(rejected,"Recursive predicate accepted.");count++;
        if(vanilla.Edges.Any(e=>e.Id=="sff.pickup")) {
            var seed=vanilla.Scenarios.Single(s=>s.Id=="sff-pickup-conflict");
            Check(Search(vanilla,seed,"risk.route_anchor_conflict",8).Witness!=null,"Bounded search missed the known native witness.");count++;
            Check(Search(effective,seed,"risk.route_anchor_conflict",8).Witness==null,"Bounded search found an unexpected modeled patched witness.");count++;
        }
        if (vanilla.Id == "target-lifecycle-1.0.1.10") {
            var seed = vanilla.Scenarios.Single(s => s.Id == "retriever-retires-outbound-scav-target");
            foreach (string finding in new[] { "target.retained_goal", "target.divergent_route" }) {
                Check(Search(vanilla, seed, finding, 6).Witness != null, "Target-lifetime search missed the native " + finding + " witness."); count++;
                Check(Search(effective, seed, finding, 6).Witness == null, "Target-lifetime search found a repaired " + finding + " witness."); count++;
            }
        }
        return count;
    }
    static SearchResult Search(Model m, Scenario seed, string finding, int bound) {
        Check(m.Findings.Any(f=>f.Id==finding),"Unknown finding: "+finding);
        var result=new SearchResult {Bound=bound};
        var queue=new Queue<Trace>();queue.Enqueue(new() {Node=seed.Start,Facts=new(seed.Facts)});
        var seen=new HashSet<string>();
        while(queue.Count>0 && result.Visited<4096) {
            var state=queue.Dequeue();
            string key=state.Node+"|"+string.Join(";",state.Facts.OrderBy(p=>p.Key).Select(p=>p.Key+"="+p.Value));
            if(!seen.Add(key))continue;
            result.Visited++;
            var match=Evaluate(m,m.Findings.Single(f=>f.Id==finding).When,state.Facts);
            if(match==true) {result.Witness=state;return result;}
            if(match==null)result.UnknownFindings++;
            if(state.Edges.Count>=bound) {result.BoundReached=true;continue;}
            foreach(var edge in m.Edges.Where(e=>e.From==state.Node)) {
                var enabled=Enabled(m,edge,state.Facts);
                if(enabled==null){result.UnknownEdges++;continue;}
                if(enabled==false)continue;
                var next=new Trace {Node=edge.To,Facts=new(state.Facts),Edges=new(state.Edges)};
                foreach(var effect in edge.Set)next.Facts[effect.Key]=effect.Value;
                next.Edges.Add(edge.Id);queue.Enqueue(next);
            }
        }
        result.StateLimitReached=queue.Count>0;
        return result;
    }
    static void VerifySources(Model m, string? gameRoot) {
        int verified=0;
        foreach(var (id,source) in m.Sources) {
            if(source.Origin=="game" && gameRoot==null)continue;
            string path=Path.Combine(source.Origin=="game" ? gameRoot! : root,source.Path);
            Check(File.Exists(path),"Missing source: "+id+" ("+source.Path+")");
            string text=File.ReadAllText(path);
            foreach(string anchor in source.Anchors)Check(text.Contains(anchor,StringComparison.Ordinal),"Source anchor changed: "+id+" / "+anchor);
            if(source.Sha256.Length>0)Check(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.Replace("\r\n","\n")))).Equals(source.Sha256,StringComparison.OrdinalIgnoreCase),"Source fingerprint changed; review graph: "+id);
            verified++;
        }
        Console.Error.WriteLine("Verified "+verified+" source files/anchors/fingerprints; lexical checks are not semantic equivalence proofs.");
        if(gameRoot==null)Console.Error.WriteLine("Game source fingerprints not checked; supply --game-source for version review.");
    }
    static string Render(Model m) {
        var b=new StringBuilder("<!-- Generated by Tools/AIBehaviorGraph. Edit JSON sources, not this file. -->\n\n# "+m.Id+"\n\nPartial control-point graph, not a full state machine. Edge guards and effects are in JSON. Operator events are explicit, never autonomous defaults.\n\n");
        foreach(var view in m.Nodes.ContainsKey("scav_hold") ? new[]{"flow"} : new[] { "flow", "motion", "lifecycle" }) {
            var edges=m.Edges.Where(e=>view=="motion" ? e.From is "path" or "group" or "queued_haul" || e.To is "path" or "group" or "queued_haul" || e.Id=="fix.handoff" : view=="lifecycle" ? new[]{"retired","reserve","spawn","load","replacement","unregistered","operator","reserve_blocked"}.Contains(e.From) || new[]{"retired","unregistered","reserve_blocked"}.Contains(e.To) : !new[]{"path","group","reserve","spawn","load","replacement"}.Contains(e.From) && !new[]{"path","group","reserve","spawn","load","replacement"}.Contains(e.To)).ToArray();
            if (edges.Length == 0) continue;
            b.AppendLine("## "+view+"\n\n```mermaid\nflowchart TD");
            foreach(string id in edges.SelectMany(e=>new[]{e.From,e.To}).Distinct()) b.AppendLine("  n_"+id+"[\""+m.Nodes[id].Label.Replace("\"","'")+"\"]");
            foreach(var e in edges) b.AppendLine("  n_"+e.From+" -->|\""+e.Event+(e.Patch.Length>0 ? " / "+e.Patch : "")+(e.Guards.Count>0 ? " [conditional]" : "")+"\"| n_"+e.To);
            b.AppendLine("```\n");
        }
        return b.ToString();
    }
    static string Audit(Model vanilla, Model effective, Overlay overlay) {
        var b=new StringBuilder("<!-- Generated by Tools/AIBehaviorGraph. -->\n\n# Ship transition audit\n\nThese are conditional reference traces, not live-game measurements or new native tests. Missing facts stop with unknown; blocked events are not proofs that no unmodeled path exists.\n\n| Scenario | Vanilla | Effective |\n| --- | --- | --- |\n");
        foreach(var s in vanilla.Scenarios) {
            string Show(Model m) { var t=Run(m,s);return t.Node+"; "+(t.Findings.Count>0 ? string.Join(", ",t.Findings) : "no modeled finding")+(t.Stop.Length>0 ? "; "+t.Stop : ""); }
            b.AppendLine("| "+s.Id+" | "+Show(vanilla)+" | "+Show(effective)+" |");
        }
        b.AppendLine("\n## Behavioral tradeoffs\n");
        foreach(var p in overlay.Patches) b.AppendLine("- **"+p.Id+" / "+p.Classification+"**: "+p.Summary+" Removes: "+string.Join("; ",p.Removes)+". Preserves: "+string.Join("; ",p.Preserves)+".");
        b.AppendLine("\n## Dependencies not transition-expanded\n");
        foreach(var dependency in overlay.DocumentedDependencies)b.AppendLine("- **"+dependency.Id+"**: "+dependency.Reason);
        b.AppendLine("\nThese unresolved boundaries prevent calling this an all-patches effective simulation. Native inner/outer Harmony phases and side effects must be expanded before drawing conclusions across them.\n");
        return b.ToString();
    }
}

sealed class Model {
    public int SchemaVersion {get;set;} public string Id {get;set;}=""; public string GameVersion {get;set;}=""; public string AssemblySha256 {get;set;}=""; public string BaselineCommit {get;set;}="";
    public JsonElement Scope {get;set;} public Dictionary<string,Source> Sources {get;set;}=new(); public Dictionary<string,Evidence> Evidence {get;set;}=new();
    public Dictionary<string,Variable> Variables {get;set;}=new(); public Dictionary<string,Condition> Predicates {get;set;}=new(); public Dictionary<string,Node> Nodes {get;set;}=new();
    public List<Edge> Edges {get;set;}=new(); public List<Finding> Findings {get;set;}=new(); public List<Scenario> Scenarios {get;set;}=new();
}
sealed class Source {public string Origin {get;set;}="";public string Path {get;set;}="";public string Symbol {get;set;}="";public string[] Anchors {get;set;}=Array.Empty<string>();public string Sha256 {get;set;}="";}
sealed class Evidence {public string Level {get;set;}="";public string Artifact {get;set;}="";public string Supports {get;set;}="";}
sealed class Variable {public string[] Values {get;set;}=Array.Empty<string>();public string Description {get;set;}="";public string Kind {get;set;}="";}
sealed class Node {public string Label {get;set;}="";public string Layer {get;set;}="";public string[] Sources {get;set;}=Array.Empty<string>();public string Detail {get;set;}="";}
sealed class Condition {public Dictionary<string,string[]> Facts {get;set;}=new();public string[] Require {get;set;}=Array.Empty<string>();public string[] Forbid {get;set;}=Array.Empty<string>();}
sealed class Guard {public string Predicate {get;set;}="";public bool Negated {get;set;} public Dictionary<string,string> ProbeSet {get;set;}=new();}
sealed class Edge {
    public string Id {get;set;}="";public string From {get;set;}="";public string To {get;set;}="";public string Event {get;set;}="";public Condition When {get;set;}=new();public Dictionary<string,string> Set {get;set;}=new();
    public string[] Sources {get;set;}=Array.Empty<string>();public string[] Evidence {get;set;}=Array.Empty<string>();public string Note {get;set;}="";public List<Guard> Guards {get;set;}=new();public string Patch {get;set;}="";
}
sealed class Finding {public string Id {get;set;}="";public string Kind {get;set;}="";public string Severity {get;set;}="";public Condition When {get;set;}=new();public string[] Sources {get;set;}=Array.Empty<string>();public string[] Evidence {get;set;}=Array.Empty<string>();public string Note {get;set;}="";}
sealed class Scenario {public string Id {get;set;}="";public string Start {get;set;}="";public Dictionary<string,string> Facts {get;set;}=new();public string[] Events {get;set;}=Array.Empty<string>();public Dictionary<string,Expectation> Expect {get;set;}=new();public string Note {get;set;}="";}
sealed class Expectation {public string? Node {get;set;}public string? Finding {get;set;}public string? Without {get;set;}public string? Fact {get;set;}public string? Value {get;set;}public string? Stop {get;set;}}
sealed class Trace {public string Node {get;set;}="";public Dictionary<string,string> Facts {get;set;}=new();public List<string> Edges {get;set;}=new();public List<string> Findings {get;set;}=new();public List<string> UnknownChecks {get;set;}=new();public string Stop {get;set;}="";}
sealed class SearchResult {public string Interpretation {get;set;}="Conditional bounded search with fixed external outcomes; no witness is not a safety proof.";public int Bound {get;set;}public int Visited {get;set;}public int UnknownEdges {get;set;}public int UnknownFindings {get;set;}public bool BoundReached {get;set;}public bool StateLimitReached {get;set;}public Trace? Witness {get;set;}}
sealed class Overlay {public int SchemaVersion {get;set;}public string Id {get;set;}="";public string Base {get;set;}="";public List<Patch> Patches {get;set;}=new();public List<Dependency> DocumentedDependencies {get;set;}=new();public JsonElement NotApplicable {get;set;}}
sealed class Dependency {public string Id {get;set;}="";public string[] Sources {get;set;}=Array.Empty<string>();public string Status {get;set;}="";public string Reason {get;set;}="";}
sealed class Patch {public string Id {get;set;}="";public string Classification {get;set;}="";public string[] Sources {get;set;}=Array.Empty<string>();public string[] Evidence {get;set;}=Array.Empty<string>();public string Summary {get;set;}="";public string[] Removes {get;set;}=Array.Empty<string>();public string[] Preserves {get;set;}=Array.Empty<string>();public List<Operation> Operations {get;set;}=new();}
sealed class Operation {public string Kind {get;set;}="";public string Edge {get;set;}="";public string Id {get;set;}="";public string Predicate {get;set;}="";public string Phase {get;set;}="before";public string From {get;set;}="";public string To {get;set;}="";public string Event {get;set;}="";public Dictionary<string,string> Set {get;set;}=new();public string[] RemoveSet {get;set;}=Array.Empty<string>();}
