using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Threading;
using HarmonyLib;
using Ostrabus.Inspection;
using Ostrabus.Optimizations;
using UnityEngine;
using UnityEngine.UI;

static class Program {
    const string Owner="com.ostranauts.template.profiling.socials";
    static int checks,frame=10;
    static bool fail;
    static readonly Exception Error=new InvalidOperationException("native fixture error");
    static T Empty<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    static void Check(bool ok,string reason) {checks++;if(!ok)throw new Exception(reason);}
    static int Owned() => Harmony.GetAllPatchedMethods().Count(m=>Harmony.GetPatchInfo(m).Owners.Contains(Owner));
    static bool Quiet() => false;
    static bool Shader(ref int __result) {__result=1;return false;}
    static bool Marker(ref IntPtr __result) {__result=new IntPtr(1);return false;}
    static bool Eq(UnityEngine.Object __0,UnityEngine.Object __1,ref bool __result) {__result=ReferenceEquals(__0,__1);return false;}
    static bool Neq(UnityEngine.Object __0,UnityEngine.Object __1,ref bool __result) {__result=!ReferenceEquals(__0,__1);return false;}
    static bool Portrait(ref Texture2D __result) {Thread.Sleep(2);__result=null;return false;}
    static bool Row() {FaceAnim2.GetPNG(null);PortraitComposition.TryCompose(null,null);PortraitComposition.NativePixels(null,null);Thread.Sleep(1);return false;}
    static bool Show() {
        GUIPDA.GetKnownSocialContacts(null);
        var row=Empty<GUISocialsRow>();row.SetContact(null,null,null);row.SetContact(null,null,null);
        if(fail)throw Error;
        return false;
    }
    static void Patch(Harmony h,Type type,string method,string prefix,Type[] args=null) => h.Patch(AccessTools.Method(type,method,args),prefix:new HarmonyMethod(typeof(Program),prefix));
    static Dictionary<string,object> Snapshot() => (Dictionary<string,object>)SocialsProfiling.Snapshot();
    static Dictionary<string,object> Metric(string name) => ((List<object>)Snapshot()["metrics"]).Cast<Dictionary<string,object>>().Single(r=>(string)r["method"]==name);
    static void Main() {
        var boundary=new Harmony("offline.socials.profiling.boundaries");
        try {
            Patch(boundary,typeof(Shader),"PropertyToID",nameof(Shader),new[]{typeof(string)});
            var profiler=typeof(Texture).Assembly.GetType("Unity.Profiling.LowLevel.Unsafe.ProfilerUnsafeUtility",true);
            var marker=profiler.GetMethods(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static).Single(m=>m.Name=="CreateMarker" && m.GetParameters()[0].ParameterType==typeof(string));
            boundary.Patch(marker,prefix:new HarmonyMethod(typeof(Program),nameof(Marker)));
            Patch(boundary,typeof(UnityEngine.Object),"op_Equality",nameof(Eq));
            Patch(boundary,typeof(UnityEngine.Object),"op_Inequality",nameof(Neq));
            Patch(boundary,typeof(CrewSim),"Update",nameof(Quiet));
            Patch(boundary,typeof(GUIPDA),"ShowSocials",nameof(Show));
            Patch(boundary,typeof(GUISocialsRow),"SetContact",nameof(Row));
            Patch(boundary,typeof(FaceAnim2),"GetPNG",nameof(Portrait));
            Patch(boundary,typeof(PortraitComposition),"NativePixels",nameof(Quiet));
            Patch(boundary,typeof(LayoutRebuilder),"Rebuild",nameof(Quiet));
            SocialsProfiling.ReadFrame=()=>frame;
            Check(!SocialsProfiling.Active && Owned()==0,"Startup installed diagnostic hooks");
            SocialsProfiling.Snapshot();Check(Owned()==0,"GET installed diagnostic hooks");
            Check(!typeof(SocialsProfiling).IsDefined(typeof(HarmonyPatch),false),"PatchAll installs diagnostic hooks");
            SocialsProfiling.Start();Check(SocialsProfiling.Active && Owned()==19,"Start did not bind expected 19 methods: "+Owned());
            SocialsProfiling.Start();Check(Owned()==19,"Repeat start duplicated hooks");
            FaceAnim2.GetPNG(null);Check((int)Metric("FaceAnim2.GetPNG")["calls"]==0,"Unrelated portrait counted before Socials");
            var show=AccessTools.Method(typeof(GUIPDA),"ShowSocials");var gui=Empty<GUIPDA>();show.Invoke(gui,null);
            Check((int)Metric("GUIPDA.ShowSocials")["calls"]==1 && (int)Snapshot()["openings"]==1,"Opening not measured");
            Check((int)Metric("GUISocialsRow.SetContact")["calls"]==2 && (int)Metric("FaceAnim2.GetPNG")["calls"]==2,"Nested row/portrait counts wrong");
            Check((double)Metric("GUISocialsRow.SetContact")["inclusiveMs"]>(double)Metric("GUISocialsRow.SetContact")["exclusiveMs"],"Nested times were not subtracted");
            Check((int)Metric("PortraitComposition.NativePixels")["calls"]==2 && Snapshot()["firstInputs"]!=null,"Fallback/input inspection absent");
            var layout=Empty<LayoutRebuilder>();layout.Rebuild(CanvasUpdate.Layout);
            Check((int)Metric("LayoutRebuilder.Rebuild")["calls"]==1,"Deferred layout not captured");
            var tick=AccessTools.Method(typeof(CrewSim),"Update");var crew=Empty<CrewSim>();frame=14;tick.Invoke(crew,null);
            Check(!SocialsProfiling.Active && Owned()==0,"Automatic completion left hooks");
            Check(SnapshotJson.Encode(Snapshot()).Contains("Completed"),"Completed results not retained");
            SocialsProfiling.Stop();Check(Owned()==0,"Repeated stop changed hook ownership");
            SocialsProfiling.Start();fail=true;Exception caught=null;
            try{show.Invoke(gui,null);}catch(TargetInvocationException ex){caught=ex.InnerException;}
            Check(ReferenceEquals(caught,Error),"Profiler swallowed or replaced native exception");
            Check((int)Snapshot()["errors"]==1 && (int)AccessTools.Field(typeof(SocialsProfiling),"depth").GetValue(null)==0,"Exception leaked root scope");
            SocialsProfiling.Stop("Fixture cancellation");Check(Owned()==0,"Cancellation left hooks");
            SocialsProfiling.Start();AccessTools.Field(typeof(SocialsProfiling),"deadline").SetValue(null,0L);tick.Invoke(crew,null);
            Check(!SocialsProfiling.Active && Owned()==0 && SnapshotJson.Encode(Snapshot()).Contains("Expired"),"Timeout left hooks");
            Console.WriteLine(checks+" temporary Socials profiling binding/scope/timing/exception/unpatch checks passed.");
        } finally {SocialsProfiling.Stop();boundary.UnpatchSelf();}
    }
}
