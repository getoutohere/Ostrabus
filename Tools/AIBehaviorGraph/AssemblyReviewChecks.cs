using System.Text.Json;
using Mono.Cecil;
using Mono.Cecil.Cil;

static class AssemblyReviewChecks {
    static int checks;
    static void Check(bool ok,string message) {checks++;if(!ok)throw new Exception(message);}
    static string Fixture(string folder,string id,int helper=1,bool noise=false,bool rootChange=false,bool rename=false,int fieldValue=1,bool extraVirtual=false,bool genericRename=false) {
        string file=Path.Combine(folder,id+".dll");
        using var assembly=AssemblyDefinition.CreateAssembly(new AssemblyNameDefinition("BehaviorFixture",new Version(1,0)),"BehaviorFixture",ModuleKind.Dll);
        var module=assembly.MainModule;
        var agent=new TypeDefinition("Fixture",rename ? "Gone" : "Agent",TypeAttributes.Public|TypeAttributes.Class,module.TypeSystem.Object);module.Types.Add(agent);
        MethodDefinition Method(TypeDefinition type,string name,int value) {
            var m=new MethodDefinition(name,MethodAttributes.Public|MethodAttributes.Static,module.TypeSystem.Int32);type.Methods.Add(m);
            m.Body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4,value));m.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));return m;
        }
        if(noise) {var unrelated=new TypeDefinition("Fixture","Unrelated",TypeAttributes.Public,module.TypeSystem.Object);module.Types.Insert(0,unrelated);Method(unrelated,"Noise",99);}
        var callee=Method(agent,"Helper",helper);
        var run=new MethodDefinition("Run",MethodAttributes.Public|MethodAttributes.Static,module.TypeSystem.Int32);agent.Methods.Add(run);
        if(noise)run.Body.Instructions.Add(Instruction.Create(OpCodes.Nop));
        run.Body.Instructions.Add(rootChange ? Instruction.Create(OpCodes.Ldc_I4,10) : Instruction.Create(OpCodes.Call,callee));run.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        var limit=new FieldDefinition("Limit",FieldAttributes.Public|FieldAttributes.Static|FieldAttributes.HasDefault,module.TypeSystem.Int32) {Constant=fieldValue};agent.Fields.Add(limit);
        var readField=new MethodDefinition("ReadLimit",MethodAttributes.Public|MethodAttributes.Static,module.TypeSystem.Int32);agent.Methods.Add(readField);
        readField.Body.Instructions.Add(Instruction.Create(OpCodes.Ldsfld,limit));readField.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        var arrayType=new ArrayType(agent,2);
        var arrayGet=new MethodReference("Get",agent,arrayType) {HasThis=true};arrayGet.Parameters.Add(new ParameterDefinition(module.TypeSystem.Int32));arrayGet.Parameters.Add(new ParameterDefinition(module.TypeSystem.Int32));
        var readArray=new MethodDefinition("ReadArray",MethodAttributes.Public|MethodAttributes.Static,agent);readArray.Parameters.Add(new ParameterDefinition(arrayType));agent.Methods.Add(readArray);
        readArray.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));readArray.Body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_0));readArray.Body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_0));readArray.Body.Instructions.Add(Instruction.Create(OpCodes.Call,arrayGet));readArray.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        var identity=new MethodDefinition("Identity",MethodAttributes.Public|MethodAttributes.Static,module.TypeSystem.Void);agent.Methods.Add(identity);
        var generic=new GenericParameter(genericRename ? "Renamed" : "T",identity);identity.GenericParameters.Add(generic);identity.ReturnType=generic;identity.Parameters.Add(new ParameterDefinition(generic));
        identity.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));identity.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        var contract=new TypeDefinition("Fixture","IWorker",TypeAttributes.Interface|TypeAttributes.Abstract|TypeAttributes.Public);module.Types.Add(contract);
        var virtualCall=new MethodDefinition("Act",MethodAttributes.Public|MethodAttributes.Abstract|MethodAttributes.Virtual|MethodAttributes.NewSlot,module.TypeSystem.Int32);contract.Methods.Add(virtualCall);
        var dispatch=new MethodDefinition("Dispatch",MethodAttributes.Public|MethodAttributes.Static,module.TypeSystem.Int32);dispatch.Parameters.Add(new ParameterDefinition(contract));agent.Methods.Add(dispatch);
        dispatch.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));dispatch.Body.Instructions.Add(Instruction.Create(OpCodes.Callvirt,virtualCall));dispatch.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        void Worker(string name,int value) {
            var worker=new TypeDefinition("Fixture",name,TypeAttributes.Public,module.TypeSystem.Object);module.Types.Add(worker);worker.Interfaces.Add(new InterfaceImplementation(contract));
            var m=Method(worker,"Act",value);m.Attributes=MethodAttributes.Public|MethodAttributes.Virtual|MethodAttributes.NewSlot;
        }
        Worker("Worker",1);if(extraVirtual)Worker("ExtraWorker",2);
        if(noise) {agent.Methods.Remove(run);agent.Methods.Insert(0,run);}
        assembly.Write(file);return file;
    }
    internal static void Run(string root,Model vanilla,Model effective,Overlay overlay) {
        checks=0;
        string folder=Path.Combine(root,"obj","assembly-review-tests",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        var config=new DependencyManifest {Bindings=new() {
            ["retriever"]=new[]{new MethodSelector {Type="Fixture.Agent",Methods=new[]{"Run"}}},
            ["dispatch"]=new[]{new MethodSelector {Type="Fixture.Agent",Methods=new[]{"Dispatch"}}},
            ["physical"]=new[]{new MethodSelector {Type="Fixture.Agent",Methods=new[]{"ReadLimit"}}},
            ["generic"]=new[]{new MethodSelector {Type="Fixture.Agent",Methods=new[]{"Identity"}}},
            ["array"]=new[]{new MethodSelector {Type="Fixture.Agent",Methods=new[]{"ReadArray"}}}
        }};
        using var beforeGraph=new AssemblyFingerprint(Fixture(folder,"before"));
        var before=AssemblyReview.Capture(beforeGraph,config,null);
        var tiny=new Model {Sources=new() { ["retriever"]=new(),["dispatch"]=new(),["physical"]=new(),["generic"]=new(),["array"]=new() }};
        Dictionary<string,SourceReview> Compare(string path) {
            using var graph=new AssemblyFingerprint(path);return AssemblyReview.Compare(before.Snapshot,AssemblyReview.Capture(graph,config,null),graph,tiny);
        }
        Check(Compare(Fixture(folder,"identical")).Values.All(r=>r.Status=="unchanged"),"MVID-only rebuild should leave tracked methods unchanged.");
        Check(before.Snapshot.Groups["array"].UnresolvedMembers.Length==0,"Runtime multidimensional-array methods were mistaken for missing game methods.");
        Check(Compare(Fixture(folder,"noise",noise:true)).Values.All(r=>r.Status=="unchanged"),"Metadata token/order, NOP and unrelated type changes invalidated stable methods.");
        var direct=Compare(Fixture(folder,"direct",rootChange:true));Check(direct["retriever"].Status=="direct_changed","Root body change was not direct.");
        var helper=Compare(Fixture(folder,"helper",helper:2));Check(helper["retriever"].Status=="dependency_changed","Changed helper failed to invalidate caller.");
        Check(helper["retriever"].DependencyPath.Length>=2 && helper["retriever"].ChangedMembers.Any(m=>m.Contains("Helper")),"Indirect change lacks explanatory member/path.");
        Check(Compare(Fixture(folder,"field",fieldValue:2))["physical"].Status=="dependency_changed","Referenced field metadata change missed.");
        Check(Compare(Fixture(folder,"virtual",extraVirtual:true))["dispatch"].Status=="dependency_changed","New virtual implementation did not invalidate dispatch.");
        Check(Compare(Fixture(folder,"generic",genericRename:true))["generic"].Status=="unchanged","Generic parameter spelling changed semantic fingerprint.");
        Check(Compare(Fixture(folder,"removed",rename:true))["retriever"].Status=="unresolved","Removed/renamed root was not unresolved.");
        var modelEdit=AssemblyReview.Capture(beforeGraph,config,null,"reviewed-model-edited");
        Check(AssemblyReview.Compare(before.Snapshot,modelEdit,beforeGraph,tiny).Values.All(r=>r.Status=="declaration_changed"),"Graph edits silently retained accepted status.");
        var manifestEdit=AssemblyReview.Capture(beforeGraph,config,null);
        manifestEdit.Snapshot.ManifestHash="changed-declarations";
        Check(AssemblyReview.Compare(before.Snapshot,manifestEdit,beforeGraph,tiny).Values.All(r=>r.Status=="declaration_changed"),"Dependency declaration edits silently retained accepted status.");
        var incompatible=AssemblyReview.Capture(beforeGraph,config,null);
        incompatible.Snapshot.Normalizer="future-normalizer";
        Check(AssemblyReview.Compare(before.Snapshot,incompatible,beforeGraph,tiny).Values.All(r=>r.Status=="unresolved"),"Incompatible normalizers compared as unchanged.");
        var externalBase=AssemblyReview.Capture(beforeGraph,config,null);
        var externalNow=AssemblyReview.Capture(beforeGraph,config,null);
        externalBase.Snapshot.Groups["retriever"].ExternalReferences=new[]{"Fixture.External"};
        externalNow.Snapshot.Groups["retriever"].ExternalReferences=new[]{"Fixture.External"};
        externalBase.Snapshot.References["Fixture.External"]="previous-bytes";
        externalNow.Snapshot.References["Fixture.External"]="changed-bytes";
        Check(AssemblyReview.Compare(externalBase.Snapshot,externalNow,beforeGraph,tiny)["retriever"].Status=="dependency_changed","External dependency change missed.");
        externalNow.Snapshot.References["Fixture.External"]="not-found";
        Check(AssemblyReview.Compare(externalBase.Snapshot,externalNow,beforeGraph,tiny)["retriever"].Status=="unresolved","Disappeared external dependency treated as unchanged.");
        string data=Path.Combine(folder,"data");Directory.CreateDirectory(data);
        string asset=Path.Combine(data,"policy.json");File.WriteAllText(asset,"[{\"strName\":\"Drop\",\"forbid\":[\"Dead\"]}]");
        config.Assets=new[]{new AssetSelector {Id="drop",Path="policy.json",Entry="Drop",Sources=new[]{"retriever"}}};
        var assetBase=AssemblyReview.Capture(beforeGraph,config,data);
        File.WriteAllText(asset,"[{\"forbid\":[\"Dead\"],\"strName\":\"Drop\"}]");
        Check(AssemblyReview.Compare(assetBase.Snapshot,AssemblyReview.Capture(beforeGraph,config,data),beforeGraph,tiny)["retriever"].Status=="unchanged","JSON property order should not change entry hash.");
        File.WriteAllText(asset,"[{\"strName\":\"Drop\",\"forbid\":[\"Dead\",\"Pirate\"]}]");
        Check(AssemblyReview.Compare(assetBase.Snapshot,AssemblyReview.Capture(beforeGraph,config,data),beforeGraph,tiny)["retriever"].Status=="data_changed","Selected JSON behavior change missed.");
        Check(AssemblyReview.Compare(assetBase.Snapshot,AssemblyReview.Capture(beforeGraph,config,null),beforeGraph,tiny)["retriever"].Status=="unresolved","Missing JSON input silently treated as unchanged.");
        var sourceStates=vanilla.Sources.Keys.ToDictionary(id=>id,id=>new SourceReview());sourceStates["retriever"].Status="direct_changed";
        var realManifest=AssemblyReview.Read<DependencyManifest>(Path.Combine(root,"Navigation","BehaviorGraph","assembly-dependencies.json"));
        var impacted=AssemblyReview.Propagate(vanilla,effective,overlay,realManifest,sourceStates);
        Check(impacted["node:target"].Status=="requires_revalidation","Node propagation failed.");
        Check(impacted["edge:sff.pickup"].Status=="requires_revalidation","Fallback edge propagation failed.");
        Check(impacted["predicate:handoff_scope"].Status=="requires_revalidation","Predicate propagation failed.");
        Check(impacted["scenario:sff-pickup-conflict"].Status=="requires_revalidation","Scenario propagation failed.");
        Check(impacted["node:haul"].Status=="tracked_unchanged","Unrelated node was needlessly invalidated.");
        bool refused=false;try{AssemblyReview.Accept(folder,before.Snapshot,false,"");}catch{refused=true;}
        Check(refused && !File.Exists(Path.Combine(folder,"assembly-baseline.json")),"Baseline was accepted without explicit review.");
        var accepted=AssemblyReview.Accept(folder,before.Snapshot,true,"Fixture initial review.");
        string pointer=File.ReadAllText(Path.Combine(folder,"assembly-baseline.json"));
        _=Compare(Fixture(folder,"no_accept",helper:3));
        Check(pointer==File.ReadAllText(Path.Combine(folder,"assembly-baseline.json")),"Comparison changed the accepted baseline.");
        using var revisedGraph=new AssemblyFingerprint(Fixture(folder,"reviewed",helper:2));
        var revised=AssemblyReview.Capture(revisedGraph,config,data);
        var next=AssemblyReview.Accept(folder,revised.Snapshot,true,"Fixture reviewed revision.");
        Check(next.PreviousSnapshot==accepted.Snapshot && File.Exists(Path.Combine(folder,accepted.Snapshot)),"Accepted history was not retained.");
        Console.WriteLine(checks+" assembly revalidation checks passed (synthetic assemblies are metadata fixtures, never executed).");
    }
}
