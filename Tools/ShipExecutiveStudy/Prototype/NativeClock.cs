using System;
using System.Runtime.InteropServices;

namespace ShipExecutivePrototype;

static class NativeClock
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    delegate float FloatGetter();
    static readonly FloatGetter Scale = () => 1f;
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    delegate void DestroyObject(IntPtr pointer, float delay);
    static readonly DestroyObject Destroy = (pointer, delay) => ExcludedSceneCalls++;
    internal static int ExcludedSceneCalls;
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    delegate void RotateVector(ref UnityEngine.Vector3 current, ref UnityEngine.Vector3 target, float angle, float magnitude, out UnityEngine.Vector3 result);
    static readonly RotateVector Rotate = (ref UnityEngine.Vector3 current, ref UnityEngine.Vector3 target, float angle, float magnitude, out UnityEngine.Vector3 result) =>
    {
        ExcludedSteeringCalls++;
        result = current;
    };
    internal static int ExcludedSteeringCalls;
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    delegate void DebugBreak();
    static readonly DebugBreak Break = () => ExcludedSceneCalls++;
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    delegate void DestroyImmediateObject(IntPtr pointer, bool allowAssets);
    static readonly DestroyImmediateObject Immediate = (pointer, allowAssets) => ExcludedSceneCalls++;

    [DllImport("mono-2.0-bdwgc.dll", CallingConvention = CallingConvention.Cdecl)]
    static extern void mono_add_internal_call([MarshalAs(UnmanagedType.LPStr)] string name, IntPtr function);

    internal static void Install()
    {
        mono_add_internal_call("UnityEngine.Time::get_timeScale", Marshal.GetFunctionPointerForDelegate(Scale));
        // Mono resolves this engine call while compiling the SFF iterator,
        // including its excluded scene/UI tail. Verify the ABI and record any
        // accidental invocation; tests must show the excluded tail never ran.
        var destroy = HarmonyLib.AccessTools.Method(typeof(UnityEngine.Object), "Destroy_Injected");
        var parameters = destroy?.GetParameters();
        if (destroy == null || destroy.ReturnType != typeof(void) || parameters.Length != 2 || parameters[0].ParameterType != typeof(IntPtr) || parameters[1].ParameterType != typeof(float))
            throw new InvalidOperationException("Unity destruction fixture signature changed");
        mono_add_internal_call("UnityEngine.Object::Destroy_Injected", Marshal.GetFunctionPointerForDelegate(Destroy));
        // FlyToManual's JIT resolves this even while its native pause guard
        // returns before steering. It is not a replacement steering algorithm.
        // Any actual call fails the qualification assertion.
        var rotate = HarmonyLib.AccessTools.Method(typeof(UnityEngine.Vector3), "Internal_RotateTowards_Injected");
        var rotateParameters = rotate?.GetParameters();
        if (rotate == null || rotate.ReturnType != typeof(void) || rotateParameters.Length != 5 ||
            rotateParameters[0].ParameterType != typeof(UnityEngine.Vector3).MakeByRefType() ||
            rotateParameters[1].ParameterType != typeof(UnityEngine.Vector3).MakeByRefType() ||
            rotateParameters[2].ParameterType != typeof(float) || rotateParameters[3].ParameterType != typeof(float) ||
            rotateParameters[4].ParameterType != typeof(UnityEngine.Vector3).MakeByRefType())
            throw new InvalidOperationException("Unity rotation fixture signature changed: " + rotate);
        mono_add_internal_call("UnityEngine.Vector3::Internal_RotateTowards_Injected", Marshal.GetFunctionPointerForDelegate(Rotate));
        var stop = HarmonyLib.AccessTools.Method(typeof(UnityEngine.Debug), "Break");
        var immediate = HarmonyLib.AccessTools.Method(typeof(UnityEngine.Object), "DestroyImmediate_Injected");
        var immediateParameters = immediate?.GetParameters();
        if (stop == null || stop.ReturnType != typeof(void) || stop.GetParameters().Length != 0 || immediate == null ||
            immediate.ReturnType != typeof(void) || immediateParameters.Length != 2 || immediateParameters[0].ParameterType != typeof(IntPtr) || immediateParameters[1].ParameterType != typeof(bool))
            throw new InvalidOperationException("Excluded native arrival scene ABI changed");
        mono_add_internal_call("UnityEngine.Debug::Break", Marshal.GetFunctionPointerForDelegate(Break));
        mono_add_internal_call("UnityEngine.Object::DestroyImmediate_Injected", Marshal.GetFunctionPointerForDelegate(Immediate));
    }
}
