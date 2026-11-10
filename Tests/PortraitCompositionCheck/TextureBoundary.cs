using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using HarmonyLib;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;

static partial class Program {
    sealed class References : IEqualityComparer<Texture2D> {
        public bool Equals(Texture2D a, Texture2D b) => ReferenceEquals(a,b);
        public int GetHashCode(Texture2D texture) => RuntimeHelpers.GetHashCode(texture);
    }
    sealed class Image : IDisposable {
        internal int Width, Height;
        internal TextureFormat Format;
        internal bool Readable = true;
        internal byte[] Bytes;
        internal Color[] Written;
        internal GCHandle Pin;
        internal Image(int w, int h, TextureFormat format, byte[] bytes) {
            Width=w;Height=h;Format=format;Bytes=bytes;Pin=GCHandle.Alloc(bytes,GCHandleType.Pinned);
        }
        public void Dispose() { if(Pin.IsAllocated) Pin.Free(); }
    }
    static readonly Dictionary<Texture2D,Image> images = new Dictionary<Texture2D,Image>(new References());
    static readonly Dictionary<string,Texture2D> cache = new Dictionary<string,Texture2D>();
    static int gets, sets, reads, writes, applies;
    static bool throwWrite, reenter;
    static Texture2D nested;
    static Texture2D[] nestedLayers;
    static T Empty<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    static Texture2D Make(int width,int height,TextureFormat format,byte[] bytes=null) {
        var texture=Empty<Texture2D>();int stride=format==TextureFormat.RGB24?3:4;
        images.Add(texture,new Image(width,height,format,bytes??new byte[width*height*stride]));return texture;
    }
    static Texture2D New(int width,int height) => Make(width,height,TextureFormat.RGBA32);
    static int Width(Texture t) => images[(Texture2D)t].Width;
    static int Height(Texture t) => images[(Texture2D)t].Height;
    static TextureFormat Format(Texture2D t) => images[t].Format;
    static bool Readable(Texture t) => images[(Texture2D)t].Readable;
    static void Filter(Texture texture,FilterMode mode) { }
    static void Name(UnityEngine.Object texture,string name) { }
    static bool Eq(UnityEngine.Object a,UnityEngine.Object b) => ReferenceEquals(a,b);
    static bool Neq(UnityEngine.Object a,UnityEngine.Object b) => !ReferenceEquals(a,b);
    static unsafe NativeArray<byte> Bytes(Texture2D texture,int mip) {
        reads++;var image=images[texture];
        return NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<byte>((void*)image.Pin.AddrOfPinnedObject(),image.Bytes.Length,Allocator.None);
    }
    static unsafe void* Pointer(NativeArray<byte> data) => NativeArrayUnsafeUtility.GetUnsafeBufferPointerWithoutChecks(data);
    static Color Get(Texture2D texture,int x,int y) {
        gets++;var t=images[texture];if(!t.Readable)throw new InvalidOperationException("unreadable fixture");
        x=Math.Max(0,Math.Min(t.Width-1,x));y=Math.Max(0,Math.Min(t.Height-1,y));
        if(t.Written!=null)return t.Written[y*t.Width+x];
        int stride=t.Format==TextureFormat.RGB24?3:4,p=(y*t.Width+x)*stride;
        if(t.Format==TextureFormat.ARGB32)return (Color)new Color32(t.Bytes[p+1],t.Bytes[p+2],t.Bytes[p+3],t.Bytes[p]);
        return (Color)new Color32(t.Bytes[p],t.Bytes[p+1],t.Bytes[p+2],stride==3?(byte)255:t.Bytes[p+3]);
    }
    static void Set(Texture2D texture,int x,int y,Color color) {
        sets++;var t=images[texture];if(t.Written==null)t.Written=new Color[t.Width*t.Height];t.Written[y*t.Width+x]=color;
    }
    static void Write(Texture2D texture,Color[] pixels) {
        writes++;if(throwWrite)throw new InvalidOperationException("fixture upload failure");
        if(reenter) {reenter=false;Ostrabus.Optimizations.PortraitComposition.Compose(nested,nestedLayers);}
        var t=images[texture];t.Written=(Color[])pixels.Clone();
    }
    static void Apply(Texture2D texture) { applies++; }
    static bool Load(string strFileName,ref Texture2D __result) {cache.TryGetValue(strFileName,out __result);return false;}
    static bool Add(string strFileName,Texture2D bmp) {cache[strFileName]=bmp;return false;}
    static MethodInfo Method(string name) => AccessTools.Method(typeof(Program),name);
    static IEnumerable<CodeInstruction> Boundary(IEnumerable<CodeInstruction> input) {
        var map=new Dictionary<MethodBase,MethodInfo> {
            [AccessTools.PropertyGetter(typeof(Texture),"width")]=Method(nameof(Width)),
            [AccessTools.PropertyGetter(typeof(Texture),"height")]=Method(nameof(Height)),
            [AccessTools.PropertyGetter(typeof(Texture2D),"width")]=Method(nameof(Width)),
            [AccessTools.PropertyGetter(typeof(Texture2D),"height")]=Method(nameof(Height)),
            [AccessTools.PropertyGetter(typeof(Texture2D),"format")]=Method(nameof(Format)),
            [AccessTools.PropertyGetter(typeof(Texture2D),"isReadable")]=Method(nameof(Readable)),
            [AccessTools.PropertySetter(typeof(Texture),"filterMode")]=Method(nameof(Filter)),
            [AccessTools.PropertySetter(typeof(UnityEngine.Object),"name")]=Method(nameof(Name)),
            [AccessTools.Method(typeof(UnityEngine.Object),"op_Equality")]=Method(nameof(Eq)),
            [AccessTools.Method(typeof(UnityEngine.Object),"op_Inequality")]=Method(nameof(Neq)),
            [AccessTools.Method(typeof(Texture2D),"GetPixel",new[]{typeof(int),typeof(int)})]=Method(nameof(Get)),
            [AccessTools.Method(typeof(Texture2D),"SetPixel",new[]{typeof(int),typeof(int),typeof(Color)})]=Method(nameof(Set)),
            [AccessTools.Method(typeof(Texture2D),"SetPixels",new[]{typeof(Color[])})]=Method(nameof(Write)),
            [AccessTools.Method(typeof(Texture2D),"Apply",Type.EmptyTypes)]=Method(nameof(Apply)),
            [AccessTools.Constructor(typeof(Texture2D),new[]{typeof(int),typeof(int)})]=Method(nameof(New))
        };
        foreach(var instruction in input) {
            if(instruction.operand is MethodInfo property && property.Name=="get_isReadable") {
                instruction.opcode=System.Reflection.Emit.OpCodes.Call;instruction.operand=Method(nameof(Readable));
            } else if(instruction.operand is MethodInfo generic && generic.IsGenericMethod && generic.Name=="GetPixelData") {
                instruction.opcode=System.Reflection.Emit.OpCodes.Call;instruction.operand=Method(nameof(Bytes));
            } else if(instruction.operand is MethodInfo ptr && ptr.Name=="GetUnsafeReadOnlyPtr") {
                instruction.opcode=System.Reflection.Emit.OpCodes.Call;instruction.operand=Method(nameof(Pointer));
            } else if(instruction.operand is MethodBase key && map.TryGetValue(key,out var method)) {
                instruction.opcode=System.Reflection.Emit.OpCodes.Call;instruction.operand=method;
            }
            yield return instruction;
        }
    }
    static void ResetCounters() {gets=sets=reads=writes=applies=0;}
}
