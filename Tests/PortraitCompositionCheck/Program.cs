using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Ostrabus.Optimizations;
using UnityEngine;

static partial class Program {
    static int checks;
    static void Check(bool value,string reason) {checks++;if(!value)throw new Exception(reason);}
    static void Same(Color[] expected,Color[] actual,string reason) {
        Check(expected.Length==actual.Length,reason+" length");
        for(int i=0;i<expected.Length;i++) {
            var a=expected[i];var b=actual[i];
            if(a.r!=b.r||a.g!=b.g||a.b!=b.b||a.a!=b.a)throw new Exception(reason+" pixel "+i+": "+a+" != "+b);
        }
        checks++;
    }
    static Texture2D[] Layers(int width,int height,int seed,bool mixed=false) {
        var random=new System.Random(seed);var layers=new Texture2D[11];
        for(int i=0;i<layers.Length;i++) {
            var format=mixed && i%3==1?TextureFormat.RGB24:mixed && i%3==2?TextureFormat.ARGB32:TextureFormat.RGBA32;
            byte[] bytes=new byte[width*height*(format==TextureFormat.RGB24?3:4)];random.NextBytes(bytes);
            // Include transparent, opaque and quantization-boundary channels.
            if(format==TextureFormat.RGBA32)for(int p=3;p<bytes.Length;p+=4)if(p%5==0)bytes[p]=p%2==0?(byte)0:(byte)255;
            layers[i]=Make(width,height,format,bytes);
        }
        return layers;
    }
    static CondOwner Character(Texture2D[] layers) {
        var co=Empty<CondOwner>();var crew=Empty<Crew>();
        AccessTools.Field(typeof(CondOwner),"bTriedGettingCrewRef").SetValue(co,true);
        AccessTools.Field(typeof(CondOwner),"_crewComponentReference").SetValue(co,crew);
        var parts=new string[layers.Length];
        for(int i=0;i<parts.Length;i++){parts[i]="fixture-"+i;cache["portraits/"+parts[i]+".png"]=layers[i];}
        AccessTools.Field(typeof(Crew),"aFaceParts").SetValue(crew,parts);
        AccessTools.Field(typeof(FaceAnim2),"_facePartNames").SetValue(null,parts);
        AccessTools.Field(typeof(FaceAnim2),"_facePartOrder").SetValue(null,Enumerable.Range(0,parts.Length).Select(i=>i.ToString()).ToArray());return co;
    }
    static void Main() {
        var boundaries=new Harmony("offline.portraits.boundaries");var patch=new Harmony("offline.portraits.production");
        try {
            foreach(var method in new[]{AccessTools.Method(typeof(FaceAnim2),"GetPNG"),AccessTools.Method(typeof(PortraitComposition),"TryCompose"),AccessTools.Method(typeof(PortraitComposition),"NativePixels")})
                boundaries.Patch(method,transpiler:new HarmonyMethod(Method(nameof(Boundary))){priority=Priority.Last});
            boundaries.Patch(AccessTools.Method(typeof(DataHandler),"LoadPNG"),prefix:new HarmonyMethod(Method(nameof(Load))));
            boundaries.Patch(AccessTools.Method(typeof(DataHandler),"AddPNG"),prefix:new HarmonyMethod(Method(nameof(Add))));
            var layers=Layers(166,258,74,true);var co=Character(layers);ResetCounters();
            var watch=Stopwatch.StartNew();var before=FaceAnim2.GetPNG(co);watch.Stop();var baseline=(Color[])images[before].Written.Clone();
            Check(gets==471108 && sets==42828 && applies==1,"Native cold path did not execute expected individual pixel calls");
            Console.WriteLine("Native controlled cold composition: "+watch.Elapsed.TotalMilliseconds.ToString("F2")+" ms; GetPixel="+gets+", SetPixel="+sets);
            cache.Remove(string.Concat(Enumerable.Range(0,11).Select(i=>"fixture-"+i)));
            patch.CreateClassProcessor(typeof(PortraitCompositionPatch)).Patch();ResetCounters();watch.Restart();
            var after=FaceAnim2.GetPNG(co);watch.Stop();Same(baseline,images[after].Written,"Actual native GetPNG with production transpiler");
            Check(gets==0 && sets==0 && reads==11 && writes==1 && applies==1,"Bulk path did not replace individual pixel calls with one read per layer and one write");
            Console.WriteLine("Patched controlled cold composition: "+watch.Elapsed.TotalMilliseconds.ToString("F2")+" ms; bulk reads="+reads+", writes="+writes);
            ResetCounters();Check(ReferenceEquals(FaceAnim2.GetPNG(co),after) && gets==0 && sets==0 && reads==0 && writes==0 && applies==0,"Warm native cache did not remain unchanged");
            Check(ReferenceEquals(FaceAnim2.GetPNG(null),null),"Null-person native branch changed");
            string key=string.Concat(Enumerable.Range(0,11).Select(i=>"fixture-"+i));cache[key]=New(1,1);ResetCounters();
            var rebuilt=FaceAnim2.GetPNG(co);Same(baseline,images[rebuilt].Written,"Wrong-size native cache regeneration");
            Check(reads==11 && writes==1 && applies==1 && ReferenceEquals(cache[key],rebuilt),"Native cache replacement or Apply changed");
            var legacy=Empty<CondOwner>();AccessTools.Field(typeof(CondOwner),"bTriedGettingCrewRef").SetValue(legacy,true);legacy.strPortraitImg="legacy.png";cache[legacy.strPortraitImg]=after;
            ResetCounters();Check(ReferenceEquals(FaceAnim2.GetPNG(legacy),after) && reads==0 && writes==0,"Crewless/native portrait branch changed");
            PixelCases();FallbackCases();Allocation();KernelComparison();Guards();
            Console.WriteLine(checks+" portrait checks passed; actual Unity texture IO is a test boundary.");
        } finally {patch.UnpatchSelf();boundaries.UnpatchSelf();foreach(var image in images.Values)image.Dispose();images.Clear();cache.Clear();}
    }
    static void PixelCases() {
        foreach(var size in new[]{(1,1),(3,7),(17,9),(166,258)})foreach(bool mixed in new[]{false,true}) {
            var layers=Layers(size.Item1,size.Item2,51+checks,mixed);var a=New(size.Item1,size.Item2);var b=New(size.Item1,size.Item2);
            PortraitComposition.NativePixels(a,layers);PortraitComposition.Compose(b,layers);
            Same(images[a].Written,images[b].Written,"Layer/alpha/order equivalence "+size+" mixed="+mixed);
        }
    }
    static void FallbackCases() {
        var layers=Layers(3,7,98,true);
        Action<string> fallback=reason=>{var a=New(3,7);var b=New(3,7);PortraitComposition.NativePixels(a,layers);ResetCounters();PortraitComposition.Compose(b,layers);Same(images[a].Written,images[b].Written,reason);Check(gets==231 && sets==21 && writes==0,reason+" did not use native pixels");};
        images[layers[0]].Format=TextureFormat.BGRA32;fallback("Unsupported format preserves native fallback");images[layers[0]].Format=TextureFormat.RGBA32;
        var old=layers[0];layers[0]=Make(1,1,TextureFormat.RGBA32,new byte[]{255,0,128,77});fallback("Mixed dimensions preserve native wrapping");layers[0]=old;
        images[layers[0]].Readable=false;bool threw=false;try{PortraitComposition.Compose(New(3,7),layers);}catch(InvalidOperationException){threw=true;}Check(threw,"Unreadable texture should keep native failure");images[layers[0]].Readable=true;
        throwWrite=true;try{PortraitComposition.Compose(New(3,7),layers);}catch(InvalidOperationException){}finally{throwWrite=false;}
        ResetCounters();PortraitComposition.Compose(New(3,7),layers);Check(reads==11 && writes==1 && gets==0,"Upload error left composition busy");
        nested=New(3,7);nestedLayers=layers;reenter=true;var outer=New(3,7);PortraitComposition.Compose(outer,layers);Same(images[outer].Written,images[nested].Written,"Reentrancy preserves both results");
        var a=PortraitComposition.Buffer(21);Check(ReferenceEquals(a,PortraitComposition.Buffer(21)),"Output buffer is not reused");
    }
    static void Guards() {
        var original=AccessTools.Method(typeof(FaceAnim2),"GetPNG");
        // The original assembly's IL remains available independently of detours.
        var reader=typeof(PatchProcessor).GetMethods().Single(m=>m.Name=="GetOriginalInstructions" && m.GetParameters().Length==2 && !m.GetParameters()[1].ParameterType.IsByRef);
        var code=((IEnumerable<CodeInstruction>)reader.Invoke(null,new object[]{original,null})).ToList();
        Check(code.Count(i=>i.Calls(AccessTools.Method(typeof(Texture2D),"GetPixel",new[]{typeof(int),typeof(int)})))==2,"Native fixture changed");
        var transformed=PortraitCompositionPatch.Transpiler(code,original).ToList();
        Check(transformed.Count(i=>i.Calls(AccessTools.Method(typeof(PortraitComposition),"Compose")))==1,"Composition helper not installed once");
        bool refused=false;try{PortraitCompositionPatch.Transpiler(new[]{new CodeInstruction(System.Reflection.Emit.OpCodes.Ret)},original).ToList();}catch(InvalidOperationException){refused=true;}
        Check(refused,"Unknown native IL was accepted");
    }
    static unsafe void Allocation() {
        byte[] bytes={128,25,77,119,255,0,33,255};
        fixed(byte* pointer=bytes) {
            PortraitComposition.Layer* layers=stackalloc PortraitComposition.Layer[11];
            for(int i=0;i<11;i++)layers[i]=new PortraitComposition.Layer{Pixels=pointer,Stride=4,Alpha=3};
            var buffer=PortraitComposition.Buffer(2);PortraitComposition.Blend(layers,11,2,buffer);
            long calibration=GC.GetAllocatedBytesForCurrentThread();var probe=new byte[256];GC.KeepAlive(probe);
            bool available=GC.GetAllocatedBytesForCurrentThread()-calibration>=256;
            long before=GC.GetAllocatedBytesForCurrentThread();
            for(int i=0;i<1000;i++)PortraitComposition.Blend(layers,11,2,PortraitComposition.Buffer(2));
            long allocated=GC.GetAllocatedBytesForCurrentThread()-before;
            Check(ReferenceEquals(buffer,PortraitComposition.Buffer(2)) && (!available || allocated==0),"Warmed blending/output-buffer reuse allocated "+allocated+" bytes");
            Console.WriteLine(available?"Warmed kernel + output-buffer reuse: "+allocated+" managed bytes / 1000 compositions (texture IO excluded).":"Allocation counter unavailable in this Mono runtime; warmed output-buffer identity verified.");
        }
        var oversized=New(1,1);images[oversized].Width=262145;
        Check(!PortraitComposition.TryCompose(oversized,new Texture2D[11]),"Oversized buffer was accepted");
        Check(!PortraitComposition.TryCompose(New(1,1),new Texture2D[33]),"Unbounded layer stack allocation was accepted");
        Check(!PortraitComposition.TryCompose(New(1,1),Array.Empty<Texture2D>()),"Empty layer set accepted");
        Check(PortraitComposition.Supports(TextureFormat.ARGB32) && !PortraitComposition.Supports(TextureFormat.DXT5),"Native PNG format or unsupported compressed layout mishandled");
    }
    static unsafe void PreviousBulkKernel(PortraitComposition.Layer* layers,int layerCount,int count,Color[] destination) {
        for(int p=0;p<count;p++) {
            Color color=Color.black;
            for(int layer=0;layer<layerCount;layer++) {
                var source=layers[layer];byte* pixel=source.Pixels+p*source.Stride;
                Color next=(Color)new Color32(pixel[source.Red],pixel[source.Red+1],pixel[source.Red+2],source.Stride==3?(byte)255:pixel[source.Alpha]);
                color=Color.Lerp(color,next,next.a);
            }
            destination[p]=color;
        }
    }
    static unsafe void KernelComparison() {
        const int count=166*258,rounds=10;
        foreach(bool sparse in new[]{false,true}) {
            var sources=Layers(166,258,472,true);
            var views=stackalloc PortraitComposition.Layer[sources.Length];
            for(int layer=0;layer<sources.Length;layer++) {
                var image=images[sources[layer]];int stride=image.Format==TextureFormat.RGB24?3:4;bool argb=image.Format==TextureFormat.ARGB32;
                int alpha=argb?0:3;
                if(sparse && stride==4)for(int p=0;p<count;p++)if(p%10!=0)image.Bytes[p*stride+alpha]=0;
                views[layer]=new PortraitComposition.Layer{Pixels=(byte*)image.Pin.AddrOfPinnedObject(),Stride=stride,Red=argb?1:0,Alpha=alpha};
            }
            var before=new Color[count];var after=new Color[count];
            PreviousBulkKernel(views,11,count,before);PortraitComposition.Blend(views,11,count,after);
            Same(before,after,"Previous bulk kernel exact float equivalence sparse="+sparse);
            var clock=Stopwatch.StartNew();for(int i=0;i<rounds;i++)PreviousBulkKernel(views,11,count,before);clock.Stop();double oldMs=clock.Elapsed.TotalMilliseconds/rounds;
            clock.Restart();for(int i=0;i<rounds;i++)PortraitComposition.Blend(views,11,count,after);clock.Stop();
            Console.WriteLine("Warmed managed kernel, sparse="+sparse+": previous bulk="+oldMs.ToString("F2")+" ms, revised="+(clock.Elapsed.TotalMilliseconds/rounds).ToString("F2")+" ms (10 rounds; no texture IO).");
        }
    }
}
