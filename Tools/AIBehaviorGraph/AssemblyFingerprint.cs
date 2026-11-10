using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Mono.Cecil;
using Mono.Cecil.Cil;

// Metadata-only inspection. Never Resolve() references or load the game in CLR.
sealed class AssemblyFingerprint : IDisposable {
    readonly AssemblyDefinition assembly;
    readonly string name;
    readonly Dictionary<string,MethodDefinition> methods = new(StringComparer.Ordinal);
    readonly Dictionary<string,FieldDefinition> fields = new(StringComparer.Ordinal);
    readonly Dictionary<string,TypeDefinition> types = new(StringComparer.Ordinal);
    readonly Dictionary<string,List<MethodDefinition>> virtuals = new(StringComparer.Ordinal);
    internal readonly Dictionary<string,MemberStamp> Members = new(StringComparer.Ordinal);
    internal readonly Dictionary<string,string> ReferenceHashes = new(StringComparer.Ordinal);
    internal string BinaryHash {get;}
    internal AssemblyFingerprint(string path) {
        BinaryHash=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
        assembly=AssemblyDefinition.ReadAssembly(path,new ReaderParameters {ReadSymbols=false,InMemory=true});
        name=assembly.Name.Name;
        foreach(var reference in assembly.MainModule.AssemblyReferences) {
            string file=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!,reference.Name+".dll");
            ReferenceHashes[reference.FullName]=File.Exists(file) ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))).ToLowerInvariant() : "not-found";
        }
        foreach(var type in AllTypes(assembly.MainModule.Types)) {
            types[TypeId(type)]=type;
            foreach(var method in type.Methods) {
                methods[MethodId(method)]=method;
                if(method.IsVirtual || method.HasOverrides) {
                    string key=DispatchKey(method);
                    if(!virtuals.TryGetValue(key,out var candidates))virtuals[key]=candidates=new();
                    candidates.Add(method);
                }
            }
            foreach(var field in type.Fields)fields[FieldId(field)]=field;
        }
        foreach(var type in types.Values)StampType(type);
        foreach(var field in fields.Values)StampField(field);
        foreach(var method in methods.Values)StampMethod(method);
    }
    static IEnumerable<TypeDefinition> AllTypes(IEnumerable<TypeDefinition> list) {
        foreach(var type in list) {yield return type;foreach(var nested in AllTypes(type.NestedTypes))yield return nested;}
    }
    internal static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    static string Value(object? value) => value switch {
        null => "null", string s => "string:"+JsonSerializer.Serialize(s),
        float f => "f32:"+BitConverter.SingleToInt32Bits(f).ToString("X8"),
        double d => "f64:"+BitConverter.DoubleToInt64Bits(d).ToString("X16"),
        byte[] bytes => "bytes:"+Convert.ToHexString(bytes),
        IFormattable number => value.GetType().Name+":"+number.ToString(null,CultureInfo.InvariantCulture),
        _ => value.GetType().Name+":"+value
    };
    bool Local(TypeReference type) {
        var element=type.GetElementType();
        return element.Scope is ModuleDefinition module && module.Assembly?.Name.Name==name || element.Scope is AssemblyNameReference reference && reference.Name==name;
    }
    string Scope(TypeReference type) {
        var element=type.GetElementType();
        if(Local(element))return name;
        return element.Scope is AssemblyNameReference reference ? reference.FullName : element.Scope?.Name ?? "<unknown-scope>";
    }
    string TypeId(TypeReference type) {
        if(type is GenericParameter parameter)return (parameter.Type==GenericParameterType.Method ? "!!" : "!")+parameter.Position;
        if(type is ArrayType array)return TypeId(array.ElementType)+"["+string.Join(",",array.Dimensions.Select(d=>(d.LowerBound?.ToString() ?? "")+":"+(d.UpperBound?.ToString() ?? "")))+"]";
        if(type is GenericInstanceType generic)return TypeId(generic.ElementType)+"<"+string.Join(",",generic.GenericArguments.Select(TypeId))+">";
        if(type is ByReferenceType byref)return TypeId(byref.ElementType)+"&";
        if(type is PointerType pointer)return TypeId(pointer.ElementType)+"*";
        if(type is PinnedType pinned)return "pinned("+TypeId(pinned.ElementType)+")";
        if(type is RequiredModifierType required)return "modreq("+TypeId(required.ModifierType)+")"+TypeId(required.ElementType);
        if(type is OptionalModifierType optional)return "modopt("+TypeId(optional.ModifierType)+")"+TypeId(optional.ElementType);
        if(type is FunctionPointerType function)return "fnptr:"+function.CallingConvention+":"+TypeId(function.ReturnType)+"("+string.Join(",",function.Parameters.Select(p=>TypeId(p.ParameterType)))+")";
        return Scope(type)+"::"+type.FullName;
    }
    string Owner(TypeReference type) => TypeId(type is GenericInstanceType generic ? generic.ElementType : type);
    string MethodId(MethodReference method) {
        var element=method is GenericInstanceMethod generic ? generic.ElementMethod : method;
        return "M:"+Owner(element.DeclaringType)+"::"+element.Name+"``"+element.GenericParameters.Count+"("+string.Join(",",element.Parameters.Select(p=>TypeId(p.ParameterType)))+")->"+TypeId(element.ReturnType)+"/"+element.CallingConvention+"/"+element.HasThis;
    }
    string MethodOperand(MethodReference method) => MethodId(method)+(method is GenericInstanceMethod generic ? "<"+string.Join(",",generic.GenericArguments.Select(TypeId))+">" : "");
    string FieldId(FieldReference field) => "F:"+Owner(field.DeclaringType)+"::"+field.Name+":"+TypeId(field.FieldType);
    string Attributes(ICustomAttributeProvider owner) => string.Join(";",owner.CustomAttributes.Select(a=>MethodId(a.Constructor)+":"+Convert.ToHexString(a.GetBlob())).OrderBy(x=>x,StringComparer.Ordinal));
    string Generics(IGenericParameterProvider provider) => string.Join(";",provider.GenericParameters.Select(p=>p.Position+":"+p.Attributes+":"+string.Join(",",p.Constraints.Select(TypeId).OrderBy(x=>x,StringComparer.Ordinal))));
    void DependType(MemberStamp stamp,TypeReference? type) {
        if(type==null || type is GenericParameter)return;
        if(type is GenericInstanceType generic)foreach(var argument in generic.GenericArguments)DependType(stamp,argument);
        if(type is TypeSpecification specification) {DependType(stamp,specification.ElementType);return;}
        if(Local(type))stamp.Dependencies.Add("T:"+TypeId(type));
        else {stamp.Caveats.Add("external-assembly-bodies-not-analyzed");stamp.ExternalAssemblies.Add(Scope(type));}
    }
    void StampType(TypeDefinition type) {
        string id="T:"+TypeId(type);
        var stamp=new MemberStamp();Members[id]=stamp;
        DependType(stamp,type.BaseType);
        foreach(var contract in type.Interfaces)DependType(stamp,contract.InterfaceType);
        foreach(var field in type.Fields.Where(f=>!f.IsStatic || f.IsLiteral))stamp.Dependencies.Add(FieldId(field));
        foreach(var initializer in type.Methods.Where(m=>m.Name is ".cctor" or ".ctor"))stamp.Dependencies.Add(MethodId(initializer));
        string fieldsShape=string.Join(";",type.Fields.Where(f=>!f.IsStatic || f.IsLiteral).Select(f=>FieldId(f)+":"+f.Attributes+":"+f.Offset+":"+(f.HasConstant ? Value(f.Constant) : "")).OrderBy(x=>x,StringComparer.Ordinal));
        string virtualShape=string.Join(";",type.Methods.Where(m=>m.IsVirtual || m.HasOverrides).Select(MethodId).OrderBy(x=>x,StringComparer.Ordinal));
        stamp.Hash=Hash(string.Join("|",id,type.Attributes,type.PackingSize,type.ClassSize,type.BaseType==null ? "" : TypeId(type.BaseType),
            string.Join(";",type.Interfaces.Select(i=>TypeId(i.InterfaceType)).OrderBy(x=>x,StringComparer.Ordinal)),fieldsShape,virtualShape,Generics(type),Attributes(type)));
    }
    void StampField(FieldDefinition field) {
        var stamp=new MemberStamp();Members[FieldId(field)]=stamp;
        DependType(stamp,field.DeclaringType);DependType(stamp,field.FieldType);
        stamp.Hash=Hash(string.Join("|",FieldId(field),field.Attributes,field.Offset,field.HasConstant ? Value(field.Constant) : "no-constant",Convert.ToHexString(field.InitialValue ?? Array.Empty<byte>()),Attributes(field)));
    }
    string ResolveMethod(MethodReference method,MemberStamp caller) {
        string id=MethodId(method);
        if(method.DeclaringType is ArrayType || method.DeclaringType is GenericParameter) {
            string intrinsic="R:"+id;
            if(!Members.ContainsKey(intrinsic))Members[intrinsic]=new() {Hash=Hash(intrinsic),Caveats=new(new[]{"runtime-array-or-generic-intrinsic-not-analyzed"},StringComparer.Ordinal)};
            return intrinsic;
        }
        if(!Local(method.DeclaringType)) {caller.Caveats.Add("external-methods-not-inspected");return "";}
        if(methods.ContainsKey(id))return id;
        var candidates=methods.Where(p=>Owner(p.Value.DeclaringType)==Owner(method.DeclaringType) && p.Value.Name==method.Name && p.Value.Parameters.Count==method.Parameters.Count && p.Value.GenericParameters.Count==method.GenericParameters.Count).Select(p=>p.Key).OrderBy(x=>x,StringComparer.Ordinal).ToArray();
        if(candidates.Length>0) {
            string alias="A:"+id;
            if(!Members.ContainsKey(alias))Members[alias]=new() {Hash=Hash(string.Join("|",candidates)),Dependencies=new(candidates,StringComparer.Ordinal),Caveats=new(new[]{"conservative-overload-alias"},StringComparer.Ordinal)};
            return alias;
        }
        caller.Caveats.Add("unresolved-local-reference");
        if(!Members.ContainsKey(id))Members[id]=new() {Hash="unresolved",Caveats=new(new[]{"unresolved-local-reference"},StringComparer.Ordinal)};
        return id;
    }
    static string DispatchKey(MethodReference method) => method.Name.Split('.').Last()+"/"+method.Parameters.Count+"/"+method.GenericParameters.Count;
    void DependCall(MemberStamp caller,MethodReference method,bool dynamic) {
        string target=ResolveMethod(method,caller);if(target.Length>0)caller.Dependencies.Add(target);
        DependType(caller,method.DeclaringType);
        if(method.DeclaringType.Namespace.StartsWith("System.Reflection",StringComparison.Ordinal) || method.DeclaringType.FullName is "System.Type" or "System.Activator")caller.Caveats.Add("reflection-or-runtime-type-selection");
        if(method.Name is "Invoke" or "DynamicInvoke")caller.Caveats.Add("delegate-or-indirect-invocation");
        // callvirt also occurs for nonvirtual instance methods. Use the exact local
        // declaration when it proves dispatch is not virtual.
        if(!dynamic || methods.TryGetValue(MethodId(method),out var exact) && !exact.IsVirtual)return;
        string key=DispatchKey(method);
        if(!virtuals.TryGetValue(key,out var candidates))return;
        string dispatch="V:"+key;
        if(!Members.ContainsKey(dispatch))Members[dispatch]=new() {Hash=Hash(string.Join("|",candidates.Select(MethodId).OrderBy(x=>x,StringComparer.Ordinal))),Dependencies=new(candidates.Select(MethodId),StringComparer.Ordinal),Caveats=new(new[]{"conservative-virtual-dispatch-by-name-and-arity"},StringComparer.Ordinal)};
        caller.Dependencies.Add(dispatch);
    }
    void StampMethod(MethodDefinition method) {
        var stamp=new MemberStamp();Members[MethodId(method)]=stamp;
        DependType(stamp,method.DeclaringType);DependType(stamp,method.ReturnType);
        foreach(var parameter in method.Parameters)DependType(stamp,parameter.ParameterType);
        foreach(var declaration in method.Overrides) {string id=ResolveMethod(declaration,stamp);if(id.Length>0)stamp.Dependencies.Add(id);}
        var b=new StringBuilder(MethodId(method)+"|"+method.Attributes+"|"+method.ImplAttributes+"|"+Generics(method)+"|"+Attributes(method));
        foreach(var p in method.Parameters)b.Append("|param:").Append(p.Attributes).Append(':').Append(p.HasConstant ? Value(p.Constant) : "no-default").Append(':').Append(Attributes(p));
        if(method.HasPInvokeInfo)b.Append("|pinvoke:").Append(method.PInvokeInfo.Module.Name).Append(':').Append(method.PInvokeInfo.EntryPoint).Append(':').Append(method.PInvokeInfo.Attributes);
        if(!method.HasBody) {stamp.Hash=Hash(b+"|no-body");return;}
        var body=method.Body;
        b.Append("|locals:").Append(body.InitLocals).Append('|').Append(body.MaxStackSize);
        foreach(var variable in body.Variables) {b.Append('|').Append(TypeId(variable.VariableType));DependType(stamp,variable.VariableType);}
        var indices=new Dictionary<Instruction,int>();int ordinal=body.Instructions.Count(i=>i.OpCode.Code!=Code.Nop);
        int end=ordinal;
        for(int i=body.Instructions.Count-1;i>=0;i--) {var instruction=body.Instructions[i];if(instruction.OpCode.Code!=Code.Nop)ordinal--;indices[instruction]=ordinal;}
        string Label(Instruction? instruction)=>instruction==null ? "end:"+end : "i:"+indices[instruction];
        foreach(var instruction in body.Instructions) {
            if(instruction.OpCode.Code==Code.Nop)continue;
            string op=instruction.OpCode.Name;
            string operand="";
            if(op.StartsWith("ldc.i4.",StringComparison.Ordinal) && op!="ldc.i4.s") {operand=op.EndsWith("m1",StringComparison.Ordinal) ? "-1" : op[7..];op="ldc.i4";}
            else if(op.StartsWith("ldarg.",StringComparison.Ordinal) && char.IsDigit(op[^1])) {operand=op[6..];op="ldarg";}
            else if((op.StartsWith("ldloc.",StringComparison.Ordinal) || op.StartsWith("stloc.",StringComparison.Ordinal)) && char.IsDigit(op[^1])) {operand=op[6..];op=op[..5];}
            else {
                if(op.EndsWith(".s",StringComparison.Ordinal))op=op[..^2];
                operand=instruction.Operand switch {
                    null=>"",Instruction target=>Label(target),Instruction[] targets=>string.Join(",",targets.Select(Label)),
                    MethodReference reference=>MethodOperand(reference),FieldReference field=>FieldId(field),TypeReference type=>TypeId(type),
                    VariableDefinition variable=>"local:"+variable.Index,ParameterDefinition parameter=>"arg:"+(parameter.Index+(method.HasThis ? 1 : 0)),
                    CallSite site=>"callsite:"+site.CallingConvention+":"+TypeId(site.ReturnType)+"("+string.Join(",",site.Parameters.Select(p=>TypeId(p.ParameterType)))+")",
                    _=>Value(instruction.Operand)
                };
                if(op=="ldc.i4")operand=Convert.ToInt32(instruction.Operand,CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);
                if(instruction.Operand is ParameterDefinition argument)operand=(argument.Index+(method.HasThis ? 1 : 0)).ToString(CultureInfo.InvariantCulture);
                if(instruction.Operand is VariableDefinition local)operand=local.Index.ToString(CultureInfo.InvariantCulture);
            }
            b.Append('|').Append(op).Append(':').Append(operand);
            if(instruction.Operand is MethodReference call)DependCall(stamp,call,instruction.OpCode.Code is Code.Callvirt or Code.Ldvirtftn);
            if(instruction.Operand is FieldReference fieldRef) {
                DependType(stamp,fieldRef.DeclaringType);
                string fieldId=FieldId(fieldRef);
                if(Local(fieldRef.DeclaringType)) {
                    if(!fields.ContainsKey(fieldId)) {
                        var match=fields.FirstOrDefault(p=>Owner(p.Value.DeclaringType)==Owner(fieldRef.DeclaringType) && p.Value.Name==fieldRef.Name);
                        if(match.Key!=null)fieldId=match.Key;
                        else if(!Members.ContainsKey(fieldId))Members[fieldId]=new() {Hash="unresolved",Caveats=new(new[]{"unresolved-local-reference"},StringComparer.Ordinal)};
                    }
                    stamp.Dependencies.Add(fieldId);
                } else stamp.Caveats.Add("external-fields-not-inspected");
            }
            if(instruction.Operand is TypeReference typeRef)DependType(stamp,typeRef);
            if(instruction.OpCode.Code==Code.Calli)stamp.Caveats.Add("indirect-calli-target-not-resolved");
        }
        foreach(var handler in body.ExceptionHandlers) {
            b.Append("|eh:").Append(handler.HandlerType).Append(':').Append(Label(handler.TryStart)).Append(':').Append(Label(handler.TryEnd)).Append(':').Append(Label(handler.HandlerStart)).Append(':').Append(Label(handler.HandlerEnd)).Append(':').Append(Label(handler.FilterStart)).Append(':').Append(handler.CatchType==null ? "" : TypeId(handler.CatchType));
            DependType(stamp,handler.CatchType);
        }
        stamp.Hash=Hash(b.ToString());
    }
    internal string[] Select(MethodSelector selector) => methods.Where(p=>p.Value.DeclaringType.FullName==selector.Type && selector.Methods.Contains(p.Value.Name,StringComparer.Ordinal)).Select(p=>p.Key).OrderBy(x=>x,StringComparer.Ordinal).ToArray();
    internal string[] Missing(MethodSelector selector) => selector.Methods.Where(name=>!methods.Values.Any(m=>m.DeclaringType.FullName==selector.Type && m.Name==name)).Select(name=>selector.Type+"::"+name).ToArray();
    internal HashSet<string> Closure(IEnumerable<string> roots) {
        var seen=new HashSet<string>(StringComparer.Ordinal);var pending=new Stack<string>(roots);
        while(pending.Count>0) {
            string id=pending.Pop();if(!seen.Add(id))continue;
            if(!Members.TryGetValue(id,out var stamp))continue;
            foreach(string dependency in stamp.Dependencies)pending.Push(dependency);
        }
        return seen;
    }
    internal string Digest(IEnumerable<string> ids) => Hash(string.Join("\n",ids.OrderBy(x=>x,StringComparer.Ordinal).Select(id=>id+"="+(Members.TryGetValue(id,out var stamp) ? stamp.Hash : "missing"))));
    public void Dispose()=>assembly.Dispose();
}

sealed class MemberStamp {
    public string Hash {get;set;}="";
    public HashSet<string> Dependencies {get;set;}=new(StringComparer.Ordinal);
    public HashSet<string> Caveats {get;set;}=new(StringComparer.Ordinal);
    public HashSet<string> ExternalAssemblies {get;set;}=new(StringComparer.Ordinal);
}
sealed class MethodSelector {public string Type {get;set;}="";public string[] Methods {get;set;}=Array.Empty<string>();}
