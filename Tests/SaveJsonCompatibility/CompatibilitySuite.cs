internal static partial class Benchmark {
    static object TypedParse(System.Type type, string json, bool fast) {
        System.Reflection.MethodInfo method;
        if (fast) method = typeof(Ostrabus.Optimizations.CompatibleJsonDeserializer).GetMethod("DeserializeNewtonsoft", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        else method = System.Linq.Enumerable.Single(typeof(LitJson.JsonMapper).GetMethods(), m => m.Name == "ToObject" && m.IsGenericMethodDefinition && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(string));
        try { return method.MakeGenericMethod(type).Invoke(null, new object[] { json }); }
        catch (System.Reflection.TargetInvocationException ex) { throw ex.InnerException; }
    }
    static string Difference(object left, object right, string path) {
        visited++;
        if (left == null || right == null) return left == right ? null : path + ": null mismatch";
        System.Type type = left.GetType();
        if (type != right.GetType()) return path + ": type " + type + " != " + right.GetType();
        if (left is double) return System.BitConverter.DoubleToInt64Bits((double)left) == System.BitConverter.DoubleToInt64Bits((double)right) ? null : path + ": double " + left + " != " + right;
        if (left is float) return new FloatBits { Value = (float)left }.Bits == new FloatBits { Value = (float)right }.Bits ? null : path + ": float " + left + " != " + right;
        if (type.IsPrimitive || type.IsEnum || left is string || left is decimal || left is System.DateTime) return left.Equals(right) ? null : path + ": " + left + " != " + right;
        var ld = left as System.Collections.IDictionary;
        if (ld != null) {
            var rd = (System.Collections.IDictionary)right;
            if (ld.Count != rd.Count) return path + ": dictionary count";
            foreach (object key in ld.Keys) {
                if (!rd.Contains(key)) return path + ": missing key " + key;
                string difference = Difference(ld[key], rd[key], path + "[" + key + "]");
                if (difference != null) return difference;
            }
            return null;
        }
        var ll = left as System.Collections.IList;
        if (ll != null) {
            var rl = (System.Collections.IList)right;
            if (ll.Count != rl.Count) return path + ": list count";
            for (int i = 0; i < ll.Count; i++) { string difference = Difference(ll[i], rl[i], path + "[" + i + "]"); if (difference != null) return difference; }
            return null;
        }
        System.Reflection.MemberInfo[] members;
        if (!Members.TryGetValue(type, out members)) {
            members = System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Concat<System.Reflection.MemberInfo>(
                System.Linq.Enumerable.Where(type.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance), p => p.CanRead && p.CanWrite && p.GetIndexParameters().Length == 0 && !p.IsDefined(typeof(System.NonSerializedAttribute), true)),
                System.Linq.Enumerable.Where(type.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance), f => !f.IsDefined(typeof(System.NonSerializedAttribute), true))));
            Members.Add(type, members);
        }
        foreach (var member in members) {
            var property = member as System.Reflection.PropertyInfo;
            object a = property == null ? ((System.Reflection.FieldInfo)member).GetValue(left) : property.GetValue(left, null);
            object b = property == null ? ((System.Reflection.FieldInfo)member).GetValue(right) : property.GetValue(right, null);
            string difference = Difference(a, b, path + "." + member.Name);
            if (difference != null) return difference;
        }
        return null;
    }
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    static object CheckFile(string path, string root) {
        string relative = path.Substring(root.Length).TrimStart('\\', '/').Replace('\\', '/');
        System.Type type = relative == "saveInfo.json" ? typeof(JsonSaveInfo[]) : relative.StartsWith("ships/") ? typeof(JsonShip[]) : typeof(JsonGameSave[]);
        string json = System.Text.Encoding.UTF8.GetString(System.IO.File.ReadAllBytes(path));
        object vanilla = null, replacement = null; string ve = null, re = null;
        var timer = System.Diagnostics.Stopwatch.StartNew();
        try { vanilla = TypedParse(type, json, false); } catch (System.Exception ex) { ve = ex.GetType().Name + ": " + ex.Message; }
        timer.Stop(); double vanillaMs = timer.Elapsed.TotalMilliseconds; timer.Restart();
        try { replacement = TypedParse(type, json, true); } catch (System.Exception ex) { re = ex.GetType().Name + ": " + ex.Message; }
        timer.Stop(); double replacementMs = timer.Elapsed.TotalMilliseconds;
        visited = 0;
        string difference = ve == null && re == null ? Difference(vanilla, replacement, "$") : ve == re ? null : "Exception mismatch";
        return new { Entry = relative, Bytes = new System.IO.FileInfo(path).Length, VanillaMs = vanillaMs, ReplacementMs = replacementMs, Values = visited, Difference = difference, VanillaError = ve, ReplacementError = re };
    }
    internal static int Validate(string root, string output) {
        root = System.IO.Path.GetFullPath(root);
        var rows = new System.Collections.Generic.List<object>(); int failures = 0;
        foreach (string path in System.IO.Directory.GetFiles(root, "*.json", System.IO.SearchOption.AllDirectories)) {
            System.GC.Collect(); System.GC.WaitForPendingFinalizers(); System.GC.Collect();
            object result = CheckFile(path, root); rows.Add(result);
            string json = Newtonsoft.Json.JsonConvert.SerializeObject(result);
            if (!json.Contains("\"Difference\":null")) failures++;
            System.Console.WriteLine(json);
            System.IO.File.WriteAllText(output, Newtonsoft.Json.JsonConvert.SerializeObject(new { Files = rows.Count, Failures = failures, Results = rows }, Newtonsoft.Json.Formatting.Indented));
        }
        return failures == 0 ? 0 : 1;
    }
    public class Fixture {
        public double Double { get; set; }
        public float Float { get; set; }
        public int Int { get; set; }
        public long Long { get; set; }
        public byte Byte { get; set; }
        public int? Nullable { get; set; }
        public bool Bool { get; set; }
        public string Name { get; set; }
        public System.Collections.Generic.Dictionary<string, string> Map { get; set; }
        public Ostranauts.Utils.Models.Point Point { get; set; }
        public Nested Child { get; set; }
        public Nested Readonly { get { return null; } }
        public byte[] Bytes { get; set; }
    }
    public class Nested {
        public static int Constructors;
        public int Value = 11;
        public Nested() { Constructors++; }
        public Nested(int value) { Constructors += 100; Value = value + 999; }
    }
    internal static int CompatibilityCases() {
        string[] cases = {
            "{}", "null", "{\"Double\":9E99}", "{\"Float\":9e99}", "{\"Double\":-9e99}", "{\"Float\":-9E99}",
            "{\"Double\":1.29E99}", "{\"Double\":9E999}", "{\"Double\":9E98}", "{\"Double\":-0.0}",
            "{\"Int\":1.5}", "{\"Int\":\"12\"}", "{\"Bool\":1}", "{\"Double\":\"3.1\"}", "{\"Long\":1}",
            "{\"Int\":2147483648}", "{\"Byte\":256}", "{\"Byte\":255}", "{\"Nullable\":null}", "{\"Nullable\":7}", "{\"Double\":null}",
            "{\"NAME\":\"ignored\",\"Name\":\"actual\"}", "{\"Name\":\"a\",\"Name\":\"b\"}",
            "{\"Unknown\":{\"deep\":[null,{},9e99]},\"Int\":42}", "{\"Map\":{\"x\":\"a\",\"x\":\"b\"}}", "{\"Map\":{\"Count\":1,\"x\":\"y\"}}",
            "{\"Point\":{\"X\":1.25,\"Y\":-2.5}}", "{\"Child\":{\"Value\":5}}", "{\"Child\":{}}", "{\"Readonly\":{\"Value\":3}}",
            "{\"Readonly\":{\"Value\":\"wrong\"}}", "{\"Bytes\":[1,2,255]}", "{\"Name\":null}", "/* hi */{\"Name\":'single'}"
        };
        int failures = 0;
        foreach (string json in cases) {
            object a = null, b = null; string ae = null, be = null;
            Nested.Constructors = 0;
            try { a = TypedParse(typeof(Fixture), json, false); } catch (System.Exception ex) { ae = ex.GetType().Name; }
            int constructors = Nested.Constructors;
            Nested.Constructors = 0;
            try { b = TypedParse(typeof(Fixture), json, true); } catch (System.Exception ex) { be = ex.GetType().Name; }
            string difference = ae != null || be != null ? ae == be ? null : ae + " != " + be : Difference(a, b, "$");
            if (constructors != Nested.Constructors) difference = "Constructor invocation mismatch";
            if (difference != null) failures++;
            System.Console.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(new { Json = json, Difference = difference, VanillaError = ae, ReplacementError = be }));
        }
        System.Console.WriteLine("Cases=" + cases.Length + ", failures=" + failures); return failures == 0 ? 0 : 1;
    }
}
