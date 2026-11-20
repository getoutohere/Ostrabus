using System.Collections.Generic;
using HarmonyLib;
using Ostrabus.BottleRefiller;
using UnityEngine;

internal static partial class Program
{
    private static readonly Dictionary<AudioEmitter,GameObject> AudioNodes = new Dictionary<AudioEmitter,GameObject>();
    private static readonly HashSet<GameObject> ActiveAudioNodes = new HashSet<GameObject>();
    private static int audioCreates, audioStarts, audioStops, nodeDestroys, indicatorWrites;
    private static Renderer testIndicator, testStandby;
    private static bool indicatorLit, standbyLit;

    private static bool FindIndicator(string image, ref Renderer __result)
    {
        Check(image=="ItmLitGreen"||image=="ItmLitYellow","Unexpected indicator image");
        __result=image=="ItmLitGreen"?testIndicator:testStandby;return false;
    }
    private static bool IndicatorEnabled(Renderer __instance, bool value)
    {
        if(ReferenceEquals(__instance,testIndicator)){indicatorLit=value;indicatorWrites++;return false;}
        if(ReferenceEquals(__instance,testStandby)){standbyLit=value;indicatorWrites++;return false;}
        return true;
    }

    private static bool FakeAudio(string definition, ref AudioEmitter __result)
    {
        Check(definition=="AirPumpActive"||definition=="AirPumpActiveAir","Unexpected audio profile");
        __result=Empty<AudioEmitter>();
        var node=Empty<GameObject>();AudioNodes[__result]=node;ActiveAudioNodes.Add(node);audioCreates++;
        return false;
    }
    private static bool AudioGameObject(Component __instance, ref GameObject __result)
    {
        if(__instance is AudioEmitter audio && AudioNodes.TryGetValue(audio,out __result))return false;
        return true;
    }
    private static bool PlayAudio(AudioEmitter __instance)
    {
        if(!AudioNodes.ContainsKey(__instance))return true;
        audioStarts++;return false;
    }
    private static bool StopAudio(AudioEmitter __instance)
    {
        if(!AudioNodes.ContainsKey(__instance))return true;
        audioStops++;return false;
    }
    private static bool DestroyAudioNode(UnityEngine.Object obj)
    {
        if(obj is GameObject node && ActiveAudioNodes.Remove(node)){nodeDestroys++;return false;}
        return true;
    }
    private static void AudioCases()
    {
        var boundary=new Harmony("offline.bottle-refiller.audio-boundary");
        try
        {
            testIndicator=Empty<MeshRenderer>();testStandby=Empty<MeshRenderer>();
            boundary.Patch(AccessTools.Method(typeof(Refiller),"FindIndicator"),prefix:new HarmonyMethod(typeof(Program),nameof(FindIndicator)));
            boundary.Patch(AccessTools.PropertySetter(typeof(Renderer),nameof(Renderer.enabled)),prefix:new HarmonyMethod(typeof(Program),nameof(IndicatorEnabled)));
            boundary.Patch(AccessTools.Method(typeof(Refiller),"CreateAudio"),prefix:new HarmonyMethod(typeof(Program),nameof(FakeAudio)));
            boundary.Patch(AccessTools.PropertyGetter(typeof(Component),nameof(Component.gameObject)),prefix:new HarmonyMethod(typeof(Program),nameof(AudioGameObject)));
            boundary.Patch(AccessTools.Method(typeof(AudioEmitter),nameof(AudioEmitter.PlaySteady)),prefix:new HarmonyMethod(typeof(Program),nameof(PlayAudio)));
            boundary.Patch(AccessTools.Method(typeof(AudioEmitter),nameof(AudioEmitter.Destroy)),prefix:new HarmonyMethod(typeof(Program),nameof(StopAudio)));
            boundary.Patch(AccessTools.Method(typeof(UnityEngine.Object),nameof(UnityEngine.Object.Destroy),new[]{typeof(UnityEngine.Object)}),prefix:new HarmonyMethod(typeof(Program),nameof(DestroyAudioNode)));
            var device=Empty<Refiller>();var sound=AccessTools.Method(typeof(Refiller),"SetFilling");
            var owner=Rack(MakeShip());Set(device,"owner",owner);
            Flag(owner,"IsOverrideOff",true);
            indicatorLit=true;standbyLit=true;
            AccessTools.Method(typeof(Refiller),"Start").Invoke(device,null);
            Check(!indicatorLit&&!standbyLit,"OFF unit shows an indicator");
            Flag(owner,"IsOverrideOff",false);
            sound.Invoke(device,new object[]{false});Check(audioCreates==0,"Idle device creates audio");
            Check(standbyLit&&!indicatorLit,"ON idle unit lacks amber standby");
            sound.Invoke(device,new object[]{true});Check(audioCreates==2&&audioStarts==2,"Filling lacks native motor/hiss calls");
            Check(indicatorLit&&!standbyLit,"Filling indicator is not exclusively green");
            int writes=indicatorWrites;
            for(int i=0;i<100;i++)sound.Invoke(device,new object[]{true});
            Check(audioCreates==2&&audioStarts==2,"Audio restarted/allocated each frame");
            Check(indicatorWrites==writes,"Unchanged indicator state rewrites native renderers each frame");
            sound.Invoke(device,new object[]{false});
            Check(!indicatorLit&&standbyLit,"Full/empty/idle unit does not return to amber");
            Check(audioStops==2&&nodeDestroys==2&&ActiveAudioNodes.Count==0,"Idle audio sources retained");
            sound.Invoke(device,new object[]{true});
            AccessTools.Method(typeof(Refiller),"OnDisable").Invoke(device,null);
            Check(!indicatorLit&&!standbyLit,"Unloaded device retains an indicator");
            Check(audioStops==4&&nodeDestroys==4&&ActiveAudioNodes.Count==0,"Unload audio sources retained");
            AccessTools.Method(typeof(Refiller),"OnDestroy").Invoke(device,null);
            Check(audioStops==4&&nodeDestroys==4,"Audio cleanup doubled on destruction");

            Set(device,"serviceClock",new ServiceClock());
            var crewSim=Empty<CrewSim>();Set(crewSim,"_finishedLoading",true);
            Set(typeof(Ostranauts.Core.MonoSingleton<CrewSim>),"_instance",crewSim);
            Set(typeof(CrewSim),"fTimeCoeffPause",1f);
            sound.Invoke(device,new object[]{true});Flag(owner,"IsOverrideOff",true);
            AccessTools.Method(typeof(Refiller),"Update").Invoke(device,null);
            Check(ActiveAudioNodes.Count==0,"OFF waits for next service tick to stop sound");
            Check(!indicatorLit&&!standbyLit,"OFF leaves an indicator on");
            Flag(owner,"IsOverrideOff",false);sound.Invoke(device,new object[]{true});
            Flag(owner,"IsPowered",false);
            AccessTools.Method(typeof(Refiller),"Update").Invoke(device,null);
            Check(ActiveAudioNodes.Count==0,"Power loss leaves sound running");
            Check(!indicatorLit&&!standbyLit,"Power loss leaves an indicator on");
            Flag(owner,"IsPowered",true);sound.Invoke(device,new object[]{true});
            Set(typeof(CrewSim),"fTimeCoeffPause",0f);
            AccessTools.Method(typeof(Refiller),"Update").Invoke(device,null);
            Check(ActiveAudioNodes.Count==0,"Pause leaves sound running");
            Check(indicatorLit&&!standbyLit,"Pause changes the last simulated filling state");
            Flag(owner,"IsOverrideOff",true);
            AccessTools.Method(typeof(Refiller),"Update").Invoke(device,null);
            Check(!indicatorLit&&!standbyLit,"Switching OFF while paused leaves indicator on");
            Flag(owner,"IsOverrideOff",false);
            AccessTools.Method(typeof(Refiller),"Update").Invoke(device,null);
            Check(standbyLit&&!indicatorLit&&ActiveAudioNodes.Count==0,"Switching ON while paused lacks silent amber standby");
            Set(typeof(CrewSim),"fTimeCoeffPause",1f);
            sound.Invoke(device,new object[]{true});Flag(owner,"IsDamaged",true);
            AccessTools.Method(typeof(Refiller),"Update").Invoke(device,null);
            Check(!indicatorLit&&!standbyLit&&ActiveAudioNodes.Count==0,"Damage retains filling effects");
            Flag(owner,"IsDamaged",false);sound.Invoke(device,new object[]{true});Flag(owner,"IsInstalled",false);
            AccessTools.Method(typeof(Refiller),"Update").Invoke(device,null);
            Check(!indicatorLit&&!standbyLit&&ActiveAudioNodes.Count==0,"Uninstallation retains filling effects");
        }
        finally
        {
            Set(typeof(CrewSim),"fTimeCoeffPause",1f);
            boundary.UnpatchSelf();AudioNodes.Clear();ActiveAudioNodes.Clear();testIndicator=null;testStandby=null;
        }
    }
}
