using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;

internal static partial class Benchmark {
    static long visited;
    static readonly Dictionary<Type, MemberInfo[]> Members = new Dictionary<Type, MemberInfo[]>();
    [StructLayout(LayoutKind.Explicit)] struct FloatBits {
        [FieldOffset(0)] public float Value;
        [FieldOffset(0)] public int Bits;
    }
}
