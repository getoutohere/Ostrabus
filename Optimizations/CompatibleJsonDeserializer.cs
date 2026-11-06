using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using Newtonsoft.Json.Serialization;
using Newtonsoft.Json;

namespace Ostrabus.Optimizations {
    // Keep the game's lexer and leaf conversions. Use Newtonsoft's object mapper with
    // strict contracts, compiled setters, and explicit dictionary/primitive converters.
    internal static class CompatibleJsonDeserializer {
        sealed class Member {
            internal Type Type;
            internal IValueProvider Access;
            internal bool Writable;
        }
        sealed class CompiledAccess : IValueProvider {
            readonly Action<object, object> set;
            internal CompiledAccess(MemberInfo member) {
                var field = member as FieldInfo;
                Type owner = member.DeclaringType;
                Type value = field == null ? ((PropertyInfo)member).PropertyType : field.FieldType;
                var method = new DynamicMethod("Set_" + member.Name, typeof(void), new[] { typeof(object), typeof(object) }, typeof(CompatibleJsonDeserializer).Module, true);
                ILGenerator il = method.GetILGenerator();
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(owner.IsValueType ? OpCodes.Unbox : OpCodes.Castclass, owner);
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(value.IsValueType ? OpCodes.Unbox_Any : OpCodes.Castclass, value);
                if (field != null) il.Emit(OpCodes.Stfld, field);
                else il.Emit(owner.IsValueType ? OpCodes.Call : OpCodes.Callvirt, ((PropertyInfo)member).GetSetMethod(true));
                il.Emit(OpCodes.Ret);
                set = (Action<object, object>)method.CreateDelegate(typeof(Action<object, object>));
            }
            public void SetValue(object target, object value) { set(target, value); }
            public object GetValue(object target) { throw new NotSupportedException("Deserialization-only access."); }
        }
        sealed class Plan {
            internal Type Type, Element;
            internal bool IsArray, IsList, IsDictionary;
            internal Func<object> Create;
            internal Func<Buffer> CreateBuffer;
            internal Dictionary<string, Member> Members;
        }
        abstract class Buffer {
            internal abstract void Add(object value);
            internal abstract object Finish();
        }
        sealed class Buffer<T> : Buffer {
            readonly List<T> values = new List<T>();
            internal override void Add(object value) { values.Add((T)value); }
            internal override object Finish() { return values.ToArray(); }
        }
        static Buffer NewBuffer<T>() { return new Buffer<T>(); }
        static readonly DefaultContractResolver Resolver = new DefaultContractResolver();
        static readonly StrictResolver StrictContracts = new StrictResolver();
        internal static T DeserializeNewtonsoft<T>(string json) {
            if (!Available) throw new NotSupportedException("Custom LitJson importers or verbose mode require the vanilla mapper.");
            using (var text = new StringReader(json))
            using (var reader = new GuardReader(new LitJson.JsonReader(text), typeof(T))) {
                var serializer = JsonSerializer.Create(new JsonSerializerSettings {
                    ContractResolver = StrictContracts, ObjectCreationHandling = ObjectCreationHandling.Replace,
                    TypeNameHandling = TypeNameHandling.None, MetadataPropertyHandling = MetadataPropertyHandling.Ignore,
                    DateParseHandling = DateParseHandling.None, MaxDepth = null,
                    Converters = new List<JsonConverter> { new ExactConverter() }
                });
                return serializer.Deserialize<T>(reader);
            }
        }
        sealed class DiscardAccess : IValueProvider {
            public void SetValue(object target, object value) {}
            public object GetValue(object target) { return null; }
        }
        sealed class StrictResolver : DefaultContractResolver {
            protected override List<MemberInfo> GetSerializableMembers(Type type) {
                var members = new List<MemberInfo>();
                foreach (PropertyInfo property in type.GetProperties()) if (property.Name != "Item" && !property.IsDefined(typeof(NonSerializedAttribute), true)) members.Add(property);
                foreach (FieldInfo field in type.GetFields()) if (!field.IsDefined(typeof(NonSerializedAttribute), true)) members.Add(field);
                return members;
            }
            protected override JsonProperty CreateProperty(MemberInfo member, MemberSerialization serialization) {
                var property = base.CreateProperty(member, serialization);
                property.PropertyName = member.Name;
                property.Ignored = false;
                property.Readable = false;
                property.Writable = true;
                property.Required = Required.Default;
                property.Converter = null;
                property.ShouldDeserialize = null;
                property.NullValueHandling = NullValueHandling.Include;
                property.DefaultValueHandling = DefaultValueHandling.Include;
                var info = member as PropertyInfo;
                property.ValueProvider = info != null && !info.CanWrite ? (IValueProvider)new DiscardAccess() : GetPlan(member.DeclaringType).Members[member.Name].Access;
                return property;
            }
            protected override JsonObjectContract CreateObjectContract(Type type) {
                var contract = base.CreateObjectContract(type);
                // Never select parameterized/nonpublic constructors that vanilla wouldn't call.
                Func<object> creator = contract.DefaultCreator;
                bool nonPublic = contract.DefaultCreatorNonPublic;
                contract.CreatorParameters.Clear();
                contract.OverrideCreator = args => creator != null && !nonPublic ? creator() : Activator.CreateInstance(type);
                return contract;
            }
        }
        sealed class ExactConverter : JsonConverter {
            public override bool CanConvert(Type type) {
                type = Nullable.GetUnderlyingType(type) ?? type;
                return type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal) || type == typeof(DateTime) || type == typeof(object) || typeof(IDictionary).IsAssignableFrom(type);
            }
            public override bool CanWrite { get { return false; } }
            public override void WriteJson(Newtonsoft.Json.JsonWriter writer, object value, JsonSerializer serializer) { throw new NotSupportedException(); }
            public override object ReadJson(Newtonsoft.Json.JsonReader reader, Type type, object existing, JsonSerializer serializer) {
                var guard = (GuardReader)reader;
                bool container = guard.Source.Token == LitJson.JsonToken.ObjectStart || guard.Source.Token == LitJson.JsonToken.ArrayStart;
                object result = ReadCurrent(type, guard.Source);
                if (container) guard.CompleteDirect();
                return result;
            }
        }
        // Filter unknown names before Json.NET's case-insensitive fallback can see them.
        // Preserve the game's exact numeric token types, culture and sentinel behavior.
        sealed class GuardReader : Newtonsoft.Json.JsonReader {
            struct Scope {
                internal Plan Plan;
                internal Type Next;
            }
            internal readonly LitJson.JsonReader Source;
            readonly Type root;
            readonly Stack<Scope> scopes = new Stack<Scope>();
            Scope current;
            internal GuardReader(LitJson.JsonReader source, Type type) { Source = source; root = type; MaxDepth = null; }
            internal void CompleteDirect() {
                current = scopes.Count == 0 ? default(Scope) : scopes.Pop();
                SetToken(Source.Token == LitJson.JsonToken.ObjectEnd ? Newtonsoft.Json.JsonToken.EndObject : Newtonsoft.Json.JsonToken.EndArray);
            }
            public override bool Read() {
                while (Source.Read()) {
                    var token = Source.Token;
                    if (token == LitJson.JsonToken.PropertyName) {
                        Member member;
                        if (current.Plan.Members.TryGetValue((string)Source.Value, out member)) current.Next = member.Type;
                        else if (current.Plan.IsDictionary) current.Next = current.Plan.Element;
                        else { CompatibleJsonDeserializer.Skip(Source); continue; }
                        SetToken(Newtonsoft.Json.JsonToken.PropertyName, Source.Value); return true;
                    }
                    if (token == LitJson.JsonToken.ObjectEnd || token == LitJson.JsonToken.ArrayEnd) {
                        current = scopes.Count == 0 ? default(Scope) : scopes.Pop();
                        SetToken(token == LitJson.JsonToken.ObjectEnd ? Newtonsoft.Json.JsonToken.EndObject : Newtonsoft.Json.JsonToken.EndArray); return true;
                    }
                    if (token == LitJson.JsonToken.ObjectStart || token == LitJson.JsonToken.ArrayStart) {
                        Type expected = current.Plan == null ? root : current.Next;
                        Plan plan = GetPlan(Nullable.GetUnderlyingType(expected) ?? expected);
                        bool array = token == LitJson.JsonToken.ArrayStart;
                        if (array && !plan.IsArray && !plan.IsList) throw new LitJson.JsonException("Type " + expected + " can't act as an array");
                        if (current.Plan != null) scopes.Push(current);
                        current = new Scope { Plan = plan, Next = array ? plan.Element : null };
                        SetToken(array ? Newtonsoft.Json.JsonToken.StartArray : Newtonsoft.Json.JsonToken.StartObject); return true;
                    }
                    Newtonsoft.Json.JsonToken mapped;
                    switch (token) {
                        case LitJson.JsonToken.Int: case LitJson.JsonToken.Long: mapped = Newtonsoft.Json.JsonToken.Integer; break;
                        case LitJson.JsonToken.Double: case LitJson.JsonToken.Single: mapped = Newtonsoft.Json.JsonToken.Float; break;
                        case LitJson.JsonToken.Boolean: mapped = Newtonsoft.Json.JsonToken.Boolean; break;
                        case LitJson.JsonToken.String: mapped = Newtonsoft.Json.JsonToken.String; break;
                        case LitJson.JsonToken.Null: mapped = Newtonsoft.Json.JsonToken.Null; break;
                        default: throw new LitJson.JsonException("Unexpected token " + token);
                    }
                    SetToken(mapped, Source.Value); return true;
                }
                SetToken(Newtonsoft.Json.JsonToken.None); return false;
            }
        }
        static readonly Dictionary<Type, Plan> Plans = new Dictionary<Type, Plan>();
        static readonly object Gate = new object();
        static readonly IDictionary BaseImporters;
        static readonly IDictionary CustomImporters;
        static readonly Dictionary<Type, Dictionary<Type, Func<object, object>>> Importers = new Dictionary<Type, Dictionary<Type, Func<object, object>>>();
        static CompatibleJsonDeserializer() {
            RuntimeHelpers.RunClassConstructor(typeof(LitJson.JsonMapper).TypeHandle);
            BaseImporters = (IDictionary)typeof(LitJson.JsonMapper).GetField("base_importers_table", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
            CustomImporters = (IDictionary)typeof(LitJson.JsonMapper).GetField("custom_importers_table", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
            foreach (DictionaryEntry source in BaseImporters) {
                var targets = new Dictionary<Type, Func<object, object>>();
                foreach (DictionaryEntry target in (IDictionary)source.Value) {
                    var converter = (Delegate)target.Value;
                    targets.Add((Type)target.Key, (Func<object, object>)Delegate.CreateDelegate(typeof(Func<object, object>), converter.Target, converter.Method));
                }
                Importers.Add((Type)source.Key, targets);
            }
        }
        internal static bool Available { get { return CustomImporters.Count == 0 && !LitJson.JsonMapper.bVerbose; } }
        internal static T Deserialize<T>(string json) {
            if (!Available) throw new NotSupportedException("Custom LitJson importers or verbose mode require the vanilla mapper.");
            using (var text = new StringReader(json)) return (T)Read(typeof(T), new LitJson.JsonReader(text));
        }
        internal static object Deserialize(Type type, string json) {
            if (!Available) throw new NotSupportedException("Custom LitJson importers or verbose mode require the vanilla mapper.");
            using (var text = new StringReader(json)) return Read(type, new LitJson.JsonReader(text));
        }
        static Plan GetPlan(Type type) {
            lock (Gate) {
                Plan plan;
                if (Plans.TryGetValue(type, out plan)) return plan;
                plan = new Plan { Type = type, IsArray = type.IsArray, IsList = typeof(IList).IsAssignableFrom(type), IsDictionary = typeof(IDictionary).IsAssignableFrom(type) };
                if (plan.IsArray) {
                    plan.Element = type.GetElementType();
                    plan.CreateBuffer = (Func<Buffer>)Delegate.CreateDelegate(typeof(Func<Buffer>), typeof(CompatibleJsonDeserializer).GetMethod("NewBuffer", BindingFlags.NonPublic | BindingFlags.Static).MakeGenericMethod(plan.Element));
                } else {
                    // Use only the default factory, never Newtonsoft's parameterized constructor selection.
                    var contract = Resolver.ResolveContract(type);
                    plan.Create = contract.DefaultCreatorNonPublic ? null : contract.DefaultCreator;
                }
                plan.Members = new Dictionary<string, Member>(StringComparer.Ordinal);
                foreach (PropertyInfo property in type.GetProperties()) {
                    if (property.Name == "Item") {
                        var indices = property.GetIndexParameters();
                        if (indices.Length == 1 && (indices[0].ParameterType == typeof(string) || indices[0].ParameterType == typeof(int))) plan.Element = property.PropertyType;
                    } else if (!property.IsDefined(typeof(NonSerializedAttribute), true)) {
                        IValueProvider access = property.CanWrite && !property.GetSetMethod(true).IsStatic && property.GetIndexParameters().Length == 0 ? (IValueProvider)new CompiledAccess(property) : new ReflectionValueProvider(property);
                        plan.Members.Add(property.Name, new Member { Type = property.PropertyType, Access = access, Writable = property.CanWrite });
                    }
                }
                foreach (FieldInfo field in type.GetFields()) if (!field.IsDefined(typeof(NonSerializedAttribute), true)) {
                    // Reflection preserves static/readonly field behavior in the rare types exposing those.
                    IValueProvider access = field.IsInitOnly || field.IsStatic ? (IValueProvider)new ReflectionValueProvider(field) : new CompiledAccess(field);
                    plan.Members.Add(field.Name, new Member { Type = field.FieldType, Access = access, Writable = true });
                }
                Plans.Add(type, plan);
                return plan;
            }
        }
        static object Read(Type declared, LitJson.JsonReader reader) {
            reader.Read();
            return ReadCurrent(declared, reader);
        }
        static object ReadCurrent(Type declared, LitJson.JsonReader reader) {
            var token = reader.Token;
            if (token == LitJson.JsonToken.ArrayEnd) return null;
            Type nullable = Nullable.GetUnderlyingType(declared), type = nullable ?? declared;
            if (token == LitJson.JsonToken.Null) {
                if (declared.IsClass || nullable != null) return null;
                throw new LitJson.JsonException("Can't assign null to an instance of type " + declared);
            }
            if (token == LitJson.JsonToken.Double || token == LitJson.JsonToken.Single || token == LitJson.JsonToken.Int || token == LitJson.JsonToken.Long || token == LitJson.JsonToken.String || token == LitJson.JsonToken.Boolean) {
                object value = reader.Value;
                Type source = value.GetType();
                if (type.IsAssignableFrom(source)) return value;
                // Keep correctness if another mod registers an importer during a parse.
                if (CustomImporters.Count != 0) {
                    var custom = (IDictionary)CustomImporters[source];
                    var converter = custom == null ? null : (Delegate)custom[type];
                    if (converter != null) {
                        try { return converter.DynamicInvoke(value); }
                        catch (TargetInvocationException ex) { throw ex.InnerException; }
                    }
                }
                Dictionary<Type, Func<object, object>> targets;
                Func<object, object> importer;
                if (Importers.TryGetValue(source, out targets) && targets.TryGetValue(type, out importer)) return importer(value);
                if (type.IsEnum) return Enum.ToObject(type, value);
                MethodInfo op = type.GetMethod("op_Implicit", new[] { source });
                if (op != null) return op.Invoke(null, new[] { value });
                throw new LitJson.JsonException("Can't assign value '" + value + "' (type " + source + ") to type " + declared);
            }
            if (token == LitJson.JsonToken.ArrayStart) {
                Plan plan = GetPlan(declared);
                if (!plan.IsArray && !plan.IsList) throw new LitJson.JsonException("Type " + declared + " can't act as an array");
                Buffer buffer = plan.IsArray ? plan.CreateBuffer() : null;
                IList list = plan.IsArray ? null : (IList)Create(plan);
                while (true) {
                    object item = Read(plan.Element, reader);
                    if (item == null && reader.Token == LitJson.JsonToken.ArrayEnd) break;
                    if (buffer != null) buffer.Add(item); else list.Add(item);
                }
                return buffer == null ? list : buffer.Finish();
            }
            if (token == LitJson.JsonToken.ObjectStart) {
                Plan plan = GetPlan(type);
                object instance = Create(plan);
                while (true) {
                    reader.Read();
                    if (reader.Token == LitJson.JsonToken.ObjectEnd) break;
                    string name = (string)reader.Value;
                    Member member;
                    if (plan.Members.TryGetValue(name, out member)) {
                        object value = Read(member.Type, reader);
                        if (member.Writable) member.Access.SetValue(instance, value);
                    } else if (plan.IsDictionary) ((IDictionary)instance).Add(name, Read(plan.Element, reader));
                    else {
                        if (!reader.SkipNonMembers) throw new LitJson.JsonException("The type " + declared + " doesn't have the property '" + name + "'");
                        Skip(reader);
                    }
                }
                return instance;
            }
            return null;
        }
        static object Create(Plan plan) {
            if (plan.Create == null) return Activator.CreateInstance(plan.Type);
            return plan.Create();
        }
        static void Skip(LitJson.JsonReader reader) {
            reader.Read();
            int depth = reader.Token == LitJson.JsonToken.ObjectStart || reader.Token == LitJson.JsonToken.ArrayStart ? 1 : 0;
            while (depth > 0 && reader.Read()) {
                if (reader.Token == LitJson.JsonToken.ObjectStart || reader.Token == LitJson.JsonToken.ArrayStart) depth++;
                else if (reader.Token == LitJson.JsonToken.ObjectEnd || reader.Token == LitJson.JsonToken.ArrayEnd) depth--;
            }
        }
    }
}
