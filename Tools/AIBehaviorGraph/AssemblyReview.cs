using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.IO.Compression;

static class AssemblyReview {
    internal static readonly JsonSerializerOptions Json=new() {PropertyNamingPolicy=JsonNamingPolicy.CamelCase,PropertyNameCaseInsensitive=true,UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow};
    static readonly JsonSerializerOptions Pretty=new(Json) {WriteIndented=true};
    internal static T Read<T>(string path) {
        using var file=File.OpenRead(path);
        if(path.EndsWith(".br",StringComparison.Ordinal)) {using var decoded=new BrotliStream(file,CompressionMode.Decompress);return JsonSerializer.Deserialize<T>(decoded,Json) ?? throw new Exception("Empty snapshot: "+path);}
        return JsonSerializer.Deserialize<T>(file,Json) ?? throw new Exception("Empty file: "+path);
    }
    static void Need(bool ok,string message) {if(!ok)throw new Exception(message);}
    static string Option(string[] args,string name,string? fallback=null) {
        int at=Array.IndexOf(args,name);
        return at>=0 ? args.ElementAtOrDefault(at+1) ?? throw new Exception("Missing value for "+name) : fallback ?? throw new Exception("Required option: "+name);
    }
    internal static string Canonical(JsonElement value)=>value.ValueKind switch {
        JsonValueKind.Object=>"{"+string.Join(",",value.EnumerateObject().OrderBy(p=>p.Name,StringComparer.Ordinal).Select(p=>JsonSerializer.Serialize(p.Name)+":"+Canonical(p.Value)))+"}",
        JsonValueKind.Array=>"["+string.Join(",",value.EnumerateArray().Select(Canonical))+"]",
        _=>value.GetRawText()
    };
    internal static Capture Capture(AssemblyFingerprint graph,DependencyManifest manifest,string? dataRoot,string modelHash="") {
        var result=new Capture {Snapshot=new() {ModelHash=modelHash,AssemblyHash=graph.BinaryHash,ManifestHash=AssemblyFingerprint.Hash(Canonical(JsonSerializer.SerializeToElement(manifest,Json))),References=new(graph.ReferenceHashes)}};
        foreach(var asset in manifest.Assets.OrderBy(a=>a.Id,StringComparer.Ordinal)) {
            var stamp=new AssetStamp {Path=asset.Path,Entry=asset.Entry};
            try {
                Need(!Path.IsPathRooted(asset.Path) && !asset.Path.Contains(':') && !asset.Path.Split('/').Contains(".."),"Asset path must be relative.");
                if(dataRoot==null)throw new Exception("Data root not supplied.");
                using var doc=JsonDocument.Parse(File.ReadAllText(Path.Combine(dataRoot,asset.Path)),new JsonDocumentOptions {CommentHandling=JsonCommentHandling.Skip,AllowTrailingCommas=true});
                var entries=doc.RootElement.ValueKind==JsonValueKind.Array ? doc.RootElement.EnumerateArray().Where(e=>e.ValueKind==JsonValueKind.Object && e.TryGetProperty(asset.Key,out var key) && key.ValueKind==JsonValueKind.String && key.GetString()==asset.Entry).ToArray() : Array.Empty<JsonElement>();
                Need(entries.Length==1,"Expected one matching asset entry, found "+entries.Length);
                stamp.Hash=AssemblyFingerprint.Hash(Canonical(entries[0]));stamp.Status="tracked";
            } catch(Exception error) {stamp.Status="unresolved";stamp.Note=error.Message;}
            result.Snapshot.Assets[asset.Id]=stamp;
        }
        var consumerRoots=manifest.Assets.ToDictionary(a=>a.Id,a=>a.Consumers.SelectMany(graph.Select).ToHashSet(StringComparer.Ordinal),StringComparer.Ordinal);
        foreach(var (source,selectors) in manifest.Bindings.OrderBy(p=>p.Key,StringComparer.Ordinal)) {
            string[] roots=selectors.SelectMany(graph.Select).Distinct().OrderBy(x=>x,StringComparer.Ordinal).ToArray();
            var closure=graph.Closure(roots);result.Closures[source]=closure;
            var group=new GroupStamp {Roots=roots,DirectHash=graph.Digest(roots),DependencyHash=graph.Digest(closure),MissingSelectors=selectors.SelectMany(graph.Missing).ToArray()};
            group.Assets=manifest.Assets.Where(a=>a.Sources.Contains(source) || consumerRoots[a.Id].Overlaps(closure)).Select(a=>a.Id).OrderBy(x=>x,StringComparer.Ordinal).ToArray();
            var stamps=closure.Where(graph.Members.ContainsKey).Select(id=>graph.Members[id]).ToArray();
            group.Caveats=stamps.SelectMany(s=>s.Caveats).Distinct().OrderBy(x=>x,StringComparer.Ordinal).ToArray();
            group.ExternalReferences=stamps.SelectMany(s=>s.ExternalAssemblies).Distinct().OrderBy(x=>x,StringComparer.Ordinal).ToArray();
            group.UnresolvedMembers=closure.Where(id=>!graph.Members.TryGetValue(id,out var s) || s.Hash=="unresolved").OrderBy(x=>x,StringComparer.Ordinal).ToArray();
            result.Snapshot.Groups[source]=group;
            foreach(string id in closure.OrderBy(x=>x,StringComparer.Ordinal))result.Snapshot.Members[id]=graph.Members.TryGetValue(id,out var s) ? s.Hash : "missing";
        }
        string identity=result.Snapshot.Normalizer+"\n"+result.Snapshot.ModelHash+"\n"+graph.BinaryHash+"\n"+result.Snapshot.ManifestHash+"\n"+string.Join("\n",result.Snapshot.Groups.OrderBy(p=>p.Key,StringComparer.Ordinal).Select(p=>p.Key+":"+p.Value.DirectHash+":"+p.Value.DependencyHash))+"\n"+string.Join("\n",result.Snapshot.Assets.OrderBy(p=>p.Key,StringComparer.Ordinal).Select(p=>p.Key+":"+p.Value.Hash))+"\n"+string.Join("\n",result.Snapshot.References.OrderBy(p=>p.Key,StringComparer.Ordinal).Select(p=>p.Key+":"+p.Value));
        result.Snapshot.Id=AssemblyFingerprint.Hash(identity);
        return result;
    }
    internal static Dictionary<string,SourceReview> Compare(AcceptedSnapshot baseline,Capture current,AssemblyFingerprint graph,Model model) {
        var result=new Dictionary<string,SourceReview>(StringComparer.Ordinal);
        foreach(string source in model.Sources.Keys.OrderBy(x=>x,StringComparer.Ordinal)) {
            var review=new SourceReview();result[source]=review;
            baseline.Groups.TryGetValue(source,out var before);current.Snapshot.Groups.TryGetValue(source,out var after);
            void Flag(string status,string reason) {if(review.Status=="unchanged" || status=="unresolved")review.Status=status;review.Reasons.Add(reason);}
            if(before==null || after==null) {Flag("unresolved","No accepted/current native-method binding for this source.");continue;}
            if(baseline.Normalizer!=current.Snapshot.Normalizer)Flag("unresolved","Fingerprint normalizer changed; baselines are not comparable.");
            if(baseline.ManifestHash!=current.Snapshot.ManifestHash)Flag("declaration_changed","Dependency declarations changed; explicit review and acceptance required.");
            if(baseline.ModelHash!=current.Snapshot.ModelHash)Flag("declaration_changed","Graph/overlay changed since the accepted baseline; acceptance must be explicit.");
            if(after.MissingSelectors.Length>0)Flag("unresolved","Missing method selector(s): "+string.Join(", ",after.MissingSelectors));
            if(after.UnresolvedMembers.Length>0)Flag("unresolved",after.UnresolvedMembers.Length+" unresolved in-module dependency references.");
            if(before.DirectHash!=after.DirectHash || !before.Roots.SequenceEqual(after.Roots))Flag("direct_changed","Selected method/signature/body/metadata or overload set changed.");
            else if(before.DependencyHash!=after.DependencyHash)Flag("dependency_changed","Tracked transitive method, field, type or virtual-candidate dependency changed.");
            foreach(string asset in before.Assets.Union(after.Assets)) {
                baseline.Assets.TryGetValue(asset,out var oldAsset);current.Snapshot.Assets.TryGetValue(asset,out var newAsset);
                if(oldAsset?.Status!="tracked" || newAsset?.Status!="tracked")Flag("unresolved","Asset dependency unavailable: "+asset);
                else if(oldAsset.Hash!=newAsset.Hash)Flag("data_changed","Selected JSON entry changed: "+asset);
            }
            foreach(string reference in before.ExternalReferences.Union(after.ExternalReferences)) {
                string? oldHash=baseline.References.GetValueOrDefault(reference),newHash=current.Snapshot.References.GetValueOrDefault(reference);
                if(oldHash==null || newHash==null || oldHash=="not-found" || newHash=="not-found") {
                    if(oldHash!=newHash)Flag("unresolved","External assembly availability/binding changed: "+reference);
                    review.Coverage.Add("External reference unavailable for byte comparison: "+reference);
                } else if(oldHash!=newHash)Flag("dependency_changed","Directly referenced external assembly bytes changed: "+reference);
            }
            review.Coverage.AddRange(after.Caveats);
            var closure=current.Closures[source];
            var changed=closure.Where(id=>!baseline.Members.TryGetValue(id,out var oldHash) || oldHash!=current.Snapshot.Members.GetValueOrDefault(id)).OrderBy(id=>id,StringComparer.Ordinal).ToArray();
            review.ChangedMemberCount=changed.Length;review.ChangedMembers=changed.Take(24).ToArray();
            if(changed.Length>0)review.DependencyPath=PathTo(graph,after.Roots,changed.FirstOrDefault(id=>id.StartsWith("M:",StringComparison.Ordinal)) ?? changed[0]);
        }
        return result;
    }
    static string[] PathTo(AssemblyFingerprint graph,IEnumerable<string> roots,string target) {
        var parent=new Dictionary<string,string?>(StringComparer.Ordinal);var queue=new Queue<string>();
        foreach(string root in roots){parent[root]=null;queue.Enqueue(root);}
        while(queue.Count>0) {
            string id=queue.Dequeue();
            if(id==target) {var path=new List<string>();string? step=id;while(step!=null){path.Add(step);step=parent[step];}path.Reverse();return path.ToArray();}
            if(!graph.Members.TryGetValue(id,out var stamp))continue;
            foreach(string next in stamp.Dependencies.OrderBy(x=>x,StringComparer.Ordinal))if(parent.TryAdd(next,id))queue.Enqueue(next);
        }
        return Array.Empty<string>();
    }
    internal static Dictionary<string,ElementReview> Propagate(Model vanilla,Model effective,Overlay overlay,DependencyManifest manifest,Dictionary<string,SourceReview> sources) {
        var result=new Dictionary<string,ElementReview>(StringComparer.Ordinal);
        HashSet<string> Predicate(string id,HashSet<string>? seen=null) {
            seen ??=new();if(!seen.Add(id))return new();
            var refs=new HashSet<string>(manifest.PredicateSources.GetValueOrDefault(id) ?? new[]{"<unbound-predicate:"+id+">"},StringComparer.Ordinal);
            if(effective.Predicates.TryGetValue(id,out var condition))foreach(string nested in condition.Require.Concat(condition.Forbid))refs.UnionWith(Predicate(nested,seen));
            return refs;
        }
        HashSet<string> EdgeSources(Edge edge) {
            var refs=new HashSet<string>(edge.Sources,StringComparer.Ordinal);
            foreach(string predicate in edge.When.Require.Concat(edge.When.Forbid).Concat(edge.Guards.Select(g=>g.Predicate)))refs.UnionWith(Predicate(predicate));
            return refs;
        }
        void Add(string kind,string id,IEnumerable<string> refs) {
            string[] dependencies=refs.Distinct().OrderBy(x=>x,StringComparer.Ordinal).ToArray();
            string[] changed=dependencies.Where(source=>!sources.TryGetValue(source,out var stamp) || stamp.Status!="unchanged").ToArray();
            result[kind+":"+id]=new() {Status=changed.Length>0 ? "requires_revalidation" : "tracked_unchanged",ChangedSources=changed,Sources=dependencies};
        }
        foreach(var (id,node) in effective.Nodes)Add("node",id,node.Sources);
        foreach(var edge in effective.Edges)Add("edge",edge.Id,EdgeSources(edge));
        foreach(string id in effective.Predicates.Keys)Add("predicate",id,Predicate(id));
        foreach(var finding in effective.Findings)Add("finding",finding.Id,finding.Sources.Concat(finding.When.Require.Concat(finding.When.Forbid).SelectMany(id=>Predicate(id))));
        foreach(var patch in overlay.Patches)Add("overlay",patch.Id,patch.Sources.Concat(effective.Edges.Where(e=>e.Patch==patch.Id).SelectMany(EdgeSources)));
        foreach(var scenario in vanilla.Scenarios) {
            var refs=new HashSet<string>(StringComparer.Ordinal);
            foreach(var model in new[]{vanilla,effective}) {
                var trace=Program.Run(model,scenario);
                refs.UnionWith(model.Nodes[scenario.Start].Sources);refs.UnionWith(model.Nodes[trace.Node].Sources);
                foreach(string edge in trace.Edges)refs.UnionWith(EdgeSources(model.Edges.Single(e=>e.Id==edge)));
            }
            foreach(var expectation in scenario.Expect.Values)foreach(string? id in new[]{expectation.Finding,expectation.Without})if(id!=null)refs.UnionWith(effective.Findings.Single(f=>f.Id==id).Sources);
            Add("scenario",scenario.Id,refs);
        }
        return result;
    }
    internal static BaselinePointer Accept(string folder,AcceptedSnapshot snapshot,bool reviewed,string reason) {
        Need(reviewed && !string.IsNullOrWhiteSpace(reason),"Baseline acceptance requires --accept-reviewed and a nonempty --reason. Audit never accepts changes.");
        string dir=Path.Combine(folder,"assembly-baselines");Directory.CreateDirectory(dir);
        string relative="assembly-baselines/"+snapshot.Id+".json.br",path=Path.Combine(folder,relative);
        if(File.Exists(path))Need(JsonSerializer.Serialize(Read<AcceptedSnapshot>(path),Json)==JsonSerializer.Serialize(snapshot,Json),"Immutable snapshot already exists with different contents.");
        else {using var file=File.Create(path);using var compressed=new BrotliStream(file,CompressionLevel.SmallestSize);JsonSerializer.Serialize(compressed,snapshot,Json);}
        string pointerPath=Path.Combine(folder,"assembly-baseline.json");
        var pointer=new BaselinePointer {Snapshot=relative,Reason=reason,AcceptedUtc=DateTime.UtcNow.ToString("O"),PreviousSnapshot=File.Exists(pointerPath) ? Read<BaselinePointer>(pointerPath).Snapshot : null};
        // Accepted snapshots are immutable. Previous pointer/history is retained;
        // reports live elsewhere and never call this method.
        string ledger=Path.Combine(dir,"acceptance-"+DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffffff")+".json");
        File.WriteAllText(ledger,JsonSerializer.Serialize(pointer,Pretty)+"\n");
        File.WriteAllText(pointerPath,JsonSerializer.Serialize(pointer,Pretty)+"\n");return pointer;
    }
    internal static void Run(string[] args,string root,string folder,Model vanilla,Model effective,Overlay overlay) {
        if(args[0]=="assembly-self-test") {AssemblyReviewChecks.Run(root,vanilla,effective,overlay);return;}
        var manifest=Read<DependencyManifest>(Path.Combine(folder,"assembly-dependencies.json"));
        Need(manifest.SchemaVersion==1,"Unsupported dependency manifest.");
        string assembly=Option(args,"--assembly");
        using var graph=new AssemblyFingerprint(assembly);
        string? dataRoot=Array.IndexOf(args,"--data-root")>=0 ? Option(args,"--data-root") : null;
        string modelHash=AssemblyFingerprint.Hash(Canonical(JsonSerializer.SerializeToElement(new{vanilla,overlay},Json)));
        var current=Capture(graph,manifest,dataRoot,modelHash);
        if(args[0]=="assembly-scan") {
            Console.WriteLine(JsonSerializer.Serialize(new {assemblyHash=graph.BinaryHash,members=current.Snapshot.Members.Count,
                groups=current.Snapshot.Groups.ToDictionary(p=>p.Key,p=>new{roots=p.Value.Roots.Length,missing=p.Value.MissingSelectors,unresolved=p.Value.UnresolvedMembers.Length,sample=p.Value.UnresolvedMembers.Take(6)}),
                assets=current.Snapshot.Assets},Pretty));return;
        }
        if(args[0]=="assembly-baseline") {
            Need(graph.BinaryHash==vanilla.AssemblySha256,"Update/review graph provenance before accepting a different game assembly.");
            Need(current.Snapshot.Groups.Values.All(g=>g.MissingSelectors.Length==0),"Missing selectors; no baseline accepted.");
            Need(current.Snapshot.Assets.Values.All(a=>a.Status=="tracked"),"Supply complete --data-root assets before accepting a baseline.");
            var pointer=Accept(folder,current.Snapshot,args.Contains("--accept-reviewed"),Option(args,"--reason",""));
            Console.WriteLine(JsonSerializer.Serialize(new{accepted=pointer,groups=current.Snapshot.Groups.Count,members=current.Snapshot.Members.Count,unresolved=current.Snapshot.Groups.Values.Count(g=>g.UnresolvedMembers.Length>0)},Pretty));return;
        }
        Need(args[0]=="assembly-audit","Unknown assembly command.");
        var accepted=Read<BaselinePointer>(Path.Combine(folder,"assembly-baseline.json"));
        Need(!Path.IsPathRooted(accepted.Snapshot) && accepted.Snapshot.StartsWith("assembly-baselines/",StringComparison.Ordinal) && !accepted.Snapshot.Split('/').Contains(".."),"Invalid accepted snapshot path.");
        var baseline=Read<AcceptedSnapshot>(Path.Combine(folder,accepted.Snapshot));
        var sources=Compare(baseline,current,graph,vanilla);
        foreach(var (id,source) in vanilla.Sources.Where(p=>p.Value.Origin=="repo")) {
            if(!sources.TryGetValue(id,out var status))sources[id]=status=new();
            string path=Path.Combine(root,source.Path);
            if(!File.Exists(path) || AssemblyFingerprint.Hash(File.ReadAllText(path).Replace("\r\n","\n"))!=source.Sha256) {
                status.Status=File.Exists(path) ? "direct_changed" : "unresolved";status.Reasons.Add("Catalogued patch/source file changed or disappeared.");
            }
        }
        var elements=Propagate(vanilla,effective,overlay,manifest,sources);
        var report=new ReviewReport {AcceptedSnapshot=baseline.Id,AssemblyChanged=baseline.AssemblyHash!=graph.BinaryHash,CurrentAssemblyHash=graph.BinaryHash,Sources=sources,Elements=elements};
        string output=Path.GetFullPath(Path.Combine(root,Option(args,"--output","obj/ai-assembly-review")));
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        File.WriteAllText(output+".json",JsonSerializer.Serialize(report,Pretty)+"\n");
        File.WriteAllText(output+".md",Markdown(report));
        File.WriteAllText(output+".diagram.md",Diagram(effective,report));
        Console.WriteLine(JsonSerializer.Serialize(new{report=output+".json",changedSources=sources.Count(p=>p.Value.Status!="unchanged"),elementsRequiringRevalidation=elements.Count(p=>p.Value.Status=="requires_revalidation"),acceptedBaselineUnmodified=true},Pretty));
    }
    static string Markdown(ReviewReport report) {
        var b=new StringBuilder("# Assembly revalidation report\n\nGenerated review flags, not semantic approval. Accepted baseline is unchanged. `unchanged` means tracked dependencies match, not that arbitrary runtime behavior was proved equivalent.\n\n| Source | Status | Reasons |\n| --- | --- | --- |\n");
        foreach(var (id,source) in report.Sources)b.AppendLine("| "+id+" | "+source.Status+" | "+string.Join("; ",source.Reasons).Replace("|","/")+" |");
        b.AppendLine("\n## Graph elements requiring revalidation\n");
        foreach(var (id,element) in report.Elements.Where(p=>p.Value.Status=="requires_revalidation"))b.AppendLine("- `"+id+"`: "+string.Join(", ",element.ChangedSources));
        b.AppendLine("\n## Coverage limits\n\nVirtual targets are conservatively matched by name/arity; reflection, delegates and indirect calls are not a whole-program proof. External direct assembly bytes are pinned when found, but their transitive bodies/native-engine binaries are not analyzed. Assets cover explicitly selected entries only. New unrecorded patch files still require inventory review. Member/asset details and sample dependency paths are in the JSON report.\n");return b.ToString();
    }
    static string Diagram(Model model,ReviewReport report) {
        var b=new StringBuilder("# Revalidation overlay\n\nRed marks require review; this does not change or revalidate the accepted graph.\n\n```mermaid\nflowchart TD\n");
        foreach(var (id,node) in model.Nodes) {
            bool changed=report.Elements["node:"+id].Status=="requires_revalidation";
            b.AppendLine("n_"+id+"[\""+(changed ? "REVALIDATE: " : "")+node.Label.Replace("\"","'")+"\"]"+(changed ? ":::review" : ""));
        }
        int index=0;var red=new List<int>();
        foreach(var edge in model.Edges) {
            bool changed=report.Elements["edge:"+edge.Id].Status=="requires_revalidation";
            b.AppendLine("n_"+edge.From+" -->|\""+(changed ? "REVALIDATE / " : "")+edge.Event+"\"| n_"+edge.To);if(changed)red.Add(index);index++;
        }
        b.AppendLine("classDef review fill:#ffe4df,stroke:#b3261e,stroke-width:2px");
        if(red.Count>0)b.AppendLine("linkStyle "+string.Join(",",red)+" stroke:#b3261e,stroke-width:2px");
        return b.AppendLine("```\n").ToString();
    }
}

sealed class DependencyManifest {public int SchemaVersion {get;set;}=1;public string Description {get;set;}="";public Dictionary<string,MethodSelector[]> Bindings {get;set;}=new();public Dictionary<string,string[]> PredicateSources {get;set;}=new();public AssetSelector[] Assets {get;set;}=Array.Empty<AssetSelector>();}
sealed class AssetSelector {public string Id {get;set;}="";public string Path {get;set;}="";public string Key {get;set;}="strName";public string Entry {get;set;}="";public MethodSelector[] Consumers {get;set;}=Array.Empty<MethodSelector>();public string[] Sources {get;set;}=Array.Empty<string>();}
sealed class AssetStamp {public string Path {get;set;}="";public string Entry {get;set;}="";public string Hash {get;set;}="";public string Status {get;set;}="unresolved";public string Note {get;set;}="";}
sealed class GroupStamp {public string[] Roots {get;set;}=Array.Empty<string>();public string DirectHash {get;set;}="";public string DependencyHash {get;set;}="";public string[] Assets {get;set;}=Array.Empty<string>();public string[] Caveats {get;set;}=Array.Empty<string>();public string[] ExternalReferences {get;set;}=Array.Empty<string>();public string[] MissingSelectors {get;set;}=Array.Empty<string>();public string[] UnresolvedMembers {get;set;}=Array.Empty<string>();}
sealed class AcceptedSnapshot {public int SchemaVersion {get;set;}=1;public string Normalizer {get;set;}="cecil-metadata-il-v1";public string Id {get;set;}="";public string ModelHash {get;set;}="";public string AssemblyHash {get;set;}="";public string ManifestHash {get;set;}="";public Dictionary<string,string> Members {get;set;}=new();public Dictionary<string,GroupStamp> Groups {get;set;}=new();public Dictionary<string,AssetStamp> Assets {get;set;}=new();public Dictionary<string,string> References {get;set;}=new();}
sealed class Capture {public AcceptedSnapshot Snapshot {get;set;}=new();public Dictionary<string,HashSet<string>> Closures {get;set;}=new();}
sealed class BaselinePointer {public int SchemaVersion {get;set;}=1;public string Snapshot {get;set;}="";public string Reason {get;set;}="";public string AcceptedUtc {get;set;}="";public string? PreviousSnapshot {get;set;}}
sealed class SourceReview {public string Status {get;set;}="unchanged";public List<string> Reasons {get;set;}=new();public List<string> Coverage {get;set;}=new();public int ChangedMemberCount {get;set;}public string[] ChangedMembers {get;set;}=Array.Empty<string>();public string[] DependencyPath {get;set;}=Array.Empty<string>();}
sealed class ElementReview {public string Status {get;set;}="tracked_unchanged";public string[] ChangedSources {get;set;}=Array.Empty<string>();public string[] Sources {get;set;}=Array.Empty<string>();}
sealed class ReviewReport {public int SchemaVersion {get;set;}=1;public string AcceptedSnapshot {get;set;}="";public bool AssemblyChanged {get;set;}public string CurrentAssemblyHash {get;set;}="";public Dictionary<string,SourceReview> Sources {get;set;}=new();public Dictionary<string,ElementReview> Elements {get;set;}=new();}
